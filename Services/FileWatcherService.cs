using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace HyperNote.Services;

public sealed class FileWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly string _filePath;
    private readonly Action<WatcherChangeTypes> _onChanged;
    private readonly DispatcherTimer _debounceTimer;
    private WatcherChangeTypes _pendingChangeType;
    private bool _isSuspended;

    public FileWatcherService(string filePath, Action<WatcherChangeTypes> onChanged)
    {
        _filePath = Path.GetFullPath(filePath);
        _onChanged = onChanged;

        _debounceTimer = new DispatcherTimer(DispatcherPriority.Normal, Application.Current.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _debounceTimer.Tick += DebounceTimer_Tick;

        Start();
    }

    private void Start()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            string fileName = Path.GetFileName(_filePath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

            _watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
        }
        catch
        {
            // Ignore errors (e.g. network paths, lack of permissions)
        }
    }

    public void Suspend()
    {
        _isSuspended = true;
        _debounceTimer.Stop();
    }

    public void Resume()
    {
        // Resume on background priority to let OS flush file system notifications
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            _isSuspended = false;
        }), DispatcherPriority.Background);
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (_isSuspended) return;

        _pendingChangeType = e.ChangeType;
        
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }));
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        if (!_isSuspended)
        {
            _onChanged(_pendingChangeType);
        }
    }

    public void Dispose()
    {
        _debounceTimer.Stop();
        if (_watcher != null)
        {
            _watcher.Changed -= OnFileEvent;
            _watcher.Deleted -= OnFileEvent;
            _watcher.Renamed -= OnFileEvent;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
