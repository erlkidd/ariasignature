using AriaSignature.Domain.Entities;

namespace AriaSignature.Application.Abstractions;

public interface IDiskTelemetryCollector
{
    Task<IReadOnlyCollection<Disk>> CollectAsync(CancellationToken cancellationToken);
}
