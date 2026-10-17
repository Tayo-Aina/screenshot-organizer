using System.Windows.Forms;

namespace ScreenshotOrganizer;

internal class TrayIconManager : IDisposable
{
    private const string IconResourceName = "ScreenshotOrganizer.Icon.ico";

    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseResumeItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ScreenshotWatcher _watcher;
    private bool _isPaused;
    private bool _disposed;

    public TrayIconManager(ScreenshotWatcher watcher)
    {
        _watcher = watcher;

        _statusItem = new ToolStripMenuItem("Status: Running") { Enabled = false };
        _pauseResumeItem = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause());

        _startupItem = new ToolStripMenuItem("Run on startup", null, (_, _) => ToggleStartup())
        {
            Checked = StartupManager.IsRegistered()
        };
        _startupItem.ToolTipText = "Launch Screenshot Organizer automatically when you log in";

        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => Shutdown());

        var contextMenu = new ContextMenuStrip();
        contextMenu.Opening += (_, _) => RefreshStartupState();
        contextMenu.Items.Add(_statusItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_pauseResumeItem);
        contextMenu.Items.Add(_startupItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Screenshot Organizer",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => TogglePause();
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            using var stream = typeof(TrayIconManager).Assembly
                .GetManifestResourceStream(IconResourceName);
            return stream == null
                ? SystemIcons.Application
                : new Icon(stream, 32, 32);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load tray icon, falling back to default: {ex.Message}");
            return SystemIcons.Application;
        }
    }

    private void TogglePause()
    {
        _isPaused = !_isPaused;

        if (_isPaused)
        {
            _watcher.Pause();
            _statusItem.Text = "Status: Paused";
            _pauseResumeItem.Text = "Resume";
            _notifyIcon.Text = "Screenshot Organizer (Paused)";
            Logger.Info("Watcher paused via tray icon");
        }
        else
        {
            _watcher.Resume();
            _statusItem.Text = "Status: Running";
            _pauseResumeItem.Text = "Pause";
            _notifyIcon.Text = "Screenshot Organizer";
            Logger.Info("Watcher resumed via tray icon");
        }
    }

    private void ToggleStartup()
    {
        try
        {
            if (StartupManager.IsRegistered())
            {
                StartupManager.Unregister();
                Logger.Info("Startup disabled via tray icon");
            }
            else
            {
                StartupManager.Register();
                Logger.Info("Startup enabled via tray icon");
            }

            RefreshStartupState();
        }
        catch (Exception ex)
        {
            RefreshStartupState();
            Logger.Error("Failed to toggle startup via tray icon", ex);
            MessageBox.Show(
                $"Couldn't update startup right now.\r\n\r\n{ex.Message}",
                "Screenshot Organizer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void RefreshStartupState()
    {
        _startupItem.Checked = StartupManager.IsRegistered();
    }

    private void Shutdown()
    {
        Logger.Info("Shutting down via tray icon");
        _watcher.Stop();
        Dispose();
        Application.Exit();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
