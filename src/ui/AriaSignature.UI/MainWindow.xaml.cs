using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.ServiceProcess;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using AriaSignature.UI.Services;
using Microsoft.Web.WebView2.Core;

namespace AriaSignature.UI;

public partial class MainWindow : Window
{
    private const string DefaultApiBase = "http://127.0.0.1:5160";
    private readonly App _app;
    private readonly StartupRegistrationService _startup = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private CancellationTokenSource? _startupRetryCts;

    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        TrySetWindowIcon();
        Loaded += OnLoadedAsync;
        Closing += OnClosingToTray;
        StateChanged += (_, _) => MaxRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        var serviceExePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "service", "AriaSignature.Service.exe"));
        var baseUrl = await ResolveApiBaseAsync() ?? DefaultApiBase;

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
        Browser.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
                args.Handled = true;
            }
            catch
            {
                // ignore
            }
        };
        Browser.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                RenderFallbackPage(
                    "Не удалось открыть панель",
                    "UI не получил страницу от локального API. Сервис должен быть запущен и доступен на localhost.",
                    baseUrl,
                    (int)e.WebErrorStatus,
                    e.WebErrorStatus.ToString());
                StartApiRecoveryLoop(baseUrl);
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

        // Не блокируем UI на старте: WebView2/fallback должны отрисоваться сразу,
        // даже если служба запускается долго.
        _ = Task.Run(() =>
        {
            WindowsServiceEnsure.TryStartOrFallback(serviceExePath, TimeSpan.FromSeconds(15), out var warning);
            if (!string.IsNullOrEmpty(warning))
            {
                Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show(
                        warning,
                        "AriaSignature",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
        });

        var (apiReady, apiDetail) = await WaitApiReadyAsync(baseUrl, TimeSpan.FromSeconds(20));
        if (!apiReady)
        {
            RenderFallbackPage(
                "Сервис еще запускается",
                "Локальный API пока не готов. Окно не будет пустым: приложение продолжит ожидание и автоматически откроет интерфейс.",
                baseUrl,
                null,
                apiDetail);
            StartApiRecoveryLoop(baseUrl);
            return;
        }

        Browser.Source = new Uri($"{baseUrl.TrimEnd('/')}/");
    }

    private void StartApiRecoveryLoop(string baseUrl)
    {
        _startupRetryCts?.Cancel();
        _startupRetryCts?.Dispose();
        _startupRetryCts = new CancellationTokenSource();
        var token = _startupRetryCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var (ready, detail) = await WaitApiReadyAsync(baseUrl, TimeSpan.FromSeconds(3));
                if (ready)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        Browser.Source = new Uri($"{baseUrl.TrimEnd('/')}/");
                    });
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    RenderFallbackPage(
                        "Сервис еще запускается",
                        "Локальный API пока не готов. Приложение продолжает автоматическое восстановление.",
                        baseUrl,
                        null,
                        detail);
                });

                await Task.Delay(2000, token);
            }
        }, token);
    }

    private void RenderFallbackPage(string title, string details, string baseUrl, int? code = null, string? codeName = null, string? probeDetail = null)
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        var errorCodeText = code is int c
            ? $"<p>Код ошибки WebView2: <strong>{c}</strong> ({System.Net.WebUtility.HtmlEncode(codeName ?? "unknown")})</p>"
            : string.Empty;
        var probeText = string.IsNullOrWhiteSpace(probeDetail)
            ? string.Empty
            : "<p>Диагностика запуска API: <code>" + System.Net.WebUtility.HtmlEncode(probeDetail) + "</code></p>";
        var html =
            "<!DOCTYPE html><html lang=\"ru\"><head><meta charset=\"utf-8\"/><title>AriaSignature</title>" +
            "<style>body{font-family:Segoe UI,sans-serif;padding:24px;background:#111;color:#eee;max-width:760px}" +
            "code{background:#222;padding:2px 6px} .muted{color:#aaa}</style></head><body>" +
            "<h1>" + System.Net.WebUtility.HtmlEncode(title) + "</h1>" +
            "<p>" + System.Net.WebUtility.HtmlEncode(details) + "</p>" +
            "<p>Ожидаемый адрес: <code>" + System.Net.WebUtility.HtmlEncode($"{baseUrl.TrimEnd('/')}/") + "</code></p>" +
            errorCodeText +
            probeText +
            "<p class=\"muted\">Проверьте службу AriaSignatureService и доступность localhost. " +
            "После восстановления сервиса окно автоматически загрузит интерфейс.</p></body></html>";
        Browser.CoreWebView2.NavigateToString(html);
    }

    private static async Task<(bool Ready, string Detail)> WaitApiReadyAsync(string baseUrl, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var lastDetail = "таймаут ожидания API";
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var statusResponse = await Http.GetAsync($"{baseUrl.TrimEnd('/')}/api/v1/status");
                if (!statusResponse.IsSuccessStatusCode)
                {
                    lastDetail = $"/api/v1/status => {(int)statusResponse.StatusCode}";
                    await Task.Delay(1000);
                    continue;
                }

                using var rootResponse = await Http.GetAsync($"{baseUrl.TrimEnd('/')}/");
                if (rootResponse.IsSuccessStatusCode)
                {
                    return (true, "ready");
                }

                lastDetail = $"/ => {(int)rootResponse.StatusCode}";
            }
            catch (Exception ex)
            {
                lastDetail = ex.Message;
            }

            await Task.Delay(1000);
        }

        return (false, lastDetail);
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
            else if (action == "getWindowsServiceStatus")
            {
                var payload = JsonSerializer.Serialize(TryGetWindowsServiceStatus());
                Browser.CoreWebView2?.PostWebMessageAsString(payload);
            }
            else if (action == "controlWindowsService" && root.TryGetProperty("command", out var cmdEl))
            {
                var cmd = cmdEl.GetString();
                var payload = JsonSerializer.Serialize(TryControlWindowsService(cmd));
                Browser.CoreWebView2?.PostWebMessageAsString(payload);
                var statusPayload = JsonSerializer.Serialize(TryGetWindowsServiceStatus());
                Browser.CoreWebView2?.PostWebMessageAsString(statusPayload);
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

    private static object TryGetWindowsServiceStatus()
    {
        try
        {
            using var sc = new ServiceController(WindowsServiceEnsure.ServiceName);
            sc.Refresh();
            return new
            {
                action = "windowsServiceStatus",
                ok = true,
                status = NormalizeWindowsServiceStatus(sc.Status.ToString()),
                displayName = sc.DisplayName ?? WindowsServiceEnsure.ServiceName
            };
        }
        catch (Exception ex)
        {
            return new
            {
                action = "windowsServiceStatus",
                ok = false,
                status = "Unknown",
                error = ex.Message
            };
        }
    }

    private static object TryControlWindowsService(string? command)
    {
        try
        {
            using var sc = new ServiceController(WindowsServiceEnsure.ServiceName);
            switch (command?.ToLowerInvariant())
            {
                case "start":
                    if (sc.Status == ServiceControllerStatus.Stopped)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(90));
                    }

                    break;
                case "stop":
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(90));
                    }

                    break;
                case "restart":
                    if (sc.Status == ServiceControllerStatus.Running)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(90));
                    }

                    sc.Refresh();
                    if (sc.Status == ServiceControllerStatus.Stopped)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(90));
                    }

                    break;
                default:
                    return new { action = "windowsServiceControl", ok = false, error = "Неизвестная команда" };
            }

            sc.Refresh();
            return new { action = "windowsServiceControl", ok = true, status = NormalizeWindowsServiceStatus(sc.Status.ToString()) };
        }
        catch (Exception ex)
        {
            return new { action = "windowsServiceControl", ok = false, error = ex.Message };
        }
    }

    private static string NormalizeWindowsServiceStatus(string raw)
    {
        return raw.Trim().ToLowerInvariant() switch
        {
            "running" => "Running",
            "stopped" => "Stopped",
            "paused" => "Paused",
            "startpending" => "StartPending",
            "stoppending" => "StopPending",
            "pausepending" => "PausePending",
            "continuepending" => "ContinuePending",
            _ => raw
        };
    }

    private void OnClosingToTray(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _startupRetryCts?.Cancel();
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            // Поведение как у системного окна: начать перетаскивание из Maximized.
            var point = e.GetPosition(this);
            var widthRatio = ActualWidth > 0 ? point.X / ActualWidth : 0.5;
            WindowState = WindowState.Normal;
            Left = Math.Max(0, e.GetPosition(null).X - (RestoreBounds.Width * widthRatio));
            Top = Math.Max(0, e.GetPosition(null).Y - 12);
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaxRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaxRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }
}
