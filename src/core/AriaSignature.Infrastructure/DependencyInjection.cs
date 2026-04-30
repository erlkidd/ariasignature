using AriaSignature.Application.Abstractions;
using AriaSignature.Infrastructure.Backup;
using AriaSignature.Infrastructure.Monitoring;
using AriaSignature.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AriaSignature.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDiskTelemetryCollector, WmiDiskTelemetryCollector>();
        services.AddSingleton<IDiskTelemetryRepository, InMemoryDiskTelemetryRepository>();
        services.AddSingleton<IBackupJobRepository, InMemoryBackupJobRepository>();
        services.AddSingleton<IBackupExecutor, BackupExecutor>();
        return services;
    }
}
