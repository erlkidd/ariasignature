using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AriaSignature.Application;
using AriaSignature.Application.Abstractions;
using AriaSignature.Application.SystemInfo;
using AriaSignature.Application.Telemetry;
using AriaSignature.Domain.Entities;
using Quartz;

namespace AriaSignature.Service.Jobs;

public sealed class OutboundSyncJob : IJob
{
    public const string HttpClientName = "AriaOutboundSync";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppSettingsService _settings;
    private readonly ISystemInfoService _systemInfo;
    private readonly IDiskTelemetryService _disks;
    private readonly IBackupService _backups;
    private readonly ILogger<OutboundSyncJob> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private const int RecentLogLimit = 80;

    public OutboundSyncJob(
        IHttpClientFactory httpClientFactory,
        IAppSettingsService settings,
        ISystemInfoService systemInfo,
        IDiskTelemetryService disks,
        IBackupService backups,
        ILogger<OutboundSyncJob> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _systemInfo = systemInfo;
        _disks = disks;
        _backups = backups;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var dict = await _settings.GetAllAsync(cancellationToken);
        var enabled = ParseBool(dict.GetValueOrDefault(AppSettingsOutboundKeys.Enabled));
        var url = dict.GetValueOrDefault(AppSettingsOutboundKeys.Url)?.Trim() ?? string.Empty;

        if (!enabled || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _logger.LogWarning("Исходящая синхронизация пропущена: некорректный URL «{Url}»", url);
            return;
        }

        SystemInfoSnapshot system;
        IReadOnlyCollection<Disk> diskList;
        IReadOnlyCollection<BackupJob> jobs;
        IReadOnlyCollection<BackupLog> logs;
        try
        {
            system = await _systemInfo.GetSnapshotAsync(cancellationToken);
            diskList = await _disks.GetDisksAsync(cancellationToken);
            jobs = await _backups.GetJobsAsync(cancellationToken);
            logs = await _backups.GetLogsAsync(null, null, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось собрать данные для исходящей синхронизации");
            return;
        }

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

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(uri, payload, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Исходящая синхронизация: {StatusCode} {Reason}. Тело ответа: {Body}",
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    body.Length > 500 ? body[..500] + "…" : body);
            }
            else
            {
                _logger.LogInformation("Исходящая синхронизация успешна: POST {Url}", url);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Исходящая синхронизация: сбой HTTP-запроса к {Url}", url);
        }
    }

    private static bool ParseBool(string? value)
    {
        return bool.TryParse(value, out var b) && b;
    }
}
