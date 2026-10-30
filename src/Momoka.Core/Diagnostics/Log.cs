namespace Momoka.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public readonly record struct LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, string? Detail)
{
    public override string ToString() =>
        $"{Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff} [{Level.ToString().ToUpperInvariant(),-5}] {Category}: {Message}"
        + (Detail is null ? "" : Environment.NewLine + Detail);
}

/// <summary>
/// A copy of retained entries and the last write's sequence, captured under the same ring-buffer lock.
/// The watermark belongs to this sink, not to the text or timestamp: identical writes still advance it.
/// </summary>
public readonly record struct LogSnapshot(long Watermark, IReadOnlyList<LogEntry> Entries);

public interface ILogSink
{
    void Write(in LogEntry entry);
}

/// <summary>No-op sink; the default so that unit tests never touch the disk.</summary>
public sealed class NullLogSink : ILogSink
{
    public static readonly NullLogSink Instance = new();

    public void Write(in LogEntry entry)
    {
    }
}

/// <summary>
/// Keeps the most recent entries in memory so the UI can show a diagnostics panel
/// without re-reading the log file.
/// </summary>
public sealed class RingBufferLogSink : ILogSink
{
    private readonly LogEntry[] _buffer;
    private readonly object _gate = new();
    private int _next;
    private int _count;
    private long _sequence;

    public RingBufferLogSink(int capacity = 500)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _buffer = new LogEntry[capacity];
    }

    /// <summary>How many entries this sink keeps; the oldest is dropped past that.</summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Raised for each entry as it arrives, so a log view can follow along instead of re-reading
    /// <see cref="Snapshot"/> on a timer.
    /// <para>
    /// Raised on whichever thread wrote the entry — a background download, an mpv IPC read — and
    /// deliberately outside the lock: a handler is arbitrary code, and running it under the gate would
    /// put every other thread's logging behind it. A subscriber that touches UI has to marshal, and one
    /// that throws is swallowed here, because logging failing loudly is worse than the log line being
    /// lost.
    /// </para>
    /// <para>
    /// Each subscriber is called in its own <c>try</c>, so a throwing one loses only its own notification.
    /// Invoking the multicast delegate as a whole would have been cheaper, but a throw part-way through
    /// abandons every subscriber after it — which is not 「the log line being lost」 but one page's bug
    /// silently switching off an unrelated one's live view.
    /// </para>
    /// </summary>
    public event Action<LogEntry>? Written;

    public void Write(in LogEntry entry)
    {
        lock (_gate)
        {
            _buffer[_next] = entry;
            _next = (_next + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
            _sequence++;
        }

        if (Written is not { } handler) return;
        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<LogEntry>)subscriber)(entry);
            }
            catch
            {
                // Swallowed on purpose, and not logged: Log.Write is what got us here, so reporting this
                // through the same sink would recurse.
            }
        }
    }

    public IReadOnlyList<LogEntry> Snapshot() => SnapshotAfter(0).Entries;

    /// <summary>
    /// Returns retained writes after a previously captured watermark, oldest first. Zero reads the
    /// whole buffer; falling behind capacity returns only the retained tail, just like <see cref="Snapshot"/>.
    /// <para>
    /// Written is a wake-up, not an ordered delivery channel: arbitrary subscribers run outside the
    /// gate, so two writers can notify in reverse order. Reading the tail and its watermark atomically
    /// means a UI can consume every retained write once, even across a refresh or an out-of-order wake-up,
    /// without remembering an unbounded set of delivered IDs or deduplicating equal log text.
    /// </para>
    /// </summary>
    public LogSnapshot SnapshotAfter(long watermark)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(watermark);
        lock (_gate)
        {
            var count = watermark < _sequence ? (int)Math.Min(_count, _sequence - watermark) : 0;
            var result = count == 0 ? Array.Empty<LogEntry>() : new LogEntry[count];
            var start = (_next - count + _buffer.Length) % _buffer.Length;
            for (var i = 0; i < count; i++) result[i] = _buffer[(start + i) % _buffer.Length];
            return new LogSnapshot(_sequence, result);
        }
    }
}

/// <summary>
/// Immediately flushes each write, rotates at 8 MiB, and keeps at most 64 MiB / 14 days by default.
/// The directory is indexed once at construction. Write/rotation and the periodic maintenance use
/// that index, never a synchronous directory scan on a caller (which can be the UI thread).
/// </summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    private const long DefaultMaximumFileBytes = 8 * 1024 * 1024;
    private const long DefaultMaximumTotalBytes = 64 * 1024 * 1024;
    private const int MaximumDeletesPerWrite = 8;
    private const int MaximumMaintenanceDeletes = 64;
    private readonly string _directory;
    private readonly string[] _directoryPaths;
    private readonly object _gate = new();
    private readonly LogLevel _minimum;
    private readonly int _retainDays;
    private readonly long _maximumFileBytes;
    private readonly long _maximumTotalBytes;
    private readonly TimeProvider _clock;
    private readonly ITimer _maintenance;
    private readonly SortedSet<LogFile> _files = new(Comparer<LogFile>.Create((left, right) =>
    {
        var order = left.LastWrite.CompareTo(right.LastWrite);
        if (order != 0) return order;
        order = left.Day.CompareTo(right.Day);
        if (order != 0) return order;
        order = left.Part.CompareTo(right.Part);
        return order != 0 ? order : StringComparer.Ordinal.Compare(left.Path, right.Path);
    }));
    private readonly Dictionary<DateOnly, DayFiles> _days = new();
    private long _totalBytes;
    private LogFile? _current;
    private StreamWriter? _writer;
    private bool _disposed;
    private bool _budgetKnown = true;
    private DateTimeOffset? _failedAt;

    public FileLogSink(string directory, LogLevel minimum = LogLevel.Debug, int retainDays = 14)
        : this(directory, minimum, retainDays, DefaultMaximumFileBytes, DefaultMaximumTotalBytes, TimeProvider.System)
    {
    }

    // A private directory, small limits and an inert clock/timer let regression tests execute the
    // actual file owner without reading, pruning, or changing the user's real diagnostics directory.
    internal FileLogSink(string directory, LogLevel minimum, int retainDays, long maximumFileBytes,
        long maximumTotalBytes, TimeProvider clock, TimeSpan? maintenanceInterval = null)
    {
        if (!Enum.IsDefined(minimum)) throw new ArgumentOutOfRangeException(nameof(minimum));
        ArgumentOutOfRangeException.ThrowIfNegative(retainDays);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFileBytes, 128);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumFileBytes, int.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTotalBytes, maximumFileBytes);
        ArgumentNullException.ThrowIfNull(clock);
        var interval = maintenanceInterval ?? TimeSpan.FromMinutes(15);
        ArgumentOutOfRangeException.ThrowIfLessThan(interval, TimeSpan.FromMilliseconds(1));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(interval.TotalMilliseconds, uint.MaxValue - 1d);
        _directory = Path.GetFullPath(directory);
        var parents = new List<string>();
        for (DirectoryInfo? folder = new(_directory); folder is not null; folder = folder.Parent)
            parents.Add(folder.FullName);
        _directoryPaths = parents.ToArray();
        _minimum = minimum;
        _retainDays = retainDays;
        _maximumFileBytes = maximumFileBytes;
        _maximumTotalBytes = maximumTotalBytes;
        _clock = clock;
        GuardDirectoryPath(allowMissing: true);
        Directory.CreateDirectory(_directory);
        GuardDirectoryPath();
        IndexExistingFiles();
        Prune(_clock.GetUtcNow(), 0, MaximumMaintenanceDeletes);
        _maintenance = _clock.CreateTimer(_ => Maintain(), null, interval, interval);
    }

    public void Write(in LogEntry entry)
    {
        if (entry.Level < _minimum) return;
        var (line, bytes) = FileLogFormat.BoundLine(entry.ToString(), (int)_maximumFileBytes);
        lock (_gate)
        {
            if (_disposed || !_budgetKnown) return;
            try
            {
                var now = _clock.GetUtcNow();
                // Full disks/unwritable paths must not create an empty part and retry per UI log line.
                // Keep memory diagnostics untouched and retry disk IO at most once a minute.
                if (_failedAt is { } failedAt && now - failedAt < TimeSpan.FromMinutes(1)) return;
                if (_current is not null && Expired(_current, now)) Close();
                var day = DateOnly.FromDateTime(entry.Timestamp.LocalDateTime);
                if (_writer is null || _current!.Day != day || bytes > _maximumFileBytes - _current.Length)
                    Open(day, now);

                // Bound deletion work per write as well as bytes. If old files cannot be removed, drop
                // this disk write instead of growing past the total budget or looping on a UI caller.
                if (!_budgetKnown || !Prune(now, bytes, MaximumDeletesPerWrite)) return;
                _files.Remove(_current!);
                _current!.Length += bytes;
                _totalBytes += bytes;
                _current.LastWrite = now;
                _files.Add(_current);
                // Reserve before writing: a partial failed flush must not make the index undercount.
                // AutoFlush preserves the last completed line on process crash; no async log backlog.
                _writer!.WriteLine(line);
                _failedAt = null;
            }
            catch (IOException)
            {
                Close();
                _failedAt = _clock.GetUtcNow();
            }
            catch (UnauthorizedAccessException)
            {
                Close();
                _failedAt = _clock.GetUtcNow();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Close();
        }
        // Timer.Dispose does not join a queued callback on the UI thread. Such a callback checks
        // _disposed under the same gate and cannot reopen a writer or prune after disposal.
        _maintenance.Dispose();
    }

    private void Maintain()
    {
        LogFile[] candidates;
        lock (_gate)
        {
            if (_disposed) return;
            var now = _clock.GetUtcNow();
            if (_current is not null && Expired(_current, now)) Close();
            candidates = _files.Take(MaximumMaintenanceDeletes + 1).ToArray();
        }
        // One deletion-sized critical section at a time. A timer cannot hold a UI writer behind
        // all 64 maintenance deletions, and a queued callback cannot outlive Dispose's guard.
        var attempts = 0;
        foreach (var file in candidates)
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (file == _current || !_files.Contains(file)) continue;
                if (!Expired(file, _clock.GetUtcNow()) && _totalBytes <= _maximumTotalBytes) break;
                if (attempts++ == MaximumMaintenanceDeletes) break;
                TryDelete(file);
            }
        }
    }

    private void Open(DateOnly day, DateTimeOffset now)
    {
        Close();
        GuardDirectoryPath();
        // Do not retain an empty day entry when the directory/open fails. AddFile registers a day
        // only after a real part exists, so repeated failed timestamps cannot grow the index.
        if (!_days.TryGetValue(day, out var parts)) parts = new DayFiles();

        // Reopen/restart always starts a new part, even if the previous part is not full. A crash
        // can leave an unterminated UTF-8/line tail; never read, truncate, or glue a new entry to it.
        // Normally a single attempt. The cap also bounds collisions from an unindexed concurrent
        // creator; CreateNew never overwrites yesterday's/current-run/restarted-run diagnostics.
        for (var attempt = 0; attempt < MaximumDeletesPerWrite; attempt++)
        {
            if (parts.Part == int.MaxValue) throw new IOException("日志分卷编号已耗尽");
            var part = ++parts.Part;
            var path = Path.Combine(_directory, FileLogFormat.Name(day, part));
            FileStream stream;
            try
            {
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete);
            }
            catch (IOException) when (File.Exists(path))
            {
                if (ReadFile(path) is { } collision) AddFile(collision);
                continue;
            }
            var file = new LogFile(path, day, part, 0, now);
            AddFile(file);
            SetWriter(stream, file);
            return;
        }
        throw new IOException("无法创建独立日志分卷");
    }

    private void SetWriter(FileStream stream, LogFile file)
    {
        try
        {
            _writer = new StreamWriter(stream, FileLogFormat.Utf8) { AutoFlush = true };
            _current = file;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private void Close()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (IOException)
        {
            // A failed tail flush must not break shutdown or keep the handle reachable.
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            _writer = null;
            _current = null;
        }
    }

    private bool Expired(LogFile file, DateTimeOffset now) =>
        file.LastWrite < now && (now - file.LastWrite).TotalDays > _retainDays;

    private bool Prune(DateTimeOffset now, int requiredBytes, int maximumAttempts)
    {
        var withinBudget = _totalBytes <= _maximumTotalBytes - requiredBytes;
        if (_files.Count == 0 || (withinBudget && !Expired(_files.Min!, now))) return withinBudget;

        // A fixed-size snapshot permits removal without traversing the entire index. Sorted metadata
        // remains oldest-first even when the injected/system clock moves backwards or timestamps in
        // preexisting files lie in the future. The current writer can consume one extra candidate.
        var candidates = _files.Take(maximumAttempts + 1).ToArray();
        var attempts = 0;
        foreach (var file in candidates)
        {
            if (file == _current) continue;
            if (!Expired(file, now) && _totalBytes <= _maximumTotalBytes - requiredBytes) break;
            if (attempts++ == maximumAttempts) break;
            TryDelete(file);
        }
        return _totalBytes <= _maximumTotalBytes - requiredBytes;
    }

    private bool TryDelete(LogFile file)
    {
        try
        {
            GuardDirectoryPath();
            // An indexed regular file may have been replaced after startup. Never take ownership of
            // a link or directory, even though Windows DeleteFile would only remove a file link.
            try
            {
                if ((File.GetAttributes(file.Path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    return false;
            }
            catch (FileNotFoundException)
            {
                // Already removed externally: File.Delete is a no-op and retires the known bytes.
            }
            File.Delete(file.Path);
            _files.Remove(file);
            _totalBytes -= file.Length;
            var parts = _days[file.Day];
            if (--parts.Count == 0) _days.Remove(file.Day);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void GuardDirectoryPath(bool allowMissing = false)
    {
        // Cache lexical ancestors once. This is bounded metadata checking at startup/open/delete,
        // not directory enumeration on normal writes. Existing open handles stay on their original
        // file; a later rotation or maintenance must not follow a replaced directory into a target.
        foreach (var path in _directoryPaths)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (FileNotFoundException) when (allowMissing)
            {
                continue;
            }
            catch (DirectoryNotFoundException) when (allowMissing)
            {
                continue;
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new UnauthorizedAccessException("日志目录及其祖先必须是普通目录");
        }
    }

    private void IndexExistingFiles()
    {
        // Only startup enumerates the directory; subsequent entries update this oldest-first index.
        // The sink owns app-YYYYMMDD[-part].log, not arbitrary other files in the directory. Files
        // created by somebody else later are picked up on restart, not by UI writes or a timer scan.
        var files = new List<LogFile>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(_directory, "app-*.log"))
                if (ReadFile(path) is { } file) files.Add(file);
        }
        catch (IOException)
        {
            _budgetKnown = false;
        }
        catch (UnauthorizedAccessException)
        {
            _budgetKnown = false;
        }
        foreach (var file in files.OrderBy(file => file.LastWrite).ThenBy(file => file.Path, StringComparer.Ordinal))
            AddFile(file);
    }

    private LogFile? ReadFile(string path)
    {
        if (!FileLogFormat.TryReadName(Path.GetFileName(path), out var day, out var part)) return null;
        try
        {
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return null;
            return new LogFile(path, day, part, info.Length, info.LastWriteTimeUtc);
        }
        catch (IOException)
        {
            _budgetKnown = false;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            _budgetKnown = false;
            return null;
        }
    }

    private void AddFile(LogFile file)
    {
        if (!_files.Add(file)) return;
        _totalBytes += file.Length;
        if (!_days.TryGetValue(file.Day, out var parts))
        {
            parts = new DayFiles();
            _days.Add(file.Day, parts);
        }
        parts.Count++;
        if (file.Part < parts.Part) return;
        parts.Part = file.Part;
    }

    private sealed class DayFiles
    {
        internal int Count;
        internal int Part = -1;
    }

    private sealed class LogFile(string path, DateOnly day, int part, long length, DateTimeOffset lastWrite)
    {
        internal string Path { get; } = path;
        internal DateOnly Day { get; } = day;
        internal int Part { get; } = part;
        internal long Length = length;
        internal DateTimeOffset LastWrite = lastWrite;
    }
}

public sealed class CompositeLogSink(params ILogSink[] sinks) : ILogSink
{
    public void Write(in LogEntry entry)
    {
        foreach (var sink in sinks) sink.Write(entry);
    }
}

/// <summary>
/// Ambient logger. Deliberately a static facade rather than an injected dependency:
/// this app is a single process with one log, and threading it through every
/// constructor would be ceremony without payoff.
/// </summary>
public static class Log
{
    private static ILogSink _sink = NullLogSink.Instance;

    public static void UseSink(ILogSink sink) => _sink = sink;

    public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message, null);

    public static void Info(string category, string message) => Write(LogLevel.Info, category, message, null);

    public static void Warn(string category, string message, Exception? error = null) =>
        Write(LogLevel.Warn, category, message, Describe(error));

    public static void Error(string category, string message, Exception? error = null) =>
        Write(LogLevel.Error, category, message, Describe(error));

    private static void Write(LogLevel level, string category, string message, string? detail) =>
        _sink.Write(new LogEntry(DateTimeOffset.Now, level, category, message, detail));

    private static string? Describe(Exception? error) => error?.ToString();
}
