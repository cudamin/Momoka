using System.Globalization;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.LocalMedia;
using Momoka.Metadata;
using Momoka.MoviePilot;
using Momoka.Services;
using Momoka.Shell.Platform;

namespace Momoka.Shell.Services;

/// <summary>一份目录配置对应一个监控；应用持有，页面只编辑规则及读取状态。</summary>
public sealed class LocalLibraryCoordinator : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly ISecretProtector _protector;
    private readonly MoviePilotService _moviePilot;
    private readonly IUiDispatcher _dispatcher;
    private readonly LocalMediaLibrary _library;
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly Dictionary<string, RunningLibrary> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _notices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ManualRun> _manual = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, LocalMediaMonitor> _mpMonitors = new(StringComparer.Ordinal);
    private bool _started;
    private bool _disposed;
    private long _mpGeneration;
    public event EventHandler? Changed;

    public LocalLibraryCoordinator(ISettingsService settings, ISecretProtector protector, MoviePilotService moviePilot,
        AppPaths paths, IUiDispatcher dispatcher)
    {
        _settings = settings; _protector = protector; _moviePilot = moviePilot; _dispatcher = dispatcher;
        LocalLibrarySettingsMigration.Normalize(settings.Settings.LocalMedia);
        _library = new(Path.Combine(paths.Root, "local-library.json"));
        _library.Changed += OnLibraryChanged;
        _moviePilot.AvailabilityChanged += OnAvailabilityChanged;
    }

    public IReadOnlyList<LocalMediaLibrarySettings> Libraries => _settings.Settings.LocalMedia.Libraries.Select(value => value.Copy()).ToArray();
    public IReadOnlyList<LocalMediaLibraryEntry> EntriesFor(string libraryId) =>
        _library.Entries.Where(entry => entry.LibraryId == libraryId).OrderByDescending(entry => entry.AddedAt).ToArray();
    public string StatusFor(string id)
    {
        if (_notices.TryGetValue(id, out var notice)) return notice;
        var state = _running.TryGetValue(id, out var running) ? running.Monitor.Status.Message : "自动监控未开启";
        var issues = _library.Issues.Where(issue => issue.LibraryId == id).ToArray();
        return issues.Length == 0 ? state : state + $" · {issues.Length} 项需要处理\n" + string.Join("\n", issues.Take(5).Select(issue => Path.GetFileName(issue.SourcePath) + "：" + issue.Message));
    }

    public async Task StartAsync()
    {
        if (_disposed || _started) return;
        _started = true;
        await ReconcileAsync();
    }

    public async Task SaveAsync(LocalMediaLibrarySettings configuration, CancellationToken cancellationToken = default)
    {
        configuration = configuration.Copy();
        await _changes.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            Validate(configuration);
            await StopLibraryAsync(configuration.Id);
            cancellationToken.ThrowIfCancellationRequested();
            var rows = _settings.Settings.LocalMedia.Libraries;
            var index = rows.FindIndex(value => value.Id == configuration.Id);
            var previous = index >= 0 ? rows[index] : null;
            var previousSelection = _settings.Settings.LocalMedia.SelectedLibraryId;
            if (index < 0) rows.Add(configuration.Copy()); else rows[index] = configuration.Copy();
            _settings.Settings.LocalMedia.SelectedLibraryId = configuration.Id;
            try
            {
                if (!_settings.TrySave()) throw new IOException("配置未保存成功，自动监控未启动");
            }
            catch
            {
                if (index < 0) rows.RemoveAll(value => value.Id == configuration.Id); else rows[index] = previous!;
                _settings.Settings.LocalMedia.SelectedLibraryId = previousSelection;
                _notices[configuration.Id] = "配置未保存成功，自动监控未启动";
                throw new IOException(_notices[configuration.Id]);
            }
            if (_started) await StartLibraryAsync(configuration.Copy());
        }
        finally { _changes.Release(); Notify(); }
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        await _changes.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await StopLibraryAsync(id);
            cancellationToken.ThrowIfCancellationRequested();
            var previous = _settings.Settings.LocalMedia.Libraries.Select(value => value.Copy()).ToArray();
            _settings.Settings.LocalMedia.Libraries.RemoveAll(value => value.Id == id);
            try
            {
                if (!_settings.TrySave()) throw new IOException();
            }
            catch
            {
                _settings.Settings.LocalMedia.Libraries.Clear();
                _settings.Settings.LocalMedia.Libraries.AddRange(previous);
                throw new IOException("配置未保存成功，删除尚未落盘；没有删除任何媒体文件。");
            }
        }
        finally { _changes.Release(); Notify(); }
    }

    public Task RefreshAsync(string id)
    {
        if (_running.TryGetValue(id, out var running)) running.Monitor.Refresh();
        Notify();
        return Task.CompletedTask;
    }

    internal async Task RecognitionChangedAsync()
    {
        try { if (_started && !_disposed) await ReconcileAsync(); }
        catch (Exception error) { Diagnostics.Log.Warn("本地媒体库", SafeError(error)); }
    }

    internal async Task<LocalMediaResult> ExecuteManualAsync(string libraryId, LocalMediaPlan plan,
        Func<string, CancellationToken, Task<byte[]>> images, CancellationToken cancellationToken)
    {
        // 手动确认不与同库的后台任务抢同一个源文件。
        await _changes.WaitAsync(cancellationToken);
        ManualRun? run = null;
        try
        {
            var configuration = Libraries.SingleOrDefault(value => value.Id == libraryId)
                ?? throw new InvalidOperationException("媒体库已移除，请重新打开。");
            if (!SamePath(configuration.SourceDirectory, plan.Source.RootPath) || Mode(configuration.Method) != plan.Mode
                || plan.Mode != LocalMediaLinkMode.Rename && !SamePath(configuration.TargetDirectory, plan.TargetRoot))
                throw new InvalidOperationException("媒体库规则已经改变，请重新预览。");
            await StopLibraryAsync(libraryId);
            run = new(CancellationTokenSource.CreateLinkedTokenSource(cancellationToken));
            _manual[libraryId] = run;
        }
        finally { _changes.Release(); }
        try { return await Task.Run(() => _library.ExecuteAsync(libraryId, plan, images, run.Cancellation.Token)); }
        finally
        {
            run.Completed.TrySetResult();
            await _changes.WaitAsync();
            try
            {
                var ownsSlot = _manual.TryGetValue(libraryId, out var current) && ReferenceEquals(current, run);
                if (ownsSlot) _manual.Remove(libraryId);
                run.Cancellation.Dispose();
                var configuration = Libraries.SingleOrDefault(value => value.Id == libraryId);
                if (ownsSlot && _started && !_disposed && configuration is not null)
                {
                    try { await StartLibraryAsync(configuration); }
                    catch (Exception error) { _notices[libraryId] = "自动监控恢复失败：" + SafeError(error); }
                }
            }
            finally { _changes.Release(); Notify(); }
        }
    }

    public async Task StopAsync()
    {
        _started = false;
        foreach (var operation in _manual.Values) operation.Cancellation.Cancel();
        await _changes.WaitAsync();
        try
        {
            foreach (var operation in _manual.Values.ToArray()) await operation.Completed.Task;
            foreach (var id in _running.Keys.ToArray()) await StopLibraryAsync(id);
        }
        finally { _changes.Release(); }
    }

    private async Task ReconcileAsync()
    {
        await _changes.WaitAsync();
        try
        {
            if (_disposed || !_started) return;
            var configurations = Libraries;
            foreach (var id in _running.Keys.ToArray()) await StopLibraryAsync(id);
            foreach (var configuration in configurations)
            {
                try { Validate(configuration); await StartLibraryAsync(configuration); }
                catch (Exception error) { _notices[configuration.Id] = SafeError(error); }
            }
        }
        finally { _changes.Release(); Notify(); }
    }

    private async Task StartLibraryAsync(LocalMediaLibrarySettings configuration)
    {
        _notices.Remove(configuration.Id);
        if (!configuration.MonitorEnabled || _manual.ContainsKey(configuration.Id)) return;
        TmdbClient? tmdb = null;
        var mpGeneration = Interlocked.Read(ref _mpGeneration);
        Func<LocalMediaSource, CancellationToken, Task<LocalMediaMetadata>> recognize;
        Func<string, CancellationToken, Task<byte[]>> images;
        if (configuration.Provider == "moviepilot")
        {
            if (!_moviePilot.Available) { _notices[configuration.Id] = "等待 MoviePilot 连接恢复"; return; }
            var stamp = _moviePilot.ConnectionStamp;
            images = _moviePilot.CaptureImageFetcher();
            recognize = async (source, token) =>
            {
                if (mpGeneration != Interlocked.Read(ref _mpGeneration) || !_moviePilot.Available || !_moviePilot.IsCurrentConnection(stamp)) throw new OperationCanceledException(token);
                var result = await _moviePilot.RecognizeLocalFileAsync(Path.GetRelativePath(source.RootPath, source.Path), token);
                if (mpGeneration != Interlocked.Read(ref _mpGeneration) || !_moviePilot.Available || !_moviePilot.IsCurrentConnection(stamp) || result.Detail.Media.ConnectionStamp != stamp)
                    throw new OperationCanceledException(token);
                return FromMoviePilot(result);
            };
        }
        else
        {
            string credential;
            try { credential = _protector.Unprotect(_settings.Settings.LocalMedia.ProtectedTmdbToken); }
            catch (Exception) { credential = ""; }
            if (string.IsNullOrWhiteSpace(credential)) { _notices[configuration.Id] = "请先保存 TMDB 读取令牌"; return; }
            tmdb = new(credential);
            var client = tmdb;
            images = client.FetchImageAsync;
            recognize = async (source, token) =>
            {
                var parsed = LocalMediaName.Parse(Path.GetRelativePath(source.RootPath, source.Path));
                if (parsed.Problem.Length > 0) throw new InvalidOperationException(parsed.Problem);
                var candidates = await client.SearchAsync(parsed.Title, parsed.IsSeries, parsed.Year, token);
                var selected = LocalMediaName.UniqueExactMatch(parsed, candidates)
                    ?? throw new InvalidOperationException("没有唯一匹配，请在本地整理中手动校正。");
                return FromTmdb(await client.DetailsAsync(selected.Id, parsed.IsSeries, parsed.Season, parsed.Episode, token));
            };
        }
        var monitor = new LocalMediaMonitor(_library, recognize, images);
        monitor.Updated += OnLibraryChanged;
        var state = new RunningLibrary(monitor, tmdb);
        _running[configuration.Id] = state;
        if (configuration.Provider == "moviepilot") _mpMonitors[configuration.Id] = monitor;
        try
        {
            await monitor.StartAsync(new(configuration.Id, configuration.SourceDirectory, configuration.TargetDirectory, Mode(configuration.Method))
            { Recursive = configuration.Recursive, IncludeExisting = true, ProviderKey = configuration.Provider });
            if (configuration.Provider == "moviepilot" && mpGeneration != Interlocked.Read(ref _mpGeneration)) monitor.Cancel();
        }
        catch
        {
            _running.Remove(configuration.Id);
            _mpMonitors.TryRemove(configuration.Id, out _);
            monitor.Updated -= OnLibraryChanged;
            await monitor.DisposeAsync();
            tmdb?.Dispose();
            throw;
        }
    }

    private async Task StopLibraryAsync(string id)
    {
        if (_manual.TryGetValue(id, out var manual))
        {
            manual.Cancellation.Cancel();
            await manual.Completed.Task;
            if (_manual.TryGetValue(id, out var current) && ReferenceEquals(current, manual)) _manual.Remove(id);
        }
        if (!_running.Remove(id, out var running)) return;
        _mpMonitors.TryRemove(id, out _);
        running.Monitor.Updated -= OnLibraryChanged;
        await running.Monitor.DisposeAsync();
        running.Tmdb?.Dispose();
    }

    private void Validate(LocalMediaLibrarySettings configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Name) || string.IsNullOrWhiteSpace(configuration.SourceDirectory))
            throw new InvalidOperationException("请填写媒体库名称和源目录。");
        if (string.IsNullOrWhiteSpace(configuration.Id) || configuration.Method is not ("hardlink" or "symboliclink" or "rename")
            || configuration.Provider is not ("momoka" or "moviepilot")) throw new InvalidOperationException("媒体库规则无效，请重新选择。");
        var root = Path.GetFullPath(configuration.SourceDirectory);
        if (configuration.Method == "hardlink"
            && (LocalMediaOrganizer.IsNetworkPath(root) || LocalMediaOrganizer.IsNetworkPath(configuration.TargetDirectory)))
            throw new InvalidOperationException("网络目录不支持硬链接，请选择软链接或直接改名。");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("源目录不存在或当前无法访问。");
        if (configuration.Method != "rename" && string.IsNullOrWhiteSpace(configuration.TargetDirectory))
            throw new InvalidOperationException("链接方式需要目标目录。");
        foreach (var other in Libraries.Where(value => value.Id != configuration.Id && !string.IsNullOrWhiteSpace(value.SourceDirectory)))
        {
            var existing = Path.GetFullPath(other.SourceDirectory);
            if (SamePath(existing, root) || existing.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || root.StartsWith(Path.TrimEndingDirectorySeparator(existing) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("媒体库源目录不能重复或嵌套，请选择独立目录。");
            if (configuration.Method != "rename" && Overlap(configuration.TargetDirectory, existing)
                || other.Method != "rename" && !string.IsNullOrWhiteSpace(other.TargetDirectory) && Overlap(other.TargetDirectory, root))
                throw new InvalidOperationException("一个媒体库的输出目录不能成为另一个媒体库的来源。");
        }
        if (configuration.Method != "rename" && Overlap(configuration.TargetDirectory, root))
            throw new InvalidOperationException("来源与输出目录不能重叠。");
    }

    internal static LocalMediaLinkMode Mode(string mode) => mode switch { "rename" => LocalMediaLinkMode.Rename, "symboliclink" => LocalMediaLinkMode.SymbolicLink, _ => LocalMediaLinkMode.HardLink };
    private static bool SamePath(string first, string second) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);
    private static bool Overlap(string first, string second)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(first));
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(second));
        return SamePath(a, b) || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    private static string SafeError(Exception error) => error is IOException or InvalidOperationException or ArgumentException or TmdbException or MoviePilotException ? error.Message : "本地媒体库处理失败，请检查目录和识别配置。";
    private void OnLibraryChanged(object? sender, EventArgs args) => Notify();
    private void Notify() => _dispatcher.Post(() => { if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); });
    private void OnAvailabilityChanged(object? sender, EventArgs args)
    {
        Interlocked.Increment(ref _mpGeneration);
        if (!_started || _disposed) return;
        foreach (var monitor in _mpMonitors.Values) monitor.Cancel();
        _dispatcher.Post(async () =>
        {
            try
            {
                await _changes.WaitAsync();
                try
                {
                    if (!_started || _disposed) return;
                    foreach (var configuration in Libraries.Where(value => value.Provider == "moviepilot"))
                    {
                        await StopLibraryAsync(configuration.Id);
                        await StartLibraryAsync(configuration);
                    }
                }
                finally { _changes.Release(); Notify(); }
            }
            catch (Exception error) { Diagnostics.Log.Warn("本地媒体库", SafeError(error)); }
        });
    }
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(LocalLibraryCoordinator)); }
    public void Dispose()
    {
        _disposed = true; _started = false;
        _moviePilot.AvailabilityChanged -= OnAvailabilityChanged;
        _library.Changed -= OnLibraryChanged;
        foreach (var operation in _manual.Values) operation.Cancellation.Cancel();
        foreach (var running in _running.Values)
        {
            running.Monitor.Cancel();
            _ = DisposeRunningAsync(running);
        }
    }
    private static async Task DisposeRunningAsync(RunningLibrary running)
    {
        try { await running.Monitor.DisposeAsync(); }
        catch (Exception) { }
        finally { running.Tmdb?.Dispose(); }
    }
    private sealed record RunningLibrary(LocalMediaMonitor Monitor, TmdbClient? Tmdb);
    private sealed record ManualRun(CancellationTokenSource Cancellation)
    {
        internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal static LocalMediaMetadata FromMoviePilot(MoviePilotLocalRecognition value)
    {
        var media = value.Detail.Media;
        return new(media.Title, media.Year, media.Type == "电视剧" ? LocalMediaType.Episode : LocalMediaType.Movie, media.MediaSource!, media.MediaId!)
        {
            Season = value.Season,
            Episode = value.Episode,
            EpisodeId = value.EpisodeId ?? "",
            EpisodeTitle = value.EpisodeTitle ?? "",
            Overview = media.Overview ?? "",
            EpisodeOverview = value.EpisodeOverview ?? "",
            PosterUrl = media.PosterUrl ?? "",
            BackdropUrl = media.BackdropUrl ?? "",
            EpisodeImageUrl = value.EpisodeImageUrl ?? "",
            Premiered = Date(value.Detail.Text("release_date", "first_air_date")),
            EpisodeAired = Date(value.EpisodeAirDate)
        };
    }
    internal static LocalMediaMetadata FromTmdb(TmdbLocalMetadata value) => new(value.Title, value.Year > 0 ? value.Year : null,
        value.IsSeries ? LocalMediaType.Episode : LocalMediaType.Movie, "themoviedb", value.Id)
    {
        Season = value.Season,
        Episode = value.Episode,
        EpisodeId = value.EpisodeId,
        EpisodeTitle = value.EpisodeTitle,
        Overview = value.Overview,
        EpisodeOverview = value.EpisodeOverview,
        PosterUrl = value.PosterUrl,
        BackdropUrl = value.BackdropUrl,
        EpisodeImageUrl = value.EpisodeImageUrl,
        Premiered = Date(value.Premiered),
        EpisodeAired = Date(value.EpisodeAired)
    };
    private static DateOnly? Date(string? value) => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
