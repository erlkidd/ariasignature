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
        services.AddSingleton<IDiskTelemetryCollector, WmiDiskTelemetryCollector>();
        services.AddSingleton<IDiskTelemetryRepository, SqliteDiskTelemetryRepository>();
        services.AddSingleton<IBackupJobRepository, SqliteBackupJobRepository>();
        services.AddSingleton<IBackupExecutor, BackupExecutor>();
        return services;
    }
}
