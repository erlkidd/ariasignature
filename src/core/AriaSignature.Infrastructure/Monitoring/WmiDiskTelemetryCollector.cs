using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.Versioning;
using AriaSignature.Application.Abstractions;
using AriaSignature.Application.Telemetry;
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

        var smartCtlData = _smartCtl.Read();
        var smartSnapshot = ReadSmartSnapshotSafe();
        var smartByDriveIndex = BuildSmartByPhysicalDriveIndex(smartSnapshot);
        var storageReliability = ReadStorageReliabilityByPhysicalDriveIndexSafe();
        var storageMediaType = ReadStorageMediaTypeByPhysicalDriveIndexSafe();
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
                        var smartCtlUsed = MergeSmartCtl(physicalIndex, model, serial, pnp, smartCtlData, smart);
                        var storageUsed = MergeStorageReliability(physicalIndex, storageReliability, smart);
                        var wmiUsed = HasTelemetrySignal(smart) && !smartCtlUsed;
                        if (!smartCtlUsed)
                        {
                            _logger.LogInformation(
                                "Telemetry fallback for disk {Model} ({Serial}): smartctl mapping missing, using WMI/Storage signals.",
                                model,
                                string.IsNullOrWhiteSpace(serial) ? "n/a" : serial);
                        }

                        var reallocated = smart.ReallocatedSectors;
                        var pending = smart.PendingSectors;
                        var uncorrectable = smart.UncorrectableErrors;
                        var wearConfirmed = smartCtlUsed || storageUsed;
                        var ssdLife = DiskTelemetryRules.NormalizeSsdLifeRemainingPercent(
                            smart.SsdLifeRemainingPercent,
                            wearConfirmed);
                        var hadTelemetry = DiskTelemetryRules.HasMeaningfulTelemetrySignal(
                            smart.TemperatureCelsius,
                            smart.PowerOnHours,
                            reallocated,
                            pending,
                            uncorrectable,
                            ssdLife,
                            wearConfirmed) || smart.PredictFailure;
                        var health = EstimateHealthPercent(reallocated, pending, uncorrectable, ssdLife, hadTelemetry, smart.PredictFailure);
                        var status = CalculateStatus(reallocated, pending, uncorrectable, ssdLife);

                        disks.Add(new Disk
                        {
                            Id = diskId,
                            Model = model,
                            Serial = serial,
                            Interface = iface,
                            MediaType = DiskTelemetryRules.NormalizeMediaType(
                                mediaType,
                                physicalIndex,
                                iface,
                                model,
                                storageMediaType),
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
                            SmartCtlUsed = smartCtlUsed,
                            WmiUsed = wmiUsed,
                            StorageReliabilityUsed = storageUsed,
                            TelemetryConfidence = ComputeTelemetryConfidence(smartCtlUsed, storageUsed, wmiUsed, hadTelemetry),
                            TelemetryDegradationReason = BuildTelemetryDegradationReason(
                                smartCtlUsed,
                                storageUsed,
                                wmiUsed,
                                hadTelemetry,
                                smartCtlData,
                                physicalIndex,
                                model,
                                serial),
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

        if (disks.Count > 0 && disks.All(d => d.HealthPercent is null && d.TemperatureCelsius is null))
        {
            _logger.LogWarning(
                "Disk telemetry collected without SMART signals. " +
                "Check smartctl deployment and WMI availability on this host.");
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
                        SmartCtlUsed = false,
                        WmiUsed = HasTelemetrySignal(smart),
                        StorageReliabilityUsed = false,
                        TelemetryConfidence = HasTelemetrySignal(smart) ? 45 : 10,
                        TelemetryDegradationReason = HasTelemetrySignal(smart)
                            ? "Данные получены из WMI fallback."
                            : "SMART недоступен в fallback-режиме.",
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
                    if (wear is ushort wWear && wWear <= 100)
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
                        agg.TemperatureCelsius = agg.TemperatureCelsius is int prev ? Math.Max(prev, tC) : tC;
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
            _logger.LogWarning(ex, "WMI MSFT_StorageReliabilityCounter недоступен (возможна старая ОС, политика или права).");
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
            TemperatureCelsius = a.TemperatureCelsius is int at && b.TemperatureCelsius is int bt
                ? Math.Max(at, bt)
                : a.TemperatureCelsius ?? b.TemperatureCelsius,
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
        s.TemperatureCelsius is > 0 ||
        s.PowerOnHours > 0 ||
        s.ReallocatedSectors > 0 ||
        s.PendingSectors > 0 ||
        s.UncorrectableErrors > 0 ||
        s.SsdLifeRemainingPercent is > 0;

    private static bool MergeStorageReliability(int? physicalIndex, Dictionary<int, SmartAttributes> storage, SmartAttributes target)
    {
        if (physicalIndex is not int idx || !storage.TryGetValue(idx, out var extra))
        {
            return false;
        }

        var used = false;
        if (extra.TemperatureCelsius is > 0)
        {
            target.TemperatureCelsius = target.TemperatureCelsius is int existing
                ? Math.Max(existing, extra.TemperatureCelsius.Value)
                : extra.TemperatureCelsius.Value;
            used = true;
        }

        if (extra.SsdLifeRemainingPercent is int storageLife)
        {
            target.SsdLifeRemainingPercent = target.SsdLifeRemainingPercent is int smartLife
                ? Math.Min(smartLife, storageLife)
                : storageLife;
            used = true;
        }

        if (extra.PowerCycleCount > target.PowerCycleCount)
        {
            target.PowerCycleCount = extra.PowerCycleCount;
            used = true;
        }

        return used;
    }

    private static bool MergeSmartCtl(
        int? physicalIndex,
        string model,
        string serial,
        string pnpDeviceId,
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

        if (src is null && !string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            src = smartCtlData.ByIdentity.Values.FirstOrDefault(s =>
                pnpDeviceId.Contains(s.Serial, StringComparison.OrdinalIgnoreCase) ||
                pnpDeviceId.Contains(s.Model, StringComparison.OrdinalIgnoreCase));
        }

        if (src is null)
        {
            src = FindBestSmartCtlSnapshot(model, serial, pnpDeviceId, smartCtlData);
        }

        if (src is null)
        {
            return false;
        }

        if (src.TemperatureCelsius is > 0)
        {
            target.TemperatureCelsius = target.TemperatureCelsius is int existing
                ? Math.Max(existing, src.TemperatureCelsius.Value)
                : src.TemperatureCelsius.Value;
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

        return true;
    }

    private static SmartCtlLowLevelReader.Snapshot? FindBestSmartCtlSnapshot(
        string model,
        string serial,
        string pnpDeviceId,
        SmartCtlLowLevelReader.ReadResult smartCtlData)
    {
        var modelTokens = Tokenize(model);
        var serialUpper = serial.Trim().ToUpperInvariant();
        var pnpUpper = pnpDeviceId.Trim().ToUpperInvariant();

        SmartCtlLowLevelReader.Snapshot? best = null;
        var bestScore = 0;
        foreach (var candidate in smartCtlData.ByIdentity.Values)
        {
            var score = 0;
            var cModel = candidate.Model.Trim().ToUpperInvariant();
            var cSerial = candidate.Serial.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(serialUpper) && !string.IsNullOrWhiteSpace(cSerial))
            {
                if (serialUpper.Equals(cSerial, StringComparison.OrdinalIgnoreCase))
                {
                    score += 200;
                }
                else if (serialUpper.Contains(cSerial, StringComparison.OrdinalIgnoreCase) ||
                         cSerial.Contains(serialUpper, StringComparison.OrdinalIgnoreCase))
                {
                    score += 120;
                }
            }

            if (!string.IsNullOrWhiteSpace(pnpUpper))
            {
                if (!string.IsNullOrWhiteSpace(cSerial) && pnpUpper.Contains(cSerial, StringComparison.OrdinalIgnoreCase))
                {
                    score += 80;
                }
                if (!string.IsNullOrWhiteSpace(cModel) && pnpUpper.Contains(cModel, StringComparison.OrdinalIgnoreCase))
                {
                    score += 60;
                }
            }

            foreach (var token in modelTokens)
            {
                if (cModel.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                }
            }

            if (score <= bestScore)
            {
                continue;
            }

            best = candidate;
            bestScore = score;
        }

        return bestScore >= 60 ? best : null;
    }

    private static IReadOnlyCollection<string> Tokenize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .ToUpperInvariant()
            .Split([' ', '\t', '_', '-', '/', '\\', '.', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
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

        if (m.Contains("HDD", StringComparison.Ordinal) ||
            m.Contains("SATA", StringComparison.Ordinal) ||
            m.Contains("7200", StringComparison.Ordinal) ||
            m.Contains("5400", StringComparison.Ordinal))
        {
            return "HDD";
        }

        if (iface.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
        {
            return "SSD";
        }

        if (iface.Contains("SATA", StringComparison.OrdinalIgnoreCase) ||
            iface.Contains("SCSI", StringComparison.OrdinalIgnoreCase))
        {
            return "HDD";
        }

        return "Не определён";
    }

    private static string ResolveInterfaceType(ManagementObject disk)
    {
        var pnp = disk["PNPDeviceID"]?.ToString() ?? string.Empty;
        if (pnp.Contains("USB", StringComparison.OrdinalIgnoreCase))
        {
            return "USB";
        }

        if (pnp.Contains("NVME", StringComparison.OrdinalIgnoreCase))
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

    private static int ComputeTelemetryConfidence(bool smartCtlUsed, bool storageUsed, bool wmiUsed, bool hadTelemetry)
    {
        if (!hadTelemetry)
        {
            return 5;
        }

        var score = 20;
        if (smartCtlUsed)
        {
            score += 55;
        }

        if (storageUsed)
        {
            score += 20;
        }

        if (wmiUsed)
        {
            score += 10;
        }

        return Math.Clamp(score, 0, 100);
    }

    private static string BuildTelemetryDegradationReason(
        bool smartCtlUsed,
        bool storageUsed,
        bool wmiUsed,
        bool hadTelemetry,
        SmartCtlLowLevelReader.ReadResult smartCtlData,
        int? physicalIndex,
        string model,
        string serial)
    {
        if (smartCtlUsed)
        {
            return storageUsed
                ? "Использован smartctl как первичный источник, дополнено StorageReliability."
                : "Использован smartctl как первичный источник телеметрии.";
        }

        if (wmiUsed || storageUsed)
        {
            if (smartCtlData.ByIdentity.Count == 0 && smartCtlData.ByPhysicalIndex.Count == 0)
            {
                return "smartctl недоступен или не вернул устройства; используется WMI fallback.";
            }

            var attempted = BuildMatchAttemptHint(physicalIndex, model, serial);
            return $"smartctl не сопоставлен с диском ({attempted}), используются WMI/Storage fallback-метрики.";
        }

        return hadTelemetry
            ? "Метрики получены частично, возможны ограничения контроллера."
            : "Телеметрия недоступна: диск/контроллер не отдает SMART-атрибуты. " +
              "Рекомендуется проверить режим контроллера (AHCI/RST), драйвер чипсета/NVMe и доступность SMART в BIOS.";
    }

    private static string BuildMatchAttemptHint(int? physicalIndex, string model, string serial)
    {
        var parts = new List<string>();
        if (physicalIndex is int idx)
        {
            parts.Add($@"PhysicalDrive{idx}");
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            parts.Add($"model:{TrimForHint(model, 28)}");
        }

        if (!string.IsNullOrWhiteSpace(serial))
        {
            parts.Add($"serial:{TrimForHint(serial, 20)}");
        }

        return parts.Count == 0 ? "идентификаторы отсутствуют" : string.Join(", ", parts);
    }

    private static string TrimForHint(string value, int maxLen)
    {
        var normalized = value.Trim();
        if (normalized.Length <= maxLen)
        {
            return normalized;
        }

        return normalized[..maxLen] + "...";
    }

    private static int EstimateHealth(int reallocated, int pending, int uncorrectable, int? ssdLife)
    {
        var penalty = Math.Min(reallocated, 20) + (pending * 4) + (uncorrectable * 10);
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

    private static string ResolveMediaType(
        string sourceMediaType,
        int? physicalIndex,
        string iface,
        string model,
        IReadOnlyDictionary<int, string> storageMediaType)
    {
        if (!string.IsNullOrWhiteSpace(sourceMediaType))
        {
            return sourceMediaType;
        }

        if (physicalIndex is int idx && storageMediaType.TryGetValue(idx, out var mapped))
        {
            return mapped;
        }

        return InferMediaType(iface, model);
    }

    private Dictionary<int, string> ReadStorageMediaTypeByPhysicalDriveIndexSafe()
    {
        var map = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage",
                "SELECT DeviceId, MediaType, SpindleSpeed, FriendlyName FROM MSFT_PhysicalDisk");
            using var results = searcher.Get();
            foreach (ManagementObject row in results)
            {
                using (row)
                {
                    var idx = TryParsePhysicalDriveIndex(row["DeviceId"]?.ToString());
                    if (idx is not int driveIdx)
                    {
                        continue;
                    }

                    var mediaTypeRaw = Convert.ToString(row["MediaType"], CultureInfo.InvariantCulture);
                    var spindleRaw = Convert.ToString(row["SpindleSpeed"], CultureInfo.InvariantCulture);
                    if (int.TryParse(mediaTypeRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mediaTypeCode))
                    {
                        map[driveIdx] = mediaTypeCode switch
                        {
                            3 => "HDD",
                            4 => "SSD",
                            5 => "SCM",
                            _ => "Не определён"
                        };
                        continue;
                    }

                    if (int.TryParse(spindleRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var spindle))
                    {
                        map[driveIdx] = spindle > 0 ? "HDD" : "SSD";
                        continue;
                    }

                    var name = row["FriendlyName"]?.ToString() ?? string.Empty;
                    map[driveIdx] = InferMediaType(string.Empty, name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "WMI MSFT_PhysicalDisk media type query failed.");
        }

        return map;
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
        public int? TemperatureCelsius { get; set; }
        public long PowerOnHours { get; set; }
        public long PowerCycleCount { get; set; }
        public int ReallocatedSectors { get; set; }
        public int PendingSectors { get; set; }
        public int UncorrectableErrors { get; set; }
        public int? SsdLifeRemainingPercent { get; set; }
        public bool PredictFailure { get; set; }
    }
}
