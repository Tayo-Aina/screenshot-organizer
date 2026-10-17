using System.Collections.Concurrent;

namespace ScreenshotOrganizer;

internal class ScreenshotWatcher : IDisposable
{
    private static readonly string[] ImageExtensions = { "*.png", "*.jpg", "*.jpeg", "*.bmp" };
    private static readonly HashSet<string> ImageExtensionSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp"
    };

    // How long a single readiness check waits: 8 x 150ms = 1.2s per cycle.
    // Anything slower is handled by the retry queue instead of blocking.
    private static readonly int ReadyPollAttempts = 8;
    private static readonly int ReadyPollDelayMs = 150;

    // Retry policy for failing moves: exponential backoff capped at 15s,
    // ~30 attempts covers several minutes of transient locks.
    private static readonly int MaxProcessingAttempts = 30;
    private static readonly int FirstRetryDelayMs = 300;
    private static readonly int MaxRetryDelayMs = 15_000;

    private static readonly TimeSpan DedupeWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DedupePruneAge = TimeSpan.FromSeconds(30);

    // The startup sweep only picks up recent leftovers (e.g. the app was off
    // or a move failed); older files are left alone.
    private static readonly TimeSpan SweepMaxAge = TimeSpan.FromHours(24);

    private readonly string _watchPath;
    private readonly ConcurrentDictionary<string, DateTime> _processedFiles = new();
    private readonly object _reinitLock = new();
    private readonly ManualResetEventSlim _shutdown = new(false);

    private FileSystemWatcher? _watcher;
    private BlockingCollection<WorkItem>? _queue;
    private Thread? _processorThread;
    private bool _disposed;

    public ScreenshotWatcher()
    {
        _watchPath = FolderManager.ScreenshotsRoot;
    }

    public void Start()
    {
        Directory.CreateDirectory(_watchPath);

        _queue = new BlockingCollection<WorkItem>();
        _processorThread = new Thread(ProcessLoop)
        {
            Name = "ScreenshotProcessor",
            IsBackground = true
        };
        _processorThread.Start();

        InitializeWatcher();
        EnqueueExistingFiles();

        Logger.Info($"Watching directory: {_watchPath}");
    }

    public void Stop()
    {
        DisposeWatcher();
        ShutdownProcessor();
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

            if (IsDuplicate(fileName))
                return;

            Logger.Info($"New screenshot detected: {fileName}");

            // Detect the foreground app now, while the user is still in that
            // app - not later on the processing thread.
            var appName = ForegroundAppDetector.GetForegroundAppName();
            var safeAppName = SanitizeFolderName(appName);

            Logger.Info($"Detected app: {appName} -> folder: {safeAppName}");

            Enqueue(new WorkItem(e.FullPath, fileName, safeAppName));
        }
        catch (Exception ex)
        {
            Logger.Error($"Error queueing file '{e.FullPath}'", ex);
        }
    }

    private bool IsDuplicate(string fileName)
    {
        var now = DateTime.UtcNow;

        if (_processedFiles.TryGetValue(fileName, out var lastProcessed) &&
            now - lastProcessed < DedupeWindow)
        {
            return true;
        }

        _processedFiles[fileName] = now;

        if (_processedFiles.Count > 64)
            PruneProcessedFiles(now);

        return false;
    }

    private void PruneProcessedFiles(DateTime now)
    {
        foreach (var pair in _processedFiles)
        {
            if (now - pair.Value > DedupePruneAge)
                _processedFiles.TryRemove(pair.Key, out _);
        }
    }

    private void Enqueue(WorkItem item)
    {
        var queue = _queue;
        if (queue == null || queue.IsAddingCompleted)
            return;

        try
        {
            queue.Add(item);
        }
        catch (InvalidOperationException)
        {
            // Queue was completed between the check and the add; shutting down.
        }
    }

    private void EnqueueExistingFiles()
    {
        var enqueued = 0;
        var skippedOld = 0;
        var cutoff = DateTime.UtcNow - SweepMaxAge;

        foreach (var path in Directory.EnumerateFiles(_watchPath))
        {
            if (!ImageExtensionSet.Contains(Path.GetExtension(path)))
                continue;

            if (File.GetLastWriteTimeUtc(path) < cutoff)
            {
                skippedOld++;
                continue;
            }

            // Nothing knows which app took these screenshots, so they land
            // in "Unknown" - same as a failed detection at runtime.
            Enqueue(new WorkItem(path, Path.GetFileName(path), "Unknown"));
            enqueued++;
        }

        if (enqueued > 0)
            Logger.Info($"Found {enqueued} recent file(s) in the Screenshots root to organize");

        if (skippedOld > 0)
            Logger.Info($"Left {skippedOld} older file(s) in the Screenshots root untouched");
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

    private void ProcessLoop()
    {
        var queue = _queue!;
        var pending = new List<PendingItem>();

        while (!_shutdown.IsSet)
        {
            var waitMs = GetWaitMilliseconds(pending);
            if (queue.TryTake(out var item, waitMs))
                pending.Add(new PendingItem(item));

            if (_shutdown.IsSet)
                break;

            var now = DateTime.UtcNow;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var entry = pending[i];
                if (entry.DueTimeUtc > now)
                    continue;

                if (TryProcess(entry.Item))
                {
                    pending.RemoveAt(i);
                }
                else if (entry.Attempts >= MaxProcessingAttempts)
                {
                    Logger.Warn($"Giving up on '{entry.Item.FileName}' after {MaxProcessingAttempts} attempts; " +
                                "it will be picked up again on the next start");
                    pending.RemoveAt(i);
                }
                else
                {
                    entry.Attempts++;
                    entry.DueTimeUtc = DateTime.UtcNow + RetryDelay(entry.Attempts);
                }
            }
        }
    }

    private int GetWaitMilliseconds(List<PendingItem> pending)
    {
        if (pending.Count == 0)
            return 2000;

        var nextDue = DateTime.UtcNow;
        foreach (var entry in pending)
        {
            if (entry.DueTimeUtc < nextDue)
                nextDue = entry.DueTimeUtc;
        }

        var wait = (int)(nextDue - DateTime.UtcNow).TotalMilliseconds;
        return Math.Clamp(wait, 10, 2000);
    }

    private static TimeSpan RetryDelay(int attempts)
    {
        var delayMs = FirstRetryDelayMs * Math.Pow(2, attempts - 1);
        return TimeSpan.FromMilliseconds(Math.Min(delayMs, MaxRetryDelayMs));
    }

    private static bool TryProcess(WorkItem item)
    {
        try
        {
            if (!File.Exists(item.FullPath))
            {
                // Normal Windows noise: temp files or user deletions. Not an error.
                Logger.Info($"File no longer exists, skipping: {item.FileName}");
                return true;
            }

            if (!WaitForFileReady(item.FullPath))
            {
                Logger.Info($"File still being written, will retry: {item.FileName}");
                return false;
            }

            var targetFolder = FolderManager.EnsureAppFolder(item.FolderName);
            var destPath = FolderManager.GetUniqueFilePath(targetFolder, item.FileName);
            File.Move(item.FullPath, destPath);
            Logger.Info($"Moved: {item.FileName} -> {item.FolderName}/{Path.GetFileName(destPath)}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to file '{item.FileName}' (will retry): {ex.Message}");
            return false;
        }
    }

    private static bool WaitForFileReady(string filePath)
    {
        for (var attempt = 0; attempt < ReadyPollAttempts; attempt++)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Exists && fileInfo.Length > 0)
                {
                    using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    if (stream.Length > 0)
                    {
                        Logger.Info($"File ready after {attempt * ReadyPollDelayMs}ms, size: {stream.Length} bytes");
                        return true;
                    }
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (IOException)
            {
            }

            Thread.Sleep(ReadyPollDelayMs);
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

    private void ShutdownProcessor()
    {
        var queue = _queue;
        if (queue != null && !queue.IsAddingCompleted)
            queue.CompleteAdding();

        _shutdown.Set();

        var thread = _processorThread;
        if (thread != null && thread.IsAlive)
            thread.Join(TimeSpan.FromSeconds(3));

        _processorThread = null;
        queue?.Dispose();
        _queue = null;
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
        ShutdownProcessor();
    }

    private sealed record WorkItem(string FullPath, string FileName, string FolderName);

    private sealed class PendingItem
    {
        public PendingItem(WorkItem item)
        {
            Item = item;
            DueTimeUtc = DateTime.UtcNow;
        }

        public WorkItem Item { get; }
        public int Attempts { get; set; }
        public DateTime DueTimeUtc { get; set; }
    }
}
