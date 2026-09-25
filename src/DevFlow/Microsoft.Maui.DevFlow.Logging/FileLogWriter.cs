using System.Collections.Concurrent;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Logging;

/// <summary>
/// Writes log entries to rotating JSONL files.
/// Producers enqueue to a lock-free buffer; a background timer drains to disk.
/// File I/O is protected by a ReaderWriterLockSlim (write lock) so readers can
/// access files concurrently without blocking producers.
/// </summary>
public class FileLogWriter : IDisposable
{
    private readonly string _logDir;
    private readonly long _maxFileSize;
    private readonly int _maxFiles;
    private readonly ReaderWriterLockSlim _rwLock;
    private readonly ConcurrentQueue<string> _buffer = new();
    private readonly Timer _drainTimer;
    private StreamWriter? _writer;
    private long _currentSize;
    private volatile bool _disposed;

    private const string CurrentFileName = "log-current.jsonl";

    public string LogDirectory => _logDir;

    /// <summary>
    /// Returns any entries still in the in-memory buffer that haven't been flushed to disk yet.
    /// </summary>
    internal IReadOnlyList<FileLogEntry> GetBufferedEntries()
    {
        var entries = new List<FileLogEntry>();
        foreach (var json in _buffer.ToArray())
        {
            try
            {
                var entry = JsonSerializer.Deserialize<FileLogEntry>(json);
                if (entry != null)
                    entries.Add(entry);
            }
            catch { }
        }
        return entries;
    }

    public FileLogWriter(string logDir, ReaderWriterLockSlim rwLock, long maxFileSizeBytes = 1_048_576, int maxFiles = 5)
    {
        _logDir = logDir;
        _rwLock = rwLock;
        _maxFileSize = maxFileSizeBytes;
        _maxFiles = maxFiles;
        Directory.CreateDirectory(_logDir);
        OpenCurrentFile();
        _drainTimer = new Timer(_ => DrainBuffer(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void Write(FileLogEntry entry)
    {
        if (_disposed) return;
        var json = JsonSerializer.Serialize(entry);
        _buffer.Enqueue(json);
        OnLogWritten?.Invoke(entry);
    }

    /// <summary>
    /// Fired synchronously each time a log entry is written (before disk flush).
    /// Subscribers should not block.
    /// </summary>
    public event Action<FileLogEntry>? OnLogWritten;

    /// <summary>
    /// Synchronously drains the in-memory buffer to disk.
    /// Exposed for testing — production code relies on the background timer.
    /// </summary>
    internal void Flush() => DrainBuffer();

    private void DrainBuffer()
    {
        if (_disposed || _buffer.IsEmpty) return;

        _rwLock.EnterWriteLock();
        try
        {
            while (_buffer.TryDequeue(out var json))
            {
                _writer!.WriteLine(json);
                _currentSize += json.Length + Environment.NewLine.Length;
            }
            _writer!.Flush();

            if (_currentSize >= _maxFileSize)
                Rotate();
        }
        catch
        {
            // Don't let drain failures crash the timer
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    private void OpenCurrentFile()
    {
        var path = Path.Combine(_logDir, CurrentFileName);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete);
        _currentSize = stream.Length;
        _writer = new StreamWriter(stream) { AutoFlush = false };
    }

    private void Rotate()
    {
        _writer?.Dispose();

        var currentPath = Path.Combine(_logDir, CurrentFileName);

        for (int i = _maxFiles - 1; i >= 1; i--)
        {
            var src = Path.Combine(_logDir, $"log-{i:D3}.jsonl");
            var dst = Path.Combine(_logDir, $"log-{i + 1:D3}.jsonl");
            if (File.Exists(src))
            {
                try
                {
                    if (i + 1 >= _maxFiles)
                        File.Delete(src);
                    else
                        File.Move(src, dst, true);
                }
                catch (IOException) { }
            }
        }

        try
        {
            if (File.Exists(currentPath))
                File.Move(currentPath, Path.Combine(_logDir, "log-001.jsonl"), true);
        }
        catch (IOException)
        {
            try { using var fs = new FileStream(currentPath, FileMode.Truncate, FileAccess.Write, FileShare.Read | FileShare.Delete); }
            catch (IOException) { }
        }

        OpenCurrentFile();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _drainTimer.Dispose();

        // Final drain under write lock
        _rwLock.EnterWriteLock();
        try
        {
            while (_buffer.TryDequeue(out var json))
                _writer?.WriteLine(json);
            _writer?.Flush();
            _writer?.Dispose();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }
}
