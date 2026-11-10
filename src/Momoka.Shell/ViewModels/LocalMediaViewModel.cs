using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Configuration;
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

    public ObservableCollection<LocalMediaRow> Rows { get; } = [];
    public ObservableCollection<LocalMediaCandidate> Candidates { get; } = [];
    public Func<string, Task<bool>>? Confirm { get; set; }

    [ObservableProperty] public partial string SourceDirectory { get; set; } = "";
    [ObservableProperty] public partial string TargetDirectory { get; set; } = "";
    [ObservableProperty] public partial int MethodIndex { get; set; }
    [ObservableProperty] public partial int ProviderIndex { get; set; }
    [ObservableProperty] public partial bool Recursive { get; set; } = true;
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial bool MpAvailable { get; set; }
    [ObservableProperty] public partial bool HasToken { get; set; }
    [ObservableProperty] public partial string Notice { get; set; } = "选择源目录后扫描，先核对识别结果和目标文件。";
    [ObservableProperty] public partial LocalMediaRow? CurrentRow { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial int SearchTypeIndex { get; set; }
    [ObservableProperty] public partial string SeasonText { get; set; } = "";
    [ObservableProperty] public partial string EpisodeText { get; set; } = "";
    [ObservableProperty] public partial LocalMediaCandidate? Candidate { get; set; }

    public bool Editable => !Busy;
    public bool IsMoviePilot => ProviderIndex == 1;
    public bool IsMomoka => !IsMoviePilot;
    public bool NeedsTarget => MethodIndex != 2;
    public bool ProviderReady => IsMoviePilot ? MpAvailable : HasToken;
    public bool CanScan => _attached && !Busy && ProviderReady && !string.IsNullOrWhiteSpace(SourceDirectory)
        && (!NeedsTarget || !string.IsNullOrWhiteSpace(TargetDirectory));
    public bool CanApply => _attached && !Busy && ProviderReady && Rows.Any(row => row.Ready && row.Selected);
    public bool CanCorrect => _attached && !Busy && ProviderReady && CurrentRow is { Attempted: false };
    public bool CanChooseCandidate => CanCorrect && Candidate is not null;
    public bool ShowCorrection => ProviderReady && CurrentRow is { Attempted: false };
    public string CurrentFileName => CurrentRow?.FileName ?? "";
    public bool ShowEpisodeFields => SearchTypeIndex == 1;
    public string TokenStatus => HasToken ? "已保存 TMDB 读取令牌；输入新值可替换。" : "填写 TMDB 帐号 API 设置中的 API Read Access Token。";
    public string MethodNote => MethodIndex switch
    {
        1 => "软链接保留源文件，源移动或删除后链接会失效；Windows 可能需要开发者模式或创建链接权限。",
        2 => "直接在原目录更改视频和同名字幕的文件名；确认前请核对每个新路径。",
        _ => "硬链接要求源和目标位于同一卷，不额外复制视频；两处共享内容，编辑任一处的视频会影响另一处。"
    };
    public string ApplyLabel => $"确认整理选中项（{Rows.Count(row => row.Ready && row.Selected)}）";

    internal void Attach(ISettingsService settings, ISecretProtector protector, MoviePilotService moviePilot)
    {
        _settings = settings;
        _protector = protector;
        _moviePilot = moviePilot;
        var saved = settings.Settings.LocalMedia;
        SourceDirectory = saved.SourceDirectory;
        TargetDirectory = saved.TargetDirectory;
        MethodIndex = saved.Method switch { "symboliclink" => 1, "rename" => 2, _ => 0 };
        ProviderIndex = saved.Provider == "moviepilot" ? 1 : 0;
        Recursive = saved.Recursive;
        MpAvailable = moviePilot.Available;
        OpenTmdb();
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
            Invalidate("读取令牌已保存，请重新扫描。", save: false);
            OpenTmdb();
        }
        catch (Exception) { Notice = "无法保存读取令牌，请检查本机设置文件权限后重试。"; }
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
            try { _tmdb = new(token); }
            catch (Exception) { HasToken = false; Notice = "已保存的 TMDB 读取令牌无效，请重新填写。"; }
        }
    }

    public async Task ScanAsync()
    {
        if (!CanScan) return;
        using var operation = Begin();
        var revision = _revision;
        ClearRows();
        try
        {
            _images = IsMoviePilot ? _moviePilot!.CaptureImageFetcher() : _tmdb!.FetchImageAsync;
            Notice = "正在扫描本机目录…";
            var sources = await Task.Run(() => LocalMediaOrganizer.Scan(SourceDirectory, Recursive, operation.Token), operation.Token);
            foreach (var source in sources)
            {
                var row = new LocalMediaRow(source);
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
                        var results = await _tmdb!.SearchAsync(parsed.Title, parsed.IsSeries, parsed.Year, operation.Token);
                        var match = LocalMediaName.UniqueExactMatch(parsed, results)
                            ?? throw new InvalidOperationException("未找到唯一匹配，请选择此文件后手动搜索校正。");
                        var detail = await _tmdb.DetailsAsync(match.Id, parsed.IsSeries, parsed.Season, parsed.Episode, operation.Token);
                        await SetPlanAsync(row, FromTmdb(detail), "", revision, operation.Token);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { row.Status = ErrorText(error); row.Recognized = "需要校正"; }
            }
            RejectBatchConflicts();
            Notice = Rows.Count == 0 ? "目录中没有可处理的视频文件。" : $"扫描完成：{Rows.Count} 个文件，{Rows.Count(row => row.Ready)} 个可整理。请核对后确认。";
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
            RejectBatchConflicts();
            Notice = "预览已更新，请核对新的目标。";
        }
        catch (OperationCanceledException) { Notice = "校正已停止。"; }
        catch (Exception error) { row.Status = ErrorText(error); Notice = row.Status; }
        finally { End(operation); }
    }

    private async Task SetPlanAsync(LocalMediaRow row, LocalMediaMetadata metadata, string stamp, int revision, CancellationToken token)
    {
        var target = TargetDirectory;
        var mode = MethodIndex switch { 1 => LocalMediaLinkMode.SymbolicLink, 2 => LocalMediaLinkMode.Rename, _ => LocalMediaLinkMode.HardLink };
        var plan = await Task.Run(() => LocalMediaOrganizer.Prepare(row.Source, target, metadata, mode, token), token);
        token.ThrowIfCancellationRequested();
        if (revision != _revision || IsMoviePilot && !_moviePilot!.IsCurrentConnection(stamp)) throw new OperationCanceledException();
        row.Plan = plan;
        row.ConnectionStamp = stamp;
        row.Target = plan.TargetPath;
        row.Recognized = metadata.Title + (metadata.Year is { } year ? $" ({year})" : "")
            + (metadata.MediaType == LocalMediaType.Episode ? $" · S{metadata.Season:00}E{metadata.Episode:00}" : "")
            + $" · {metadata.MediaSource} {metadata.MediaId}";
        row.Status = "待确认";
        row.Ready = row.Selected = true;
    }

    public async Task ApplyAsync()
    {
        if (!CanApply || Confirm is null || _images is null) return;
        var selected = Rows.Where(row => row.Ready && row.Selected && row.Plan is not null).ToArray();
        var revision = _revision;
        using var operation = Begin();
        try
        {
            var text = MethodNote + "\n\n" + string.Join("\n\n", selected.Select(row => $"{row.SourcePath}\n→ {row.Target}"
                + "\n" + string.Join("\n", row.Plan!.Sidecars.Select(sidecar => "  + " + sidecar.Path))))
                + "\n\n同时处理上述字幕、NFO 和图片；同名目标不覆盖。";
            if (!await Confirm(text) || revision != _revision || operation.IsCancellationRequested) return;
            foreach (var row in selected)
            {
                operation.Token.ThrowIfCancellationRequested();
                if (IsMoviePilot && (!_moviePilot!.Available || !_moviePilot.IsCurrentConnection(row.ConnectionStamp)))
                    throw new InvalidOperationException("MoviePilot 连接已变化，未继续整理。");
                var plan = row.Plan!;
                row.Attempted = true;
                row.Ready = row.Selected = false;
                row.Plan = null;
                row.Status = "正在整理…";
                var result = await Task.Run(() => LocalMediaOrganizer.ExecuteAsync(plan, _images, operation.Token));
                row.Results = result.Items;
                row.Status = result.Summary;
                row.ResultDetails = string.Join("\n", result.Items.Select(item => $"{(item.Success ? "完成" : "失败")} · {item.Path}\n{item.Message}"));
                if (result.Cancelled) break;
            }
            Notice = "本轮处理结束。逐项结果保留在列表中；已创建的文件不会因取消而自动撤销。";
        }
        catch (OperationCanceledException) { Notice = "已停止后续处理，请核对列表中已经执行的结果。"; }
        catch (Exception error) { Notice = ErrorText(error); }
        finally { End(operation); }
    }

    private static LocalMediaMetadata FromMoviePilot(MoviePilotLocalRecognition value)
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

    private static LocalMediaMetadata FromTmdb(TmdbLocalMetadata value) => new(value.Title, value.Year > 0 ? value.Year : null,
        value.IsSeries ? LocalMediaType.Episode : LocalMediaType.Movie, "themoviedb", value.Id)
    {
        Season = value.Season,
        Episode = value.Episode,
        EpisodeId = value.EpisodeId ?? "",
        EpisodeTitle = value.EpisodeTitle ?? "",
        Overview = value.Overview ?? "",
        EpisodeOverview = value.EpisodeOverview ?? "",
        PosterUrl = value.PosterUrl ?? "",
        BackdropUrl = value.BackdropUrl ?? "",
        EpisodeImageUrl = value.EpisodeImageUrl ?? "",
        Premiered = Date(value.Premiered),
        EpisodeAired = Date(value.EpisodeAired)
    };

    private static DateOnly? Date(string? value) => DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
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
    private void RejectBatchConflicts()
    {
        foreach (var group in Rows.Where(row => row.Plan is not null).GroupBy(row => row.Target, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            foreach (var row in group) { row.Plan = null; row.Ready = row.Selected = false; row.Status = "多个源文件指向同一个目标，请校正或分开处理。"; }
        Changed();
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
            var value = _settings.Settings.LocalMedia;
            value.SourceDirectory = SourceDirectory;
            value.TargetDirectory = TargetDirectory;
            value.Method = MethodIndex switch { 1 => "symboliclink", 2 => "rename", _ => "hardlink" };
            value.Provider = IsMoviePilot ? "moviepilot" : "momoka";
            value.Recursive = Recursive;
            try { if (!_settings.TrySave()) throw new IOException(); }
            catch (Exception) { Notice = "选项已更改，但未保存成功，请检查本机设置文件权限。"; }
        }
        Changed();
    }
    private void Changed()
    {
        foreach (var name in new[] { nameof(Editable), nameof(IsMoviePilot), nameof(IsMomoka), nameof(NeedsTarget), nameof(ProviderReady), nameof(CanScan), nameof(CanApply), nameof(CanCorrect), nameof(CanChooseCandidate), nameof(ShowCorrection), nameof(ShowEpisodeFields), nameof(TokenStatus), nameof(MethodNote), nameof(ApplyLabel), nameof(CurrentFileName) })
            OnPropertyChanged(name);
    }
    partial void OnSourceDirectoryChanged(string value) => Invalidate();
    partial void OnTargetDirectoryChanged(string value) => Invalidate();
    partial void OnMethodIndexChanged(int value) => Invalidate();
    partial void OnProviderIndexChanged(int value) => Invalidate();
    partial void OnRecursiveChanged(bool value) => Invalidate();
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
        _tmdb?.Dispose();
        foreach (var row in Rows) { row.Plan = null; row.Ready = false; row.PropertyChanged -= RowChanged; }
        Changed();
    }
}
