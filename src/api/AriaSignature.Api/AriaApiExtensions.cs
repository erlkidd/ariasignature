using AriaSignature.Application.Abstractions;
using AriaSignature.Api.Contracts;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;
using Quartz;

namespace AriaSignature.Api;

public static class AriaApiExtensions
{
    public static IServiceCollection AddAriaApi(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
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
            timestampUtc = DateTimeOffset.UtcNow
        }))
        .WithName("GetSystemStatus")
        .WithOpenApi();

        api.MapGet("/disks", async (IDiskTelemetryService telemetry, CancellationToken cancellationToken) =>
            Results.Ok(await telemetry.GetDisksAsync(cancellationToken)))
            .WithName("GetDisks")
            .WithOpenApi();

        api.MapGet("/disks/{id:guid}", async (Guid id, IDiskTelemetryService telemetry, CancellationToken cancellationToken) =>
        {
            var disk = await telemetry.GetDiskByIdAsync(id, cancellationToken);
            return disk is null ? Results.NotFound() : Results.Ok(disk);
        })
            .WithName("GetDiskById")
            .WithOpenApi();

        api.MapGet("/disks/{id:guid}/smart", async (Guid id, IDiskTelemetryService telemetry, CancellationToken cancellationToken) =>
            Results.Ok(await telemetry.GetSmartMetricsAsync(id, cancellationToken)))
            .WithName("GetDiskSmart")
            .WithOpenApi();

        api.MapGet("/backups", async (IBackupService backups, CancellationToken cancellationToken) =>
            Results.Ok(await backups.GetJobsAsync(cancellationToken)))
            .WithName("GetBackups")
            .WithOpenApi();

        api.MapPost("/backups", async (UpsertBackupJobRequest request, IBackupService backups, CancellationToken cancellationToken) =>
        {
            var validationError = ValidateBackupRequest(request);
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
            var validationError = ValidateBackupRequest(request);
            if (validationError is not null)
            {
                return Results.ValidationProblem(validationError);
            }

            var job = MapRequestToJob(request);
            var updated = await backups.UpdateJobAsync(id, job, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
            .WithName("UpdateBackup")
            .WithOpenApi();

        api.MapDelete("/backups/{id:guid}", async (Guid id, IBackupService backups, CancellationToken cancellationToken) =>
        {
            var deleted = await backups.DeleteJobAsync(id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
            .WithName("DeleteBackup")
            .WithOpenApi();

        api.MapPost("/backups/{id:guid}/run", async (Guid id, IBackupService backups, CancellationToken cancellationToken) =>
        {
            var log = await backups.RunJobAsync(id, cancellationToken);
            if (log.Status == BackupExecutionStatus.Failed && log.Message == "Backup job not found")
            {
                return Results.NotFound(log);
            }

            return Results.Accepted($"/api/v1/backups/logs", log);
        })
            .WithName("RunBackup")
            .WithOpenApi();

        api.MapGet("/backups/logs", async (IBackupService backups, CancellationToken cancellationToken) =>
            Results.Ok(await backups.GetLogsAsync(cancellationToken)))
            .WithName("GetBackupLogs")
            .WithOpenApi();

        return app;
    }

    private static Dictionary<string, string[]>? ValidateBackupRequest(UpsertBackupJobRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Имя задачи обязательно"];
        }

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            errors["source"] = ["Источник обязателен"];
        }

        if (string.IsNullOrWhiteSpace(request.Destination))
        {
            errors["destination"] = ["Назначение обязательно"];
        }

        if (request.RetentionCount <= 0)
        {
            errors["retentionCount"] = ["RetentionCount должен быть больше 0"];
        }

        if (string.IsNullOrWhiteSpace(request.ScheduleCron) || !CronExpression.IsValidExpression(request.ScheduleCron))
        {
            errors["scheduleCron"] = ["Некорректное cron-выражение"];
        }

        return errors.Count > 0 ? errors : null;
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
