using AriaSignature.Domain.Entities;

namespace AriaSignature.Application.Abstractions;

public interface IDiskTelemetryService
{
    Task RefreshAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Disk>> GetDisksAsync(CancellationToken cancellationToken);
    Task<Disk?> GetDiskByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SmartMetric>> GetSmartMetricsAsync(Guid id, CancellationToken cancellationToken);
}
