using System.Collections.ObjectModel;
using System.Windows.Input;
using AriaSignature.Domain.Entities;
using AriaSignature.Domain.Enums;
using AriaSignature.UI.Models;
using AriaSignature.UI.Services;

namespace AriaSignature.UI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AriaApiClient _apiClient = new();
    public ICommand RefreshCommand { get; }
    public ICommand CreateBackupCommand { get; }
    public ICommand SaveBackupCommand { get; }
    public ICommand DeleteBackupCommand { get; }
    public ICommand RunBackupCommand { get; }
    public ICommand ToggleBackupCommand { get; }

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

    private string _operationStatus = "Готово";
    public string OperationStatus
    {
        get => _operationStatus;
        set
        {
            if (_operationStatus == value)
            {
                return;
            }

            _operationStatus = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<string> SchedulePresets { get; } = ["Ежедневно", "Еженедельно", "Ежемесячно"];
    public ObservableCollection<BackupType> BackupTypes { get; } = [BackupType.File, BackupType.MsSql];

    private string _selectedSchedulePreset = "Ежедневно";
    public string SelectedSchedulePreset
    {
        get => _selectedSchedulePreset;
        set
        {
            if (_selectedSchedulePreset == value)
            {
                return;
            }

            _selectedSchedulePreset = value;
            OnPropertyChanged();
        }
    }

    private string _newBackupName = string.Empty;
    public string NewBackupName
    {
        get => _newBackupName;
        set
        {
            if (_newBackupName == value)
            {
                return;
            }

            _newBackupName = value;
            OnPropertyChanged();
        }
    }

    private string _newBackupSource = string.Empty;
    public string NewBackupSource
    {
        get => _newBackupSource;
        set
        {
            if (_newBackupSource == value)
            {
                return;
            }

            _newBackupSource = value;
            OnPropertyChanged();
        }
    }

    private BackupType _newBackupType = BackupType.File;
    public BackupType NewBackupType
    {
        get => _newBackupType;
        set
        {
            if (_newBackupType == value)
            {
                return;
            }

            _newBackupType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SourceFieldLabel));
            OnPropertyChanged(nameof(SourceFieldHint));
        }
    }

    private string _newBackupDestination = string.Empty;
    public string NewBackupDestination
    {
        get => _newBackupDestination;
        set
        {
            if (_newBackupDestination == value)
            {
                return;
            }

            _newBackupDestination = value;
            OnPropertyChanged();
        }
    }

    private int _newBackupRetention = 7;
    public int NewBackupRetention
    {
        get => _newBackupRetention;
        set
        {
            if (_newBackupRetention == value)
            {
                return;
            }

            _newBackupRetention = value;
            OnPropertyChanged();
        }
    }

    public string SourceFieldLabel => NewBackupType == BackupType.File
        ? "Источник (.1CD)"
        : "Источник (MSSQL connection string)";

    public string SourceFieldHint => NewBackupType == BackupType.File
        ? @"Пример: C:\Bases\Accounting\1Cv8.1CD"
        : "Пример: Server=HOST;Database=DB;User Id=sa;Password=***;";

    private BackupJob? _selectedBackupJob;
    public BackupJob? SelectedBackupJob
    {
        get => _selectedBackupJob;
        set
        {
            if (_selectedBackupJob == value)
            {
                return;
            }

            _selectedBackupJob = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEditSelectedBackup));
        }
    }

    public bool CanEditSelectedBackup => SelectedBackupJob is not null;

    public MainViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync);
        SaveBackupCommand = new AsyncRelayCommand(SaveBackupAsync);
        DeleteBackupCommand = new AsyncRelayCommand(DeleteBackupAsync);
        RunBackupCommand = new AsyncRelayCommand(RunBackupAsync);
        ToggleBackupCommand = new AsyncRelayCommand(ToggleBackupStateAsync);
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
            OperationStatus = $"Данные обновлены: {DateTime.Now:dd.MM.yyyy HH:mm:ss}";
        }
        catch (Exception ex)
        {
            OperationStatus = $"Сервис недоступен: {ex.Message}";
        }
    }

    public async Task CreateBackupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewBackupName) ||
            string.IsNullOrWhiteSpace(NewBackupSource) ||
            string.IsNullOrWhiteSpace(NewBackupDestination))
        {
            OperationStatus = "Заполните имя, источник и назначение архивации";
            return;
        }

        var request = new BackupJobUpsertModel
        {
            Name = NewBackupName,
            Type = NewBackupType,
            Source = NewBackupSource,
            Destination = NewBackupDestination,
            ScheduleCron = MapCronPreset(SelectedSchedulePreset),
            RetentionCount = Math.Max(NewBackupRetention, 1),
            IsEnabled = true
        };

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var created = await _apiClient.CreateBackupJobAsync(ApiBaseUrl, request, cts.Token);
            if (created is null)
            {
                OperationStatus = "Не удалось создать задачу архивации";
                return;
            }

            OperationStatus = $"Задача \"{created.Name}\" создана";
            NewBackupName = string.Empty;
            NewBackupSource = string.Empty;
            NewBackupDestination = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            OperationStatus = $"Ошибка создания задачи: {ex.Message}";
        }
    }

    public async Task SaveBackupAsync()
    {
        if (SelectedBackupJob is null)
        {
            OperationStatus = "Выберите задачу для редактирования";
            return;
        }

        var request = new BackupJobUpsertModel
        {
            Name = SelectedBackupJob.Name,
            Type = SelectedBackupJob.Type,
            Source = SelectedBackupJob.Source,
            Destination = SelectedBackupJob.Destination,
            ScheduleCron = SelectedBackupJob.ScheduleCron,
            RetentionCount = Math.Max(SelectedBackupJob.RetentionCount, 1),
            IsEnabled = SelectedBackupJob.IsEnabled
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var updated = await _apiClient.UpdateBackupJobAsync(ApiBaseUrl, SelectedBackupJob.Id, request, cts.Token);
        OperationStatus = updated is null
            ? "Не удалось сохранить изменения задачи"
            : $"Задача \"{updated.Name}\" обновлена";
        await RefreshAsync();
    }

    public async Task DeleteBackupAsync()
    {
        if (SelectedBackupJob is null)
        {
            OperationStatus = "Выберите задачу для удаления";
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var deleted = await _apiClient.DeleteBackupJobAsync(ApiBaseUrl, SelectedBackupJob.Id, cts.Token);
        OperationStatus = deleted ? "Задача удалена" : "Не удалось удалить задачу";
        await RefreshAsync();
    }

    public async Task RunBackupAsync()
    {
        if (SelectedBackupJob is null)
        {
            OperationStatus = "Выберите задачу для запуска";
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var log = await _apiClient.RunBackupJobAsync(ApiBaseUrl, SelectedBackupJob.Id, cts.Token);
        OperationStatus = log is null ? "Не удалось запустить задачу" : $"Запуск завершен: {log.Status}";
        await RefreshAsync();
    }

    public async Task ToggleBackupStateAsync()
    {
        if (SelectedBackupJob is null)
        {
            OperationStatus = "Выберите задачу для включения/выключения";
            return;
        }

        SelectedBackupJob.IsEnabled = !SelectedBackupJob.IsEnabled;
        await SaveBackupAsync();
    }

    private static string MapCronPreset(string preset)
    {
        return preset switch
        {
            "Ежедневно" => "0 0 2 * * ?",
            "Еженедельно" => "0 0 2 ? * MON",
            "Ежемесячно" => "0 0 2 1 * ?",
            _ => "0 0 2 * * ?"
        };
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
