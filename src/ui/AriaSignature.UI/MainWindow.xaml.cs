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
        var serviceExePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "service", "AriaSignature.Service.exe"));
        WindowsServiceEnsure.TryStartOrFallback(serviceExePath, TimeSpan.FromSeconds(60), out var warning);
        if (!string.IsNullOrEmpty(warning))
        {
            System.Windows.MessageBox.Show(
                warning,
                "AriaSignature",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var baseUrl = await ResolveApiBaseAsync() ?? DefaultApiBase;
        if (!await WaitApiReadyAsync(baseUrl, TimeSpan.FromSeconds(30)))
        {
            System.Windows.MessageBox.Show(
                "Локальный API не поднялся в ожидаемое время. Проверьте антивирус/брандмауэр и попробуйте перезапустить приложение.\n" +
                $"Ожидаемый адрес: {baseUrl}",
                "AriaSignature",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AriaSignature",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await Browser.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Не удалось инициализировать WebView2.\nПроверьте, что установлен «Evergreen WebView2 Runtime», и что есть доступ к папке профиля в %LocalAppData%.\n{ex.Message}",
                "AriaSignature",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        Browser.CoreWebView2.WebMessageReceived += OnWebMessage;
        Browser.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                var status = (int)e.WebErrorStatus;
                var html =
                    "<!DOCTYPE html><html lang=\"ru\"><head><meta charset=\"utf-8\"/><title>AriaSignature</title>" +
                    "<style>body{font-family:Segoe UI,sans-serif;padding:24px;background:#111;color:#eee;max-width:720px}" +
                    "code{background:#222;padding:2px 6px}</style></head><body>" +
                    "<h1>Не удалось открыть панель</h1>" +
                    "<p>Адрес: <code>" + System.Net.WebUtility.HtmlEncode($"{baseUrl.TrimEnd('/')}/") + "</code></p>" +
                    "<p>Код ошибки WebView2: <strong>" + status + "</strong> (" +
                    System.Net.WebUtility.HtmlEncode(e.WebErrorStatus.ToString()) + ")</p>" +
                    "<p>Проверьте, что локальный сервис запущен (служба AriaSignatureService или процесс AriaSignature.Service.exe), " +
                    "антивирус не блокирует <code>127.0.0.1</code> и порт из настроек API.</p></body></html>";
                Browser.CoreWebView2.NavigateToString(html);
                return;
            }

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

    private static async Task<bool> WaitApiReadyAsync(string baseUrl, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await Http.GetAsync($"{baseUrl.TrimEnd('/')}/api/v1/status");
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // retry
            }

            await Task.Delay(1000);
        }

        return false;
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
