using System.Windows;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AriaSignature.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _trayDrawingIcon;
    private nint _trayIconHandle;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InitializeTrayIcon();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _trayDrawingIcon?.Dispose();
        if (_trayIconHandle != 0)
        {
            DestroyIcon(_trayIconHandle);
        }
        base.OnExit(e);
    }

    private void InitializeTrayIcon()
    {
        var imagePath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
        if (!System.IO.File.Exists(imagePath))
        {
            return;
        }

        using var bitmap = new Drawing.Bitmap(imagePath);
        _trayIconHandle = bitmap.GetHicon();
        _trayDrawingIcon = Drawing.Icon.FromHandle(_trayIconHandle);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) =>
        {
            MainWindow?.Show();
            MainWindow?.Activate();
        });
        menu.Items.Add("Выход", null, (_, _) => Shutdown());

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "AriaSignature",
            Icon = _trayDrawingIcon,
            Visible = true,
            ContextMenuStrip = menu
        };

        _trayIcon.DoubleClick += (_, _) =>
        {
            MainWindow?.Show();
            MainWindow?.Activate();
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint hIcon);
}

