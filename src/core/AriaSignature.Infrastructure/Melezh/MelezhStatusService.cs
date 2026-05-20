using System.Net.Http;
using System.Diagnostics;
using System.ServiceProcess;
using AriaSignature.Application;
using AriaSignature.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace AriaSignature.Infrastructure.Melezh;

public sealed class MelezhStatusService : IMelezhStatusService
{
    private static readonly TimeSpan UiProbeTimeout = TimeSpan.FromSeconds(6);

    private readonly IAppSettingsService _settings;
    private readonly IConfiguration _configuration;

    public MelezhStatusService(IAppSettingsService settings, IConfiguration configuration)
    {
        _settings = settings;
        _configuration = configuration;
    }

    public async Task<MelezhStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var dict = await _settings.GetAllAsync(cancellationToken);
        var enabled = ParseBool(dict.GetValueOrDefault(AppSettingsMelezhKeys.Enabled), defaultValue: true);
        var port = ParsePort(dict.GetValueOrDefault(AppSettingsMelezhKeys.Port), _configuration.GetValue("Melezh:Port", 7788));
        var serviceName = dict.GetValueOrDefault(AppSettingsMelezhKeys.ServiceName)
            ?? _configuration.GetValue<string>("Melezh:ServiceName")
            ?? "AriaSignatureMelezhService";
        var uiUrl = $"http://127.0.0.1:{port}/ui";
        var serviceStatus = TryGetServiceStatus(serviceName);
        var logHint = GetLatestMelezhHostLogHint();
        string? lastError = null;
        var uiReachable = false;

        if (enabled)
        {
            var (reachable, probeError) = await ProbeUiAsync(port, cancellationToken);
            uiReachable = reachable;
            lastError = probeError;
            if (!reachable)
            {
                var logHintError = MelezhHostLogDiagnostics.TryGetActionableErrorFromLatestLog();
                var scmHint = TryGetServiceConfigHint(serviceName);
                if (!string.IsNullOrWhiteSpace(logHintError))
                {
                    lastError = logHintError;
                    if (!string.IsNullOrWhiteSpace(scmHint))
                    {
                        lastError += " " + scmHint;
                    }
                }
                else if (serviceStatus == "Running")
                {
                    lastError ??=
                        "Служба Melezh запущена, но HTTP Web UI не отвечает. Проверьте melezh-host.log и bundle OInt (melezh.bat, oscript).";
                    if (!string.IsNullOrWhiteSpace(scmHint))
                    {
                        lastError += " " + scmHint;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(scmHint))
                {
                    lastError = scmHint;
                }
            }
        }

        return new MelezhStatusSnapshot(
            enabled,
            port,
            uiUrl,
            serviceName,
            serviceStatus,
            uiReachable,
            lastError,
            logHint);
    }

    private static bool ParseBool(string? raw, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    private static int ParsePort(string? raw, int fallback)
    {
        return int.TryParse(raw, out var parsed) && parsed is > 0 and <= 65535 ? parsed : fallback;
    }

    private static string? TryGetServiceStatus(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var sc = new ServiceController(serviceName);
            sc.Refresh();
            return sc.Status.ToString();
        }
        catch
        {
            return "NotFound";
        }
    }

    private static string? GetLatestMelezhHostLogHint()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "AriaSignature",
                "logs");
            if (!Directory.Exists(logDir))
            {
                return null;
            }

            var latest = Directory
                .EnumerateFiles(logDir, "melezh-host-*.log", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            return latest is null ? null : latest.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetServiceConfigHint(string serviceName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var sc = new ServiceController(serviceName);
            sc.Refresh();
            var hints = new List<string>();

            if (sc.StartType != ServiceStartMode.Automatic)
            {
                hints.Add($"StartType={sc.StartType} (ожидался Automatic)");
            }

            var qc = RunScAndCapture($"qc {serviceName}");
            if (!string.IsNullOrWhiteSpace(qc))
            {
                var binPath = ExtractScField(qc, "BINARY_PATH_NAME");
                if (!string.IsNullOrWhiteSpace(binPath) &&
                    !binPath.Contains("AriaSignature.MelezhHost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    hints.Add($"binPath mismatch: {binPath}");
                }
            }

            var queryEx = RunScAndCapture($"queryex {serviceName}");
            if (!string.IsNullOrWhiteSpace(queryEx))
            {
                var upper = queryEx.ToUpperInvariant();
                if (upper.Contains("DELETE_PENDING") || upper.Contains("MARKED FOR DELETE"))
                {
                    hints.Add("SCM state: pending delete");
                }
            }

            if (hints.Count == 0)
            {
                return null;
            }

            return "SCM hints: " + string.Join("; ", hints);
        }
        catch
        {
            return null;
        }
    }

    private static string RunScAndCapture(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(4000);
        return (output + Environment.NewLine + error).Trim();
    }

    private static string? ExtractScField(string scOutput, string field)
    {
        using var reader = new StringReader(scOutput);
        while (reader.ReadLine() is { } line)
        {
            if (!line.Contains(field, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var idx = line.IndexOf(':');
            if (idx >= 0 && idx + 1 < line.Length)
            {
                return line[(idx + 1)..].Trim();
            }
        }

        return null;
    }

    private static async Task<(bool Reachable, string? Error)> ProbeUiAsync(int port, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = UiProbeTimeout };
        var uris = new[]
        {
            $"http://127.0.0.1:{port}/aria_ping",
            $"http://127.0.0.1:{port}/ui",
            $"http://127.0.0.1:{port}/",
        };
        string? lastErr = null;

        foreach (var uri in uris)
        {
            try
            {
                using var response = await http.GetAsync(uri, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return (true, null);
                }

                lastErr = $"HTTP {(int)response.StatusCode} для {uri}";
            }
            catch (Exception ex)
            {
                lastErr = $"{uri}: {ex.Message}";
            }
        }

        return (false, lastErr);
    }
}
