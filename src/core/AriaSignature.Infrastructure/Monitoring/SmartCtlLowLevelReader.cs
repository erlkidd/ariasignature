using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AriaSignature.Infrastructure.Monitoring;

public sealed class SmartCtlLowLevelReader
{
    private readonly ILogger<SmartCtlLowLevelReader> _logger;

    public SmartCtlLowLevelReader(ILogger<SmartCtlLowLevelReader> logger)
    {
        _logger = logger;
    }

    public ReadResult Read()
    {
        var exe = ResolveSmartCtlExecutable();
        if (string.IsNullOrEmpty(exe))
        {
            return ReadResult.Empty;
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
            var doc = string.IsNullOrWhiteSpace(type)
                ? RunSmartCtlJson(exe, "-a", "-j", name!)
                : RunSmartCtlJson(exe, "-a", "-j", "-d", type!, name!);
            if (doc is null)
            {
                continue;
            }

            var snap = ParseSnapshot(doc.RootElement);
            if (snap is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(snap.Serial) || !string.IsNullOrWhiteSpace(snap.Model))
            {
                byIdentity[BuildIdentityKey(snap.Model, snap.Serial)] = snap;
            }

            var idx = TryParsePhysicalDriveIndex(name);
            if (idx is int physicalIndex)
            {
                byPhysicalIndex[physicalIndex] = snap;
            }
        }

        return new ReadResult(byIdentity, byPhysicalIndex);
    }

    public static string BuildIdentityKey(string model, string serial)
    {
        static string Norm(string s) => (s ?? string.Empty).Trim().ToUpperInvariant();
        return $"{Norm(model)}|{Norm(serial)}";
    }

    private Snapshot? ParseSnapshot(JsonElement root)
    {
        var model = GetString(root, "model_name") ?? GetString(root, "model_family") ?? string.Empty;
        var serial = GetString(root, "serial_number") ?? string.Empty;
        var snap = new Snapshot
        {
            Model = model.Trim(),
            Serial = serial.Trim()
        };

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
                switch (id)
                {
                    case 5:
                        snap.ReallocatedSectors = (int)Math.Clamp(raw, 0, int.MaxValue);
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
                            snap.SsdLifeRemainingPercent = (int)raw;
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
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "smartctl", "smartctl.exe"),
            Path.Combine(baseDir, "smartctl.exe"),
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

        return int.TryParse(up.AsSpan(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx)
            ? idx
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
        public int TemperatureCelsius { get; set; }
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

