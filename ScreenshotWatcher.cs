using System.Collections.Concurrent;

namespace ScreenshotOrganizer;

internal class ScreenshotWatcher : IDisposable
{
    private static readonly string[] ImageExtensions = { "*.png", "*.jpg", "*.jpeg", "*.bmp" };
    private static readonly int MaxRetryAttempts = 50;
    private static readonly int RetryDelayMs = 200;

    private readonly string _watchPath;
    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, DateTime> _processedFiles = new();
    private readonly object _reinitLock = new();
    private bool _disposed;

    public ScreenshotWatcher()
    {
        _watchPath = FolderManager.ScreenshotsRoot;
    }

    public void Start()
    {
        Directory.CreateDirectory(_watchPath);
        InitializeWatcher();
        Logger.Info($"Watching directory: {_watchPath}");
    }

    public void Stop()
    {
        DisposeWatcher();
        Logger.Info("Watcher stopped");
    }

    public void Pause()
    {
        if (_watcher != null)
            _watcher.EnableRaisingEvents = false;
    }

    public void Resume()
    {
        if (_watcher != null)
            _watcher.EnableRaisingEvents = true;
    }

    private void InitializeWatcher()
    {
        DisposeWatcher();

        _watcher = new FileSystemWatcher(_watchPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            IncludeSubdirectories = false,
            InternalBufferSize = 64 * 1024
        };

        foreach (var ext in ImageExtensions)
            _watcher.Filters.Add(ext);

        _watcher.Created += OnFileCreated;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            var fileName = Path.GetFileName(e.FullPath);
            var now = DateTime.UtcNow;

            if (_processedFiles.TryGetValue(fileName, out var lastProcessed) &&
                (now - lastProcessed).TotalSeconds < 2)
            {
                return;
            }

            _processedFiles[fileName] = now;

            Logger.Info($"New screenshot detected: {fileName}");

            var appName = ForegroundAppDetector.GetForegroundAppName();
            var safeAppName = SanitizeFolderName(appName);

            Logger.Info($"Detected app: {appName} -> folder: {safeAppName}");

            var targetFolder = FolderManager.EnsureAppFolder(safeAppName);

            if (!WaitForFileReady(e.FullPath))
            {
                Logger.Warn($"File not ready after timeout, will move anyway: {fileName}");
            }

            var destPath = FolderManager.GetUniqueFilePath(targetFolder, fileName);
            File.Move(e.FullPath, destPath);
            Logger.Info($"Moved: {fileName} -> {safeAppName}/{Path.GetFileName(destPath)}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Error processing file '{e.FullPath}'", ex);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        Logger.Error("FileSystemWatcher error, attempting reinitialization", ex);

        lock (_reinitLock)
        {
            try
            {
                InitializeWatcher();
                Logger.Info("FileSystemWatcher reinitialized after error");
            }
            catch (Exception reinitEx)
            {
                Logger.Error("Failed to reinitialize FileSystemWatcher", reinitEx);
            }
        }
    }

    private static bool WaitForFileReady(string filePath)
    {
        for (var attempt = 0; attempt < MaxRetryAttempts; attempt++)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    Thread.Sleep(RetryDelayMs);
                    continue;
                }
            }
            catch (FileNotFoundException)
            {
                Thread.Sleep(RetryDelayMs);
                continue;
            }

            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                if (stream.Length > 0)
                {
                    Logger.Info($"File ready after {attempt * RetryDelayMs}ms, size: {stream.Length} bytes");
                    return true;
                }
            }
            catch (IOException)
            {
            }

            Thread.Sleep(RetryDelayMs);
        }
        return false;
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[name.Length];

        for (var i = 0; i < name.Length; i++)
        {
            if (Array.IndexOf(invalid, name[i]) >= 0)
                sanitized[i] = '_';
            else
                sanitized[i] = name[i];
        }

        var result = new string(sanitized).Trim();
        result = result.TrimEnd('.', ' ');
        return result.Length > 0 ? result : "Unknown";
    }

    private void DisposeWatcher()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileCreated;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeWatcher();
    }
}
