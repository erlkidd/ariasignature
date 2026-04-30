using System.Windows;
using System.Windows.Media.Imaging;
using AriaSignature.Domain.Enums;
using AriaSignature.UI.ViewModels;
using Forms = System.Windows.Forms;

namespace AriaSignature.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly App _app;

    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        TrySetWindowIcon();
        Closing += OnClosingToTray;
    }

    private void OnClosingToTray(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_app.CanCloseToTray())
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void TrySetWindowIcon()
    {
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (!System.IO.File.Exists(iconPath))
        {
            return;
        }

        Icon = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
    }

    private void OnPickSourceClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        if (vm.NewBackupType == BackupType.File)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "1C database (*.1CD)|*.1CD|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == true)
            {
                vm.NewBackupSource = dialog.FileName;
            }

            return;
        }

        Forms.MessageBox.Show("Для MSSQL укажите строку подключения вручную в поле 'Источник'.", "AriaSignature");
    }

    private void OnPickDestinationClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Выберите папку для архивов",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            vm.NewBackupDestination = dialog.SelectedPath;
        }
    }
}