using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;

namespace AriaSignature.Infrastructure.Monitoring;

public sealed class NoopDiskTelemetryCollector : IDiskTelemetryCollector
{
    public Task<IReadOnlyCollection<Disk>> CollectAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyCollection<Disk>>(Array.Empty<Disk>());
    }
}
