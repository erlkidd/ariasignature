using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;

namespace AriaSignature.Infrastructure.Storage;

public sealed class InMemoryDiskTelemetryRepository : IDiskTelemetryRepository
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Disk> _disks = [];
    private readonly Dictionary<Guid, List<SmartMetric>> _smartByDisk = [];

    public Task UpsertDisksAsync(IReadOnlyCollection<Disk> disks, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            foreach (var disk in disks)
            {
                _disks[disk.Id] = disk;

                if (!_smartByDisk.TryGetValue(disk.Id, out var metrics))
                {
                    metrics = [];
                    _smartByDisk[disk.Id] = metrics;
                }

                metrics.Add(new SmartMetric
                {
                    DiskId = disk.Id,
                    TemperatureCelsius = disk.TemperatureCelsius,
                    HealthPercent = disk.HealthPercent,
                    ReallocatedSectors = disk.ReallocatedSectors,
                    PendingSectors = disk.PendingSectors,
                    UncorrectableErrors = disk.UncorrectableErrors,
                    Status = disk.Status,
                    TimestampUtc = disk.UpdatedAtUtc
                });

                if (metrics.Count > 500)
                {
                    metrics.RemoveRange(0, metrics.Count - 500);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<Disk>> GetDisksAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult<IReadOnlyCollection<Disk>>(_disks.Values.ToArray());
        }
    }

    public Task<Disk?> GetDiskAsync(Guid diskId, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _disks.TryGetValue(diskId, out var disk);
            return Task.FromResult(disk);
        }
    }

    public Task<IReadOnlyCollection<SmartMetric>> GetSmartMetricsAsync(Guid diskId, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (!_smartByDisk.TryGetValue(diskId, out var metrics))
            {
                return Task.FromResult<IReadOnlyCollection<SmartMetric>>(Array.Empty<SmartMetric>());
            }

            return Task.FromResult<IReadOnlyCollection<SmartMetric>>(metrics.ToArray());
        }
    }
}
