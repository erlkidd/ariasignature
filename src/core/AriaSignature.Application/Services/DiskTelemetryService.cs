using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;

namespace AriaSignature.Application.Services;

public sealed class DiskTelemetryService : IDiskTelemetryService
{
    private readonly IDiskTelemetryCollector _collector;
    private readonly IDiskTelemetryRepository _repository;

    public DiskTelemetryService(IDiskTelemetryCollector collector, IDiskTelemetryRepository repository)
    {
        _collector = collector;
        _repository = repository;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var disks = await _collector.CollectAsync(cancellationToken);
        await _repository.UpsertDisksAsync(disks, cancellationToken);
    }

    public Task<IReadOnlyCollection<Disk>> GetDisksAsync(CancellationToken cancellationToken)
    {
        return _repository.GetDisksAsync(cancellationToken);
    }

    public Task<Disk?> GetDiskByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return _repository.GetDiskAsync(id, cancellationToken);
    }

    public Task<IReadOnlyCollection<SmartMetric>> GetSmartMetricsAsync(Guid id, CancellationToken cancellationToken)
    {
        return _repository.GetSmartMetricsAsync(id, cancellationToken);
    }

    public Task<int> ClearSmartMetricsAsync(Guid id, CancellationToken cancellationToken)
    {
        return _repository.ClearSmartMetricsAsync(id, cancellationToken);
    }

    public Task<int> ClearAllSmartMetricsAsync(CancellationToken cancellationToken)
    {
        return _repository.ClearAllSmartMetricsAsync(cancellationToken);
    }
}
