using System.Globalization;
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
    private readonly SmartCtlLowLevelReader _smartCtl;

    public WmiDiskTelemetryCollector(ILogger<WmiDiskTelemetryCollector> logger, SmartCtlLowLevelReader smartCtl)
    {
        _logger = logger;
        _smartCtl = smartCtl;
    }

    public Task<IReadOnlyCollection<Disk>> CollectAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IReadOnlyCollection<Disk>>(Array.Empty<Disk>());
        }

        var smartSnapshot = ReadSmartSnapshotSafe();
        var smartByDriveIndex = BuildSmartByPhysicalDriveIndex(smartSnapshot);
        var storageReliability = ReadStorageReliabilityByPhysicalDriveIndexSafe();
        var smartCtlData = _smartCtl.Read();
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
                        var physicalIndex = TryParsePhysicalDriveIndex(item["DeviceID"]?.ToString());
                        var pnp = item["PNPDeviceID"]?.ToString() ?? string.Empty;

                        var diskCapacity = ResolvePhysicalDiskCapacitySafe(item, size);
                        var smart = ResolveSmartForDisk(smartSnapshot, smartByDriveIndex, physicalIndex, model, serial, pnp);
                        MergeStorageReliability(physicalIndex, storageReliability, smart);
                        MergeSmartCtl(physicalIndex, model, serial, smartCtlData, smart);

                        var reallocated = smart.ReallocatedSectors;
                        var pending = smart.PendingSectors;
                        var uncorrectable = smart.UncorrectableErrors;
                        var ssdLife = smart.SsdLifeRemainingPercent;
                        var hadTelemetry = HasTelemetrySignal(smart) || smart.PredictFailure;
                        var health = EstimateHealthPercent(reallocated, pending, uncorrectable, ssdLife, hadTelemetry, smart.PredictFailure);
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
                    var smart = CopySmartAttributes(ResolveSmartAttributes(smartSnapshot, model, label));
                    var reallocated = smart.ReallocatedSectors;
                    var pending = smart.PendingSectors;
                    var uncorrectable = smart.UncorrectableErrors;
                    var ssdLife = smart.SsdLifeRemainingPercent;
                    var hadTelemetry = HasTelemetrySignal(smart) || smart.PredictFailure;
                    var health = EstimateHealthPercent(reallocated, pending, uncorrectable, ssdLife, hadTelemetry, smart.PredictFailure);
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

    /// <summary>
    /// Счётчики из root\Microsoft\Windows\Storage (как у Get-StorageReliabilityCounter): износ SSD (Wear), температура.
    /// Для многих NVMe/SSD даёт достовернее оценку, чем только MSStorageDriver ATAPI SMART.
    /// </summary>
    private Dictionary<int, SmartAttributes> ReadStorageReliabilityByPhysicalDriveIndexSafe()
    {
        var map = new Dictionary<int, SmartAttributes>();
        try
        {
            var diskOidByIndex = ReadMsftPhysicalDiskObjectIdsByIndex();
            const string ns = @"root\Microsoft\Windows\Storage";
            using var searcher = new ManagementObjectSearcher(ns, "SELECT * FROM MSFT_StorageReliabilityCounter");
            using var results = searcher.Get();
            foreach (ManagementObject row in results)
            {
                using (row)
                {
                    var oid = row["ObjectId"]?.ToString() ?? string.Empty;
                    var idx = TryParsePhysicalDriveIndex(oid);
                    if (idx is null)
                    {
                        foreach (var kv in diskOidByIndex)
                        {
                            if (oid.Contains($"PhysicalDrive{kv.Key}", StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(kv.Value) && oid.StartsWith(kv.Value, StringComparison.Ordinal)))
                            {
                                idx = kv.Key;
                                break;
                            }
                        }
                    }

                    if (idx is not int driveIdx)
                    {
                        continue;
                    }

                    if (!map.TryGetValue(driveIdx, out var agg))
                    {
                        agg = new SmartAttributes();
                        map[driveIdx] = agg;
                    }

                    var wear = TryGetUInt16(row["Wear"]);
                    if (wear is ushort wWear && wWear is > 0 and <= 100)
                    {
                        var remaining = (int)Math.Clamp(100 - wWear, 0, 100);
                        agg.SsdLifeRemainingPercent = agg.SsdLifeRemainingPercent is int prev
                            ? Math.Min(prev, remaining)
                            : remaining;
                    }

                    var temp = TryGetUInt16(row["Temperature"]);
                    var normalizedTemp = NormalizeStorageTemperature(temp);
                    if (normalizedTemp is int tC && tC is > 0 and < 125)
                    {
                        agg.TemperatureCelsius = Math.Max(agg.TemperatureCelsius, tC);
                    }

                    var cycles = TryGetUInt64(row["LoadUnloadCycleCount"]);
                    if (cycles is ulong lc && lc > 0)
                    {
                        var c = (long)Math.Min(lc, long.MaxValue);
                        agg.PowerCycleCount = Math.Max(agg.PowerCycleCount, c);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WMI MSFT_StorageReliabilityCounter недоступен (возможна старая ОС или политика).");
        }

        return map;
    }

    private Dictionary<int, string> ReadMsftPhysicalDiskObjectIdsByIndex()
    {
        var map = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT DeviceId, ObjectId FROM MSFT_PhysicalDisk");
            using var results = searcher.Get();
            foreach (ManagementObject row in results)
            {
                using (row)
                {
                    var dev = row["DeviceId"]?.ToString();
                    var objectId = row["ObjectId"]?.ToString();
                    var idx = TryParsePhysicalDriveIndex(dev);
                    if (idx is int i && !string.IsNullOrEmpty(objectId))
                    {
                        map[i] = objectId;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WMI MSFT_PhysicalDisk недоступен.");
        }

        return map;
    }

    private static Dictionary<int, SmartAttributes> BuildSmartByPhysicalDriveIndex(Dictionary<string, SmartAttributes> snapshot)
    {
        var map = new Dictionary<int, SmartAttributes>();
        foreach (var kv in snapshot)
        {
            var idx = TryParsePhysicalDriveIndex(kv.Key);
            if (idx is not int n)
            {
                continue;
            }

            if (!map.TryGetValue(n, out var agg))
            {
                map[n] = CopySmartAttributes(kv.Value);
            }
            else
            {
                map[n] = MergeSmart(agg, kv.Value);
            }
        }

        return map;
    }

    private static SmartAttributes ResolveSmartForDisk(
        Dictionary<string, SmartAttributes> snapshot,
        Dictionary<int, SmartAttributes> byDriveIndex,
        int? physicalIndex,
        string model,
        string serial,
        string pnpDeviceId)
    {
        SmartAttributes smart;
        if (physicalIndex is int pi && byDriveIndex.TryGetValue(pi, out var direct))
        {
            smart = CopySmartAttributes(direct);
        }
        else
        {
            smart = CopySmartAttributes(ResolveSmartAttributes(snapshot, model, serial));
        }

        if (!HasTelemetrySignal(smart) && !smart.PredictFailure && !string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            var frag = pnpDeviceId.Replace("\\", "#", StringComparison.Ordinal);
            foreach (var kv in snapshot)
            {
                if (kv.Key.Contains(frag, StringComparison.OrdinalIgnoreCase) ||
                    pnpDeviceId.Contains(kv.Key.Replace('#', '\\'), StringComparison.OrdinalIgnoreCase))
                {
                    smart = MergeSmart(smart, kv.Value);
                    break;
                }
            }
        }

        return smart;
    }

    private static SmartAttributes MergeSmart(SmartAttributes a, SmartAttributes b) =>
        new()
        {
            TemperatureCelsius = Math.Max(a.TemperatureCelsius, b.TemperatureCelsius),
            PowerOnHours = Math.Max(a.PowerOnHours, b.PowerOnHours),
            PowerCycleCount = Math.Max(a.PowerCycleCount, b.PowerCycleCount),
            ReallocatedSectors = Math.Max(a.ReallocatedSectors, b.ReallocatedSectors),
            PendingSectors = Math.Max(a.PendingSectors, b.PendingSectors),
            UncorrectableErrors = Math.Max(a.UncorrectableErrors, b.UncorrectableErrors),
            SsdLifeRemainingPercent = a.SsdLifeRemainingPercent is int la && b.SsdLifeRemainingPercent is int lb
                ? Math.Min(la, lb)
                : a.SsdLifeRemainingPercent ?? b.SsdLifeRemainingPercent,
            PredictFailure = a.PredictFailure || b.PredictFailure
        };

    private static bool HasTelemetrySignal(SmartAttributes s) =>
        s.TemperatureCelsius > 0 ||
        s.PowerOnHours > 0 ||
        s.ReallocatedSectors > 0 ||
        s.PendingSectors > 0 ||
        s.UncorrectableErrors > 0 ||
        s.SsdLifeRemainingPercent is > 0;

    private static void MergeStorageReliability(int? physicalIndex, Dictionary<int, SmartAttributes> storage, SmartAttributes target)
    {
        if (physicalIndex is not int idx || !storage.TryGetValue(idx, out var extra))
        {
            return;
        }

        if (extra.TemperatureCelsius > 0)
        {
            target.TemperatureCelsius = Math.Max(target.TemperatureCelsius, extra.TemperatureCelsius);
        }

        if (extra.SsdLifeRemainingPercent is int storageLife)
        {
            target.SsdLifeRemainingPercent = target.SsdLifeRemainingPercent is int smartLife
                ? Math.Min(smartLife, storageLife)
                : storageLife;
        }

        if (extra.PowerCycleCount > target.PowerCycleCount)
        {
            target.PowerCycleCount = extra.PowerCycleCount;
        }
    }

    private static void MergeSmartCtl(
        int? physicalIndex,
        string model,
        string serial,
        SmartCtlLowLevelReader.ReadResult smartCtlData,
        SmartAttributes target)
    {
        SmartCtlLowLevelReader.Snapshot? src = null;

        if (physicalIndex is int idx && smartCtlData.ByPhysicalIndex.TryGetValue(idx, out var byIdx))
        {
            src = byIdx;
        }

        if (src is null)
        {
            var key = SmartCtlLowLevelReader.BuildIdentityKey(model, serial);
            smartCtlData.ByIdentity.TryGetValue(key, out src);
        }

        if (src is null)
        {
            return;
        }

        if (src.TemperatureCelsius > 0)
        {
            target.TemperatureCelsius = Math.Max(target.TemperatureCelsius, src.TemperatureCelsius);
        }

        target.PowerOnHours = Math.Max(target.PowerOnHours, src.PowerOnHours);
        target.PowerCycleCount = Math.Max(target.PowerCycleCount, src.PowerCycleCount);
        target.ReallocatedSectors = Math.Max(target.ReallocatedSectors, src.ReallocatedSectors);
        target.PendingSectors = Math.Max(target.PendingSectors, src.PendingSectors);
        target.UncorrectableErrors = Math.Max(target.UncorrectableErrors, src.UncorrectableErrors);

        if (src.SsdLifeRemainingPercent is int life)
        {
            target.SsdLifeRemainingPercent = target.SsdLifeRemainingPercent is int existing
                ? Math.Min(existing, life)
                : life;
        }
    }

    private static SmartAttributes CopySmartAttributes(SmartAttributes s) =>
        new()
        {
            TemperatureCelsius = s.TemperatureCelsius,
            PowerOnHours = s.PowerOnHours,
            PowerCycleCount = s.PowerCycleCount,
            ReallocatedSectors = s.ReallocatedSectors,
            PendingSectors = s.PendingSectors,
            UncorrectableErrors = s.UncorrectableErrors,
            SsdLifeRemainingPercent = s.SsdLifeRemainingPercent,
            PredictFailure = s.PredictFailure
        };

    /// <summary>Ищет номер физического диска в строке (DeviceID Win32 или ObjectId Storage WMI).</summary>
    private static int? TryParsePhysicalDriveIndex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var directIdx) && directIdx >= 0)
        {
            return directIdx;
        }

        var u = text.ToUpperInvariant();
        const string key = "PHYSICALDRIVE";
        var i = u.IndexOf(key, StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        var start = i + key.Length;
        var end = start;
        while (end < u.Length && char.IsDigit(u[end]))
        {
            end++;
        }

        if (end == start)
        {
            return null;
        }

        return int.Parse(u.AsSpan(start, end - start), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Для части NVMe-поставщиков температура в StorageReliabilityCounter приходит в Kelvin.
    /// </summary>
    private static int? NormalizeStorageTemperature(ushort? raw)
    {
        if (raw is not ushort value || value == 0)
        {
            return null;
        }

        if (value is > 150 and < 400)
        {
            var celsius = value - 273;
            return celsius is > 0 and < 125 ? celsius : null;
        }

        return value is > 0 and < 125 ? value : null;
    }

    private static ushort? TryGetUInt16(object? o)
    {
        if (o is null)
        {
            return null;
        }

        if (o is ushort u)
        {
            return u;
        }

        return ushort.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    private static ulong? TryGetUInt64(object? o)
    {
        if (o is null)
        {
            return null;
        }

        if (o is ulong ul)
        {
            return ul;
        }

        return ulong.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
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
                    parsed.PredictFailure = true;
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

    private static int? EstimateHealthPercent(
        int reallocated,
        int pending,
        int uncorrectable,
        int? ssdLife,
        bool hadTelemetrySignal,
        bool predictFailure)
    {
        if (predictFailure)
        {
            return Math.Clamp(EstimateHealth(reallocated, pending, uncorrectable, ssdLife), 0, 35);
        }

        if (!hadTelemetrySignal)
        {
            return null;
        }

        return EstimateHealth(reallocated, pending, uncorrectable, ssdLife);
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
        public bool PredictFailure { get; set; }
    }
}
