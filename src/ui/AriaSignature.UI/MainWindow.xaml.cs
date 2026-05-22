using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using AriaSignature.UI.Services;
using Microsoft.Web.WebView2.Core;

namespace AriaSignature.UI;

public partial class MainWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private static readonly IntPtr MonitorDefaultToNearest = new(2);
    private const string DefaultApiBase = "http://127.0.0.1:5160";
    private const int DefaultMelezhPort = 7788;
    private readonly App _app;
    private readonly StartupRegistrationService _startup = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly HttpClient StartupProbeHttp = new() { Timeout = TimeSpan.FromSeconds(2.5) };
    private CancellationTokenSource? _startupRetryCts;
    private DateTime _recoveryServiceEnsureNotBeforeUtc = DateTime.MinValue;
    private DateTime _recoveryMelezhEnsureNotBeforeUtc = DateTime.MinValue;
    private CancellationTokenSource? _appReadyFallbackCts;
    private bool _expectStartupLoadingHtml;
    private string? _deferredServiceStartWarning;
    private string? _startupBaseUrl;
    private string? _serviceExePath;
    private string? _serviceBootstrapExePath;
    private const string MelezhServiceName = "AriaSignatureMelezhService";
    private Stopwatch? _startupSw;
    private bool _startupFlowStarted;
    private bool _startupWarmupPrepared;
    private bool _awaitingAppReady;
    private string? _pendingLocalApiNavigation;
    private DateTime _lastMelezhRestartUtc = DateTime.MinValue;
    private int _melezhWebUiFailStreak;
    private static readonly TimeSpan StartupMaxWait = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan MelezhRestartCooldown = TimeSpan.FromSeconds(45);

    private sealed class StartupReadinessState
    {
        public bool MelezhBundlePresent;
        public string? BundleError;
        public bool MelezhRequired = true;
        public bool AriaServiceRunning;
        public bool ApiReady;
        public string? ApiDetail;
        public bool MelezhServiceRunning;
        public bool MelezhWebUiOk;
        public bool MelezhPingOk;
        public string? MelezhDetail;
        public int MelezhPort = DefaultMelezhPort;

        public bool IsReady =>
            MelezhBundlePresent
            && AriaServiceRunning
            && ApiReady
            && (!MelezhRequired
                || (MelezhServiceRunning && MelezhWebUiOk && MelezhPingOk));
    }

    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        TrySetWindowIcon();
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoadedAsync;
        IsVisibleChanged += (_, _) => TryStartStartupFlowIfVisible();
        Closing += OnClosingToTray;
        StateChanged += (_, _) =>
        {
            MaxRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            TryStartStartupFlowIfVisible();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            WmGetMinMaxInfoHandler(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void WmGetMinMaxInfoHandler(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MonitorInfo();
            monitorInfo.Size = Marshal.SizeOf<MonitorInfo>();
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var workArea = monitorInfo.WorkArea;
                var monitorArea = monitorInfo.MonitorArea;
                mmi.MaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
                mmi.MaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
                mmi.MaxSize.X = Math.Abs(workArea.Right - workArea.Left);
                mmi.MaxSize.Y = Math.Abs(workArea.Bottom - workArea.Top);
            }
        }

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        _startupSw = Stopwatch.StartNew();
        _serviceExePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "service", "AriaSignature.Service.exe"));
        _serviceBootstrapExePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "bootstrap", "AriaSignature.ServiceBootstrap.exe"));
        ShowLoadingOverlay("Проверка адреса API…", "Краткий запрос к локальному сервису…");
        _startupBaseUrl = await ResolveApiBaseAsync();

        ShowLoadingOverlay("Инициализация WebView2…", "При первом запуске это может занять до минуты. Убедитесь, что установлен WebView2 Runtime.");

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AriaSignature",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            using var ensureCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await Browser.EnsureCoreWebView2Async(env).WaitAsync(ensureCts.Token);
        }
        catch (OperationCanceledException)
        {
            HideLoadingOverlay();
            System.Windows.MessageBox.Show(
                "Превышено время ожидания инициализации WebView2 (2 минуты).\n\n" +
                "Проверьте:\n" +
                "— установлен Evergreen WebView2 Runtime;\n" +
                "— доступ к папке %LocalAppData%\\AriaSignature\\WebView2;\n" +
                "— антивирус не блокирует процесс.",
                "AriaSignature",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        catch (Exception ex)
        {
            HideLoadingOverlay();
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
        Browser.CoreWebView2.NavigationStarting += (_, navArgs) =>
        {
            if (IsLocalApiUrl(navArgs.Uri, _startupBaseUrl ?? DefaultApiBase))
            {
                _pendingLocalApiNavigation = navArgs.Uri;
            }
        };
        Browser.CoreWebView2.NavigationCompleted += (_, navArgs) =>
            OnBrowserNavigationCompleted(navArgs, _startupBaseUrl ?? DefaultApiBase);
        _startupWarmupPrepared = true;
        _ = Task.Run(() => WindowsServiceAutostartConfigurator.EnsureDefaultAutostartApplied(_startup));
        TryStartStartupFlowIfVisible();
    }

    private void ShowLoadingOverlay(string message, string? hint = null)
    {
        LoadingOverlayMessage.Text = message;
        if (!string.IsNullOrEmpty(hint))
        {
            LoadingOverlayHint.Text = hint;
        }

        WebViewLoadingOverlay.Visibility = Visibility.Visible;
    }

    private void HideLoadingOverlay()
    {
        WebViewLoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnBrowserNavigationCompleted(CoreWebView2NavigationCompletedEventArgs e, string baseUrl)
    {
        if (!e.IsSuccess)
        {
            var failedNavSource = Browser.CoreWebView2?.Source ?? string.Empty;
            var elapsedSinceUiLoad = _startupSw?.Elapsed ?? TimeSpan.Zero;
            var status = TryGetServiceControllerStatus();
            if (IsAttemptedLocalApiNavigation(failedNavSource, baseUrl)
                && IsRecoverableStartupNavError(e.WebErrorStatus, elapsedSinceUiLoad)
                && !ShouldShowHardStartupFailure(
                    elapsedSinceUiLoad,
                    status,
                    serviceEnsureCompleted: true,
                    apiReady: true))
            {
                _awaitingAppReady = false;
                CancelAppReadyFallback();
                _expectStartupLoadingHtml = false;
                ShowLoadingOverlay(
                    "Подключение к сервису…",
                    $"Временный обрыв связи с API, повторяем… ({(int)elapsedSinceUiLoad.TotalSeconds} с)");
                StartApiRecoveryLoop(baseUrl);
                return;
            }

            _awaitingAppReady = false;
            CancelAppReadyFallback();
            HideLoadingOverlay();
            _expectStartupLoadingHtml = false;
            RenderFallbackPage(
                "Не удалось открыть панель",
                "UI не получил страницу от локального API. Сервис должен быть запущен и доступен на localhost.",
                baseUrl,
                (int)e.WebErrorStatus,
                e.WebErrorStatus.ToString());
            StartApiRecoveryLoop(baseUrl);
            return;
        }

        var uri = Browser.CoreWebView2?.Source ?? string.Empty;
        if (uri.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || uri.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
        {
            _pendingLocalApiNavigation = null;
            _awaitingAppReady = true;
            ScheduleAppReadyFallbackHide();
            _expectStartupLoadingHtml = false;
            TryPostAutostartPayload();
            return;
        }

        if (_expectStartupLoadingHtml
            && (string.IsNullOrEmpty(uri) || uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)))
        {
            ShowLoadingOverlay("Подготовка служб…", "AriaSignature :5160 и Melezh :7788…");
            TryPostAutostartPayload();
            return;
        }

        HideLoadingOverlay();
        _awaitingAppReady = false;
        CancelAppReadyFallback();
        _expectStartupLoadingHtml = false;
        TryPostAutostartPayload();
    }

    private void TryPostAutostartPayload()
    {
        try
        {
            if (Browser.CoreWebView2 is null)
            {
                return;
            }

            Browser.CoreWebView2.PostWebMessageAsString(
                WindowsServiceAutostartConfigurator.SerializeAutostartWebMessage(_startup));
        }
        catch
        {
            // ignore
        }
    }

    private void TryStartStartupFlowIfVisible()
    {
        if (!_startupWarmupPrepared || _startupFlowStarted)
        {
            return;
        }

        if (!IsLoaded || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_startupBaseUrl) || string.IsNullOrWhiteSpace(_serviceExePath) || _startupSw is null)
        {
            return;
        }

        _startupFlowStarted = true;
        RenderStartupLoadingPage();
        _ = PollUntilReadyAndNavigateAsync(_startupBaseUrl, _startupSw, _serviceExePath);
    }

    private void ScheduleAppReadyFallbackHide()
    {
        CancelAppReadyFallback();
        _appReadyFallbackCts = new CancellationTokenSource();
        var token = _appReadyFallbackCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(45), token).ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (!_awaitingAppReady)
                    {
                        return;
                    }

                    _awaitingAppReady = false;
                    HideLoadingOverlay();
                }, DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
                // expected on navigation or appReady
            }
        }, token);
    }

    private void CancelAppReadyFallback()
    {
        _appReadyFallbackCts?.Cancel();
        _appReadyFallbackCts?.Dispose();
        _appReadyFallbackCts = null;
    }

    private void StartApiRecoveryLoop(string baseUrl)
    {
        _startupRetryCts?.Cancel();
        _startupRetryCts?.Dispose();
        _startupRetryCts = new CancellationTokenSource();
        var token = _startupRetryCts.Token;
        var sw = _startupSw ?? Stopwatch.StartNew();
        var exe = _serviceExePath ?? string.Empty;
        _ = Task.Run(async () =>
        {
            try
            {
                await RunStartupReadinessUntilNavigateOrFailAsync(baseUrl, sw, exe, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // recovery cancelled
            }
        }, token);
    }

    private async Task PollUntilReadyAndNavigateAsync(string baseUrl, Stopwatch startupSw, string serviceExePath)
    {
        using var cts = new CancellationTokenSource();
        await RunStartupReadinessUntilNavigateOrFailAsync(baseUrl, startupSw, serviceExePath, cts.Token).ConfigureAwait(false);
    }

    private async Task RunStartupReadinessUntilNavigateOrFailAsync(
        string baseUrl,
        Stopwatch startupSw,
        string serviceExePath,
        CancellationToken token)
    {
        var loadStart = DateTime.UtcNow;
        _deferredServiceStartWarning = null;
        _melezhWebUiFailStreak = 0;
        var lastOverlaySecond = -1;
        var melezhPort = DefaultMelezhPort;
        bool? melezhRequired = null;
        var ensureTask = Task.Run(() =>
        {
            WindowsServiceEnsure.TryStartOrFallback(serviceExePath, TimeSpan.FromSeconds(30), _serviceBootstrapExePath, out var warning);
            _deferredServiceStartWarning = warning;
        }, token);
        _ = Task.Run(TryEnsureMelezhServiceStartedBestEffort, token);

        while (!token.IsCancellationRequested)
        {
            var bundle = ValidateMelezhBundle();
            if (!bundle.Ok)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _expectStartupLoadingHtml = false;
                    RenderFallbackPage(
                        "Компоненты Melezh не найдены",
                        bundle.Error ?? "Переустановите AriaSignature с полным bundle OInt/Melezh.",
                        baseUrl,
                        null,
                        "bundle-missing");
                }, DispatcherPriority.Background);
                return;
            }

            if (!melezhRequired.HasValue)
            {
                var settings = await TryReadMelezhSettingsAsync(baseUrl).ConfigureAwait(false);
                melezhRequired = settings.Enabled;
                melezhPort = settings.Port;
            }

            var state = await EvaluateStartupReadinessAsync(baseUrl, melezhPort, melezhRequired == true).ConfigureAwait(false);

            if (state.IsReady)
            {
                await NavigateToPanelAsync(baseUrl).ConfigureAwait(false);
                return;
            }

            await ApplyStartupRepairAsync(state, serviceExePath, token).ConfigureAwait(false);

            var elapsed = DateTime.UtcNow - loadStart;
            var ariaStatus = TryGetServiceControllerStatus();
            var melezhStatus = TryGetMelezhServiceControllerStatus();
            var ensureDone = ensureTask.IsCompleted;

            if (elapsed >= StartupMaxWait
                || ShouldShowHardStartupFailure(elapsed, ariaStatus, ensureDone, state.ApiReady))
            {
                var apiDetail = state.ApiDetail;
                var melezhDetail = state.MelezhDetail;
                await Dispatcher.InvokeAsync(() =>
                {
                    _expectStartupLoadingHtml = false;
                    var warn = string.IsNullOrWhiteSpace(_deferredServiceStartWarning)
                        ? string.Empty
                        : " Дополнительно: " + _deferredServiceStartWarning;
                    var stage = BuildStartupFailureStage(
                        elapsed,
                        startupSw.Elapsed,
                        ariaStatus,
                        melezhStatus,
                        ensureDone,
                        state.MelezhRequired,
                        _deferredServiceStartWarning);
                    RenderFallbackPage(
                        "Службы не готовы к открытию панели",
                        "Ожидаются AriaSignatureService (API :5160) и AriaSignatureMelezhService (Web UI :7788). " +
                        "Автовосстановление не завершилось — используйте «Восстановить службу Melezh» в настройках после открытия или переустановите продукт." + warn,
                        baseUrl,
                        null,
                        $"API: {apiDetail}; Melezh: {melezhDetail}",
                        stage);
                    StartApiRecoveryLoop(baseUrl);
                }, DispatcherPriority.Background);
                return;
            }

            var sec = (int)elapsed.TotalSeconds;
            if (sec != lastOverlaySecond)
            {
                lastOverlaySecond = sec;
                var (overlayTitle, overlayHint) = BuildStartupOverlayMessage(sec, state);
                await Dispatcher.InvokeAsync(() =>
                {
                    ShowLoadingOverlay(overlayTitle, overlayHint);
                }, DispatcherPriority.Background);
            }

            await Task.Delay(850, token).ConfigureAwait(false);
        }
    }

    private async Task NavigateToPanelAsync(string baseUrl)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            _expectStartupLoadingHtml = false;
            Browser.Source = new Uri($"{baseUrl.TrimEnd('/')}/");
        }, DispatcherPriority.Background);
    }

    private static (bool Ok, string? Error) ValidateMelezhBundle()
    {
        var hostExe = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "melezh-host", "AriaSignature.MelezhHost.exe"));
        var melezhBat = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "melezh", "bin", "melezh.bat"));
        if (!File.Exists(hostExe))
        {
            return (false, $"Не найден Melezh host: {hostExe}");
        }

        if (!File.Exists(melezhBat))
        {
            return (false, $"Не найден OInt bundle (melezh.bat): {melezhBat}");
        }

        return (true, null);
    }

    private async Task<StartupReadinessState> EvaluateStartupReadinessAsync(
        string baseUrl,
        int melezhPort,
        bool melezhRequired)
    {
        var bundle = ValidateMelezhBundle();
        var state = new StartupReadinessState
        {
            MelezhBundlePresent = bundle.Ok,
            BundleError = bundle.Error,
            MelezhRequired = melezhRequired,
            MelezhPort = melezhPort,
        };

        state.AriaServiceRunning = TryGetServiceControllerStatus() == ServiceControllerStatus.Running;
        (state.ApiReady, state.ApiDetail) = await TryProbeApiOnceAsync(baseUrl).ConfigureAwait(false);

        if (!melezhRequired)
        {
            state.MelezhServiceRunning = true;
            state.MelezhWebUiOk = true;
            state.MelezhPingOk = true;
            return state;
        }

        state.MelezhServiceRunning = TryGetMelezhServiceControllerStatus() == ServiceControllerStatus.Running;
        (state.MelezhWebUiOk, var webDetail) = await TryProbeMelezhWebUiAsync(melezhPort).ConfigureAwait(false);
        (state.MelezhPingOk, var pingDetail) = await TryProbeMelezhPingAsync(melezhPort).ConfigureAwait(false);
        state.MelezhDetail = !state.MelezhWebUiOk
            ? webDetail
            : !state.MelezhPingOk
                ? pingDetail
                : null;
        return state;
    }

    private async Task ApplyStartupRepairAsync(
        StartupReadinessState state,
        string serviceExePath,
        CancellationToken token)
    {
        if (!state.AriaServiceRunning || !state.ApiReady)
        {
            await Task.Run(() =>
                WindowsServiceEnsure.TryStartOrFallback(
                    serviceExePath,
                    TimeSpan.FromSeconds(30),
                    _serviceBootstrapExePath,
                    out _), token).ConfigureAwait(false);
        }

        if (!state.MelezhRequired)
        {
            return;
        }

        if (!state.MelezhServiceRunning)
        {
            await Task.Run(TryEnsureMelezhServiceStartedBestEffort, token).ConfigureAwait(false);
            return;
        }

        if (!state.MelezhWebUiOk)
        {
            _melezhWebUiFailStreak++;
            if (_melezhWebUiFailStreak >= 20)
            {
                RequestMelezhServiceRestartIfDue();
                _melezhWebUiFailStreak = 0;
            }

            return;
        }

        _melezhWebUiFailStreak = 0;

        if (!state.MelezhPingOk)
        {
            RequestMelezhServiceRestartIfDue();
        }
    }

    private void RequestMelezhServiceRestartIfDue()
    {
        var now = DateTime.UtcNow;
        if (now - _lastMelezhRestartUtc < MelezhRestartCooldown)
        {
            return;
        }

        _lastMelezhRestartUtc = now;
        _ = Task.Run(TryRestartMelezhServiceBestEffort);
    }

    private static void TryRestartMelezhServiceBestEffort()
    {
        try
        {
            using var sc = new ServiceController(MelezhServiceName);
            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
        }
        catch
        {
            // best-effort
        }
    }

    private static (string Title, string Hint) BuildStartupOverlayMessage(int seconds, StartupReadinessState state)
    {
        if (!state.AriaServiceRunning)
        {
            return (
                "Запуск AriaSignature…",
                $"Служба AriaSignatureService… ({seconds} с){FormatProbeSuffix(state.ApiDetail)}");
        }

        if (!state.ApiReady)
        {
            return (
                "Запуск API агента…",
                $"Ожидаем ответ :5160… ({seconds} с){FormatProbeSuffix(state.ApiDetail)}");
        }

        if (!state.MelezhRequired)
        {
            return (
                "Подключение к панели…",
                $"Загружаем интерфейс… ({seconds} с)");
        }

        if (!state.MelezhServiceRunning)
        {
            return (
                "Запуск Melezh…",
                $"Служба AriaSignatureMelezhService… ({seconds} с){FormatProbeSuffix(state.MelezhDetail)}");
        }

        if (!state.MelezhWebUiOk)
        {
            return (
                "Запуск Web UI Melezh…",
                $"Ожидаем http://127.0.0.1:{state.MelezhPort}/ui … ({seconds} с){FormatProbeSuffix(state.MelezhDetail)}");
        }

        if (!state.MelezhPingOk)
        {
            return (
                "Настройка handlers Melezh…",
                $"Bootstrap aria_ping / aria_* … ({seconds} с){FormatProbeSuffix(state.MelezhDetail)}");
        }

        return (
            "Подключение к панели…",
            $"Все службы готовы, загружаем интерфейс… ({seconds} с)");
    }

    private static string FormatProbeSuffix(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? string.Empty : $" · {detail}";

    private static string FormatServiceStatus(ServiceControllerStatus? status) =>
        status?.ToString() ?? "не удалось опросить";

    private static void TryEnsureMelezhServiceStartedBestEffort()
    {
        try
        {
            using var sc = new ServiceController(MelezhServiceName);
            sc.Refresh();
            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(25));
            }
        }
        catch
        {
            // best-effort only: do not block UI startup path
        }
    }

    private static ServiceControllerStatus? TryGetServiceControllerStatus()
    {
        try
        {
            using var sc = new ServiceController(WindowsServiceEnsure.ServiceName);
            sc.Refresh();
            return sc.Status;
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldShowHardStartupFailure(
        TimeSpan elapsed,
        ServiceControllerStatus? ariaStatus,
        bool serviceEnsureCompleted,
        bool apiReady) =>
        ShouldShowHardStartupFailureForService(elapsed, ariaStatus, serviceEnsureCompleted, apiReady);

    private static bool ShouldShowHardStartupFailureForService(
        TimeSpan elapsed,
        ServiceControllerStatus? status,
        bool serviceEnsureCompleted,
        bool httpReady)
    {
        const int ensureHardCapSeconds = 210;
        const int stoppedGraceAfterEnsureSeconds = 90;

        if (status == ServiceControllerStatus.StartPending)
        {
            return elapsed >= TimeSpan.FromMinutes(4);
        }

        if (status == ServiceControllerStatus.Running && !httpReady && elapsed >= TimeSpan.FromSeconds(90))
        {
            return true;
        }

        if (status == ServiceControllerStatus.Stopped)
        {
            if (!serviceEnsureCompleted)
            {
                return elapsed >= TimeSpan.FromSeconds(ensureHardCapSeconds);
            }

            return elapsed >= TimeSpan.FromSeconds(stoppedGraceAfterEnsureSeconds);
        }

        if (!status.HasValue)
        {
            if (!serviceEnsureCompleted && elapsed < TimeSpan.FromSeconds(ensureHardCapSeconds))
            {
                return false;
            }

            return elapsed >= TimeSpan.FromSeconds(120);
        }

        return false;
    }

    private static string BuildStartupFailureStage(
        TimeSpan elapsed,
        TimeSpan appElapsed,
        ServiceControllerStatus? ariaStatus,
        ServiceControllerStatus? melezhStatus,
        bool serviceEnsureCompleted,
        bool melezhRequired,
        string? warning)
    {
        var baseStage =
            $"Ожидание: {elapsed.TotalSeconds:F0} с · AriaSignature: {FormatServiceStatus(ariaStatus)}" +
            (melezhRequired ? $" · Melezh: {FormatServiceStatus(melezhStatus)}" : string.Empty) +
            $" · ensure: {(serviceEnsureCompleted ? "done" : "running")} · с момента открытия панели: {appElapsed.TotalSeconds:F0} с";

        if (string.IsNullOrWhiteSpace(warning))
        {
            return baseStage;
        }

        var marker = string.Empty;
        if (warning.Contains("stage=post-1053-check", StringComparison.OrdinalIgnoreCase))
        {
            marker = " · этап: post-1053-check";
        }
        else if (warning.Contains("stage=api-probe", StringComparison.OrdinalIgnoreCase))
        {
            marker = " · этап: api-probe";
        }
        else if (warning.Contains("stage=sc-start", StringComparison.OrdinalIgnoreCase))
        {
            marker = " · этап: sc-start";
        }
        else if (warning.Contains("1053", StringComparison.OrdinalIgnoreCase))
        {
            marker = " · этап: scm-timeout-1053";
        }

        if (warning.Contains("setup-category=AccessDenied", StringComparison.OrdinalIgnoreCase))
        {
            marker += " · категория: access-denied";
        }
        else if (warning.Contains("setup-category=ServiceCrashedOnStart", StringComparison.OrdinalIgnoreCase))
        {
            marker += " · категория: service-crashed";
        }
        else if (warning.Contains("setup-category=ServiceStartTimeout", StringComparison.OrdinalIgnoreCase))
        {
            marker += " · категория: service-timeout";
        }
        else if (warning.Contains("setup-category=MissingServiceBinary", StringComparison.OrdinalIgnoreCase))
        {
            marker += " · категория: missing-binary";
        }

        return baseStage + marker;
    }

    private bool IsAttemptedLocalApiNavigation(string? currentSource, string baseUrl) =>
        IsLocalApiUrl(_pendingLocalApiNavigation, baseUrl) || IsLocalApiUrl(currentSource, baseUrl);

    private static bool IsLocalApiUrl(string? uri, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!Uri.TryCreate($"{baseUrl.TrimEnd('/')}/", UriKind.Absolute, out var baseParsed))
        {
            return false;
        }

        var host = parsed.Host.Trim();
        if (!string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return parsed.Port == baseParsed.Port;
    }

    private static bool IsTransientWebNavError(CoreWebView2WebErrorStatus status) =>
        status is CoreWebView2WebErrorStatus.ConnectionAborted
            or CoreWebView2WebErrorStatus.ConnectionReset
            or CoreWebView2WebErrorStatus.CannotConnect
            or CoreWebView2WebErrorStatus.Disconnected
            or CoreWebView2WebErrorStatus.OperationCanceled
            or CoreWebView2WebErrorStatus.Timeout;

    private static bool IsRecoverableStartupNavError(CoreWebView2WebErrorStatus status, TimeSpan elapsedSinceUiLoad) =>
        IsTransientWebNavError(status)
        || (status == CoreWebView2WebErrorStatus.Unknown && elapsedSinceUiLoad < TimeSpan.FromMinutes(3));

    private static async Task<(bool Enabled, int Port)> TryReadMelezhSettingsAsync(string baseUrl)
    {
        try
        {
            using var response = await StartupProbeHttp.GetAsync($"{baseUrl.TrimEnd('/')}/api/v1/settings").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (true, DefaultMelezhPort);
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var enabled = true;
            if (doc.RootElement.TryGetProperty("melezhEnabled", out var enabledEl)
                && (enabledEl.ValueKind == JsonValueKind.False
                    || (enabledEl.ValueKind == JsonValueKind.String
                        && string.Equals(enabledEl.GetString(), "false", StringComparison.OrdinalIgnoreCase))))
            {
                enabled = false;
            }

            var port = DefaultMelezhPort;
            if (doc.RootElement.TryGetProperty("melezhPort", out var portEl)
                && portEl.TryGetInt32(out var parsed)
                && parsed is > 0 and < 65536)
            {
                port = parsed;
            }

            return (enabled, port);
        }
        catch
        {
            return (true, DefaultMelezhPort);
        }
    }

    private static async Task<(bool Ok, string? Detail)> TryProbeMelezhWebUiAsync(int port)
    {
        var uri = $"http://127.0.0.1:{port}/ui";
        try
        {
            using var response = await StartupProbeHttp.GetAsync(uri).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            return (false, $"{uri} => {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"{uri}: {ex.Message}");
        }
    }

    private static async Task<(bool Ok, string? Detail)> TryProbeMelezhPingAsync(int port)
    {
        var uri = $"http://127.0.0.1:{port}/aria_ping";
        try
        {
            using var response = await StartupProbeHttp.GetAsync(uri).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"{uri} => {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (body.Contains("\"result\":false", StringComparison.OrdinalIgnoreCase)
                || body.Contains("\"result\": false", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "aria_ping result=false (нужен bootstrap)");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"{uri}: {ex.Message}");
        }
    }

    private static ServiceControllerStatus? TryGetMelezhServiceControllerStatus()
    {
        try
        {
            using var sc = new ServiceController(MelezhServiceName);
            sc.Refresh();
            return sc.Status;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(bool Ready, string Detail)> TryProbeApiOnceAsync(string baseUrl)
    {
        try
        {
            using var statusResponse = await StartupProbeHttp.GetAsync($"{baseUrl.TrimEnd('/')}/api/v1/status").ConfigureAwait(false);
            if (!statusResponse.IsSuccessStatusCode)
            {
                return (false, $"/api/v1/status => {(int)statusResponse.StatusCode}");
            }

            using var rootResponse = await StartupProbeHttp.GetAsync($"{baseUrl.TrimEnd('/')}/").ConfigureAwait(false);
            if (rootResponse.IsSuccessStatusCode)
            {
                return (true, "ready");
            }

            return (false, $"/ => {(int)rootResponse.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private void RenderStartupLoadingPage()
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        _expectStartupLoadingHtml = true;
        ShowLoadingOverlay("Подготовка служб…", "AriaSignature :5160 и Melezh :7788…");

        const string html =
            "<!DOCTYPE html><html lang=\"ru\"><head><meta charset=\"utf-8\"/><title>AriaSignature</title>" +
            "<style>body{font-family:Segoe UI,sans-serif;padding:48px;background:#2F3347;color:#EEF1FA;font-size:18px;max-width:720px;margin:0 auto}" +
            ".spin{display:inline-block;width:18px;height:18px;border:3px solid #5c6378;border-top-color:#8af;border-radius:50%;" +
            "animation:a 0.9s linear infinite;vertical-align:middle;margin-right:12px}" +
            "@keyframes a{to{transform:rotate(360deg)}} .muted{color:#bcc3d4;font-size:15px;margin-top:16px}</style></head><body>" +
            "<p><span class=\"spin\"></span>Загрузка панели…</p>" +
            "<p class=\"muted\">Подготовка AriaSignature и Melezh (службы, API :5160, Web UI :7788).</p>" +
            "</body></html>";
        Browser.CoreWebView2.NavigateToString(html);
    }

    private void RenderFallbackPage(string title, string details, string baseUrl, int? code = null, string? codeName = null, string? probeDetail = null, string? stage = null)
    {
        if (Browser.CoreWebView2 is null)
        {
            return;
        }

        _expectStartupLoadingHtml = false;
        HideLoadingOverlay();

        var errorCodeText = code is int c
            ? $"<p>Код ошибки WebView2: <strong>{c}</strong> ({System.Net.WebUtility.HtmlEncode(codeName ?? "unknown")})</p>"
            : string.Empty;
        var probeText = string.IsNullOrWhiteSpace(probeDetail)
            ? string.Empty
            : "<p>Диагностика запуска API: <code>" + System.Net.WebUtility.HtmlEncode(probeDetail) + "</code></p>";
        var stageText = string.IsNullOrWhiteSpace(stage)
            ? string.Empty
            : "<p>Текущий этап: <strong>" + System.Net.WebUtility.HtmlEncode(stage) + "</strong></p>";
        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AriaSignature",
            "logs");
        var hints =
            "<p class=\"muted\"><strong>Подсказки:</strong> журналы — <code>" +
            System.Net.WebUtility.HtmlEncode(logsDir) +
            "</code>; при отказе автозапуска службы попробуйте запустить это приложение от имени администратора; " +
            "службу можно проверить в <code>services.msc</code> (AriaSignatureService).</p>";
        var html =
            "<!DOCTYPE html><html lang=\"ru\"><head><meta charset=\"utf-8\"/><title>AriaSignature</title>" +
            "<style>body{font-family:Segoe UI,sans-serif;padding:24px;background:#111;color:#eee;max-width:760px}" +
            "code{background:#222;padding:2px 6px} .muted{color:#aaa}</style></head><body>" +
            "<h1>" + System.Net.WebUtility.HtmlEncode(title) + "</h1>" +
            "<p>" + System.Net.WebUtility.HtmlEncode(details) + "</p>" +
            "<p>Ожидаемый адрес: <code>" + System.Net.WebUtility.HtmlEncode($"{baseUrl.TrimEnd('/')}/") + "</code></p>" +
            errorCodeText +
            probeText +
            stageText +
            hints +
            "<p class=\"muted\">Проверьте службу AriaSignatureService и доступность localhost. " +
            "После восстановления сервиса окно автоматически загрузит интерфейс.</p></body></html>";
        Browser.CoreWebView2.NavigateToString(html);
    }

    private static async Task<string> ResolveApiBaseAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await Http.GetAsync($"{DefaultApiBase}/api/v1/settings", cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return DefaultApiBase;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            if (doc.RootElement.TryGetProperty("apiPort", out var portEl) &&
                portEl.TryGetInt32(out var port) &&
                port is > 0 and < 65536)
            {
                return $"http://127.0.0.1:{port}";
            }
        }
        catch (OperationCanceledException)
        {
            // таймаут или отмена — сразу используем порт по умолчанию
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
            if (action == "appReady")
            {
                _awaitingAppReady = false;
                CancelAppReadyFallback();
                HideLoadingOverlay();
                TryPostAutostartPayload();
            }
            else if (action == "setAutostart" && root.TryGetProperty("enabled", out var en))
            {
                var want = en.GetBoolean();
                WindowsServiceAutostartConfigurator.ApplyAutostart(want, _startup);
                PostWebMessageJson(new { action = "autostartProgress", phase = "panel" });
                _ = RunAutostartServicesAsync(want);
            }
            else if (action == "getAutostart")
            {
                PostWebMessageString(WindowsServiceAutostartConfigurator.SerializeAutostartWebMessage(_startup));
            }
            else if (action == "getWindowsServiceStatus")
            {
                _ = RunWindowsServiceStatusAsync();
            }
            else if (action == "controlWindowsService" && root.TryGetProperty("command", out var cmdEl))
            {
                var cmd = cmdEl.GetString();
                _ = RunWindowsServiceControlAsync(cmd);
            }
            else if (action == "controlMelezhService" && root.TryGetProperty("command", out var mzCmdEl))
            {
                var cmd = mzCmdEl.GetString();
                _ = RunMelezhServiceControlAsync(cmd);
            }
            else if (action == "repairMelezh")
            {
                _ = RunMelezhRepairAsync();
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
            else if (action == "openFolder" && root.TryGetProperty("path", out var pathEl))
            {
                var folderPath = pathEl.GetString();
                if (string.IsNullOrWhiteSpace(folderPath) || !Path.IsPathRooted(folderPath) || !Directory.Exists(folderPath))
                {
                    return;
                }

                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folderPath}\"")
                    {
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // ignore shell launch errors
                }
            }
        }
        catch
        {
            // ignore malformed messages
        }
    }

    private void PostWebMessageString(string json) =>
        Dispatcher.InvokeAsync(() => Browser.CoreWebView2?.PostWebMessageAsString(json));

    private void PostWebMessageJson(object payload) =>
        PostWebMessageString(JsonSerializer.Serialize(payload));

    private async Task RunAutostartServicesAsync(bool want)
    {
        PostWebMessageJson(new { action = "autostartProgress", phase = "services" });
        var (svcOk, svcErr) = await Task.Run(() =>
            WindowsServicesAutostartCoordinator.ApplyServicesBootStart(want, startIfEnabled: want)).ConfigureAwait(true);

        string? svcWarn = null;
        if (want && svcOk)
        {
            var serviceExePath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "service", "AriaSignature.Service.exe"));
            svcWarn = await Task.Run(() =>
            {
                WindowsServiceEnsure.TryStartOrFallback(
                    serviceExePath,
                    TimeSpan.FromSeconds(25),
                    _serviceBootstrapExePath,
                    out var warn);
                return warn;
            }).ConfigureAwait(true);
        }

        await Dispatcher.InvokeAsync(() =>
        {
            if (!svcOk && !string.IsNullOrEmpty(svcErr))
            {
                System.Windows.MessageBox.Show(svcErr, "AriaSignature — автозапуск служб", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (!string.IsNullOrEmpty(svcWarn))
            {
                System.Windows.MessageBox.Show(svcWarn, "AriaSignature", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            PostWebMessageString(WindowsServiceAutostartConfigurator.SerializeAutostartWebMessage(_startup));
        });
    }

    private async Task RunWindowsServiceStatusAsync()
    {
        var payload = await Task.Run(TryGetWindowsServiceStatus).ConfigureAwait(true);
        PostWebMessageJson(payload);
    }

    private async Task RunWindowsServiceControlAsync(string? cmd)
    {
        PostWebMessageJson(new { action = "windowsServiceProgress", command = cmd });
        var controlPayload = await Task.Run(() => TryControlWindowsService(cmd)).ConfigureAwait(true);
        var statusPayload = await Task.Run(TryGetWindowsServiceStatus).ConfigureAwait(true);
        await Dispatcher.InvokeAsync(() =>
        {
            PostWebMessageJson(controlPayload);
            PostWebMessageJson(statusPayload);
        });
    }

    private async Task RunMelezhServiceControlAsync(string? cmd)
    {
        PostWebMessageJson(new { action = "melezhServiceProgress", command = cmd });
        var controlPayload = await Task.Run(() => TryControlMelezhService(cmd)).ConfigureAwait(true);
        await Dispatcher.InvokeAsync(() =>
        {
            PostWebMessageJson(controlPayload);
        });
    }

    private async Task RunMelezhRepairAsync()
    {
        PostWebMessageJson(new { action = "repairMelezhProgress", phase = "start" });
        var result = await Task.Run(MelezhServiceRepair.TryRepairWithElevation).ConfigureAwait(true);
        await Dispatcher.InvokeAsync(() =>
        {
            PostWebMessageJson(new { action = "repairMelezh", ok = result.Ok, error = result.Error });
        });
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

    private static object TryControlMelezhService(string? command)
    {
        try
        {
            using var sc = new ServiceController(MelezhServiceRepair.ServiceName);
            switch (command?.ToLowerInvariant())
            {
                case "start":
                    if (sc.Status == ServiceControllerStatus.Stopped)
                    {
                        WindowsServiceEnsure.StartServiceAllowingScmTimeout1053(sc, TimeSpan.FromSeconds(90));
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
                        WindowsServiceEnsure.StartServiceAllowingScmTimeout1053(sc, TimeSpan.FromSeconds(90));
                    }

                    break;
                default:
                    return new { action = "melezhServiceControl", ok = false, error = "Неизвестная команда" };
            }

            sc.Refresh();
            return new
            {
                action = "melezhServiceControl",
                ok = true,
                status = NormalizeWindowsServiceStatus(sc.Status.ToString())
            };
        }
        catch (Exception ex)
        {
            return new { action = "melezhServiceControl", ok = false, error = ex.Message };
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
                        WindowsServiceEnsure.StartServiceAllowingScmTimeout1053(sc, TimeSpan.FromSeconds(90));
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
                        WindowsServiceEnsure.StartServiceAllowingScmTimeout1053(sc, TimeSpan.FromSeconds(90));
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
        CancelAppReadyFallback();
        if (!_app.CanCloseToTray())
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    public void NotifyWindowRestored()
    {
        TryStartStartupFlowIfVisible();
    }

    private void TrySetWindowIcon()
    {
        var pngPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
        if (!File.Exists(pngPath))
        {
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(Path.GetFullPath(pngPath), UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            Icon = bitmap;
        }
        catch
        {
            // ignore
        }
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
            var cursor = GetCursorScreenDip();
            WindowState = WindowState.Normal;
            Left = cursor.X - (RestoreBounds.Width * widthRatio);
            Top = cursor.Y - 12;
        }
        try
        {
            DragMove();
        }
        catch
        {
            // Ignore drag race with state transitions.
        }
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

    private System.Windows.Point GetCursorScreenDip()
    {
        if (!GetCursorPos(out var screenPx))
        {
            return new System.Windows.Point(Left + (ActualWidth / 2), Top + 10);
        }

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return new System.Windows.Point(screenPx.X, screenPx.Y);
        }

        var dip = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(screenPx.X, screenPx.Y));
        return dip;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out WinPoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, IntPtr flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public WinPoint Reserved;
        public WinPoint MaxSize;
        public WinPoint MaxPosition;
        public WinPoint MinTrackSize;
        public WinPoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public WinRect MonitorArea;
        public WinRect WorkArea;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
