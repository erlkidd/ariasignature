using AriaSignature.Application.Abstractions;
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
        return services;
    }
}
