using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AriaSignature.Application;
using AriaSignature.Application.Abstractions;
using AriaSignature.Application.SystemInfo;
using AriaSignature.Application.Telemetry;
using AriaSignature.Application.Runtime;
using AriaSignature.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AriaSignature.Infrastructure.Melezh;

public sealed class MelezhSyncDispatcher : IMelezhSyncDispatcher
{
    public const string HttpClientName = "AriaMelezhSync";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppSettingsService _settings;
    private readonly ISystemInfoService _systemInfo;
    private readonly IDiskTelemetryService _disks;
    private readonly IBackupService _backups;
    private readonly IMelezhStatusService _melezhStatus;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MelezhSyncDispatcher> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private const int RecentLogLimit = 80;

    public MelezhSyncDispatcher(
        IHttpClientFactory httpClientFactory,
        IAppSettingsService settings,
        ISystemInfoService systemInfo,
        IDiskTelemetryService disks,
        IBackupService backups,
        IMelezhStatusService melezhStatus,
        IConfiguration configuration,
        ILogger<MelezhSyncDispatcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _systemInfo = systemInfo;
        _disks = disks;
        _backups = backups;
        _melezhStatus = melezhStatus;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<MelezhSyncResult> PushSnapshotAsync(CancellationToken cancellationToken)
    {
        var dict = await _settings.GetAllAsync(cancellationToken);
        var enabled = ParseBool(dict.GetValueOrDefault(AppSettingsMelezhSyncKeys.Enabled), defaultValue: true);
        if (!enabled)
        {
            return new MelezhSyncResult(false, "Melezh sync disabled in settings.", null);
        }

        var melezh = await _melezhStatus.GetSnapshotAsync(cancellationToken);
        if (!melezh.Enabled)
        {
            return new MelezhSyncResult(false, "Melezh integration disabled.", null);
        }

        var handler = (dict.GetValueOrDefault(AppSettingsMelezhSyncKeys.Handler) ?? "aria_sync").Trim();
        if (string.IsNullOrWhiteSpace(handler))
        {
            handler = "aria_sync";
        }

        var port = melezh.Port > 0 ? melezh.Port : _configuration.GetValue("Melezh:Port", 7788);
        var targetUrl = $"http://127.0.0.1:{port}/{handler.TrimStart('/')}";

        try
        {
            var system = await _systemInfo.GetSnapshotAsync(cancellationToken);
            var diskList = await _disks.GetDisksAsync(cancellationToken);
            var jobs = await _backups.GetJobsAsync(cancellationToken);
            var logs = await _backups.GetLogsAsync(null, null, null, cancellationToken);
            var recentLogs = logs.OrderByDescending(l => l.StartTimeUtc).Take(RecentLogLimit).ToList();

            var payload = new OutboundTelemetryPayload
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                AgentVersion = system.AgentVersion,
                System = system,
                Disks = diskList.ToList(),
                Backups = new OutboundBackupSnapshot
                {
                    Jobs = jobs.ToList(),
                    RecentLogs = recentLogs,
                },
            };

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(targetUrl, payload, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var err = $"Melezh POST {(int)response.StatusCode}: {(body.Length > 200 ? body[..200] + "…" : body)}";
                await RecordFailureAsync(err, cancellationToken);
                RuntimeObservability.RecordMelezhSync(success: false);
                return new MelezhSyncResult(false, err, targetUrl);
            }

            await RecordSuccessAsync(cancellationToken);
            RuntimeObservability.RecordMelezhSync(success: true);
            _logger.LogInformation("Melezh sync OK: POST {Url}", targetUrl);
            return new MelezhSyncResult(true, null, targetUrl);
        }
        catch (Exception ex)
        {
            var err = ex.Message;
            await RecordFailureAsync(err, cancellationToken);
            RuntimeObservability.RecordMelezhSync(success: false);
            _logger.LogWarning(ex, "Melezh sync failed for {Url}", targetUrl);
            return new MelezhSyncResult(false, err, targetUrl);
        }
    }

    private async Task RecordSuccessAsync(CancellationToken cancellationToken)
    {
        await _settings.SetAsync(AppSettingsMelezhSyncKeys.LastOkUtc, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);
        await _settings.SetAsync(AppSettingsMelezhSyncKeys.LastError, string.Empty, cancellationToken);
    }

    private async Task RecordFailureAsync(string error, CancellationToken cancellationToken)
    {
        await _settings.SetAsync(AppSettingsMelezhSyncKeys.LastError, error, cancellationToken);
    }

    private static bool ParseBool(string? value, bool defaultValue) =>
        value is null ? defaultValue : bool.TryParse(value, out var b) && b;
}
