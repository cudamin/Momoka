using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Configuration;
using Momoka.Infrastructure;
using Momoka.LocalMedia;
using Momoka.Metadata;
using Momoka.MoviePilot;
using Momoka.Services;

namespace Momoka.Shell.ViewModels;

/// <summary>本机整理一次页面访问的草稿、读取和确认；外部数据与文件操作均交给 Core。</summary>
public sealed partial class LocalMediaViewModel : ObservableObject, IDisposable
{
    private ISettingsService? _settings;
    private ISecretProtector? _protector;
    private MoviePilotService? _moviePilot;
    private TmdbClient? _tmdb;
    private CancellationTokenSource? _operation;
    private Func<string, CancellationToken, Task<byte[]>>? _images;
    private int _revision;
    private bool _attached;
    private bool _loadingCorrection;
    private bool _proxyChangedWhileBusy;

    public ObservableCollection<LocalMediaRow> Rows { get; } = [];
    public ObservableCollection<LocalMediaCandidate> Candidates { get; } = [];
    public Func<string, Task<bool>>? Confirm { get; set; }

    [ObservableProperty] public partial int ProviderIndex { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial bool MpAvailable { get; set; }
    [ObservableProperty] public partial bool HasToken { get; set; }
    [ObservableProperty] public partial string Notice { get; set; } = "添加目录并保存媒体库后扫描；文件保持原位，元数据写入元数据目录。";
    [ObservableProperty] public partial LocalMediaRow? CurrentRow { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial int SearchTypeIndex { get; set; }
    [ObservableProperty] public partial string SeasonText { get; set; } = "";
    [ObservableProperty] public partial string EpisodeText { get; set; } = "";
    [ObservableProperty] public partial LocalMediaCandidate? Candidate { get; set; }

    public bool Editable => !Busy;
    public bool IsMoviePilot => ProviderIndex == 1;
    public bool IsMomoka => !IsMoviePilot;
    public bool ProviderReady => IsMoviePilot ? MpAvailable : HasToken;
    public bool CanScan => _attached && !Busy && ProviderReady && HasSavedLibrary;
    public bool CanApply => _attached && !Busy && ProviderReady && _coordinator is not null
        && Rows.Any(row => row.Ready && row.Selected);
    public bool CanCorrect => _attached && !Busy && ProviderReady && CurrentRow is { Attempted: false };
    public bool CanChooseCandidate => CanCorrect && Candidate is not null;
    public bool ShowCorrection => ProviderReady && CurrentRow is { Attempted: false };
    public string CurrentFileName => CurrentRow?.FileName ?? "";
    public bool ShowEpisodeFields => SearchTypeIndex == 1;
    public string TokenStatus => HasToken
        ? "已保存 API 读访问令牌；输入新值可替换。"
        : "填写 TMDB 网站上 API 设置中的 API 读访问令牌。";
    public string ApplyLabel => $"确认入库选中项（{Rows.Count(row => row.Ready && row.Selected)}）";

    internal void Attach(ISettingsService settings, ISecretProtector protector, MoviePilotService moviePilot)
    {
        _settings = settings;
        _protector = protector;
        _moviePilot = moviePilot;
        var saved = settings.Settings.LocalMedia;
        ProviderIndex = saved.Provider == "moviepilot" ? 1 : 0;
        MpAvailable = moviePilot.Available;
        OpenTmdb();
        if (!_attached) ShellPrefs.ProxyChanged += OnProxyChanged;
        _attached = true;
        Changed();
    }

    internal void RefreshAvailability()
    {
        MpAvailable = _moviePilot?.Available == true;
        if (IsMoviePilot) Invalidate("MoviePilot 连接已变化，请重新扫描或校正。", save: false);
        Changed();
    }

    public void SaveToken(string token)
    {
        if (!_attached || _settings is null || _protector is null || Busy) return;
        try
        {
            var encrypted = string.IsNullOrWhiteSpace(token) ? "" : _protector.Protect(token.Trim());
            if (token.Trim().Length > 0 && encrypted.Length == 0) throw new InvalidOperationException();
            _settings.Settings.LocalMedia.ProtectedTmdbToken = encrypted;
            if (!_settings.TrySave()) throw new IOException();
            Invalidate("API 读访问令牌已保存，请重新扫描。", save: false);
            OpenTmdb();
            if (_coordinator is not null) _ = _coordinator.RecognitionChangedAsync();
        }
        catch (Exception) { Notice = "无法保存 API 读访问令牌，请检查本机设置文件权限后重试。"; }
        Changed();
    }

    private void OpenTmdb()
    {
        _tmdb?.Dispose();
        _tmdb = null;
        string token;
        try { token = _protector?.Unprotect(_settings?.Settings.LocalMedia.ProtectedTmdbToken ?? "") ?? ""; }
        catch (Exception) { token = ""; }
        HasToken = !string.IsNullOrWhiteSpace(token);
        if (HasToken)
        {
            try { _tmdb = new(token, proxy: ProxyEndpoint.CreateWebProxy(_settings?.Settings.Network.ProxyServer)); }
            catch (Exception) { HasToken = false; Notice = "已保存的 API 读访问令牌无效，请重新填写。"; }
        }
    }

    /// <summary>
    /// 设置页改了代理：换一个新的 TMDB 客户端，下一次搜索/扫描就走新代理。正在扫描时不打断它，
    /// 等这一轮收尾（<see cref="End"/>）再换 —— 中途 Dispose 正在用的 HttpClient 会让那一轮直接失败。
    /// </summary>
    private void OnProxyChanged(NetworkSettings network)
    {
        if (Busy) { _proxyChangedWhileBusy = true; return; }
        OpenTmdb();
    }

    public async Task ScanAsync()
    {
        if (!CanScan || _coordinator is null) return;
        var libraries = _coordinator.Libraries.Where(value => !string.IsNullOrWhiteSpace(value.SourceDirectory)).ToArray();
        if (libraries.Length == 0) { Notice = "请先添加并保存媒体库目录。"; return; }
        using var operation = Begin();
        var revision = _revision;
        ClearRows();
        try
        {
            _images = IsMoviePilot ? _moviePilot!.CaptureImageFetcher() : _tmdb!.FetchImageAsync;
            var files = new List<(LocalMediaLibrarySettings Library, LocalMediaSource Source)>();
            foreach (var library in libraries)
            {
                operation.Token.ThrowIfCancellationRequested();
                Notice = $"正在扫描 {library.Name}…";
                try
                {
                    var sources = await Task.Run(() => LocalMediaOrganizer.Scan(library.SourceDirectory, library.Recursive, operation.Token), operation.Token);
                    foreach (var source in sources) files.Add((library, source));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { Notice = $"扫描“{library.Name}”失败：{ErrorText(error)}"; }
            }
            foreach (var (library, source) in files)
            {
                var row = new LocalMediaRow(source, library.Id, library.Name);
                row.PropertyChanged += RowChanged;
                Rows.Add(row);
            }
            for (var index = 0; index < Rows.Count; index++)
            {
                operation.Token.ThrowIfCancellationRequested();
                var row = Rows[index];
                Notice = $"正在识别 {index + 1} / {Rows.Count}：{row.FileName}";
                try
                {
                    // 只发送相对目录文本，不将用户名和驱动器信息交给元数据服务。
                    var context = Path.GetRelativePath(row.Source.RootPath, row.SourcePath);
                    if (IsMoviePilot)
                    {
                        var recognized = await _moviePilot!.RecognizeLocalFileAsync(context, operation.Token);
                        await SetPlanAsync(row, FromMoviePilot(recognized), recognized.Detail.Media.ConnectionStamp, revision, operation.Token);
                    }
                    else
                    {
                        var parsed = LocalMediaName.Parse(context);
                        if (!string.IsNullOrWhiteSpace(parsed.Problem)) throw new InvalidOperationException(parsed.Problem);
                        var match = await TmdbLocalMedia.FindUniqueAsync(_tmdb!, parsed, operation.Token)
                            ?? throw new InvalidOperationException("未找到唯一匹配，请选择此文件后手动搜索校正。");
                        var detail = await _tmdb!.DetailsAsync(match.Id, parsed.IsSeries, parsed.Season, parsed.Episode, operation.Token);
                        await SetPlanAsync(row, FromTmdb(detail), "", revision, operation.Token);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { row.Status = ErrorText(error); row.Recognized = "需要校正"; }
            }
            Notice = Rows.Count == 0 ? "目录中没有可处理的视频文件。" : $"扫描完成：{Rows.Count} 个文件，{Rows.Count(row => row.Ready)} 个可入库。请核对后确认。";
        }
        catch (OperationCanceledException) { Notice = "扫描已停止，未更改媒体文件。"; }
        catch (Exception error) { Notice = ErrorText(error); }
        finally { End(operation); }
    }

    public async Task SearchAsync()
    {
        if (!CanCorrect || string.IsNullOrWhiteSpace(SearchText)) return;
        _loadingCorrection = true;
        Candidate = null;
        Candidates.Clear();
        _loadingCorrection = false;
        using var operation = Begin();
        var revision = _revision;
        var query = SearchText;
        var series = SearchTypeIndex == 1;
        try
        {
            if (IsMoviePilot)
            {
                var results = await _moviePilot!.SearchAsync(query, operation.Token);
                if (revision != _revision) return;
                foreach (var result in results.Where(media => media.CanSubscribe && media.Type == (series ? "电视剧" : "电影")))
                    Candidates.Add(new(result.Confirmation.Replace('\n', ' '), result.MediaId!, series) { MoviePilot = result });
            }
            else
            {
                var results = await _tmdb!.SearchAsync(query, series, null, operation.Token);
                if (revision != _revision) return;
                foreach (var result in results) Candidates.Add(new($"{result.Title} ({result.Year}) · TMDB {result.Id}", result.Id, series));
            }
            Notice = Candidates.Count == 0 ? "没有匹配的作品，请换一个关键词。" : "请选择正确作品，核对季集，再更新预览。";
        }
        catch (OperationCanceledException) { Notice = "搜索已停止。"; }
        catch (Exception error) { Notice = ErrorText(error); }
        finally { End(operation); }
    }

    public async Task CorrectAsync()
    {
        if (!CanChooseCandidate || CurrentRow is not { } row || Candidate is not { } candidate) return;
        var library = _coordinator?.Libraries.FirstOrDefault(value => value.Id == row.LibraryId);
        if (library is null) { Notice = "媒体库已移除，请重新扫描。"; return; }
        using var operation = Begin();
        var revision = _revision;
        ClearPlan(row);
        try
        {
            var season = candidate.Series ? ReadNumber(SeasonText, 0, "季号") : (int?)null;
            var episode = candidate.Series ? ReadNumber(EpisodeText, 1, "集号") : (int?)null;
            if (IsMoviePilot)
            {
                var recognized = await _moviePilot!.ResolveLocalMediaAsync(candidate.MoviePilot!, season, episode, operation.Token);
                await SetPlanAsync(row, FromMoviePilot(recognized), recognized.Detail.Media.ConnectionStamp, revision, operation.Token);
                _images = _moviePilot.CaptureImageFetcher();
            }
            else
            {
                var detail = await _tmdb!.DetailsAsync(candidate.Id, candidate.Series, season, episode, operation.Token);
                await SetPlanAsync(row, FromTmdb(detail), "", revision, operation.Token);
                _images = _tmdb.FetchImageAsync;
            }
            Notice = "预览已更新，请核对新的目标。";
        }
        catch (OperationCanceledException) { Notice = "校正已停止。"; }
        catch (Exception error) { row.Status = ErrorText(error); Notice = row.Status; }
        finally { End(operation); }
    }

    private async Task SetPlanAsync(LocalMediaRow row, LocalMediaMetadata metadata, string stamp, int revision, CancellationToken token)
    {
        if (_coordinator is null) throw new InvalidOperationException("媒体库服务不可用。");
        var metadataRoot = _coordinator.MetadataRoot;
        var plan = await Task.Run(() => LocalMediaOrganizer.Prepare(row.Source, metadataRoot, row.LibraryName, metadata, token), token);
        token.ThrowIfCancellationRequested();
        if (revision != _revision || IsMoviePilot && !_moviePilot!.IsCurrentConnection(stamp)) throw new OperationCanceledException();
        row.Plan = plan;
        row.ConnectionStamp = stamp;
        row.VideoPath = plan.VideoPath;
        row.Recognized = metadata.Title + (metadata.Year is { } year ? $" ({year})" : "")
            + (metadata.MediaType == LocalMediaType.Episode ? $" · S{metadata.Season:00}E{metadata.Episode:00}" : "")
            + $" · {metadata.MediaSource} {metadata.MediaId}";
        row.Status = "待确认（原文件不改动）";
        row.Ready = row.Selected = true;
    }

    public async Task ApplyAsync()
    {
        if (!CanApply || Confirm is null || _images is null || _coordinator is null) return;
        var selected = Rows.Where(row => row.Ready && row.Selected && row.Plan is not null).ToArray();
        var revision = _revision;
        using var operation = Begin();
        try
        {
            var text = $"文件保持原位：不改名、不移动、不创建硬链接或软链接。\n元数据目录：{_coordinator.MetadataRoot}\n\n"
                + string.Join("\n\n", selected.Select(row => $"{row.SourcePath}\n→ {row.Recognized}"))
                + "\n\n写入 NFO、海报与剧照；同名元数据不覆盖。";
            if (!await Confirm(text) || revision != _revision || operation.IsCancellationRequested) return;
            foreach (var row in selected)
            {
                operation.Token.ThrowIfCancellationRequested();
                var plan = row.Plan!;
                row.Attempted = true;
                row.Ready = row.Selected = false;
                row.Plan = null;
                row.Status = "正在入库…";
                var result = await _coordinator.ExecuteManualAsync(row.LibraryId, plan, _images, operation.Token);
                row.Results = result.Items;
                row.Status = result.Summary;
                row.ResultDetails = string.Join("\n", result.Items.Select(item => $"{(item.Success ? "完成" : "失败")} · {item.Path}\n{item.Message}"));
                if (result.Cancelled) break;
            }
            Notice = "本轮处理结束。逐项结果保留在列表中；已写入的元数据不会因取消而自动撤销。";
        }
        catch (OperationCanceledException) { Notice = "已停止后续处理，请核对列表中已经执行的结果。"; }
        catch (Exception error) { Notice = ErrorText(error); }
        finally { End(operation); }
    }

    private static LocalMediaMetadata FromMoviePilot(MoviePilotLocalRecognition value) => Services.LocalLibraryCoordinator.FromMoviePilot(value);

    private static LocalMediaMetadata FromTmdb(TmdbLocalMetadata value) => Services.LocalLibraryCoordinator.FromTmdb(value);
    private static int ReadNumber(string value, int minimum, string label) => int.TryParse(value, out var number) && number >= minimum
        ? number : throw new InvalidOperationException($"请填写有效的{label}（至少 {minimum}）。");
    private static string ErrorText(Exception error) => error is IOException or UnauthorizedAccessException or InvalidOperationException or MoviePilotException
        or TmdbException
        ? error.Message : "处理失败，请检查网络、目录权限及识别服务配置。";

    private CancellationTokenSource Begin()
    {
        var source = new CancellationTokenSource();
        _operation = source;
        Busy = true;
        Changed();
        return source;
    }
    private void End(CancellationTokenSource source)
    {
        if (ReferenceEquals(_operation, source)) _operation = null;
        Busy = false;
        if (_proxyChangedWhileBusy)
        {
            _proxyChangedWhileBusy = false;
            OpenTmdb();
        }
        Changed();
    }
    public void Stop() => _operation?.Cancel();
    private void RowChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName is nameof(LocalMediaRow.Selected) or nameof(LocalMediaRow.Ready)) Changed(); }
    private void ClearRows()
    {
        foreach (var row in Rows) row.PropertyChanged -= RowChanged;
        Rows.Clear();
        CurrentRow = null;
        Candidates.Clear();
    }
    private void Invalidate(string notice = "选项已变化，请重新扫描或校正。", bool save = true)
    {
        if (!_attached) return;
        _revision++;
        Stop();
        foreach (var row in Rows.Where(row => !row.Attempted)) { row.Plan = null; row.Ready = row.Selected = false; row.Status = "预览已失效"; }
        Candidate = null;
        Candidates.Clear();
        Notice = notice;
        if (save && _settings is not null)
        {
            _settings.Settings.LocalMedia.Provider = IsMoviePilot ? "moviepilot" : "momoka";
            try { if (!_settings.TrySave()) throw new IOException(); }
            catch (Exception) { Notice = "选项已更改，但未保存成功，请检查本机设置文件权限。"; }
        }
        Changed();
    }
    private void Changed()
    {
        foreach (var name in new[] { nameof(Editable), nameof(IsMoviePilot), nameof(IsMomoka), nameof(ProviderReady), nameof(CanScan), nameof(CanApply), nameof(CanCorrect), nameof(CanChooseCandidate), nameof(ShowCorrection), nameof(ShowEpisodeFields), nameof(TokenStatus), nameof(ApplyLabel), nameof(CurrentFileName) })
            OnPropertyChanged(name);
        LibrariesStateChanged();
    }
    partial void OnProviderIndexChanged(int value)
    {
        if (!_attached) return;
        Invalidate();
        if (_coordinator is not null) _ = _coordinator.RecognitionChangedAsync();
    }
    private void ClearPlan(LocalMediaRow row)
    {
        if (row.Attempted) return;
        row.Plan = null;
        row.Ready = row.Selected = false;
        row.Status = "请更新预览后再确认";
        Changed();
    }
    private void CorrectionChanged()
    {
        if (!_loadingCorrection && CurrentRow is { } row)
        {
            _revision++;
            Stop();
            ClearPlan(row);
        }
    }
    partial void OnCandidateChanged(LocalMediaCandidate? value) { CorrectionChanged(); Changed(); }
    partial void OnSearchTypeIndexChanged(int value) { CorrectionChanged(); Candidate = null; Candidates.Clear(); Changed(); }
    partial void OnSearchTextChanged(string value) { CorrectionChanged(); Candidate = null; Candidates.Clear(); }
    partial void OnSeasonTextChanged(string value) => CorrectionChanged();
    partial void OnEpisodeTextChanged(string value) => CorrectionChanged();
    partial void OnCurrentRowChanged(LocalMediaRow? value)
    {
        _loadingCorrection = true;
        Candidate = null; Candidates.Clear();
        if (value is not null)
        {
            var name = LocalMediaName.Parse(value.SourcePath);
            SearchText = name.Title; SearchTypeIndex = name.IsSeries ? 1 : 0;
            SeasonText = name.Season?.ToString(CultureInfo.InvariantCulture) ?? "";
            EpisodeText = name.Episode?.ToString(CultureInfo.InvariantCulture) ?? "";
        }
        _loadingCorrection = false;
        Changed();
    }
    public void Dispose()
    {
        _attached = false;
        _revision++;
        Stop();
        ShellPrefs.ProxyChanged -= OnProxyChanged;
        _tmdb?.Dispose();
        if (_coordinator is not null) _coordinator.Changed -= LibrariesChanged;
        foreach (var row in Rows) { row.Plan = null; row.Ready = false; row.PropertyChanged -= RowChanged; }
        Changed();
    }
}
