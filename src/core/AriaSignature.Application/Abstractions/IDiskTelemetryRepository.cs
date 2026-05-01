using AriaSignature.Domain.Entities;

namespace AriaSignature.Application.Abstractions;

public interface IDiskTelemetryRepository
{
    Task UpsertDisksAsync(IReadOnlyCollection<Disk> disks, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Disk>> GetDisksAsync(CancellationToken cancellationToken);
    Task<Disk?> GetDiskAsync(Guid diskId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SmartMetric>> GetSmartMetricsAsync(Guid diskId, CancellationToken cancellationToken);
    Task<int> ClearSmartMetricsAsync(Guid diskId, CancellationToken cancellationToken);
    Task<int> ClearAllSmartMetricsAsync(CancellationToken cancellationToken);
}
