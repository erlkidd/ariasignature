using System.IO;
using System.Management;
using System.Runtime.Versioning;
using AriaSignature.Application.Abstractions;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AriaSignature.Infrastructure.Monitoring;

[SupportedOSPlatform("windows")]
public sealed class WmiDiskTelemetryCollector : IDiskTelemetryCollector
{
    private readonly ILogger<WmiDiskTelemetryCollector> _logger;

    public WmiDiskTelemetryCollector(ILogger<WmiDiskTelemetryCollector> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyCollection<Disk>> CollectAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IReadOnlyCollection<Disk>>(Array.Empty<Disk>());
        }

        var smartSnapshot = ReadSmartSnapshotSafe();
        var disks = new List<Disk>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Model, SerialNumber, InterfaceType, Size, DeviceID, MediaType, PNPDeviceID FROM Win32_DiskDrive");
            using var results = searcher.Get();

            foreach (var item in results.OfType<ManagementObject>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using (item)
                    {
                        var size = TryParseLong(item["Size"]);
                        var model = item["Model"]?.ToString()?.Trim() ?? "Unknown";
                        var serial = item["SerialNumber"]?.ToString()?.Trim() ?? string.Empty;
                        var mediaType = item["MediaType"]?.ToString()?.Trim() ?? string.Empty;
                        var iface = ResolveInterfaceType(item);
                        var diskId = CreateStableId(model, serial, iface);

                        var diskCapacity = ResolvePhysicalDiskCapacitySafe(item, size);
                        var smart = ResolveSmartAttributes(smartSnapshot, model, serial);

                        var reallocated = smart.ReallocatedSectors;
                        var pending = smart.PendingSectors;
                        var uncorrectable = smart.UncorrectableErrors;
                        var ssdLife = smart.SsdLifeRemainingPercent;
                        var health = EstimateHealth(reallocated, pending, uncorrectable, ssdLife);
                        var status = CalculateStatus(reallocated, pending, uncorrectable, ssdLife);

                        disks.Add(new Disk
                        {
                            Id = diskId,
                            Model = model,
                            Serial = serial,
                            Interface = iface,
                            MediaType = string.IsNullOrWhiteSpace(mediaType) ? InferMediaType(iface, model) : mediaType,
                            SizeTotalBytes = diskCapacity.total > 0 ? diskCapacity.total : size,
                            SizeFreeBytes = diskCapacity.free,
                            SsdLifeRemainingPercent = ssdLife,
                            TemperatureCelsius = smart.TemperatureCelsius,
                            HealthPercent = health,
                            PowerOnHours = smart.PowerOnHours,
                            PowerCycleCount = smart.PowerCycleCount,
                            ReallocatedSectors = reallocated,
                            PendingSectors = pending,
                            UncorrectableErrors = uncorrectable,
                            Status = status,
                            UpdatedAtUtc = DateTimeOffset.UtcNow
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "WMI Win32_DiskDrive: не удалось прочитать один из накопителей, пропуск.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI Win32_DiskDrive: общий сбой запроса, будет использован запасной способ.");
        }

        if (disks.Count == 0)
        {
            AppendLogicalDriveFallback(disks, smartSnapshot, cancellationToken);
        }

        return Task.FromResult<IReadOnlyCollection<Disk>>(disks);
    }

    private void AppendLogicalDriveFallback(
        List<Disk> disks,
        Dictionary<string, SmartAttributes> smartSnapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                try
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }

                    var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name.TrimEnd('\\') : drive.VolumeLabel.Trim();
                    var root = drive.RootDirectory.FullName.TrimEnd('\\');
                    var fmt = string.IsNullOrWhiteSpace(drive.DriveFormat) ? "том" : drive.DriveFormat;
                    var model = $"{root} ({fmt})";
                    var iface = "Логический том";
                    var diskId = CreateStableId(model, root, iface);
                    var smart = ResolveSmartAttributes(smartSnapshot, model, label);
                    var reallocated = smart.ReallocatedSectors;
                    var pending = smart.PendingSectors;
                    var uncorrectable = smart.UncorrectableErrors;
                    var ssdLife = smart.SsdLifeRemainingPercent;
                    var health = EstimateHealth(reallocated, pending, uncorrectable, ssdLife);
                    var status = CalculateStatus(reallocated, pending, uncorrectable, ssdLife);

                    disks.Add(new Disk
                    {
                        Id = diskId,
                        Model = model,
                        Serial = label,
                        Interface = iface,
                        MediaType = InferMediaType(iface, model),
                        SizeTotalBytes = drive.TotalSize,
                        SizeFreeBytes = drive.AvailableFreeSpace,
                        SsdLifeRemainingPercent = ssdLife,
                        TemperatureCelsius = smart.TemperatureCelsius,
                        HealthPercent = health,
                        PowerOnHours = smart.PowerOnHours,
                        PowerCycleCount = smart.PowerCycleCount,
                        ReallocatedSectors = reallocated,
                        PendingSectors = pending,
                        UncorrectableErrors = uncorrectable,
                        Status = status,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Запасной обход: пропуск тома {Drive}", drive.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Запасной перечень логических томов не удался.");
        }
    }

    private static string InferMediaType(string iface, string model)
    {
        var m = model.ToUpperInvariant();
        if (m.Contains("SSD", StringComparison.Ordinal) || m.Contains("NVME", StringComparison.Ordinal))
        {
            return "SSD";
        }

        return iface.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ? "SSD" : "HDD";
    }

    private static string ResolveInterfaceType(ManagementObject disk)
    {
        var pnp = disk["PNPDeviceID"]?.ToString() ?? string.Empty;
        if (pnp.Contains("USB", StringComparison.OrdinalIgnoreCase))
        {
            return "USB";
        }

        if (pnp.Contains("NVME", StringComparison.OrdinalIgnoreCase) ||
            pnp.Contains("VEN_144D", StringComparison.OrdinalIgnoreCase))
        {
            return "NVMe";
        }

        var wmiIface = disk["InterfaceType"]?.ToString()?.Trim() ?? "Unknown";
        return wmiIface.ToUpperInvariant() switch
        {
            "IDE" => "SATA",
            "SCSI" => "SATA/SCSI",
            _ => wmiIface
        };
    }

    private (long total, long free) ResolvePhysicalDiskCapacitySafe(ManagementObject diskDrive, long fallbackTotal)
    {
        try
        {
            var total = 0L;
            var free = 0L;

            foreach (var partitionObject in diskDrive.GetRelated("Win32_DiskPartition").OfType<ManagementObject>())
            {
                using (partitionObject)
                {
                    foreach (var logicalDisk in partitionObject.GetRelated("Win32_LogicalDisk").OfType<ManagementObject>())
                    {
                        using (logicalDisk)
                        {
                            total += TryParseLong(logicalDisk["Size"]);
                            free += TryParseLong(logicalDisk["FreeSpace"]);
                        }
                    }
                }
            }

            if (total == 0)
            {
                total = fallbackTotal;
            }

            return (total, free);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WMI: не удалось сопоставить разделы накопителю, используется ёмкость из Win32_DiskDrive.");
            return (fallbackTotal, 0);
        }
    }

    private Dictionary<string, SmartAttributes> ReadSmartSnapshotSafe()
    {
        try
        {
            return ReadSmartSnapshot();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "WMI root\\MSStorageDriver: SMART недоступен. Показываются объёмы и модель; детальные атрибуты SMART могут отсутствовать.");
            return new Dictionary<string, SmartAttributes>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, SmartAttributes> ReadSmartSnapshot()
    {
        var snapshot = new Dictionary<string, SmartAttributes>(StringComparer.OrdinalIgnoreCase);

        using var statusSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus");
        using var dataSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_ATAPISmartData");

        var statusByInstance = statusSearcher.Get()
            .OfType<ManagementObject>()
            .ToDictionary(
                s => NormalizeInstanceName(s["InstanceName"]?.ToString()),
                s => s["PredictFailure"] is true,
                StringComparer.OrdinalIgnoreCase);

        foreach (var row in dataSearcher.Get().OfType<ManagementObject>())
        {
            using (row)
            {
                var instance = NormalizeInstanceName(row["InstanceName"]?.ToString());
                var vendorSpecific = row["VendorSpecific"] as byte[] ?? Array.Empty<byte>();
                var parsed = ParseAtaSmartAttributes(vendorSpecific);
                if (statusByInstance.TryGetValue(instance, out var predictFailure) && predictFailure)
                {
                    parsed.PendingSectors = Math.Max(parsed.PendingSectors, 1);
                }

                snapshot[instance] = parsed;
            }
        }

        return snapshot;
    }

    private static SmartAttributes ResolveSmartAttributes(Dictionary<string, SmartAttributes> snapshot, string model, string serial)
    {
        var match = snapshot.FirstOrDefault(pair =>
            pair.Key.Contains(model, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(serial) && pair.Key.Contains(serial, StringComparison.OrdinalIgnoreCase)));

        return match.Equals(default(KeyValuePair<string, SmartAttributes>))
            ? SmartAttributes.Empty
            : match.Value;
    }

    private static SmartAttributes ParseAtaSmartAttributes(byte[] data)
    {
        if (data.Length < 362)
        {
            return new SmartAttributes();
        }

        var attributes = new SmartAttributes();
        for (var i = 2; i + 11 < 362; i += 12)
        {
            var id = data[i];
            if (id == 0)
            {
                continue;
            }

            var raw = BitConverter.ToInt64([data[i + 5], data[i + 6], data[i + 7], data[i + 8], data[i + 9], data[i + 10], 0, 0], 0);
            switch (id)
            {
                case 5:
                    attributes.ReallocatedSectors = (int)raw;
                    break;
                case 9:
                    attributes.PowerOnHours = raw;
                    break;
                case 12:
                    attributes.PowerCycleCount = raw;
                    break;
                case 194:
                    attributes.TemperatureCelsius = (int)Math.Clamp(raw & 0xFF, 0, 120);
                    break;
                case 197:
                    attributes.PendingSectors = (int)raw;
                    break;
                case 198:
                    attributes.UncorrectableErrors = (int)raw;
                    break;
                case 231:
                    attributes.SsdLifeRemainingPercent = (int)Math.Clamp(raw & 0xFF, 0, 100);
                    break;
                case 233:
                    if (attributes.SsdLifeRemainingPercent is null)
                    {
                        var wear = (int)Math.Clamp((raw >> 16) & 0xFF, 0, 100);
                        if (wear > 0)
                        {
                            attributes.SsdLifeRemainingPercent = wear;
                        }
                    }

                    break;
            }
        }

        return attributes;
    }

    private static string NormalizeInstanceName(string? value)
    {
        return (value ?? string.Empty).Replace("_0", string.Empty, StringComparison.OrdinalIgnoreCase);
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

    private static int EstimateHealth(int reallocated, int pending, int uncorrectable, int? ssdLife)
    {
        var penalty = (reallocated * 2) + (pending * 5) + (uncorrectable * 8);
        var baseHealth = Math.Clamp(100 - penalty, 0, 100);
        if (ssdLife is int life)
        {
            baseHealth = Math.Min(baseHealth, life);
        }

        return baseHealth;
    }

    private static DiskHealthStatus CalculateStatus(int reallocated, int pending, int uncorrectable, int? ssdLife)
    {
        if (uncorrectable > 0 || pending > 50)
        {
            return DiskHealthStatus.Critical;
        }

        if (ssdLife is <= 10)
        {
            return DiskHealthStatus.Critical;
        }

        if (ssdLife is <= 20)
        {
            return DiskHealthStatus.Warning;
        }

        if (reallocated > 0 || pending > 0)
        {
            return DiskHealthStatus.Warning;
        }

        return DiskHealthStatus.Ok;
    }

    private sealed record SmartAttributes
    {
        public static readonly SmartAttributes Empty = new();
        public int TemperatureCelsius { get; set; }
        public long PowerOnHours { get; set; }
        public long PowerCycleCount { get; set; }
        public int ReallocatedSectors { get; set; }
        public int PendingSectors { get; set; }
        public int UncorrectableErrors { get; set; }
        public int? SsdLifeRemainingPercent { get; set; }
    }
}
