using System.Collections.ObjectModel;
using System.Windows.Input;
using AriaSignature.Domain.Entities;
using AriaSignature.UI.Services;

namespace AriaSignature.UI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AriaApiClient _apiClient = new();
    public ICommand RefreshCommand { get; }

    public ObservableCollection<Disk> Disks { get; } = [];
    public ObservableCollection<BackupJob> BackupJobs { get; } = [];
    public ObservableCollection<BackupLog> BackupLogs { get; } = [];

    private string _apiBaseUrl = "http://127.0.0.1:5160/api/v1";
    public string ApiBaseUrl
    {
        get => _apiBaseUrl;
        set
        {
            if (_apiBaseUrl == value)
            {
                return;
            }

            _apiBaseUrl = value;
            OnPropertyChanged();
        }
    }

    private int _smartIntervalSeconds = 60;
    public int SmartIntervalSeconds
    {
        get => _smartIntervalSeconds;
        set
        {
            if (_smartIntervalSeconds == value)
            {
                return;
            }

            _smartIntervalSeconds = value;
            OnPropertyChanged();
        }
    }

    private string _lastSyncStatus = "Синхронизация не выполнялась";
    public string LastSyncStatus
    {
        get => _lastSyncStatus;
        private set
        {
            if (_lastSyncStatus == value)
            {
                return;
            }

            _lastSyncStatus = value;
            OnPropertyChanged();
        }
    }

    public MainViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var disks = await _apiClient.GetDisksAsync(ApiBaseUrl, cts.Token);
            var jobs = await _apiClient.GetBackupJobsAsync(ApiBaseUrl, cts.Token);
            var logs = await _apiClient.GetBackupLogsAsync(ApiBaseUrl, cts.Token);

            ReplaceCollection(Disks, disks);
            ReplaceCollection(BackupJobs, jobs);
            ReplaceCollection(BackupLogs, logs);
            LastSyncStatus = $"Обновлено: {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        }
        catch (Exception ex)
        {
            LastSyncStatus = $"Ошибка синхронизации: {ex.Message}";
        }
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IReadOnlyCollection<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
