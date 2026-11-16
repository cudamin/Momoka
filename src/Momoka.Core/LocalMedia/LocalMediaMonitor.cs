namespace Momoka.LocalMedia;

/// <summary>一库一后台工作者，文件事件请求扫描，稳定且可拒写读取的文件才交给只读识别和密封整理计划。</summary>
public sealed class LocalMediaMonitor(LocalMediaLibrary library,
    Func<LocalMediaSource, CancellationToken, Task<LocalMediaMetadata>> recognize,
    Func<string, CancellationToken, Task<byte[]>> imageFetch) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private LocalMediaMonitorOptions? _options;
    private LocalMediaMonitorStatus _status = new(false, false, 0, "已停止");
    private int _scanRequested;
    private int _retryRecognition;
    private bool _disposed;
    public LocalMediaMonitorStatus Status => Volatile.Read(ref _status);
    public event EventHandler? Updated;
    public Task StartAsync(LocalMediaMonitorOptions options) => UpdateAsync(options);

    public async Task UpdateAsync(LocalMediaMonitorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            options = Normalize(options);
            if (_options == options && _worker is { IsCompleted: false }) return;
            await StopCoreAsync().ConfigureAwait(false);
            _options = options;
            _cancellation = new();
            Interlocked.Exchange(ref _scanRequested, 1);
            SetStatus(new(true, false, 0, "正在监控目录"));
            var cancellation = _cancellation.Token;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _worker = Task.Run(() => RunAsync(options, cancellation, ready), CancellationToken.None);
            await ready.Task.ConfigureAwait(false); // Initial snapshot and watcher are ready, not the lifetime loop.
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    public void Cancel()
    {
        var cancellation = Volatile.Read(ref _cancellation);
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { /* Stop already disposed the same generation. */ }
    }

    private async Task StopCoreAsync()
    {
        Cancel();
        if (_worker is not null) await _worker.ConfigureAwait(false);
        _worker = null;
        _cancellation?.Dispose();
        _cancellation = null;
        SetStatus(new(false, false, 0, "已停止"));
    }

    public void Refresh()
    {
        Interlocked.Exchange(ref _retryRecognition, 1);
        Interlocked.Exchange(ref _scanRequested, 1);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { _disposed = true; await StopCoreAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task RunAsync(LocalMediaMonitorOptions options, CancellationToken cancellationToken, TaskCompletionSource ready)
    {
        FileSystemWatcher? watcher = null;
        var pending = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var firstScan = true;
        var nextScan = DateTimeOffset.MinValue;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (Interlocked.Exchange(ref _retryRecognition, 0) != 0) library.RetryRecognition(options.LibraryId);
                    if (watcher is null && Directory.Exists(options.SourceRoot))
                    {
                        using var paths = new LocalMediaFiles();
                        paths.Directories(options.SourceRoot, create: false);
                        LocalMediaFiles.RequirePlain(options.SourceRoot, directory: true);
                        watcher = new(options.SourceRoot)
                        {
                            IncludeSubdirectories = options.Recursive,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                            InternalBufferSize = 32 * 1024
                        };
                        watcher.Created += OnFileChanged;
                        watcher.Changed += OnFileChanged;
                        watcher.Renamed += OnRenamed;
                        watcher.Deleted += OnFileChanged;
                        watcher.Error += OnError;
                        watcher.EnableRaisingEvents = true;
                        Interlocked.Exchange(ref _scanRequested, 1);
                    }
                    var now = DateTimeOffset.UtcNow;
                    if (Interlocked.Exchange(ref _scanRequested, 0) != 0 || now >= nextScan)
                    {
                        var files = LocalMediaOrganizer.Scan(options.SourceRoot, options.Recursive, cancellationToken);
                        var found = files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        foreach (var vanished in pending.Keys.Where(path => !found.Contains(path)).ToArray()) pending.Remove(vanished);
                        if (firstScan && !options.IncludeExisting) foreach (var file in files) excluded.Add(file.Path);
                        firstScan = false;
                        foreach (var file in files)
                        {
                            if (excluded.Contains(file.Path) || library.WasAttempted(options.LibraryId, file.Path)) continue;
                            if (!pending.ContainsKey(file.Path)) pending.Add(file.Path, new(file, now));
                        }
                        nextScan = now + options.RescanInterval;
                        ready.TrySetResult();
                    }
                    foreach (var pair in pending.ToArray())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (library.WasAttempted(options.LibraryId, pair.Key)) { pending.Remove(pair.Key); continue; }
                        var candidate = pair.Value;
                        if (!Observe(candidate, options, now)) continue;
                        try
                        {
                            using var paths = new LocalMediaFiles();
                            _ = paths.Read(candidate.Source.Path);
                        }
                        catch (IOException) { candidate.StableSince = now; continue; }
                        catch (UnauthorizedAccessException) { candidate.StableSince = now; continue; }
                        SetStatus(new(true, true, pending.Count, "正在识别和整理", candidate.Source.Path));
                        await ProcessAsync(candidate.Source, options, cancellationToken).ConfigureAwait(false);
                        pending.Remove(pair.Key);
                    }
                    SetStatus(new(true, false, pending.Count, pending.Count > 0 ? "正在等待文件写入稳定" : "正在监控目录"));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
                {
                    watcher?.Dispose(); watcher = null;
                    nextScan = DateTimeOffset.UtcNow + options.RescanInterval;
                    SetStatus(new(true, false, pending.Count, "目录或索引不可用，等待重新扫描。" + LocalMediaLibrary.SafeMessage(error.Message)));
                    ready.TrySetResult();
                }
                await Task.Delay(options.PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { watcher?.Dispose(); ready.TrySetResult(); }

        void OnFileChanged(object sender, FileSystemEventArgs args) => Interlocked.Exchange(ref _scanRequested, 1);
        void OnRenamed(object sender, RenamedEventArgs args) => Interlocked.Exchange(ref _scanRequested, 1);
        void OnError(object sender, ErrorEventArgs args)
        {
            Interlocked.Exchange(ref _scanRequested, 1);
            SetStatus(new(true, false, 0, "目录通知中断，正在重新扫描恢复"));
        }
    }

    private async Task ProcessAsync(LocalMediaSource source, LocalMediaMonitorOptions options, CancellationToken cancellationToken)
    {
        LocalMediaPlan plan;
        try
        {
            var metadata = await recognize(source, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            plan = LocalMediaOrganizer.Prepare(source, options.TargetRoot, metadata, options.Mode, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            library.RecognitionFailed(options.LibraryId, source.Path, error.Message);
            SetStatus(new(true, false, 0, "识别或规划失败，待校正。" + LocalMediaLibrary.SafeMessage(error.Message), source.Path));
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var result = await library.ExecuteAsync(options.LibraryId, plan, imageFetch, cancellationToken).ConfigureAwait(false);
        SetStatus(new(true, false, 0, result.Summary, source.Path));
    }

    private static bool Observe(Candidate candidate, LocalMediaMonitorOptions options, DateTimeOffset now)
    {
        try
        {
            LocalMediaFiles.RequirePlain(candidate.Source.Path, directory: false);
            var info = new FileInfo(candidate.Source.Path);
            var source = new LocalMediaSource(info.FullName, info.Length, info.LastWriteTimeUtc) { RootPath = options.SourceRoot };
            if (source.Length != candidate.Source.Length || source.LastWriteTimeUtc != candidate.Source.LastWriteTimeUtc)
            { candidate.Source = source; candidate.StableSince = now; candidate.Samples = 1; return false; }
            candidate.Samples++;
            return source.Length > 0 && candidate.Samples >= 2 && now - candidate.StableSince >= options.SettleTime;
        }
        catch (IOException) { candidate.StableSince = now; return false; }
        catch (UnauthorizedAccessException) { candidate.StableSince = now; return false; }
    }

    private static LocalMediaMonitorOptions Normalize(LocalMediaMonitorOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LibraryId);
        if (!Enum.IsDefined(options.Mode) || options.PollInterval < TimeSpan.FromMilliseconds(25) || options.PollInterval > TimeSpan.FromMinutes(1) ||
            options.SettleTime < TimeSpan.FromMilliseconds(50) || options.SettleTime > TimeSpan.FromMinutes(10) || options.RescanInterval < options.PollInterval)
            throw new ArgumentException("目录监控方式或等待时间无效");
        var source = LocalMediaFiles.FullPath(options.SourceRoot);
        var target = options.Mode == LocalMediaLinkMode.Rename ? "" : LocalMediaFiles.FullPath(options.TargetRoot);
        if (options.Mode == LocalMediaLinkMode.HardLink && (LocalMediaFiles.IsNetwork(source) || LocalMediaFiles.IsNetwork(target)))
            throw new IOException("网络目录不支持硬链接，请选择软链接或直接改名");
        if (target.Length > 0 && (target.Equals(source, StringComparison.OrdinalIgnoreCase) || target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("监控目标不能位于源目录内");
        return options with { SourceRoot = source, TargetRoot = target };
    }

    private void SetStatus(LocalMediaMonitorStatus status)
    {
        if (status == Status) return;
        Volatile.Write(ref _status, status);
        if (Updated is not { } handlers) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception) { /* UI subscribers cannot stop the worker. */ }
        }
    }

    private sealed class Candidate(LocalMediaSource source, DateTimeOffset now)
    {
        public LocalMediaSource Source { get; set; } = source;
        public DateTimeOffset StableSince { get; set; } = now;
        public int Samples { get; set; } = 1;
    }
}
