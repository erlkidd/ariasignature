using System.Text.Json;
using System.Text.Json.Serialization;
using AriaSignature.Application;
using AriaSignature.Application.Abstractions;
using Microsoft.Extensions.Logging;
using AriaSignature.Api.Contracts;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.FileProviders;
using Quartz;

namespace AriaSignature.Api;

public static class AriaApiExtensions
{
    public static IServiceCollection AddAriaApi(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
        services.AddProblemDetails();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddSingleton<ISmartRefreshCronApplier, NoOpSmartRefreshCronApplier>();
        services.AddSingleton<IOutboundSyncCronApplier, NoOpOutboundSyncCronApplier>();
        return services;
    }

    public static WebApplication UseAriaApi(this WebApplication app)
    {
        app.UseExceptionHandler(exceptionApp =>
        {
            exceptionApp.Run(async context =>
            {
                var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                var exception = feature?.Error;

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    type = "https://httpstatuses.com/500",
                    title = "Внутренняя ошибка сервера",
                    status = 500,
                    detail = exception?.Message ?? "Необработанная ошибка",
                    traceId = context.TraceIdentifier
                });
            });
        });

        app.UseSwagger();
        app.UseSwaggerUI();

        var api = app.MapGroup("/api/v1");

        api.MapGet("/status", () => Results.Ok(new
        {
            service = "AriaSignature",
            status = "Running",
            version = typeof(AriaApiExtensions).Assembly.GetName().Version?.ToString(3) ?? "0.0",
            timestampUtc = DateTimeOffset.UtcNow
        }))
        .WithName("GetSystemStatus")
        .WithOpenApi();

        api.MapGet("/settings", async (IAppSettingsService settings, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var dict = await settings.GetAllAsync(cancellationToken);
            var portStr = dict.GetValueOrDefault("Api:Port");
            var port = int.TryParse(portStr, out var p) ? p : configuration.GetValue("Api:Port", 5160);
            var cron = dict.GetValueOrDefault("SmartMonitoring:Cron") ?? configuration.GetValue<string>("SmartMonitoring:Cron") ?? "0 0 * * * ?";
            var outboundEnabled = ParseBool(dict.GetValueOrDefault(AppSettingsOutboundKeys.Enabled));
            var outboundUrl = dict.GetValueOrDefault(AppSettingsOutboundKeys.Url) ?? string.Empty;
            var outboundCron = dict.GetValueOrDefault(AppSettingsOutboundKeys.Cron)
                ?? configuration.GetValue<string>("OutboundSync:Cron")
                ?? "0 0/30 * * * ?";
            return Results.Ok(new
            {
                apiPort = port,
                smartMonitoringCron = cron,
                note = "Изменение порта API вступает в силу после перезапуска службы AriaSignatureService.",
                outboundSyncEnabled = outboundEnabled,
                outboundSyncUrl = outboundUrl,
                outboundSyncCron = outboundCron,
            });
        })
        .WithName("GetSettings")
        .WithOpenApi();

        api.MapGet("/system", async (ISystemInfoService systemInfo, CancellationToken cancellationToken) =>
            Results.Ok(await systemInfo.GetSnapshotAsync(cancellationToken)))
            .WithName("GetSystemInfo")
            .WithOpenApi();

        api.MapPut("/settings", async (
            UpdateAppSettingsRequest body,
            IAppSettingsService settings,
            ISmartRefreshCronApplier cronApplier,
            IOutboundSyncCronApplier outboundCronApplier,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var errors = new Dictionary<string, string[]>();
            var log = loggerFactory.CreateLogger("SettingsUpdate");

            var dict = await settings.GetAllAsync(cancellationToken);
            var curOutboundEnabled = ParseBool(dict.GetValueOrDefault(AppSettingsOutboundKeys.Enabled));
            var curOutboundUrl = dict.GetValueOrDefault(AppSettingsOutboundKeys.Url)?.Trim() ?? string.Empty;

            var nextOutboundEnabled = body.OutboundSyncEnabled ?? curOutboundEnabled;
            var nextOutboundUrl = body.OutboundSyncUrl != null ? body.OutboundSyncUrl.Trim() : curOutboundUrl;

            if (nextOutboundEnabled && string.IsNullOrWhiteSpace(nextOutboundUrl))
            {
                errors["outboundSyncUrl"] = ["При включённой синхронизации укажите полный URL (http/https)."];
            }

            if (body.OutboundSyncUrl is { Length: > 0 } rawUrl && !string.IsNullOrWhiteSpace(rawUrl))
            {
                if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    errors["outboundSyncUrl"] = ["URL должен быть абсолютным адресом со схемой http или https."];
                }
            }

            if (body.OutboundSyncCron is { } outboundCronRaw)
            {
                if (!CronExpression.IsValidExpression(outboundCronRaw.Trim()))
                {
                    errors["outboundSyncCron"] = ["Некорректное cron-выражение (Quartz)."];
                }
            }

            if (body.ApiPort is int ap)
            {
                if (ap is < 1 or > 65535)
                {
                    errors["apiPort"] = ["Порт должен быть в диапазоне 1–65535"];
                }
                else
                {
                    await settings.SetAsync("Api:Port", ap.ToString(), cancellationToken);
                }
            }

            if (body.SmartMonitoringCron is { } cron)
            {
                if (!CronExpression.IsValidExpression(cron))
                {
                    errors["smartMonitoringCron"] = ["Некорректное cron-выражение (Quartz)"];
                }
                else
                {
                    await settings.SetAsync("SmartMonitoring:Cron", cron, cancellationToken);
                    try
                    {
                        await cronApplier.ApplyCronAsync(cron, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex, "Не удалось перепланировать триггер обновления дисков в Quartz; значение сохранено в базе");
                    }
                }
            }

            var blockOutboundUrl = errors.ContainsKey("outboundSyncUrl");
            var blockOutboundCron = errors.ContainsKey("outboundSyncCron");

            if (!blockOutboundUrl && body.OutboundSyncEnabled is { } obEn)
            {
                await settings.SetAsync(AppSettingsOutboundKeys.Enabled, obEn ? "true" : "false", cancellationToken);
            }

            if (!blockOutboundUrl && body.OutboundSyncUrl is not null)
            {
                await settings.SetAsync(AppSettingsOutboundKeys.Url, body.OutboundSyncUrl.Trim(), cancellationToken);
            }

            if (!blockOutboundCron &&
                body.OutboundSyncCron is { } outboundCron &&
                CronExpression.IsValidExpression(outboundCron.Trim()))
            {
                var trimmed = outboundCron.Trim();
                await settings.SetAsync(AppSettingsOutboundKeys.Cron, trimmed, cancellationToken);
                try
                {
                    await outboundCronApplier.ApplyCronAsync(trimmed, cancellationToken);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Не удалось перепланировать исходящую синхронизацию в Quartz; значение сохранено в базе");
                }
            }

            return errors.Count > 0
                ? Results.ValidationProblem(errors)
                : Results.Ok(new { saved = true });
        })
        .WithName("UpdateSettings")
        .WithOpenApi();

        api.MapPost("/backups/test-mssql", async (MsSqlConnectionPayload payload, CancellationToken cancellationToken) =>
        {
            var payloadErrors = MsSqlConnectionStringBuilder.ValidatePayload(payload);
            if (payloadErrors is not null)
            {
                return Results.ValidationProblem(payloadErrors);
            }

            var cs = MsSqlConnectionStringBuilder.Build(payload);
            try
            {
                await using var connection = new SqlConnection(cs);
                await connection.OpenAsync(cancellationToken);
                await using var command = new SqlCommand("SELECT DB_NAME()", connection);
                var db = await command.ExecuteScalarAsync(cancellationToken);
                return Results.Ok(new
                {
                    ok = true,
                    message = "Подключение к Microsoft SQL Server установлено.",
                    database = db?.ToString()
                });
            }
            catch (Exception ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["connection"] = [$"Не удалось подключиться: {ex.Message}"]
                });
            }
        })
        .WithName("TestMsSqlConnection")
        .WithOpenApi();

        api.MapGet("/disks", async (IDiskTelemetryService telemetry, CancellationToken cancellationToken) =>
            Results.Ok(await telemetry.GetDisksAsync(cancellationToken)))
            .WithName("GetDisks")
            .WithOpenApi();

        api.MapPost("/disks/refresh", async (IDiskTelemetryService telemetry, CancellationToken cancellationToken) =>
            {
                await telemetry.RefreshAsync(cancellationToken);
                return Results.Ok(await telemetry.GetDisksAsync(cancellationToken));
            })
            .WithName("RefreshDisks")
            .WithOpenApi();

        api.MapGet("/disks/{id:guid}", async (Guid id, IDiskTelemetryService telemetry, CancellationToken cancellationToken) =>
        {
            var disk = await telemetry.GetDiskByIdAsync(id, cancellationToken);
            return disk is null
                ? Results.Problem(
                    title: "Диск не найден",
                    detail: $"Диск с идентификатором '{id}' не существует",
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(disk);
        })
            .WithName("GetDiskById")
            .WithOpenApi();

        api.MapGet("/disks/{id:guid}/smart", async (Guid id, IDiskTelemetryService telemetry, CancellationToken cancellationToken) =>
        {
            var disk = await telemetry.GetDiskByIdAsync(id, cancellationToken);
            if (disk is null)
            {
                return Results.Problem(
                    title: "Диск не найден",
                    detail: $"SMART-метрики недоступны, диск '{id}' не найден",
                    statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Ok(await telemetry.GetSmartMetricsAsync(id, cancellationToken));
        })
            .WithName("GetDiskSmart")
            .WithOpenApi();

        api.MapDelete("/disks/{id:guid}/smart", async (Guid id, IDiskTelemetryService telemetry, CancellationToken cancellationToken) =>
        {
            var disk = await telemetry.GetDiskByIdAsync(id, cancellationToken);
            if (disk is null)
            {
                return Results.Problem(
                    title: "Диск не найден",
                    detail: $"Очистка истории SMART невозможна: диск '{id}' не найден",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var deleted = await telemetry.ClearSmartMetricsAsync(id, cancellationToken);
            return Results.Ok(new { cleared = true, scope = "disk", diskId = id, deleted });
        })
            .WithName("ClearDiskSmart")
            .WithOpenApi();

        api.MapDelete("/disks/smart", async (IDiskTelemetryService telemetry, CancellationToken cancellationToken) =>
        {
            var deleted = await telemetry.ClearAllSmartMetricsAsync(cancellationToken);
            return Results.Ok(new { cleared = true, scope = "all", deleted });
        })
            .WithName("ClearAllSmart")
            .WithOpenApi();

        api.MapGet("/backups", async (IBackupService backups, CancellationToken cancellationToken) =>
            Results.Ok(await backups.GetJobsAsync(cancellationToken)))
            .WithName("GetBackups")
            .WithOpenApi();

        api.MapPost("/backups", async (UpsertBackupJobRequest request, IBackupService backups, CancellationToken cancellationToken) =>
        {
            var validationError = await ValidateBackupRequestAsync(request, existingForUpdate: null, cancellationToken);
            if (validationError is not null)
            {
                return Results.ValidationProblem(validationError);
            }

            var job = MapRequestToJob(request);
            var created = await backups.CreateJobAsync(job, cancellationToken);
            return Results.Created($"/api/v1/backups/{created.Id}", created);
        })
            .WithName("CreateBackup")
            .WithOpenApi();

        api.MapPut("/backups/{id:guid}", async (Guid id, UpsertBackupJobRequest request, IBackupService backups, CancellationToken cancellationToken) =>
        {
            var existing = await backups.GetJobAsync(id, cancellationToken);
            var validationError = await ValidateBackupRequestAsync(request, existingForUpdate: existing, cancellationToken);
            if (validationError is not null)
            {
                return Results.ValidationProblem(validationError);
            }

            var job = MapRequestToJob(request);
            var updated = await backups.UpdateJobAsync(id, job, cancellationToken);
            return updated is null
                ? Results.Problem(
                    title: "Задача архивации не найдена",
                    detail: $"Задача с идентификатором '{id}' не существует",
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(updated);
        })
            .WithName("UpdateBackup")
            .WithOpenApi();

        api.MapDelete("/backups/{id:guid}", async (Guid id, IBackupService backups, CancellationToken cancellationToken) =>
        {
            var deleted = await backups.DeleteJobAsync(id, cancellationToken);
            return deleted
                ? Results.NoContent()
                : Results.Problem(
                    title: "Задача архивации не найдена",
                    detail: $"Удаление невозможно: задача '{id}' не существует",
                    statusCode: StatusCodes.Status404NotFound);
        })
            .WithName("DeleteBackup")
            .WithOpenApi();

        api.MapPost("/backups/{id:guid}/run", async (Guid id, IBackupService backups, CancellationToken cancellationToken) =>
        {
            var log = await backups.RunJobAsync(id, cancellationToken);
            if (log.Status == BackupExecutionStatus.Failed && log.Message == "Задача архивации не найдена")
            {
                return Results.NotFound(log);
            }

            return Results.Accepted("/api/v1/backups/logs", log);
        })
            .WithName("RunBackup")
            .WithOpenApi();

        api.MapDelete("/backups/logs", async (IBackupService backups, CancellationToken cancellationToken) =>
        {
            var deleted = await backups.ClearAllLogsAsync(cancellationToken);
            return Results.Ok(new { cleared = true, scope = "backup-logs", deleted });
        })
            .WithName("ClearBackupLogs")
            .WithOpenApi();

        api.MapGet("/backups/logs", async (string? status, DateTimeOffset? from, DateTimeOffset? to, IBackupService backups, CancellationToken cancellationToken) =>
        {
            BackupExecutionStatus? st = null;
            if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BackupExecutionStatus>(status, true, out var parsed))
            {
                st = parsed;
            }

            return Results.Ok(await backups.GetLogsAsync(st, from, to, cancellationToken));
        })
            .WithName("GetBackupLogs")
            .WithOpenApi();

        TryUseSpaStaticFiles(app);

        return app;
    }

    private static bool ParseBool(string? value) =>
        bool.TryParse(value, out var b) && b;

    private static bool IsUnchangedFileSourceOnUpdate(string requestSource, BackupJob? existingForUpdate)
    {
        if (existingForUpdate is null || existingForUpdate.Type != BackupType.File)
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(requestSource.Trim()),
                Path.GetFullPath(existingForUpdate.Source.Trim()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(requestSource.Trim(), existingForUpdate.Source.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void TryUseSpaStaticFiles(WebApplication app)
    {
        var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!Directory.Exists(webRoot))
        {
            return;
        }

        app.Environment.WebRootPath = webRoot;
        var provider = new PhysicalFileProvider(webRoot);
        app.Environment.WebRootFileProvider = provider;

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = provider });
    }

    private static async Task<Dictionary<string, string[]>?> ValidateBackupRequestAsync(
        UpsertBackupJobRequest request,
        BackupJob? existingForUpdate,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Type == BackupType.MsSql && request.MsSql is not null)
        {
            var msSqlErrors = MsSqlConnectionStringBuilder.ValidatePayload(request.MsSql);
            if (msSqlErrors is not null)
            {
                foreach (var kv in msSqlErrors)
                {
                    errors[kv.Key] = kv.Value;
                }
            }
            else
            {
                request.Source = MsSqlConnectionStringBuilder.Build(request.MsSql);
            }
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Имя задачи обязательно"];
        }

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            errors["source"] = request.Type == BackupType.MsSql && request.MsSql is null
                ? ["Укажите параметры MSSQL или строку подключения в поле источник"]
                : ["Источник обязателен"];
        }
        else if (request.Type == BackupType.File && !Path.IsPathRooted(request.Source))
        {
            errors["source"] = ["Для файловой базы путь к .1CD должен быть абсолютным"];
        }
        else if (request.Type == BackupType.File &&
                 !File.Exists(request.Source) &&
                 !IsUnchangedFileSourceOnUpdate(request.Source, existingForUpdate))
        {
            errors["source"] = [$"Файл базы не найден: {request.Source}"];
        }
        else if (request.Type == BackupType.MsSql)
        {
            if (!request.Source.Contains("Server=", StringComparison.OrdinalIgnoreCase) &&
                !request.Source.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                errors["source"] = ["Для MsSql требуется строка подключения с сервером (Server или Data Source)"];
            }
            else if (!request.Source.Contains("Database=", StringComparison.OrdinalIgnoreCase) &&
                     !request.Source.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase))
            {
                errors["source"] = ["Для MsSql требуется Database или Initial Catalog"];
            }
            else if (!await CanConnectToMsSqlAsync(request.Source, cancellationToken))
            {
                errors["source"] = ["Подключение к MSSQL не установлено или база недоступна (проверьте сервер, имя БД и учётную запись службы Windows при Integrated Security)"];
            }
        }

        if (string.IsNullOrWhiteSpace(request.Destination))
        {
            errors["destination"] = ["Укажите папку для сохранения архивов"];
        }
        else if (!Path.IsPathRooted(request.Destination))
        {
            errors["destination"] = ["Путь к папке архивов должен быть абсолютным"];
        }
        else if (!CanAccessDestination(request.Destination, out var destinationError))
        {
            errors["destination"] = [destinationError];
        }
        else if (request.Type == BackupType.File &&
                 string.Equals(Path.GetFullPath(request.Source), Path.GetFullPath(request.Destination), StringComparison.OrdinalIgnoreCase))
        {
            errors["destination"] = ["Источник и папка назначения не должны совпадать"];
        }

        if (request.RetentionCount <= 0)
        {
            errors["retentionCount"] = ["Число хранимых копий должно быть больше 0"];
        }

        if (string.IsNullOrWhiteSpace(request.ScheduleCron) || !CronExpression.IsValidExpression(request.ScheduleCron))
        {
            errors["scheduleCron"] = ["Некорректное cron-выражение"];
        }

        return errors.Count > 0 ? errors : null;
    }

    private static bool CanAccessDestination(string destination, out string error)
    {
        try
        {
            Directory.CreateDirectory(destination);
            var probe = Path.Combine(destination, $".probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "probe");
            File.Delete(probe);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Нет доступа к папке: {ex.Message}";
            return false;
        }
    }

    private static async Task<bool> CanConnectToMsSqlAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT DB_NAME()", connection);
            var dbName = await command.ExecuteScalarAsync(cancellationToken);
            return dbName is not null and not DBNull;
        }
        catch
        {
            return false;
        }
    }

    private static BackupJob MapRequestToJob(UpsertBackupJobRequest request)
    {
        return new BackupJob
        {
            Name = request.Name,
            Type = request.Type,
            Source = request.Source,
            Destination = request.Destination,
            ScheduleCron = request.ScheduleCron,
            RetentionCount = request.RetentionCount,
            IsEnabled = request.IsEnabled
        };
    }
}
