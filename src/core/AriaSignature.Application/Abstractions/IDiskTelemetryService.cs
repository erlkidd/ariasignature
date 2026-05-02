using AriaSignature.Domain.Entities;

namespace AriaSignature.Application.Abstractions;

public interface IDiskTelemetryService
{
    Task RefreshAsync(CancellationToken cancellationToken);

    /// <param name="appendHistory">Если false — только актуализация таблицы дисков без записи в историю SmartMetrics (прогрев при старте).</param>
    Task RefreshAsync(bool appendHistory, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Disk>> GetDisksAsync(CancellationToken cancellationToken);
    Task<Disk?> GetDiskByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SmartMetric>> GetSmartMetricsAsync(Guid id, CancellationToken cancellationToken);
    Task<int> ClearSmartMetricsAsync(Guid id, CancellationToken cancellationToken);
    Task<int> ClearAllSmartMetricsAsync(CancellationToken cancellationToken);
}
