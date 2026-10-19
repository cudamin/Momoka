using System.Text;
using Momoka.Emby;
using Momoka.MoviePilot;

namespace Momoka.AniBt;

public sealed record AniBtScrapeRow(string TargetPath, string SourceName, AniBtMatch Match,
    MoviePilotTransferHistory? History = null);

public sealed record AniBtScrapeResult(string TargetPath, bool Success, string Message);

public sealed class AniBtScrapePlan
{
    private int _submitting;

    internal AniBtScrapePlan(IReadOnlyList<AniBtScrapeRow> rows, string type, MoviePilotAniBtConnection connection,
        EmbyItem item, int? season, string episode)
    {
        Rows = Array.AsReadOnly(rows.ToArray());
        Type = type;
        Connection = connection;
        Item = item;
        Season = season;
        Episode = episode;
    }

    public IReadOnlyList<AniBtScrapeRow> Rows { get; }
    internal string Type { get; }
    internal MoviePilotAniBtConnection Connection { get; }
    internal EmbyItem Item { get; }
    internal int? Season { get; }
    internal string Episode { get; }

    internal void Claim()
    {
        if (Interlocked.CompareExchange(ref _submitting, 1, 0) != 0)
            throw new MoviePilotOperationBlockedException("这批识别结果已经提交，请先在 MoviePilot 核对结果");
    }
}

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
        var connection = moviePilot.CaptureAniBtConnection();
        if (!await AniBtEligibility.ResolveAsync(emby, item, cancellationToken).ConfigureAwait(false))
            throw new MoviePilotOperationBlockedException("只有类型包含动画或动漫的媒体支持 AniBT 识别");
        var context = await MoviePilotTransferCollect.CollectAsync(emby, item, cancellationToken).ConfigureAwait(false);
        moviePilot.CheckAniBtConnection(connection, cancellationToken);
        if (context.Files.Count == 0) throw new MoviePilotException("此条目没有可识别的单个视频文件");
        var rows = new List<AniBtScrapeRow>(context.Files.Count);
        var histories = await moviePilot.FindTransferHistoriesAsync(context.Title, cancellationToken).ConfigureAwait(false);
        moviePilot.CheckAniBtConnection(connection, cancellationToken);
        foreach (var file in context.Files)
        {
            var matches = MoviePilotTransferHistoryMatch.Candidates(histories, file.Path);
            if (matches.Count == 0)
            {
                var byPath = await moviePilot.FindTransferHistoriesAsync(file.Path, cancellationToken).ConfigureAwait(false);
                moviePilot.CheckAniBtConnection(connection, cancellationToken);
                matches = MoviePilotTransferHistoryMatch.Candidates(byPath, file.Path);
            }
            var sourceName = SourceName(file.Path, matches);
            var match = await client.MatchAsync(sourceName, cancellationToken).ConfigureAwait(false);
            moviePilot.CheckAniBtConnection(connection, cancellationToken);
            rows.Add(new(file.Path, sourceName, match, matches.Count == 1 ? matches[0] : null));
        }
        return new(rows, context.Type, connection, item, context.Season,
            context.Files.Count == 1 ? context.Episode : "");
    }

    internal static string SourceName(string targetPath, IReadOnlyList<MoviePilotTransferHistory> histories)
    {
        if (histories.Count > 1)
            throw new MoviePilotOperationBlockedException($"此文件有多条原整理记录，请先在 MoviePilot 核对：{targetPath}");
        // 成功 move 的 TransferPath 是改名后的目标；识别仍应使用历史里的原 source 名字。
        var sourcePath = histories.Count == 1 ? histories[0].SourcePath : "";
        return MoviePilotTransfer.PathName(string.IsNullOrWhiteSpace(sourcePath) ? targetPath : sourcePath);
    }

    public async Task<IReadOnlyList<AniBtScrapeResult>> SubmitAsync(EmbyClient emby, AniBtScrapePlan plan,
        IReadOnlyList<AniBtAnime> choices, CancellationToken cancellationToken, Func<bool>? canContinue = null)
    {
        moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
        if (choices.Count != plan.Rows.Count || choices.Count == 0)
            throw new MoviePilotOperationBlockedException("请为每个文件选择识别结果后再重新整理");
        var selected = choices.ToArray();
        var transfers = new List<(int Index, MoviePilotAniBtTransfer Transfer, MoviePilotTransferPreview Preview)>();
        var identify = new List<(int Index, AniBtAnime Choice)>();
        // 整批只读检查（含 MoviePilot 预览和 Emby 识别候选）全部完成后才有任何写入。
        for (var index = 0; index < plan.Rows.Count; index++)
        {
            if (canContinue?.Invoke() == false) throw new OperationCanceledException("任务所属的媒体库身份已改变");
            var row = plan.Rows[index];
            var choice = selected[index];
            if (choice.BgmId <= 0 || !row.Match.Candidates.Any(candidate =>
                    candidate.AnimeId == choice.AnimeId && candidate.BgmId == choice.BgmId))
                throw new MoviePilotOperationBlockedException("所选媒体不属于此文件的 AniBT 候选结果，请重新识别");
            if (row.History is not { } history)
            {
                identify.Add((index, choice));
                continue;
            }
            var transfer = await moviePilot.PrepareAniBtTransferAsync(row.TargetPath, choice, plan.Type, history,
                plan.Season, plan.Episode, plan.Connection, cancellationToken).ConfigureAwait(false);
            moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
            var preview = await moviePilot.TransferPreviewAsync(transfer.Request, [transfer.File], cancellationToken)
                .ConfigureAwait(false);
            moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
            if (!preview.CanSubmit)
                throw new MoviePilotOperationBlockedException(preview.Result.Message.Length > 0
                    ? preview.Result.Message
                    : "MoviePilot 预览没有通过，本次没有重新整理任何文件");
            transfers.Add((index, transfer, preview));
        }
        if (transfers.Select(item => item.Transfer.Request.Histories[0].Id).Distinct().Count() != transfers.Count)
            throw new MoviePilotOperationBlockedException("多个文件对应同一条原整理记录，已停止，避免重复提交");
        if (identify.Select(item => (item.Choice.AnimeId, item.Choice.BgmId)).Distinct().Count() > 1)
            throw new MoviePilotOperationBlockedException("同一 Emby 条目的多个文件识别成了不同番剧，Emby 识别只能落一部；请分开操作");
        var identifyPlan = identify.Count > 0
            ? await PrepareEmbyIdentifyAsync(emby, plan.Item, identify[0].Choice, cancellationToken).ConfigureAwait(false)
            : null;
        moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
        plan.Claim();
        var outcomes = new AniBtScrapeResult[plan.Rows.Count];
        var moved = new List<int>();
        var stopped = false;
        foreach (var (index, transfer, preview) in transfers)
        {
            var row = plan.Rows[index];
            if (stopped)
            {
                outcomes[index] = new(row.TargetPath, false, "前一文件未完成；此文件没有提交");
                continue;
            }
            try
            {
                if (canContinue?.Invoke() == false) throw new OperationCanceledException("任务所属的媒体库身份已改变");
                var result = await moviePilot.TransferSubmitAsync(preview, background: false, cancellationToken)
                    .ConfigureAwait(false);
                outcomes[index] = new(row.TargetPath, result.Success, TransferMessage(result));
                if (result.Success) moved.Add(index);
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
                    message = await ApplyEmbyIdentifyAsync(emby, identifyPlan, cancellationToken).ConfigureAwait(false);
                    success = true;
                }
                catch (OperationCanceledException) { message = "操作已取消，Emby 识别结果未确认"; }
                catch (Exception error) { message = error.Message; }
            }
            foreach (var (index, _) in identify)
                outcomes[index] = new(plan.Rows[index].TargetPath, success, message);
        }
        if (moved.Count > 0)
        {
            var scan = await ScanAfterTransferAsync(emby, canContinue, cancellationToken).ConfigureAwait(false);
            foreach (var index in moved)
                outcomes[index] = outcomes[index] with { Message = outcomes[index].Message + scan };
        }
        return [.. outcomes];
    }

    /// <summary>
    /// 整理成功后文件已经换了路径；不通知媒体服务器，旧条目会一直留在库里，点开还会报文件缺失。
    /// 用 Emby 自带的整库扫描让它按磁盘现状移除旧条目并收录新路径；扫描是异步的，结果只代表请求已发出。
    /// </summary>
    private static async Task<string> ScanAfterTransferAsync(EmbyClient emby, Func<bool>? canContinue,
        CancellationToken cancellationToken)
    {
        if (canContinue?.Invoke() == false) return "；媒体库身份已改变，没有请求 Emby 扫描";
        try
        {
            await emby.ScanLibraryAsync(cancellationToken).ConfigureAwait(false);
            return "；已请求 Emby 扫描媒体库，旧条目会在扫描完成后移除";
        }
        catch (OperationCanceledException) { return "；Emby 扫描请求已取消，请手动扫描媒体库"; }
        catch (Exception error) { return $"；Emby 扫描请求失败（{error.Message}），请手动扫描媒体库"; }
    }

    private static string TransferMessage(MoviePilotTransferResult result)
    {
        var target = result.Items.FirstOrDefault(line => line.Target.Length > 0)?.Target;
        if (!result.Success)
            return result.Message.Length > 0 ? result.Message : result.Summary;
        return target is { Length: > 0 }
            ? $"已重新整理到 {target}；完成状态与整理记录请在 MoviePilot 核对"
            : "MoviePilot 已接收重新整理请求，请到整理历史核对";
    }

    private static async Task<AniBtIdentifyPlan> PrepareEmbyIdentifyAsync(EmbyClient emby, EmbyItem item,
        AniBtAnime choice, CancellationToken cancellationToken)
    {
        var target = await IdentifyTargetAsync(emby, item, cancellationToken).ConfigureAwait(false);
        var titles = new[] { choice.Title, choice.OriginalTitle }
            .Where(title => !string.IsNullOrWhiteSpace(title)).Distinct(StringComparer.Ordinal).ToArray();
        var found = new List<RemoteSearchResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var title in titles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = await emby.SearchRemoteAsync(target.Type, ItemIdentify.SearchQuery(
                new EmbyItem { Id = target.Id, Type = target.Type }, title, null, []), cancellationToken)
                .ConfigureAwait(false);
            foreach (var result in results)
                if (seen.Add(IdentifyKey(result))) found.Add(result);
        }
        var candidate = PickIdentifyResult(found, titles, choice.Year) ?? throw new MoviePilotOperationBlockedException(
            $"MoviePilot 没有这个文件的原整理记录，Emby 也没有找到与「{choice.Title}」匹配的条目");
        return new(target.Id, candidate);
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

    private static async Task<string> ApplyEmbyIdentifyAsync(EmbyClient emby, AniBtIdentifyPlan plan,
        CancellationToken cancellationToken)
    {
        await emby.ApplyRemoteSearchResultAsync(plan.TargetId, plan.Candidate, replaceAllImages: true, cancellationToken)
            .ConfigureAwait(false);
        await emby.RefreshItemAsync(plan.TargetId, "all", replaceImages: true, replaceThumbnails: false,
            cancellationToken).ConfigureAwait(false);
        var name = plan.Candidate.Name ?? "";
        var year = plan.Candidate.ProductionYear is { } value ? $"（{value}）" : "";
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
        if (exact.Count > 0) return ByYear(exact, year);
        var partial = results.Where(result =>
        {
            var name = NormalizeTitle(result.Name ?? "");
            return name.Length > 0 && wanted.Any(title => title.Contains(name, StringComparison.Ordinal) ||
                name.Contains(title, StringComparison.Ordinal));
        }).ToList();
        return partial.Count > 0 ? ByYear(partial, year) : results[0];
    }

    private static RemoteSearchResult ByYear(IReadOnlyList<RemoteSearchResult> results, int? year) =>
        year is { } value
            ? results.FirstOrDefault(result => result.ProductionYear is not { } found || Math.Abs(found - value) <= 1) ?? results[0]
            : results[0];

    internal static string NormalizeTitle(string value) => string.Concat(value
        .Normalize(NormalizationForm.FormKC)
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant));

    private sealed record AniBtIdentifyPlan(string TargetId, RemoteSearchResult Candidate);
}
