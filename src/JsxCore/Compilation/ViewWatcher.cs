using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace JsxCore.Compilation;

public sealed class ViewWatcher(
    string directory,
    IReadOnlyCollection<string> extensions,
    ILogger logger) : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(120);

    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private bool _disposed;

    public event Func<Task>? Changed;

    public void Start()
    {
        if (_watcher is not null || !Directory.Exists(directory))
        {
            return;
        }

        _timer = new Timer(_ => _ = OnElapsedAsync(), null, Timeout.Infinite, Timeout.Infinite);

        _watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
        };

        _watcher.Changed += OnFileSystemEvent;
        _watcher.Created += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Renamed += OnFileSystemEvent;
        _watcher.EnableRaisingEvents = true;

        logger.LogInformation("JsxCore is watching {Directory} for changes.", directory);
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (!IsInput(e.FullPath) && e.ChangeType != WatcherChangeTypes.Deleted)
        {
            return;
        }

        // Editors write a file in several bursts; collapse them into one notification.
        _timer?.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    private bool IsInput(string path)
    {
        // The editor tsconfig lives among the views but is written by JsxCore, not read by it.
        if (Path.GetFileName(path).Equals("tsconfig.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private async Task OnElapsedAsync()
    {
        if (Changed is { } handler)
        {
            await handler().ConfigureAwait(false);
        }
    }

    [SuppressMessage("Usage", "CA1816", Justification = "Sealed type with no finalizer.")]
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileSystemEvent;
            _watcher.Created -= OnFileSystemEvent;
            _watcher.Deleted -= OnFileSystemEvent;
            _watcher.Renamed -= OnFileSystemEvent;
            _watcher.Dispose();
        }

        _timer?.Dispose();
    }
}
