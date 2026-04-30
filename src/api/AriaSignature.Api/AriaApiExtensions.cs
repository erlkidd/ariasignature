using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;

namespace AriaSignature.Api;

public static class AriaApiExtensions
{
    public static IServiceCollection AddAriaApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }

    public static WebApplication UseAriaApi(this WebApplication app)
    {
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

        api.MapPost("/backups", async (BackupJob job, IBackupService backups, CancellationToken cancellationToken) =>
        {
            var created = await backups.CreateJobAsync(job, cancellationToken);
            return Results.Created($"/api/v1/backups/{created.Id}", created);
        })
            .WithName("CreateBackup")
            .WithOpenApi();

        api.MapPut("/backups/{id:guid}", async (Guid id, BackupJob job, IBackupService backups, CancellationToken cancellationToken) =>
        {
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
}
