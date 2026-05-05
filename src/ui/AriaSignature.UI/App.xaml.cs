using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AriaSignature.UI;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\AriaSignature.UI.SingleInstance.v1";
    private const string ActivateExistingEventName = @"Local\AriaSignature.UI.Activate.v1";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private EventWaitHandle? _activateExistingHandle;
    private CancellationTokenSource? _activateExistingCts;

    private Forms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _trayDrawingIcon;
    private bool _isExitRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            var activatedExisting = SingleInstanceActivator.SignalExistingInstance(ActivateExistingEventName);
            if (!activatedExisting && TryRecoverFromStaleSingleInstanceMutex(out createdNew))
            {
                // Stale mutex case: continue startup as primary instance.
            }
            else
            {
                // Prevent "nothing happened" UX: explicit message when process is already running.
                if (!e.Args.Any(arg => string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase)))
                {
                    System.Windows.MessageBox.Show(
                        activatedExisting
                            ? "AriaSignature уже запущен. Окно существующего экземпляра должно быть поднято на передний план."
                            : "AriaSignature уже запущен, но не удалось активировать существующее окно. Проверьте значок в системном трее и завершите зависший процесс при необходимости.",
                        "AriaSignature",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }
        }

        _ownsSingleInstanceMutex = true;
        _activateExistingHandle = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateExistingEventName);
        _activateExistingCts = new CancellationTokenSource();
        StartExternalActivationPump();

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppendBootLogIfTray(e.Args);
        EnsureTrayIconShell();

        var mainWindow = new MainWindow(this);
        MainWindow = mainWindow;

        // --tray: не вызывать Hide() и не манипулировать Opacity между Show — это ломает рендер WebView2 (чёрный экран).
        // Достаточно минимизировать окно без панели задач; при «Закрыть в трей» сработает существующий Hide().
        var startInTray = e.Args.Any(arg => string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase));
        if (startInTray)
        {
            mainWindow.ShowInTaskbar = false;
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.ShowActivated = false;
            mainWindow.Show();
        }
        else
        {
            mainWindow.Show();
        }
    }

    private bool TryRecoverFromStaleSingleInstanceMutex(out bool createdNew)
    {
        createdNew = false;
        try
        {
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            Thread.Sleep(250);
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            return createdNew;
        }
        catch
        {
            return false;
        }
    }

    private static void AppendBootLogIfTray(string[] args)
    {
        if (!args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AriaSignature",
                "logs");
            Directory.CreateDirectory(dir);
            var argLine = string.Join(" ", args.Select(a => a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a));
            var line = $"{DateTimeOffset.Now:O}\tpid={Environment.ProcessId}\t{argLine}{Environment.NewLine}";
            File.AppendAllText(Path.Combine(dir, "ui-boot.log"), line);
        }
        catch
        {
            // ignore
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_activateExistingCts is not null)
        {
            _activateExistingCts.Cancel();
            _activateExistingCts.Dispose();
            _activateExistingCts = null;
        }

        _activateExistingHandle?.Dispose();
        _activateExistingHandle = null;

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _trayDrawingIcon?.Dispose();

        if (_ownsSingleInstanceMutex && _singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch
            {
                // ignore
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Иконка в трее до создания MainWindow/WebView — чтобы процесс был виден при автозапуске.
    /// </summary>
    private void EnsureTrayIconShell()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        _trayDrawingIcon = LoadTrayIcon(iconPath);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => RestoreMainWindow());
        menu.Items.Add("Выход", null, (_, _) => ExitApplication());

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "AriaSignature",
            Icon = _trayDrawingIcon,
            Visible = true,
            ContextMenuStrip = menu
        };

        _trayIcon.DoubleClick += (_, _) => RestoreMainWindow();
    }

    private void RestoreMainWindow()
    {
        if (MainWindow is not Window window)
        {
            return;
        }

        window.ShowActivated = true;
        window.ShowInTaskbar = true;

        // WPF падает, если пытаться Show() у скрытого окна с ShowActivated=false и WindowState=Maximized.
        var wasMaximized = window.WindowState == WindowState.Maximized;
        if (wasMaximized)
        {
            window.WindowState = WindowState.Normal;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (wasMaximized)
        {
            _ = window.Dispatcher.BeginInvoke(() =>
            {
                window.WindowState = WindowState.Maximized;
            }, DispatcherPriority.ApplicationIdle);
        }
        else if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        if (window is MainWindow mainWindow)
        {
            mainWindow.NotifyWindowRestored();
        }
    }

    private static Drawing.Icon LoadTrayIcon(string iconPath)
    {
        try
        {
            if (File.Exists(iconPath))
            {
                return new Drawing.Icon(iconPath);
            }
        }
        catch
        {
            // ignore
        }

        return Drawing.SystemIcons.Application;
    }

    public bool CanCloseToTray()
    {
        return !_isExitRequested;
    }

    public void ExitApplication()
    {
        _isExitRequested = true;
        MainWindow?.Close();
        Shutdown();
    }

    private void StartExternalActivationPump()
    {
        if (_activateExistingHandle is null || _activateExistingCts is null)
        {
            return;
        }

        var handle = _activateExistingHandle;
        var token = _activateExistingCts.Token;
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!handle.WaitOne(1000))
                    {
                        continue;
                    }

                    _ = Dispatcher.BeginInvoke(() => RestoreMainWindow(), DispatcherPriority.ApplicationIdle);
                }
                catch
                {
                    // best-effort: restore signaling should not crash UI process
                }
            }
        }, token);
    }
}
