using System.Collections.ObjectModel;
using AriaSignature.Domain.Entities;

namespace AriaSignature.UI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
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
}
