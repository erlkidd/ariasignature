using AriaSignature.Application.Abstractions;
using AriaSignature.Infrastructure.Backup;
using AriaSignature.Infrastructure.Monitoring;
using AriaSignature.Infrastructure.Persistence;
using AriaSignature.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AriaSignature.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<ISqliteDatabaseInitializer, SqliteDatabaseInitializer>();
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IDiskTelemetryCollector, WmiDiskTelemetryCollector>();
        }
        else
        {
            services.AddSingleton<IDiskTelemetryCollector, NoopDiskTelemetryCollector>();
        }

        services.AddSingleton<IDiskTelemetryRepository, SqliteDiskTelemetryRepository>();
        services.AddSingleton<IAppSettingsService, SqliteAppSettingsService>();
        services.AddSingleton<IBackupJobRepository, SqliteBackupJobRepository>();
        services.AddSingleton<IBackupExecutor, BackupExecutor>();
        return services;
    }
}
