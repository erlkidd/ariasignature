using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;

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

        api.MapGet("/backups", () => Results.Ok(Array.Empty<BackupJob>()))
            .WithName("GetBackups")
            .WithOpenApi();

        api.MapPost("/backups", (BackupJob job) => Results.Created($"/api/v1/backups/{job.Id}", job))
            .WithName("CreateBackup")
            .WithOpenApi();

        api.MapPut("/backups/{id:guid}", (Guid id, BackupJob job) => Results.Ok(new { id, job }))
            .WithName("UpdateBackup")
            .WithOpenApi();

        api.MapDelete("/backups/{id:guid}", (Guid id) => Results.NoContent())
            .WithName("DeleteBackup")
            .WithOpenApi();

        api.MapPost("/backups/{id:guid}/run", (Guid id) => Results.Accepted($"/api/v1/backups/logs", new { id, started = true }))
            .WithName("RunBackup")
            .WithOpenApi();

        api.MapGet("/backups/logs", () => Results.Ok(Array.Empty<BackupLog>()))
            .WithName("GetBackupLogs")
            .WithOpenApi();

        return app;
    }
}
