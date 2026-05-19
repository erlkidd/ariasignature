using AriaSignature.Application.Abstractions;
using AriaSignature.Infrastructure.Backup;
using AriaSignature.Infrastructure.Monitoring;
using AriaSignature.Infrastructure.Melezh;
using AriaSignature.Infrastructure.Persistence;
using AriaSignature.Infrastructure.Storage;
using AriaSignature.Infrastructure.SystemInfo;
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
            services.AddSingleton<ISystemInfoService, WindowsSystemInfoService>();
            services.AddSingleton<SmartCtlLowLevelReader>();
            services.AddSingleton<IDiskTelemetryCollector, WmiDiskTelemetryCollector>();
            services.AddSingleton<IMelezhStatusService, MelezhStatusService>();
        }
        else
        {
            services.AddSingleton<ISystemInfoService, NoopSystemInfoService>();
            services.AddSingleton<IDiskTelemetryCollector, NoopDiskTelemetryCollector>();
            services.AddSingleton<IMelezhStatusService, NoopMelezhStatusService>();
        }

        services.AddSingleton<IDiskTelemetryRepository, SqliteDiskTelemetryRepository>();
        services.AddSingleton<IAppSettingsService, SqliteAppSettingsService>();
        services.AddSingleton<IBackupJobRepository, SqliteBackupJobRepository>();
        services.AddSingleton<IBackupExecutor, BackupExecutor>();
        return services;
    }
}
