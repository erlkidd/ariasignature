using System.Net.Http;
using System.ServiceProcess;
using AriaSignature.Application;
using AriaSignature.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace AriaSignature.Infrastructure.Melezh;

public sealed class MelezhStatusService : IMelezhStatusService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

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
        var uiReachable = enabled && await ProbeUiAsync(uiUrl, cancellationToken);

        return new MelezhStatusSnapshot(
            enabled,
            port,
            uiUrl,
            serviceName,
            serviceStatus,
            uiReachable);
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

    private static async Task<bool> ProbeUiAsync(string uiUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(uiUrl, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
