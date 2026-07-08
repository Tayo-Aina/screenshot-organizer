using System.Windows.Forms;

namespace ScreenshotOrganizer;

internal class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseResumeItem;
    private readonly ScreenshotWatcher _watcher;
    private bool _isPaused;
    private bool _disposed;

    public TrayIconManager(ScreenshotWatcher watcher)
    {
        _watcher = watcher;

        _statusItem = new ToolStripMenuItem("Status: Running") { Enabled = false };
        _pauseResumeItem = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause());

        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => Shutdown());

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(_statusItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_pauseResumeItem);
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Screenshot Organizer",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => TogglePause();
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
