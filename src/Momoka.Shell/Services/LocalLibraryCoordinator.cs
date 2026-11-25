using System.Globalization;
using System.Text.Json;
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
    private readonly IUiDispatcher? _dispatcher;
    private readonly AppPaths _paths;
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
        AppPaths paths, IUiDispatcher? dispatcher)
    {
        _settings = settings; _protector = protector; _moviePilot = moviePilot; _dispatcher = dispatcher; _paths = paths;
        LocalLibrarySettingsMigration.Normalize(settings.Settings.LocalMedia);
        _library = new(Path.Combine(paths.Root, "local-library.json"));
        _library.Changed += OnLibraryChanged;
        _moviePilot.AvailabilityChanged += OnAvailabilityChanged;
        // 代理改了：重开各库监控，让每一份 TmdbClient 拿到新的 HttpClient。设置页在另一个窗口，
        // 这根静态线就是它唯一能碰到这里的方式（同 ShellPrefs 的其它信号）。
        ShellPrefs.ProxyChanged += OnProxyChanged;
    }

    public IReadOnlyList<LocalMediaLibrarySettings> Libraries => _settings.Settings.LocalMedia.Libraries.Select(value => value.Copy()).ToArray();
    public IReadOnlyList<LocalMediaLibraryEntry> EntriesFor(string libraryId) =>
        _library.Entries.Where(entry => entry.LibraryId == libraryId).OrderByDescending(entry => entry.AddedAt).ToArray();

    /// <summary>元数据默认落点；界面用它显示「留空时保存在哪里」。</summary>
    public string DefaultMetadataDirectory => _paths.LocalMediaMetadataDirectory;

    /// <summary>当前实际使用的元数据根目录：自定义值优先，空串回落到默认目录。</summary>
    public string MetadataRoot => ResolveMetadataRoot(_settings.Settings.LocalMedia.MetadataDirectory);

    internal string ResolveMetadataRoot(string? configured)
    {
        var value = TypedPath.Clean(configured);
        if (value.Length == 0) return _paths.LocalMediaMetadataDirectory;
        try { return Path.GetFullPath(value); }
        catch (Exception) { return value; }
    }
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

    /// <summary>重试本库已入库但附属文件未完成的条目；只补写缺失项，不覆盖已核对的同作品元数据。</summary>
    public Task RetryIncompleteAsync(string id)
    {
        _library.RetryIncomplete(id);
        if (_running.TryGetValue(id, out var running)) running.Monitor.Refresh();
        Notify();
        return Task.CompletedTask;
    }

    /// <summary>本机详情页的已看/收藏：写到本地索引，作品分组一起改；成功返回 true。</summary>
    public bool SetLocalUserState(string libraryId, string groupKey, bool? watched, bool? favorite)
    {
        if (_disposed) return false;
        var updated = _library.SetUserState(libraryId, groupKey, watched, favorite);
        if (updated) Notify();
        return updated;
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
            if (!SamePath(configuration.SourceDirectory, plan.Source.RootPath))
                throw new InvalidOperationException("媒体库目录已经改变，请重新预览。");
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
                // 启动时共享可能暂时离线：让监控先跑起来按重新扫描间隔等待恢复，不把规则卡在错误里。
                try { Validate(configuration, requireSource: false); await StartLibraryAsync(configuration); }
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
        var provider = _settings.Settings.LocalMedia.Provider == "moviepilot" ? "moviepilot" : "momoka";
        Func<LocalMediaSource, CancellationToken, Task<LocalMediaMetadata>> recognize;
        Func<string, CancellationToken, Task<byte[]>> images;
        if (provider == "moviepilot")
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
            if (string.IsNullOrWhiteSpace(credential)) { _notices[configuration.Id] = "请先保存 API 读访问令牌"; return; }
            // 代理在每次重建监控时读一次：设置页改完会喊 ShellPrefs.ApplyProxy，这里重开监控就换上新代理。
            tmdb = new(credential, proxy: ProxyEndpoint.CreateWebProxy(_settings.Settings.Network.ProxyServer));
            var client = tmdb;
            images = client.FetchImageAsync;
            recognize = async (source, token) =>
            {
                var parsed = LocalMediaName.Parse(Path.GetRelativePath(source.RootPath, source.Path));
                if (parsed.Problem.Length > 0) throw new InvalidOperationException(parsed.Problem);
                var selected = await TmdbLocalMedia.FindUniqueAsync(client, parsed, token)
                    ?? throw new InvalidOperationException("没有唯一匹配，请在本机整理中手动校正。");
                return FromTmdb(await client.DetailsAsync(selected.Id, parsed.IsSeries, parsed.Season, parsed.Episode, token));
            };
        }
        var monitor = new LocalMediaMonitor(_library, recognize, images);
        monitor.Updated += OnLibraryChanged;
        var state = new RunningLibrary(monitor, tmdb);
        _running[configuration.Id] = state;
        if (provider == "moviepilot") _mpMonitors[configuration.Id] = monitor;
        try
        {
            // 旧版取图把 WebP 当后缀不符拒写；启动时自动补一次，用户不必先找到重试按钮。
            _library.RetryBrokenArtwork(configuration.Id);
            // 旧版没有人物编号与头像：同样在启动时补一次，本机详情页的演职人员才不会永久缺一块。
            if (tmdb is not null) _library.RetryMissingPeople(configuration.Id);
            await monitor.StartAsync(new(configuration.Id, configuration.SourceDirectory, MetadataRoot, configuration.Name)
            { Recursive = configuration.Recursive, IncludeExisting = true, ProviderKey = provider });
            if (provider == "moviepilot" && mpGeneration != Interlocked.Read(ref _mpGeneration)) monitor.Cancel();
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

    private void Validate(LocalMediaLibrarySettings configuration, bool requireSource = true)
    {
        if (string.IsNullOrWhiteSpace(configuration.Name) || string.IsNullOrWhiteSpace(configuration.SourceDirectory))
            throw new InvalidOperationException("请填写媒体库名称和目录。");
        if (string.IsNullOrWhiteSpace(configuration.Id))
            throw new InvalidOperationException("媒体库规则无效，请重新选择。");
        var root = Path.GetFullPath(configuration.SourceDirectory);
        if (requireSource && !Directory.Exists(root)) throw new DirectoryNotFoundException("媒体库目录不存在或当前无法访问。");
        foreach (var other in Libraries.Where(value => value.Id != configuration.Id && !string.IsNullOrWhiteSpace(value.SourceDirectory)))
        {
            if (string.Equals(other.Name.Trim(), configuration.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("媒体库名称不能重复，元数据按名称分目录保存。");
            var existing = Path.GetFullPath(other.SourceDirectory);
            if (SamePath(existing, root) || existing.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || root.StartsWith(Path.TrimEndingDirectorySeparator(existing) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("媒体库目录不能重复或嵌套，请选择独立目录。");
        }
    }

    /// <summary>代理改了：重开所有监控，每一份 TmdbClient 都按新代理重建。不重开的话后台刮削会一直用旧连接。</summary>
    private void OnProxyChanged(NetworkSettings network)
    {
        if (!_started || _disposed) return;
        _ = RecognitionChangedAsync();
    }

    private static bool SamePath(string first, string second) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);
    private static string SafeError(Exception error) => error is IOException or InvalidOperationException or ArgumentException or TmdbException or MoviePilotException ? error.Message : "本地媒体库处理失败，请检查目录和识别配置。";
    private void OnLibraryChanged(object? sender, EventArgs args) => Notify();
    private void Notify()
    {
        if (_disposed) return;
        if (_dispatcher is null) { Changed?.Invoke(this, EventArgs.Empty); return; }
        _dispatcher.Post(() => { if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); });
    }
    private void OnAvailabilityChanged(object? sender, EventArgs args)
    {
        Interlocked.Increment(ref _mpGeneration);
        if (!_started || _disposed) return;
        foreach (var monitor in _mpMonitors.Values) monitor.Cancel();
        if (_dispatcher is null) return;
        _dispatcher.Post(async () =>
        {
            try
            {
                await _changes.WaitAsync();
                try
                {
                    if (!_started || _disposed) return;
                    foreach (var configuration in Libraries.Where(value => _settings.Settings.LocalMedia.Provider == "moviepilot"))
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
        ShellPrefs.ProxyChanged -= OnProxyChanged;
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
            Tagline = MoviePilotSubscription.Text(value.Detail.Data, "tagline"),
            CommunityRating = media.Rating ?? (value.Detail.Vote is > 0 and var vote ? vote : null),
            Genres = SplitNames(value.Detail.Genres),
            Studios = MpNames(value.Detail.Data, value.Detail.Media.Type == "电视剧" ? "networks" : "production_companies"),
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
        Tagline = value.Tagline,
        CommunityRating = value.Rating > 0 ? value.Rating : null,
        RuntimeMinutes = value.Runtime > 0 ? value.Runtime : null,
        Genres = value.Genres.ToArray(),
        Studios = value.Studios.ToArray(),
        People = value.People.Select(person => new LocalMediaPerson(person.Name, person.Role, person.Director)
        {
            Id = person.Id,
            ImageUrl = person.ImageUrl
        }).ToArray(),
        Premiered = Date(value.Premiered),
        EpisodeAired = Date(value.EpisodeAired)
    };
    private static string[] SplitNames(string value) => string.IsNullOrWhiteSpace(value) ? [] : value
        .Split(['/', '、', ',', '，', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal).Take(8).ToArray();
    private static string[] MpNames(JsonElement data, string field)
    {
        if (MoviePilotSubscription.Member(data, field) is not { ValueKind: JsonValueKind.Array } array) return [];
        return array.EnumerateArray().Take(2)
            .Select(item => item.ValueKind == JsonValueKind.Object ? MoviePilotSubscription.Text(item, "name").Trim() : "")
            .Where(name => name.Length > 0).ToArray();
    }
    private static DateOnly? Date(string? value) => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
