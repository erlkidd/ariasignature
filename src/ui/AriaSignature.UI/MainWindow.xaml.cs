using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using AriaSignature.UI.Services;
using Microsoft.Web.WebView2.Core;

namespace AriaSignature.UI;

public partial class MainWindow : Window
{
    private const string DefaultApiBase = "http://127.0.0.1:5160";
    private readonly App _app;
    private readonly StartupRegistrationService _startup = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        TrySetWindowIcon();
        Loaded += OnLoadedAsync;
        Closing += OnClosingToTray;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        WindowsServiceEnsure.TryStart(TimeSpan.FromSeconds(60), out var err);
        if (!string.IsNullOrEmpty(err))
        {
            System.Windows.MessageBox.Show(
                err,
                "AriaSignature",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var baseUrl = await ResolveApiBaseAsync() ?? DefaultApiBase;
        try
        {
            await Browser.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Не удалось инициализировать WebView2. Установите компонент «Evergreen WebView2 Runtime» от Microsoft.\n{ex.Message}",
                "AriaSignature",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        Browser.CoreWebView2.WebMessageReceived += OnWebMessage;
        Browser.CoreWebView2.NavigationCompleted += async (_, _) =>
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { action = "autostart", enabled = _startup.IsEnabled() });
                Browser.CoreWebView2.PostWebMessageAsString(payload);
            }
            catch
            {
                // ignore
            }
        };

        Browser.Source = new Uri($"{baseUrl.TrimEnd('/')}/");
    }

    private static async Task<string?> ResolveApiBaseAsync()
    {
        try
        {
            using var response = await Http.GetAsync($"{DefaultApiBase}/api/v1/settings");
            if (!response.IsSuccessStatusCode)
            {
                return DefaultApiBase;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (doc.RootElement.TryGetProperty("apiPort", out var portEl) &&
                portEl.TryGetInt32(out var port) &&
                port is > 0 and < 65536)
            {
                return $"http://127.0.0.1:{port}";
            }
        }
        catch
        {
            // use default
        }

        return DefaultApiBase;
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(json))
            {
                json = e.WebMessageAsJson;
            }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("action", out var actionEl))
            {
                return;
            }

            var action = actionEl.GetString();
            if (action == "setAutostart" && root.TryGetProperty("enabled", out var en))
            {
                _startup.SetEnabled(en.GetBoolean());
                var payload = JsonSerializer.Serialize(new { action = "autostart", enabled = _startup.IsEnabled() });
                Browser.CoreWebView2?.PostWebMessageAsString(payload);
            }
            else if (action == "getAutostart")
            {
                var payload = JsonSerializer.Serialize(new { action = "autostart", enabled = _startup.IsEnabled() });
                Browser.CoreWebView2?.PostWebMessageAsString(payload);
            }
            else if (action == "pickFile")
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "База 1С (*.1CD)|*.1CD|Все файлы (*.*)|*.*",
                    CheckFileExists = true
                };

                if (dlg.ShowDialog() == true)
                {
                    var payload = JsonSerializer.Serialize(new { action = "pickedFile", path = dlg.FileName });
                    Browser.CoreWebView2?.PostWebMessageAsString(payload);
                }
            }
            else if (action == "pickFolder")
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Папка для архивов"
                };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
                {
                    var payload = JsonSerializer.Serialize(new { action = "pickedFolder", path = dlg.SelectedPath });
                    Browser.CoreWebView2?.PostWebMessageAsString(payload);
                }
            }
        }
        catch
        {
            // ignore malformed messages
        }
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
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (!File.Exists(iconPath))
        {
            return;
        }

        Icon = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
    }
}
