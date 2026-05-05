using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AriaSignature.Infrastructure.Monitoring;

public sealed class SmartCtlLowLevelReader
{
    private readonly ILogger<SmartCtlLowLevelReader> _logger;
    private bool _missingLogged;
    private bool _detectedLogged;

    public SmartCtlLowLevelReader(ILogger<SmartCtlLowLevelReader> logger)
    {
        _logger = logger;
    }

    public ReadResult Read()
    {
        var exe = ResolveSmartCtlExecutable();
        if (string.IsNullOrEmpty(exe))
        {
            if (!_missingLogged)
            {
                _logger.LogWarning(
                    "smartctl.exe not found. Low-level SMART/NVMe telemetry is disabled. " +
                    "Expected one of: ARIASIGNATURE_SMARTCTL, service/smartctl/smartctl.exe, PATH.");
                _missingLogged = true;
            }
            return ReadResult.Empty;
        }

        if (!_detectedLogged)
        {
            _logger.LogInformation("Low-level SMART source enabled via smartctl: {Path}", exe);
            _detectedLogged = true;
        }

        var devicesDoc = RunSmartCtlJson(exe, "--scan-open", "-j");
        if (devicesDoc is null)
        {
            return ReadResult.Empty;
        }

        var byIdentity = new Dictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase);
        var byPhysicalIndex = new Dictionary<int, Snapshot>();
        if (!devicesDoc.RootElement.TryGetProperty("devices", out var devicesEl) || devicesEl.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("smartctl scan returned no devices array.");
            return new ReadResult(byIdentity, byPhysicalIndex);
        }

        foreach (var dev in devicesEl.EnumerateArray())
        {
            var name = dev.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var type = dev.TryGetProperty("type", out var t) ? t.GetString() : null;
            var doc = TryReadDeviceWithFallbacks(exe, name!, type);
            if (doc is null)
            {
                continue;
            }

            var snap = ParseSnapshot(doc.RootElement, name!);
            if (snap is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(snap.Serial) || !string.IsNullOrWhiteSpace(snap.Model))
            {
                byIdentity[BuildIdentityKey(snap.Model, snap.Serial)] = snap;
            }

            var idx = snap.PhysicalDriveIndex ?? TryParsePhysicalDriveIndex(name);
            if (idx is int physicalIndex)
            {
                byPhysicalIndex[physicalIndex] = snap;
            }
        }

        ProbePhysicalDrives(exe, byIdentity, byPhysicalIndex);

        return new ReadResult(byIdentity, byPhysicalIndex);
    }

    public static string BuildIdentityKey(string model, string serial)
    {
        static string Norm(string s) => (s ?? string.Empty).Trim().ToUpperInvariant();
        return $"{Norm(model)}|{Norm(serial)}";
    }

    private JsonDocument? TryReadDeviceWithFallbacks(string exe, string name, string? scannedType)
    {
        foreach (var candidate in BuildDeviceTypeCandidates(name, scannedType))
        {
            var doc = string.IsNullOrWhiteSpace(candidate)
                ? RunSmartCtlJson(exe, "-a", "-j", name)
                : RunSmartCtlJson(exe, "-a", "-j", "-d", candidate, name);
            if (doc is null)
            {
                continue;
            }

            if (GetInt(doc.RootElement, "exit_status") is int code && code >= 8)
            {
                doc.Dispose();
                continue;
            }

            return doc;
        }

        _logger.LogInformation("smartctl: no readable payload for device {Device}", name);
        return null;
    }

    private static IReadOnlyList<string?> BuildDeviceTypeCandidates(string deviceName, string? scannedType)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string?>();
        void Add(string? type)
        {
            var key = type ?? "<auto>";
            if (seen.Add(key))
            {
                list.Add(type);
            }
        }

        Add(scannedType);
        Add(null);

        var up = deviceName.ToUpperInvariant();
        var isPhysical = up.Contains("PHYSICALDRIVE", StringComparison.Ordinal);
        if (isPhysical)
        {
            Add("auto");
            Add("sat");
            Add("sat,auto");
            Add("sat,12");
            Add("sat,16");
            Add("scsi");
            Add("ata");
            Add("nvme");
            Add("usbjmicron");
            Add("usbsunplus");
            Add("usbprolific");
            Add("sntjmicron");
            Add("sntasmedia");
            Add("sntrealtek");
            Add("jmb39x");
            Add("megaraid,0");
            Add("megaraid,1");
        }

        if (up.Contains("NVME", StringComparison.Ordinal))
        {
            Add("nvme");
        }

        if (up.Contains("USB", StringComparison.Ordinal))
        {
            Add("sat");
            Add("scsi");
        }

        return list;
    }

    private void ProbePhysicalDrives(
        string exe,
        IDictionary<string, Snapshot> byIdentity,
        IDictionary<int, Snapshot> byPhysicalIndex)
    {
        for (var idx = 0; idx < 16; idx++)
        {
            if (byPhysicalIndex.ContainsKey(idx))
            {
                continue;
            }

            var name = $@"\\.\PhysicalDrive{idx}";
            var doc = TryReadDeviceWithFallbacks(exe, name, null);
            if (doc is null)
            {
                continue;
            }

            var snap = ParseSnapshot(doc.RootElement, name);
            if (snap is null)
            {
                continue;
            }

            if (!HasAnyTelemetrySignal(snap))
            {
                continue;
            }

            var physicalIndex = snap.PhysicalDriveIndex ?? idx;
            byPhysicalIndex[physicalIndex] = snap;
            if (!string.IsNullOrWhiteSpace(snap.Serial) || !string.IsNullOrWhiteSpace(snap.Model))
            {
                byIdentity[BuildIdentityKey(snap.Model, snap.Serial)] = snap;
            }
        }
    }

    private static bool HasAnyTelemetrySignal(Snapshot snapshot)
    {
        return snapshot.TemperatureCelsius is > 0
               || snapshot.PowerOnHours > 0
               || snapshot.PowerCycleCount > 0
               || snapshot.ReallocatedSectors > 0
               || snapshot.PendingSectors > 0
               || snapshot.UncorrectableErrors > 0
               || snapshot.SsdLifeRemainingPercent is >= 0;
    }

    private Snapshot? ParseSnapshot(JsonElement root, string deviceName)
    {
        var model = GetString(root, "model_name") ?? GetString(root, "model_family") ?? string.Empty;
        var serial = GetString(root, "serial_number") ?? string.Empty;
        var snap = new Snapshot
        {
            Model = model.Trim(),
            Serial = serial.Trim(),
            DeviceName = deviceName,
            DeviceType = GetString(root, "device", "type"),
            PhysicalDriveIndex = TryParsePhysicalDriveIndex(deviceName)
        };

        var infoName = GetString(root, "device", "name");
        if (snap.PhysicalDriveIndex is null && !string.IsNullOrWhiteSpace(infoName))
        {
            snap.PhysicalDriveIndex = TryParsePhysicalDriveIndex(infoName);
        }

        var infoPath = GetString(root, "info_name");
        if (snap.PhysicalDriveIndex is null && !string.IsNullOrWhiteSpace(infoPath))
        {
            snap.PhysicalDriveIndex = TryParsePhysicalDriveIndex(infoPath);
        }

        if (root.TryGetProperty("temperature", out var tempObj))
        {
            snap.TemperatureCelsius = GetInt(tempObj, "current") ?? snap.TemperatureCelsius;
        }

        if (root.TryGetProperty("power_on_time", out var po))
        {
            snap.PowerOnHours = GetLong(po, "hours") ?? 0;
        }

        snap.PowerCycleCount = GetLong(root, "power_cycle_count") ?? 0;

        if (root.TryGetProperty("ata_smart_attributes", out var ataAttrs) &&
            ataAttrs.TryGetProperty("table", out var table) &&
            table.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in table.EnumerateArray())
            {
                var id = GetInt(row, "id");
                var raw = row.TryGetProperty("raw", out var rawObj) ? (GetLong(rawObj, "value") ?? 0) : 0;
                var attrName = GetString(row, "name") ?? string.Empty;
                switch (id)
                {
                    case 5:
                        snap.ReallocatedSectors = (int)Math.Clamp(raw, 0, int.MaxValue);
                        break;
                    case 9:
                        snap.PowerOnHours = Math.Max(snap.PowerOnHours, Math.Clamp(raw, 0, long.MaxValue));
                        break;
                    case 12:
                        snap.PowerCycleCount = Math.Max(snap.PowerCycleCount, Math.Clamp(raw, 0, long.MaxValue));
                        break;
                    case 194:
                        if (raw > 0 && raw < 125)
                        {
                            snap.TemperatureCelsius = (int)raw;
                        }

                        break;
                    case 197:
                        snap.PendingSectors = (int)Math.Clamp(raw, 0, int.MaxValue);
                        break;
                    case 198:
                        snap.UncorrectableErrors = (int)Math.Clamp(raw, 0, int.MaxValue);
                        break;
                    case 231:
                    case 233:
                        if (raw is > 0 and <= 100)
                        {
                            var normalized = NormalizeLifePercent((int)raw, attrName);
                            snap.SsdLifeRemainingPercent = snap.SsdLifeRemainingPercent is int prev
                                ? Math.Min(prev, normalized)
                                : normalized;
                        }

                        break;
                }
            }
        }

        if (root.TryGetProperty("nvme_smart_health_information_log", out var nvme))
        {
            if (snap.TemperatureCelsius <= 0)
            {
                var k = GetInt(nvme, "temperature");
                if (k is > 150 and < 450)
                {
                    snap.TemperatureCelsius = k.Value - 273;
                }
                else if (k is > 0 and < 125)
                {
                    snap.TemperatureCelsius = k.Value;
                }
            }

            var percentageUsed = GetInt(nvme, "percentage_used");
            if (percentageUsed is >= 0 and <= 100)
            {
                snap.SsdLifeRemainingPercent = Math.Clamp(100 - percentageUsed.Value, 0, 100);
            }

            var mediaErrors = GetLong(nvme, "media_errors") ?? 0;
            if (mediaErrors > 0)
            {
                snap.UncorrectableErrors = (int)Math.Clamp(mediaErrors, 0, int.MaxValue);
            }

            snap.PowerOnHours = Math.Max(snap.PowerOnHours, GetLong(nvme, "power_on_hours") ?? 0);
            snap.PowerCycleCount = Math.Max(snap.PowerCycleCount, GetLong(nvme, "power_cycles") ?? 0);
        }

        return snap;
    }

    private JsonDocument? RunSmartCtlJson(string exe, params string[] args)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Arguments = string.Join(" ", args.Select(QuoteArg))
                }
            };
            p.Start();
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(7000))
            {
                try { p.Kill(true); } catch { /* ignore */ }
                _logger.LogDebug("smartctl timeout for args: {Args}", p.StartInfo.Arguments);
                return null;
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    _logger.LogDebug("smartctl stderr: {Error}", stderr);
                }

                return null;
            }

            return JsonDocument.Parse(stdout);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "smartctl invocation failed.");
            return null;
        }
    }

    private static string? ResolveSmartCtlExecutable()
    {
        var env = Environment.GetEnvironmentVariable("ARIASIGNATURE_SMARTCTL");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
        {
            return env;
        }

        var baseDir = AppContext.BaseDirectory;
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            Path.Combine(baseDir, "smartctl", "smartctl.exe"),
            Path.Combine(baseDir, "smartctl.exe"),
            Path.Combine(pf, "AriaSignature", "service", "smartctl", "smartctl.exe"),
            "smartctl.exe"
        };

        foreach (var c in candidates)
        {
            if (c.Contains(Path.DirectorySeparatorChar) || c.Contains(Path.AltDirectorySeparatorChar))
            {
                if (File.Exists(c))
                {
                    return c;
                }
            }
            else
            {
                return c;
            }
        }

        return null;
    }

    private static string QuoteArg(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }

        return arg.IndexOfAny([' ', '\t', '"']) >= 0
            ? $"\"{arg.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : arg;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string? GetString(JsonElement element, string parent, string child)
    {
        if (!element.TryGetProperty(parent, out var p) || p.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(p, child);
    }

    private static int? TryParsePhysicalDriveIndex(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }

        var up = deviceName.Trim().ToUpperInvariant();
        const string marker = "PHYSICALDRIVE";
        var pos = up.IndexOf(marker, StringComparison.Ordinal);
        if (pos < 0)
        {
            return null;
        }

        var start = pos + marker.Length;
        var end = start;
        while (end < up.Length && char.IsDigit(up[end]))
        {
            end++;
        }

        if (start == end)
        {
            return null;
        }

        if (int.TryParse(up.AsSpan(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
        {
            return idx;
        }

        var regex = Regex.Match(up, @"PHYSICALDRIVE(\d+)", RegexOptions.CultureInvariant);
        return regex.Success && int.TryParse(regex.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rx)
            ? rx
            : null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null
        };
    }

    private static int NormalizeLifePercent(int rawPercent, string attrName)
    {
        var normalizedName = attrName.Trim().ToUpperInvariant();
        if (normalizedName.Contains("WEAR", StringComparison.Ordinal) ||
            normalizedName.Contains("USED", StringComparison.Ordinal) ||
            normalizedName.Contains("LIFE_USED", StringComparison.Ordinal))
        {
            return Math.Clamp(100 - rawPercent, 0, 100);
        }

        return Math.Clamp(rawPercent, 0, 100);
    }

    private static long? GetLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetInt64(out var i) => i,
            JsonValueKind.String when long.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null
        };
    }

    public sealed class Snapshot
    {
        public string Model { get; init; } = string.Empty;
        public string Serial { get; init; } = string.Empty;
        public string DeviceName { get; init; } = string.Empty;
        public string? DeviceType { get; init; }
        public int? PhysicalDriveIndex { get; set; }
        public int? TemperatureCelsius { get; set; }
        public long PowerOnHours { get; set; }
        public long PowerCycleCount { get; set; }
        public int ReallocatedSectors { get; set; }
        public int PendingSectors { get; set; }
        public int UncorrectableErrors { get; set; }
        public int? SsdLifeRemainingPercent { get; set; }
    }

    public sealed record ReadResult(
        IReadOnlyDictionary<string, Snapshot> ByIdentity,
        IReadOnlyDictionary<int, Snapshot> ByPhysicalIndex)
    {
        public static ReadResult Empty { get; } = new(
            new Dictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<int, Snapshot>());
    }
}

