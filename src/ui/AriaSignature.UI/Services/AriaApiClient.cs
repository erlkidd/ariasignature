using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using AriaSignature.Domain.Entities;

namespace AriaSignature.UI.Services;

public sealed class AriaApiClient
{
    private readonly HttpClient _httpClient = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyCollection<Disk>> GetDisksAsync(string apiBaseUrl, CancellationToken cancellationToken)
    {
        var disks = await _httpClient.GetFromJsonAsync<List<Disk>>($"{apiBaseUrl.TrimEnd('/')}/disks", _jsonOptions, cancellationToken);
        return disks ?? [];
    }

    public async Task<IReadOnlyCollection<BackupJob>> GetBackupJobsAsync(string apiBaseUrl, CancellationToken cancellationToken)
    {
        var jobs = await _httpClient.GetFromJsonAsync<List<BackupJob>>($"{apiBaseUrl.TrimEnd('/')}/backups", _jsonOptions, cancellationToken);
        return jobs ?? [];
    }

    public async Task<IReadOnlyCollection<BackupLog>> GetBackupLogsAsync(string apiBaseUrl, CancellationToken cancellationToken)
    {
        var logs = await _httpClient.GetFromJsonAsync<List<BackupLog>>($"{apiBaseUrl.TrimEnd('/')}/backups/logs", _jsonOptions, cancellationToken);
        return logs ?? [];
    }
}
