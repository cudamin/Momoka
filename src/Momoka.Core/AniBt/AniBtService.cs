using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;

namespace Momoka.AniBt;

public sealed record AniBtScrapeRow(string TargetPath, string SourceName, AniBtMatch Match,
    MoviePilotTransferHistory? History = null);

public sealed record AniBtScrapeResult(string TargetPath, bool Success, string Message)
{
    /// <summary>界面用简短结果；Message 保留完整逐文件回执，便于核对而不堆到弹窗里。</summary>
    public string DisplayMessage { get; init; } = "";
}

public sealed class AniBtScrapePlan
{
    private int _submitting;

    internal AniBtScrapePlan(IReadOnlyList<AniBtScrapeRow> rows, string type, MoviePilotAniBtConnection connection,
        EmbyItem item, int? season, string episode, string seriesTitle = "", string seriesMediaId = "")
    {
        Rows = Array.AsReadOnly(rows.ToArray());
        Type = type;
        Connection = connection;
        Item = item;
        Season = season;
        Episode = episode;
        SeriesTitle = seriesTitle;
        SeriesMediaId = seriesMediaId;
    }

    public IReadOnlyList<AniBtScrapeRow> Rows { get; }
    internal string Type { get; }
    internal MoviePilotAniBtConnection Connection { get; }
    internal EmbyItem Item { get; }
    internal int? Season { get; }
    internal string Episode { get; }

    /// <summary>库内这一条所属剧集的名字与 TMDB 编号；为空时只按 AniBT 的 Bangumi 身份整理。</summary>
    internal string SeriesTitle { get; }
    internal string SeriesMediaId { get; }

    internal void EnsureNotSubmitted()
    {
        if (Volatile.Read(ref _submitting) != 0)
            throw new MoviePilotOperationBlockedException("这批识别结果已经提交，请先在 MoviePilot 核对结果");
    }

    internal void Claim()
    {
        if (Interlocked.CompareExchange(ref _submitting, 1, 0) != 0)
            throw new MoviePilotOperationBlockedException("这批识别结果已经提交，请先在 MoviePilot 核对结果");
    }
}

/// <summary>只公开可核验文案，不把 MoviePilot 写入请求或可变 Emby 候选交给 UI。</summary>
public sealed record AniBtScrapeConfirmationRow(string TargetPath, string Heading, string Details)
{
    public string Title { get; init; } = "";
    public string EpisodeLabel { get; init; } = "";
    public string Method { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string DestinationPath { get; init; } = "";
    public string Warning { get; init; } = "";
}

/// <summary>
/// 一次只读准备得到的确认凭证。提交复用其内部封存预览/候选；取消、改选和消费后不可恢复。
/// 原批次 Claim 与 MoviePilot 的实际发送账本仍是防重边界，重新预览不能解除它们。
/// </summary>
public sealed class AniBtPreparedScrapePlan
{
    private int _state;
    private readonly IReadOnlyList<AniBtAnime> _choices;

    internal AniBtPreparedScrapePlan(AniBtService owner, AniBtScrapePlan plan, EmbyConnection connection,
        IReadOnlyList<AniBtAnime> choices, IReadOnlyList<AniBtPreparedTransfer> transfers,
        IReadOnlyList<int> identifyRows, AniBtPreparedIdentify? identify, IReadOnlyList<AniBtScrapeConfirmationRow> rows)
    {
        Owner = owner;
        Plan = plan;
        Connection = connection;
        _choices = Array.AsReadOnly(choices.ToArray());
        Transfers = Array.AsReadOnly(transfers.ToArray());
        IdentifyRows = Array.AsReadOnly(identifyRows.ToArray());
        Identify = identify;
        Rows = Array.AsReadOnly(rows.ToArray());
    }

    public IReadOnlyList<AniBtScrapeConfirmationRow> Rows { get; }
    public bool CanSubmit => Volatile.Read(ref _state) == 0;
    public bool MatchesChoices(IReadOnlyList<AniBtAnime> choices) => _choices.SequenceEqual(choices);
    public void Invalidate() => Interlocked.CompareExchange(ref _state, -1, 0);
    internal AniBtService Owner { get; }
    internal AniBtScrapePlan Plan { get; }
    internal EmbyConnection Connection { get; }
    internal IReadOnlyList<AniBtPreparedTransfer> Transfers { get; }
    internal IReadOnlyList<int> IdentifyRows { get; }
    internal AniBtPreparedIdentify? Identify { get; }

    internal void CheckConnection(EmbyClient emby)
    {
        if (!Connection.IsSameIdentityAs(emby.Connection))
            throw new MoviePilotOperationBlockedException("Emby 连接身份已改变，请重新识别并预览");
    }

    internal void Claim()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new MoviePilotOperationBlockedException("此识别计划已取消、失效或提交，请重新预览；已发送的文件须先核对");
        Plan.Claim();
    }
}

internal sealed record AniBtPreparedTransfer(int Index, MoviePilotTransferPreview Preview);
internal sealed record AniBtPreparedIdentify(string TargetId, string TargetType, string TargetName,
    int? TargetYear, JsonElement Candidate);

/// <summary>
/// AniBT 只负责识别。确认后用 MoviePilot 原整理记录做一次真正的重新整理（改名、移动并刮削）；没有原记录的
/// 文件改用 Emby 自己的「识别」按同一个番剧重新刮削，不伪造 MoviePilot 整理历史。
/// </summary>
public sealed class AniBtService(AniBtClient client, MoviePilotService moviePilot)
{
    public async Task<AniBtSearchPage> SearchAsync(string term, int page, CancellationToken cancellationToken)
    {
        var connection = moviePilot.CaptureAniBtConnection();
        var result = await client.SearchAsync(term, page, cancellationToken).ConfigureAwait(false);
        moviePilot.CheckAniBtConnection(connection, cancellationToken);
        return result;
    }

    public async Task<AniBtSearchPage> SearchCatalogAsync(string term, string? cursor, CancellationToken cancellationToken)
    {
        var connection = moviePilot.CaptureAniBtConnection();
        var result = await client.SearchCatalogAsync(term, cursor, cancellationToken).ConfigureAwait(false);
        moviePilot.CheckAniBtConnection(connection, cancellationToken);
        return result;
    }

    public async Task<AniBtBrowsePage> BrowseAsync(AniBtAnime anime, CancellationToken cancellationToken)
    {
        var connection = moviePilot.CaptureAniBtConnection();
        var page = await client.GroupPageAsync(anime, cancellationToken).ConfigureAwait(false);
        moviePilot.CheckAniBtConnection(connection, cancellationToken);
        var releases = page.Groups.SelectMany(group => group.Items).DistinctBy(release => release.ReleaseId).ToArray();
        var resources = await moviePilot.AniBtResourcesAsync(page.Anime, releases, connection, cancellationToken).ConfigureAwait(false);
        moviePilot.CheckAniBtConnection(connection, cancellationToken);
        return new AniBtBrowsePage(page.Anime, page.Groups,
            [.. resources.Select((resource, index) => new AniBtResource(releases[index], releases[index].HasTorrent
                ? resource : resource with { DownloadProblem = "此发布未提供种子文件" }))]);
    }

    public async Task<IReadOnlyList<AniBtResource>> ResourcesAsync(AniBtAnime anime, CancellationToken cancellationToken, int? episode = null)
    {
        var connection = moviePilot.CaptureAniBtConnection();
        var releases = await client.ReleasePageAsync(anime, cancellationToken, episode).ConfigureAwait(false);
        moviePilot.CheckAniBtConnection(connection, cancellationToken);
        var resources = await moviePilot.AniBtResourcesAsync(anime with { BgmId = releases.BgmId }, releases.Items, connection, cancellationToken).ConfigureAwait(false);
        return [.. resources.Select((resource, index) => new AniBtResource(releases.Items[index], resource))];
    }

    public async Task<AniBtScrapePlan> PrepareAsync(EmbyClient emby, EmbyItem item, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var connection = moviePilot.CaptureAniBtConnection();
        if (!AniBtEligibility.CanIdentify(item)
            && !await AniBtEligibility.ResolveAsync(emby, item, cancellationToken).ConfigureAwait(false))
            throw new MoviePilotOperationBlockedException("AniBT 识别支持剧集、季、单集和动漫电影");
        var context = await MoviePilotTransferCollect.CollectAsync(emby, item, cancellationToken, seriesFiles: true).ConfigureAwait(false);
        moviePilot.CheckAniBtConnection(connection, cancellationToken);
        if (context.Files.Count == 0) throw new MoviePilotException("此条目没有可识别的单个视频文件");
        var histories = await moviePilot.FindTransferHistoriesAsync(context.Title, cancellationToken).ConfigureAwait(false);
        moviePilot.CheckAniBtConnection(connection, cancellationToken);

        var rows = new AniBtScrapeRow[context.Files.Count];
        using var failed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            // 每个文件的匹配互不依赖；整剧最多三十个文件，串行等每一条 AniBT 回话会把识别拖得很长。
            await Parallel.ForEachAsync(Enumerable.Range(0, context.Files.Count),
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = failed.Token },
                async (index, token) =>
                {
                    var file = context.Files[index];
                    var matches = MoviePilotTransferHistoryMatch.Candidates(histories, file.Path);
                    if (matches.Count == 0)
                    {
                        var byPath = await moviePilot.FindTransferHistoriesAsync(file.Path, token).ConfigureAwait(false);
                        moviePilot.CheckAniBtConnection(connection, token);
                        matches = MoviePilotTransferHistoryMatch.Candidates(byPath, file.Path);
                    }
                    var sourceName = SourceName(file.Path, matches);
                    var match = await client.MatchAsync(sourceName, token).ConfigureAwait(false);
                    moviePilot.CheckAniBtConnection(connection, token);
                    rows[index] = new(file.Path, sourceName, match, matches.Count == 1 ? matches[0] : null);
                }).ConfigureAwait(false);
        }
        catch
        {
            failed.Cancel();
            throw;
        }
        // 电影不能沿用主剧集身份。缺少库内编号时，只从本批文件精确配对的历史主身份补号，
        // 不接受标题搜索里别的文件或 Bangumi 历史的辅助 tmdbid；详情归属在确认准备时再核验。
        var seriesMediaId = context.Type == MoviePilotTransferRequest.TypeSeries
            ? context.MediaId.Length > 0 ? context.MediaId : FindSeriesTmdbId(context, rows)
            : "";
        Log.Info("anibt", $"识别准备完成：{rows.Length} 个文件，用时 {Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} 秒");
        return new(rows, context.Type, connection, item, context.Season,
            context.Files.Count == 1 ? context.Episode : "", context.Title, seriesMediaId);
    }

    private static string FindSeriesTmdbId(MoviePilotTransferContext context, IReadOnlyList<AniBtScrapeRow> rows)
    {
        string? mediaId = null;
        foreach (var row in rows)
        {
            if (row.History is not { } history || !history.Matches(row.TargetPath)
                || history.Type != MoviePilotTransferRequest.TypeSeries || history.MediaSource != "themoviedb"
                || !IsPositiveId(history.MediaId)
                || history.TmdbId.Length > 0 && history.TmdbId != history.MediaId
                || !AniBtSeriesIdentity.SameSeries([history.Title], [context.Title]))
                return "";
            if (mediaId is not null && mediaId != history.MediaId) return "";
            mediaId = history.MediaId;
        }
        return mediaId ?? "";
    }

    private static bool IsPositiveId(string value) => long.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out var id) && id > 0;

    internal static string SourceName(string targetPath, IReadOnlyList<MoviePilotTransferHistory> histories)
    {
        if (histories.Count > 1)
            throw new MoviePilotOperationBlockedException($"此文件有多条原整理记录，请先在 MoviePilot 核对：{targetPath}");
        // 成功 move 的 TransferPath 是改名后的目标；识别仍应使用历史里的原 source 名字。
        var sourcePath = histories.Count == 1 ? histories[0].SourcePath : "";
        return MoviePilotTransfer.PathName(string.IsNullOrWhiteSpace(sourcePath) ? targetPath : sourcePath);
    }

    /// <summary>整批只读准备，自动显示实际目标；用户点击“重新整理”后才消费凭证，这里不 Claim 或写入。</summary>
    public async Task<AniBtPreparedScrapePlan> PrepareForConfirmationAsync(EmbyClient emby, AniBtScrapePlan plan,
        IReadOnlyList<AniBtAnime> choices, CancellationToken cancellationToken, Func<bool>? canContinue = null)
    {
        var started = Stopwatch.GetTimestamp();
        moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
        plan.EnsureNotSubmitted();
        if (choices.Count != plan.Rows.Count || choices.Count == 0)
            throw new MoviePilotOperationBlockedException("请为每个文件选择识别结果");
        var selected = choices.ToArray();
        var connection = emby.Connection;
        var transfers = new List<AniBtPreparedTransfer>();
        var identify = new List<(int Index, AniBtAnime Choice)>();
        var rows = new AniBtScrapeConfirmationRow[plan.Rows.Count];
        MoviePilotAniBtMediaInfo? seriesMedia = null;
        var seriesMediaRead = false;
        var candidates = new Dictionary<long, MoviePilotAniBtMediaInfo>();

        // 同名也必须核验编号对应的作品；详情失败可回退 Bangumi，但明确身份/类型冲突不能吞掉。
        // 主剧集详情与同一 Bangumi 候选在本批次只读取一次。
        async Task<MoviePilotAniBtMediaInfo?> SeriesMediaAsync()
        {
            if (seriesMediaRead) return seriesMedia;
            seriesMediaRead = true;
            try
            {
                seriesMedia = await moviePilot.ReadAniBtMediaAsync("themoviedb", plan.SeriesMediaId, plan.Type,
                    plan.Connection, cancellationToken).ConfigureAwait(false);
                moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (MoviePilotOperationBlockedException) { throw; }
            catch (MoviePilotException) { seriesMedia = null; }
            return seriesMedia;
        }

        // 所有 MoviePilot 预览和最终 Emby 候选全部准备好，才把同一份凭证交回用户。
        for (var index = 0; index < plan.Rows.Count; index++)
        {
            CheckContinuation(canContinue, cancellationToken);
            var row = plan.Rows[index];
            var choice = selected[index];
            if (choice.BgmId <= 0 || !row.Match.Candidates.Contains(choice))
                throw new MoviePilotOperationBlockedException("所选媒体不属于此文件的 AniBT 候选结果，请重新识别");
            if (row.History is not { } history)
            {
                identify.Add((index, choice));
                continue;
            }
            var numbering = plan.Type == MoviePilotTransferRequest.TypeSeries
                ? AniBtScrapeNumbering.Resolve(row, plan.Season, plan.Episode) : new(null, "");
            if (!candidates.TryGetValue(choice.BgmId, out var candidate))
            {
                candidate = await moviePilot.VerifyAniBtCandidateAsync(choice, plan.Type, plan.Connection, cancellationToken)
                    .ConfigureAwait(false);
                candidates.Add(choice.BgmId, candidate);
            }
            var target = await ResolveTargetAsync(choice, candidate, plan, SeriesMediaAsync).ConfigureAwait(false);
            var transfer = await moviePilot.PrepareAniBtTransferAsync(row.TargetPath, choice, candidate, target, plan.Type, history,
                numbering.Season, numbering.Episode, plan.Connection, cancellationToken).ConfigureAwait(false);
            moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
            CheckContinuation(canContinue, cancellationToken);
            var preview = await moviePilot.TransferPreviewAsync(transfer.Request, [transfer.File], cancellationToken)
                .ConfigureAwait(false);
            moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
            CheckContinuation(canContinue, cancellationToken);
            if (!preview.CanSubmit)
                throw new MoviePilotOperationBlockedException(preview.Result.Message.Length > 0
                    ? preview.Result.Message
                    : "MoviePilot 没有返回可用的整理目标，本次没有重新整理任何文件");
            transfers.Add(new(index, preview));
            var method = preview.Request.TransferType switch
            {
                "softlink" => "软链接",
                "link" => "硬链接",
                "copy" => "复制",
                "move" => "移动",
                _ => "按目录设置"
            };
            method += target.MediaSource == "themoviedb" ? " · 沿用主剧集" : " · Bangumi " + target.MediaId;
            method += " · 自动刮削";
            var source = MoviePilotTransfer.Text(transfer.File, "path");
            var destination = preview.Result.Items.Single().Target;
            var warning = preview.Request.TransferType switch
            {
                "move" => "源文件将移走，并替换旧目标；同名文件可能覆盖。",
                "softlink" or "link" or "copy" => "保留源文件，替换旧目标；同名文件可能覆盖。",
                _ => "源文件和旧目标按目录设置处理，同名文件可能覆盖。"
            };
            var heading = $"{target.Title}{(numbering.Label.Length > 0 ? " " + numbering.Label : "")}";
            rows[index] = new(row.TargetPath, heading, $"{method}\n源：{source}\n目标：{destination}\n{warning}")
            {
                Title = target.Title,
                EpisodeLabel = numbering.Label,
                Method = method,
                SourcePath = source,
                DestinationPath = destination,
                Warning = warning
            };
        }
        if (transfers.Select(item => item.Preview.Request.Histories[0].Id).Distinct().Count() != transfers.Count)
            throw new MoviePilotOperationBlockedException("多个文件对应同一条原整理记录，已停止，避免重复提交");
        if (identify.Select(item => (item.Choice.AnimeId, item.Choice.BgmId)).Distinct().Count() > 1)
            throw new MoviePilotOperationBlockedException("同一 Emby 条目的多个文件识别成了不同番剧，Emby 识别只能落一部；请分开操作");
        var identifyPlan = identify.Count > 0
            ? await PrepareEmbyIdentifyAsync(emby, plan.Item, identify[0].Choice, cancellationToken).ConfigureAwait(false)
            : null;
        moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
        CheckContinuation(canContinue, cancellationToken);
        if (identifyPlan is not null)
        {
            var candidate = identifyPlan.Candidate.Deserialize<RemoteSearchResult>()!;
            var type = identifyPlan.TargetType == EmbyItemType.Movie ? "电影" : "整部剧";
            var destination = identifyPlan.TargetName + YearText(identifyPlan.TargetYear);
            var warning = "仅更新" + type + "的元数据与图片，不移动文件。";
            var details = $"实际 Emby 目标：{destination}\n目标 ID：{identifyPlan.TargetId}\n影响范围：{type}\n" +
                $"最终识别候选：{candidate.Name}{YearText(candidate.ProductionYear)}\n{warning}";
            foreach (var (index, _) in identify)
                rows[index] = new(plan.Rows[index].TargetPath, "Emby 识别 · " + ChoiceName(identify[0].Choice), details)
                {
                    Title = (candidate.Name ?? identify[0].Choice.Title) + YearText(candidate.ProductionYear),
                    Method = "Emby 识别 · " + type,
                    DestinationPath = destination,
                    Warning = warning
                };
        }
        Log.Info("anibt", $"整理目标读取完成：{plan.Rows.Count} 个文件，用时 {Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} 秒");
        return new(this, plan, connection, selected, transfers, identify.Select(item => item.Index).ToArray(), identifyPlan, rows);
    }

    /// <summary>
    /// 候选是同一部作品的新一季时，整理身份改用库内主剧集已有的 TMDB 编号：MoviePilot 会把它写进主剧集目录，
    /// 而不是按 Bangumi 的季名新建一部剧。候选与库内剧集不是同一部时仍用 Bangumi，保留修正误识别的能力。
    /// </summary>
    private static async Task<MoviePilotAniBtTarget> ResolveTargetAsync(AniBtAnime choice,
        MoviePilotAniBtMediaInfo candidate, AniBtScrapePlan plan, Func<Task<MoviePilotAniBtMediaInfo?>> seriesMedia)
    {
        if (plan.Type == MoviePilotTransferRequest.TypeSeries && IsPositiveId(plan.SeriesMediaId))
        {
            var media = await seriesMedia().ConfigureAwait(false);
            if (media is not null)
            {
                string[] seriesTitles = [media.Title, media.OriginalTitle, .. media.Names];
                // 库内标题和候选标题各自都必须对应经过身份/类型核验的主剧集；不能让两份错误标题互相作证。
                // AniBT 来源标题仅用于显示，不覆盖 Bangumi 详情的归属，尤其不能把同名电影当成新一季。
                if (AniBtSeriesIdentity.SameSeries([plan.SeriesTitle], seriesTitles)
                    && AniBtSeriesIdentity.SameSeries([candidate.Title, candidate.OriginalTitle, .. candidate.Names], seriesTitles))
                    return new("themoviedb", plan.SeriesMediaId, media.Title);
            }
        }
        return new("bangumi", choice.BgmId.ToString(CultureInfo.InvariantCulture), candidate.Title);
    }

    /// <summary>只消费已显示并由“重新整理”确认的凭证，不重新搜索/预览，不接受任意新请求或新目标。</summary>
    public async Task<IReadOnlyList<AniBtScrapeResult>> SubmitAsync(EmbyClient emby, AniBtPreparedScrapePlan prepared,
        CancellationToken cancellationToken, Func<bool>? canContinue = null)
    {
        var started = Stopwatch.GetTimestamp();
        var plan = prepared.Plan;
        try
        {
            if (!ReferenceEquals(prepared.Owner, this))
                throw new MoviePilotOperationBlockedException("此识别计划不属于当前服务，请重新预览");
            moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
            CheckContinuation(canContinue, cancellationToken);
            prepared.CheckConnection(emby);
            if (!prepared.CanSubmit)
                throw new MoviePilotOperationBlockedException("此识别计划已取消、失效或提交，请先核对结果");
            // 原服务仍在每个实际发送前重核历史/依赖；这里先核完整批次，避免后项已知读失败时先写前项。
            await RecheckPreparedHistoriesAsync(prepared, canContinue, cancellationToken).ConfigureAwait(false);
            moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
            CheckContinuation(canContinue, cancellationToken);
            prepared.CheckConnection(emby);
            prepared.Claim();
        }
        catch
        {
            prepared.Invalidate();
            throw;
        }
        var transfers = prepared.Transfers;
        var identifyPlan = prepared.Identify;
        var outcomes = new AniBtScrapeResult[plan.Rows.Count];
        var scanAffected = new List<int>();
        var stopped = false;
        foreach (var (index, preview) in transfers)
        {
            var row = plan.Rows[index];
            if (stopped)
            {
                outcomes[index] = new(row.TargetPath, false, "前一文件未完成；此文件没有提交");
                continue;
            }
            try
            {
                CheckContinuation(canContinue, cancellationToken);
                moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
                prepared.CheckConnection(emby);
                var result = await moviePilot.TransferSubmitAsync(preview, background: false, cancellationToken)
                    .ConfigureAwait(false);
                // Success 保留「请求已处理」的原契约；accepted/skipped 不等于文件完成，也不能因此停发后续项。
                var message = TransferMessage(result);
                var scanReason = TransferScanReason(result, preview);
                if (scanReason.Length > 0)
                {
                    message += "；" + scanReason;
                    scanAffected.Add(index);
                }
                outcomes[index] = new(row.TargetPath, result.Success, message)
                {
                    DisplayMessage = TransferDisplayMessage(result)
                };
            }
            catch (Exception error) when (error is MoviePilotException or OperationCanceledException)
            {
                outcomes[index] = new(row.TargetPath, false, error is OperationCanceledException
                    ? "操作已取消或连接改变，请在 MoviePilot 核对已提交文件" : error.Message);
                stopped = true;
            }
        }
        if (identifyPlan is not null)
        {
            var success = false;
            var message = "前一文件未完成；此文件没有交给 Emby 识别";
            if (!stopped)
            {
                try
                {
                    moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
                    prepared.CheckConnection(emby);
                    CheckContinuation(canContinue, cancellationToken);
                    message = await ApplyEmbyIdentifyAsync(emby, identifyPlan, cancellationToken, canContinue).ConfigureAwait(false);
                    success = true;
                }
                catch (OperationCanceledException) { message = "操作已取消，Emby 识别结果未确认"; }
                catch (Exception error) { message = error.Message; }
            }
            foreach (var index in prepared.IdentifyRows)
                outcomes[index] = new(plan.Rows[index].TargetPath, success, message)
                {
                    DisplayMessage = success ? "识别完成，已刷新元数据" : message
                };
        }
        if (scanAffected.Count > 0)
        {
            var scan = await ScanAfterTransferAsync(emby, canContinue, cancellationToken).ConfigureAwait(false);
            foreach (var index in scanAffected)
                outcomes[index] = outcomes[index] with
                {
                    Message = outcomes[index].Message + scan,
                    DisplayMessage = scan.Contains("已请求 Emby", StringComparison.Ordinal)
                        ? "整理完成 · 已请求媒体库重扫"
                        : "整理完成 · 媒体库重扫未完成，请手动扫描"
                };
        }
        Log.Info("anibt", $"重新整理提交完成：{outcomes.Length} 个文件，用时 {Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} 秒");
        return [.. outcomes];
    }

    private async Task RecheckPreparedHistoriesAsync(AniBtPreparedScrapePlan prepared, Func<bool>? canContinue,
        CancellationToken cancellationToken)
    {
        var histories = prepared.Transfers.SelectMany(item => item.Preview.Request.Histories).ToArray();
        foreach (var group in histories.GroupBy(history => history.Title))
        {
            CheckContinuation(canContinue, cancellationToken);
            var current = await moviePilot.FindTransferHistoriesAsync(group.Key, cancellationToken).ConfigureAwait(false);
            moviePilot.CheckAniBtConnection(prepared.Plan.Connection, cancellationToken);
            foreach (var history in group)
            {
                var fresh = current.FirstOrDefault(item => item.Id == history.Id);
                if (fresh is null || history.Snapshot.ValueKind != JsonValueKind.Object ||
                    !JsonElement.DeepEquals(fresh.Snapshot, history.Snapshot))
                    throw new MoviePilotTransferBlockedException("原整理记录已经改变，请重新查找记录和预览；本批次没有提交");
                MoviePilotTransfer.RequireFile(fresh.TransferFile, history.TransferPath);
            }
        }
        foreach (var path in histories.Select(history => history.TargetPath).Where(path => path.Length > 0).Distinct(StringComparer.Ordinal))
        {
            CheckContinuation(canContinue, cancellationToken);
            var current = await moviePilot.FindTransferHistoriesAsync(path, cancellationToken).ConfigureAwait(false);
            moviePilot.CheckAniBtConnection(prepared.Plan.Connection, cancellationToken);
            if (MoviePilotTransferHistoryMatch.Dependents(histories, current).Count > 0)
                throw new MoviePilotTransferBlockedException("旧目标还被软链接记录引用，请先在 MoviePilot 处理；本批次没有提交");
        }
    }

    private static void CheckContinuation(Func<bool>? canContinue, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (canContinue?.Invoke() == false) throw new OperationCanceledException("任务所属的媒体库身份或选择已改变");
    }

    private static string ChoiceName(AniBtAnime choice) => choice.Title + YearText(choice.Year);
    private static string YearText(int? year) => year is { } value ? $"（{value}）" : "（年份未返回）";

    /// <summary>
    /// 一批中有唯一明确 completed 文件就请求一次重扫，不再限定移动或旧目标变动；不用 Success 猜完成。
    /// 扫描只是异步请求；失败不撤销已完成整理，也不解除 MoviePilot 的实际发送防重账本。
    /// </summary>
    private static async Task<string> ScanAfterTransferAsync(EmbyClient emby, Func<bool>? canContinue,
        CancellationToken cancellationToken)
    {
        if (canContinue?.Invoke() == false)
            return "；媒体库身份已改变，没有请求 Emby 扫描；请在正确媒体库手动扫描，勿重复整理";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await emby.ScanLibraryAsync(cancellationToken).ConfigureAwait(false);
            return "；已请求 Emby 扫描媒体库，收录新路径和移除旧条目由服务器决定";
        }
        catch (OperationCanceledException) { return "；Emby 扫描请求已取消，请手动扫描媒体库，勿重复整理"; }
        catch (Exception error) { return $"；Emby 扫描请求失败（{error.Message}），请手动扫描媒体库，勿重复整理"; }
    }

    private static string TransferMessage(MoviePilotTransferResult result)
    {
        if (result.Items.Count == 0)
            return result.Success
                ? "MoviePilot 已接收重新整理请求，完成状态待确认；请先核对整理历史，勿重复提交"
                : "MoviePilot 没有返回逐文件回执，状态未确认；请先核对整理历史，勿重复提交";
        var messages = result.Items.Select(line =>
        {
            var status = line.State switch
            {
                "completed" when line.Success => line.Target.Length > 0
                    ? $"已完成整理，已重新整理到 {line.Target}"
                    : "已完成整理，目标路径未返回，请核对",
                "completed" => "completed 与成功标志不一致，状态待核对",
                "accepted" => "已接收，等待整理完成（完成状态待确认）",
                "skipped" => "已跳过整理",
                "failed" => "整理失败",
                "retry_wait" => "等待重试",
                "manual_review" => "需要人工复核",
                _ => "状态未确认，待核对"
            };
            // 多行或部分回执保留每个文件的状态；顶层成功信封不能覆盖 line.State。
            var file = line.Source.Length > 0 ? line.Source : line.Target;
            var message = $"MoviePilot 回执{(file.Length > 0 ? $"（{file}）" : "")}：{status}";
            if (line.State != "completed" && line.Target.Length > 0) message += $"，回执目标：{line.Target}";
            if (line.Message.Length > 0) message += " — " + line.Message;
            return message;
        });
        var detail = result.Message.Length > 0 ? "；MoviePilot 说明：" + result.Message : "";
        return string.Join("；", messages) + detail + "；请先在 MoviePilot 核对整理历史，勿重复提交";
    }

    private static string TransferDisplayMessage(MoviePilotTransferResult result)
    {
        if (result.Items.Count != 1)
            return "结果待核对，请在 MoviePilot 查看，勿重复提交";
        var line = result.Items[0];
        return line.State switch
        {
            "completed" when line.Success => "整理完成",
            "accepted" => "已接收，等待 MoviePilot 完成整理",
            "skipped" => "已跳过整理",
            "retry_wait" => "等待 MoviePilot 重试",
            "failed" => line.Message.Length > 0 ? "整理失败：" + line.Message : "整理失败，请在 MoviePilot 查看",
            _ => "结果待核对，请在 MoviePilot 查看，勿重复提交"
        };
    }

    private static string TransferScanReason(MoviePilotTransferResult result, MoviePilotTransferPreview preview)
    {
        var request = preview.Request;
        if (result.IsPreview) return "";
        var targetIsLocal = request.TargetStorage is "" or "local";
        foreach (var expected in preview.Result.Items)
        {
            var matches = result.Items.Where(line => MoviePilotTransfer.SamePath(line.Source, expected.Source) ||
                line.Source.Length == 0 && line.Target.Length > 0 && MoviePilotTransfer.SamePath(line.Target, expected.Target)).ToList();
            // 缺行、重复或矛盾回执不能授权扫描；即便整体部分失败，唯一明确完成的文件仍可核对路径影响。
            if (matches.Count != 1 || matches[0] is not { State: "completed", Success: true } completed ||
                string.IsNullOrWhiteSpace(completed.Target) ||
                targetIsLocal && !MoviePilotTransfer.IsAbsolutePath(completed.Target)) continue;
            if (request.TransferType == "move" && (!targetIsLocal || !MoviePilotTransfer.SamePath(expected.Source, completed.Target)) &&
                request.Files.Any(file => MoviePilotTransfer.SamePath(file.Path, expected.Source)))
                return $"原媒体路径 {expected.Source} 已换到 {completed.Target}，需要服务器核对旧条目";
            // 按 logid 重整成功历史会处理旧目标，不只受 Reorganize 或历史 mode 控制。
            // copy/link/softlink 虽保留原下载源，旧入库目标被清理并换路径/存储时仍要请求服务器核对。
            var cleaned = request.Histories.FirstOrDefault(history => history.Success &&
                MoviePilotTransfer.SamePath(history.TransferPath, expected.Source) &&
                MoviePilotTransfer.IsAbsolutePath(history.TargetPath) &&
                MoviePilotTransfer.Text(history.TargetFile, "storage") is "" or "local" &&
                (!targetIsLocal || !MoviePilotTransfer.SamePath(history.TargetPath, completed.Target)) &&
                request.Files.Any(file => MoviePilotTransfer.SamePath(file.Path, history.TargetPath)));
            if (cleaned is not null)
                return $"原记录重整涉及清理旧目标 {cleaned.TargetPath}，需要服务器核对旧条目";
            // 复制/链接、原路径重整也可能更新了元数据；明确完成后统一重扫一次。
            return "整理已完成，需要服务器重新扫描媒体库";
        }
        return "";
    }

    private static async Task<AniBtPreparedIdentify> PrepareEmbyIdentifyAsync(EmbyClient emby, EmbyItem item,
        AniBtAnime choice, CancellationToken cancellationToken)
    {
        var target = await IdentifyTargetAsync(emby, item, cancellationToken).ConfigureAwait(false);
        var actual = await emby.GetItemAsync(target.Id, cancellationToken, "Type,ProductionYear").ConfigureAwait(false);
        if (actual.Id != target.Id || actual.Type != target.Type || string.IsNullOrWhiteSpace(actual.Name))
            throw new MoviePilotOperationBlockedException("无法核验实际 Emby 识别目标，请关闭后使用普通识别");
        var titles = new[] { choice.Title, choice.OriginalTitle }
            .Where(title => !string.IsNullOrWhiteSpace(title)).Distinct(StringComparer.Ordinal).ToArray();
        var found = new List<RemoteSearchResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var title in titles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = await emby.SearchRemoteAsync(target.Type, ItemIdentify.SearchQuery(
                new EmbyItem { Id = target.Id, Type = target.Type }, title, choice.Year?.ToString(CultureInfo.InvariantCulture), []), cancellationToken)
                .ConfigureAwait(false);
            foreach (var result in results)
                if (seen.Add(IdentifyKey(result))) found.Add(result);
        }
        var candidate = PickIdentifyResult(found, titles, choice.Year) ?? throw new MoviePilotOperationBlockedException(
            $"MoviePilot 没有这个文件的原整理记录，Emby 没有可靠匹配「{choice.Title}」的候选；本次没有写入，请关闭后使用普通识别手动核对");
        // RemoteSearch DTO 可变，封存全字段（含扩展字段）；UI 只取得上面的字符串投影。
        return new(actual.Id, actual.Type, actual.Name, actual.ProductionYear, JsonSerializer.SerializeToElement(candidate));
    }

    private static async Task<(string Id, string Type)> IdentifyTargetAsync(EmbyClient emby, EmbyItem item,
        CancellationToken cancellationToken)
    {
        if (item.Type is EmbyItemType.Movie or EmbyItemType.Series)
            return (item.Id, item.Type);
        if (item.Type is EmbyItemType.Episode or EmbyItemType.Season && !string.IsNullOrWhiteSpace(item.SeriesId))
            return (item.SeriesId, EmbyItemType.Series);
        var detail = await emby.GetItemAsync(item.Id, cancellationToken, "Type,SeriesId,ParentId").ConfigureAwait(false);
        var type = detail.Type.Length > 0 ? detail.Type : item.Type;
        if (type is EmbyItemType.Movie or EmbyItemType.Series) return (detail.Id, type);
        var seriesId = detail.SeriesId;
        if (string.IsNullOrWhiteSpace(seriesId)) seriesId = item.SeriesId;
        // 单集的 ParentId 是季，不能拿它去识别；只有季的父级才是剧。
        if (type == EmbyItemType.Season && string.IsNullOrWhiteSpace(seriesId)) seriesId = detail.ParentId ?? item.ParentId;
        if (string.IsNullOrWhiteSpace(seriesId))
            throw new MoviePilotOperationBlockedException("无法确定此条目所属的剧集，不能使用 Emby 识别");
        return (seriesId, EmbyItemType.Series);
    }

    private static async Task<string> ApplyEmbyIdentifyAsync(EmbyClient emby, AniBtPreparedIdentify plan,
        CancellationToken cancellationToken, Func<bool>? canContinue)
    {
        var candidate = plan.Candidate.Deserialize<RemoteSearchResult>()!;
        CheckContinuation(canContinue, cancellationToken);
        await emby.ApplyRemoteSearchResultAsync(plan.TargetId, candidate, replaceAllImages: true, cancellationToken)
            .ConfigureAwait(false);
        CheckContinuation(canContinue, cancellationToken);
        await emby.RefreshItemAsync(plan.TargetId, "all", replaceImages: true, replaceThumbnails: false,
            cancellationToken).ConfigureAwait(false);
        var name = candidate.Name ?? "";
        var year = candidate.ProductionYear is { } value ? $"（{value}）" : "";
        return $"MoviePilot 没有原整理记录，已用 Emby 识别为「{name}」{year}并刷新元数据";
    }

    private static string IdentifyKey(RemoteSearchResult result) => result.SearchProviderName + "|" +
        string.Join(",", result.ProviderIds.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key.ToLowerInvariant() + "=" + pair.Value)) + "|" + result.Name + "|" + result.ProductionYear;

    internal static RemoteSearchResult? PickIdentifyResult(IReadOnlyList<RemoteSearchResult> results,
        IEnumerable<string> titles, int? year)
    {
        if (results.Count == 0) return null;
        var wanted = titles.Select(NormalizeTitle).Where(title => title.Length > 0).ToHashSet(StringComparer.Ordinal);
        var exact = results.Where(result => wanted.Contains(NormalizeTitle(result.Name ?? ""))).ToList();
        // 包含关系可能是续作、同名前缀或完全不同作品；只允许规范化后的精确中文/原文标题。
        return ByYear(exact, year);
    }

    private static RemoteSearchResult? ByYear(IReadOnlyList<RemoteSearchResult> results, int? year)
    {
        if (year is not { } value) return results.FirstOrDefault();
        // 延续原来的 ±1 年容差（首播/当地发行年份差异）和精确标题但未知年份的候选；先选同年。
        return results.FirstOrDefault(result => result.ProductionYear == value)
            ?? results.FirstOrDefault(result => result.ProductionYear is { } found && Math.Abs((long)found - value) <= 1)
            ?? results.FirstOrDefault(result => result.ProductionYear is null);
    }

    internal static string NormalizeTitle(string value) => string.Concat(value
        .Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant));

}
