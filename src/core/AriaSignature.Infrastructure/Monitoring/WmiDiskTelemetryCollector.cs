using System.Management;
using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;

namespace AriaSignature.Infrastructure.Monitoring;

public sealed class WmiDiskTelemetryCollector : IDiskTelemetryCollector
{
    public Task<IReadOnlyCollection<Disk>> CollectAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IReadOnlyCollection<Disk>>(Array.Empty<Disk>());
        }

        var freeByDeviceId = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .ToDictionary(d => d.Name.TrimEnd('\\'), d => (total: d.TotalSize, free: d.TotalFreeSpace));

        var disks = new List<Disk>();
        using var searcher = new ManagementObjectSearcher("SELECT Model, SerialNumber, InterfaceType, Size, DeviceID FROM Win32_DiskDrive");
        using var results = searcher.Get();

        foreach (var item in results.OfType<ManagementObject>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var size = TryParseLong(item["Size"]);
            var model = item["Model"]?.ToString()?.Trim() ?? "Unknown";
            var serial = item["SerialNumber"]?.ToString()?.Trim() ?? string.Empty;
            var iface = item["InterfaceType"]?.ToString()?.Trim() ?? "Unknown";
            var diskId = CreateStableId(model, serial, iface);

            // WMI SMART fields are vendor-specific; baseline defaults are conservative.
            var reallocated = 0;
            var pending = 0;
            var uncorrectable = 0;
            var health = EstimateHealth(reallocated, pending, uncorrectable);
            var status = CalculateStatus(reallocated, pending, uncorrectable);

            var driveAggregate = freeByDeviceId.Values.Aggregate((total: 0L, free: 0L), (acc, cur) =>
                (acc.total + cur.total, acc.free + cur.free));

            disks.Add(new Disk
            {
                Id = diskId,
                Model = model,
                Serial = serial,
                Interface = iface,
                SizeTotalBytes = size > 0 ? size : driveAggregate.total,
                SizeFreeBytes = driveAggregate.free,
                TemperatureCelsius = 0,
                HealthPercent = health,
                PowerOnHours = 0,
                PowerCycleCount = 0,
                ReallocatedSectors = reallocated,
                PendingSectors = pending,
                UncorrectableErrors = uncorrectable,
                Status = status,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        return Task.FromResult<IReadOnlyCollection<Disk>>(disks);
    }

    private static Guid CreateStableId(string model, string serial, string iface)
    {
        var raw = $"{model}|{serial}|{iface}";
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return new Guid(bytes);
    }

    private static long TryParseLong(object? value)
    {
        return long.TryParse(value?.ToString(), out var parsed) ? parsed : 0;
    }

    private static int EstimateHealth(int reallocated, int pending, int uncorrectable)
    {
        var penalty = (reallocated * 2) + (pending * 5) + (uncorrectable * 8);
        return Math.Clamp(100 - penalty, 0, 100);
    }

    private static DiskHealthStatus CalculateStatus(int reallocated, int pending, int uncorrectable)
    {
        if (uncorrectable > 0 || pending > 50)
        {
            return DiskHealthStatus.Critical;
        }

        if (reallocated > 0 || pending > 0)
        {
            return DiskHealthStatus.Warning;
        }

        return DiskHealthStatus.Ok;
    }
}
