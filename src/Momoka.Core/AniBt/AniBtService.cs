using Momoka.Emby;
using Momoka.MoviePilot;

namespace Momoka.AniBt;

public sealed record AniBtScrapeRow(string TargetPath, string SourceName, AniBtMatch Match);

public sealed record AniBtScrapeResult(string TargetPath, bool Success, string Message);

public sealed class AniBtScrapePlan
{
    private int _submitting;

    internal AniBtScrapePlan(IReadOnlyList<AniBtScrapeRow> rows, string type, MoviePilotAniBtConnection connection)
    {
        Rows = Array.AsReadOnly(rows.ToArray());
        Type = type;
        Connection = connection;
    }

    public IReadOnlyList<AniBtScrapeRow> Rows { get; }
    internal string Type { get; }
    internal MoviePilotAniBtConnection Connection { get; }

    internal void Claim()
    {
        if (Interlocked.CompareExchange(ref _submitting, 1, 0) != 0)
            throw new MoviePilotOperationBlockedException("这批刮削已经提交，请先在 MoviePilot 核对结果");
    }
}

/// <summary>AniBT 只负责识别；下载和当前目标文件的刮削始终由同一份 MoviePilot 连接处理。</summary>
public sealed class AniBtService(AniBtClient client, MoviePilotService moviePilot)
{
    public async Task<AniBtSearchPage> SearchAsync(string term, int page, CancellationToken cancellationToken)
    {
        var connection = moviePilot.CaptureAniBtConnection();
        var result = await client.SearchAsync(term, page, cancellationToken).ConfigureAwait(false);
        moviePilot.CheckAniBtConnection(connection, cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<MoviePilotResource>> ResourcesAsync(AniBtAnime anime, CancellationToken cancellationToken, int? episode = null)
    {
        var connection = moviePilot.CaptureAniBtConnection();
        var releases = await client.ReleasePageAsync(anime, cancellationToken, episode).ConfigureAwait(false);
        moviePilot.CheckAniBtConnection(connection, cancellationToken);
        return await moviePilot.AniBtResourcesAsync(anime with { BgmId = releases.BgmId }, releases.Items, connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AniBtScrapePlan> PrepareAsync(EmbyClient emby, EmbyItem item, CancellationToken cancellationToken)
    {
        var connection = moviePilot.CaptureAniBtConnection();
        if (!await AniBtEligibility.ResolveAsync(emby, item, cancellationToken).ConfigureAwait(false))
            throw new MoviePilotOperationBlockedException("只有类型包含动画或动漫的媒体支持 AniBT 识别刮削");
        var context = await MoviePilotTransferCollect.CollectAsync(emby, item, cancellationToken).ConfigureAwait(false);
        moviePilot.CheckAniBtConnection(connection, cancellationToken);
        if (context.Files.Count == 0) throw new MoviePilotException("此条目没有可刮削的单个视频文件");
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
            rows.Add(new(file.Path, sourceName, match));
        }
        return new(rows, context.Type, connection);
    }

    internal static string SourceName(string targetPath, IReadOnlyList<MoviePilotTransferHistory> histories)
    {
        if (histories.Count > 1)
            throw new MoviePilotOperationBlockedException($"此文件有多条原整理记录，请先在 MoviePilot 核对：{targetPath}");
        // 成功 move 的 TransferPath 是改名后的目标；识别仍应使用历史里的原 source 名字。
        var sourcePath = histories.Count == 1 ? histories[0].SourcePath : "";
        return MoviePilotTransfer.PathName(string.IsNullOrWhiteSpace(sourcePath) ? targetPath : sourcePath);
    }

    public async Task<IReadOnlyList<AniBtScrapeResult>> SubmitAsync(AniBtScrapePlan plan,
        IReadOnlyList<AniBtAnime> choices, CancellationToken cancellationToken, Func<bool>? canContinue = null)
    {
        moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
        if (choices.Count != plan.Rows.Count || choices.Count == 0)
            throw new MoviePilotOperationBlockedException("请为每个文件选择识别结果后再刮削");
        var selected = choices.ToArray();
        var prepared = new List<MoviePilotAniBtScrapeTarget>(plan.Rows.Count);
        // 整批只读检查全部完成后才有任何写入；不同季、特别篇各自保留自己的 Bangumi 身份。
        for (var index = 0; index < plan.Rows.Count; index++)
        {
            if (canContinue?.Invoke() == false) throw new OperationCanceledException("任务所属的媒体库身份已改变");
            var row = plan.Rows[index];
            var choice = selected[index];
            if (choice.BgmId <= 0 || !row.Match.Candidates.Any(candidate =>
                    candidate.AnimeId == choice.AnimeId && candidate.BgmId == choice.BgmId))
                throw new MoviePilotOperationBlockedException("所选媒体不属于此文件的 AniBT 候选结果，请重新识别");
            prepared.Add(await moviePilot.PrepareAniBtScrapeAsync(row.TargetPath, choice, plan.Type,
                plan.Connection, cancellationToken).ConfigureAwait(false));
        }
        moviePilot.CheckAniBtConnection(plan.Connection, cancellationToken);
        plan.Claim();
        var results = new List<AniBtScrapeResult>(prepared.Count);
        var stopped = false;
        foreach (var target in prepared)
        {
            if (stopped)
            {
                results.Add(new(target.Path, false, "前一文件未完成；此文件没有提交"));
                continue;
            }
            try
            {
                if (canContinue?.Invoke() == false) throw new OperationCanceledException("任务所属的媒体库身份已改变");
                results.Add(await moviePilot.SubmitAniBtScrapeAsync(target, plan.Connection, cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (Exception error) when (error is MoviePilotException or OperationCanceledException)
            {
                results.Add(new(target.Path, false, error is OperationCanceledException
                    ? "操作已取消或连接改变，请在 MoviePilot 核对已提交文件" : error.Message));
                stopped = true;
            }
        }
        return results;
    }
}
