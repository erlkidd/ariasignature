using AriaSignature.Application.Abstractions;
using AriaSignature.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AriaSignature.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IDiskTelemetryService, DiskTelemetryService>();
        services.AddSingleton<IBackupService, BackupService>();
        return services;
    }
}
