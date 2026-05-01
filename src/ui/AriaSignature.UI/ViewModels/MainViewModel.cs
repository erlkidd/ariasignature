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
    private readonly StartupRegistrationService _startupRegistrationService = new();
    public ICommand RefreshCommand { get; }
    public ICommand CreateBackupCommand { get; }
    public ICommand SaveBackupCommand { get; }
    public ICommand DeleteBackupCommand { get; }
    public ICommand RunBackupCommand { get; }
    public ICommand ToggleBackupCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public ObservableCollection<Disk> Disks { get; } = [];
    public ObservableCollection<BackupJob> BackupJobs { get; } = [];
    public ObservableCollection<BackupLog> BackupLogs { get; } = [];
    public ObservableCollection<string> ThemeOptions { get; } = [..ThemeService.Themes];

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

    public ObservableCollection<string> SchedulePresets { get; } = ["Ежедневно", "Еженедельно", "Ежемесячно", "Пользовательский"];
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

    private string _newBackupCron = "0 0 2 * * ?";
    public string NewBackupCron
    {
        get => _newBackupCron;
        set
        {
            if (_newBackupCron == value)
            {
                return;
            }

            _newBackupCron = value;
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

    private string _selectedTheme = "Светлая";
    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (_selectedTheme == value)
            {
                return;
            }

            _selectedTheme = value;
            OnPropertyChanged();
        }
    }

    private bool _launchAtStartup;
    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set
        {
            if (_launchAtStartup == value)
            {
                return;
            }

            _launchAtStartup = value;
            OnPropertyChanged();
        }
    }

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
    public int DiskCount => Disks.Count;
    public int BackupJobCount => BackupJobs.Count;
    public int FailedBackupCount => BackupLogs.Count(x => x.Status == BackupExecutionStatus.Failed);

    public MainViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync);
        SaveBackupCommand = new AsyncRelayCommand(SaveBackupAsync);
        DeleteBackupCommand = new AsyncRelayCommand(DeleteBackupAsync);
        RunBackupCommand = new AsyncRelayCommand(RunBackupAsync);
        ToggleBackupCommand = new AsyncRelayCommand(ToggleBackupStateAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        LaunchAtStartup = _startupRegistrationService.IsEnabled();
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
            OnPropertyChanged(nameof(DiskCount));
            OnPropertyChanged(nameof(BackupJobCount));
            OnPropertyChanged(nameof(FailedBackupCount));
            SetStatus($"Данные обновлены: {DateTime.Now:dd.MM.yyyy HH:mm:ss}", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Сервис недоступен: {ex.Message}", isError: true);
        }
    }

    public async Task CreateBackupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewBackupName) ||
            string.IsNullOrWhiteSpace(NewBackupSource) ||
            string.IsNullOrWhiteSpace(NewBackupDestination))
        {
            SetStatus("Заполните имя, источник и назначение архивации", isError: true);
            return;
        }

        var request = new BackupJobUpsertModel
        {
            Name = NewBackupName,
            Type = NewBackupType,
            Source = NewBackupSource,
            Destination = NewBackupDestination,
            ScheduleCron = MapCronPreset(SelectedSchedulePreset, NewBackupCron),
            RetentionCount = Math.Max(NewBackupRetention, 1),
            IsEnabled = true
        };

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var created = await _apiClient.CreateBackupJobAsync(ApiBaseUrl, request, cts.Token);
            if (created is null)
            {
                SetStatus("Не удалось создать задачу архивации. Проверьте поля и доступы.", isError: true);
                return;
            }

            SetStatus($"Задача \"{created.Name}\" создана", isError: false);
            NewBackupName = string.Empty;
            NewBackupSource = string.Empty;
            NewBackupDestination = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка создания задачи: {ex.Message}", isError: true);
        }
    }

    public async Task SaveBackupAsync()
    {
        if (SelectedBackupJob is null)
        {
            SetStatus("Выберите задачу для редактирования", isError: true);
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
        SetStatus(updated is null
            ? "Не удалось сохранить изменения задачи"
            : $"Задача \"{updated.Name}\" обновлена", isError: updated is null);
        await RefreshAsync();
    }

    public async Task DeleteBackupAsync()
    {
        if (SelectedBackupJob is null)
        {
            SetStatus("Выберите задачу для удаления", isError: true);
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var deleted = await _apiClient.DeleteBackupJobAsync(ApiBaseUrl, SelectedBackupJob.Id, cts.Token);
        SetStatus(deleted ? "Задача удалена" : "Не удалось удалить задачу", isError: !deleted);
        await RefreshAsync();
    }

    public async Task RunBackupAsync()
    {
        if (SelectedBackupJob is null)
        {
            SetStatus("Выберите задачу для запуска", isError: true);
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var log = await _apiClient.RunBackupJobAsync(ApiBaseUrl, SelectedBackupJob.Id, cts.Token);
        SetStatus(log is null ? "Не удалось запустить задачу" : $"Запуск завершен: {log.Status}", isError: log is null);
        await RefreshAsync();
    }

    public async Task ToggleBackupStateAsync()
    {
        if (SelectedBackupJob is null)
        {
            SetStatus("Выберите задачу для включения/выключения", isError: true);
            return;
        }

        SelectedBackupJob.IsEnabled = !SelectedBackupJob.IsEnabled;
        await SaveBackupAsync();
    }

    public Task SaveSettingsAsync()
    {
        try
        {
            _startupRegistrationService.SetEnabled(LaunchAtStartup);
            ThemeService.Apply(SelectedTheme);
            SetStatus("Настройки применены", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка сохранения настроек: {ex.Message}", isError: true);
        }

        return Task.CompletedTask;
    }

    private void SetStatus(string message, bool isError)
    {
        OperationStatus = message;
        if (isError)
        {
            System.Windows.MessageBox.Show(message, "AriaSignature", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private static string MapCronPreset(string preset, string customCron)
    {
        return preset switch
        {
            "Ежедневно" => "0 0 2 * * ?",
            "Еженедельно" => "0 0 2 ? * MON",
            "Ежемесячно" => "0 0 2 1 * ?",
            "Пользовательский" when !string.IsNullOrWhiteSpace(customCron) => customCron,
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
