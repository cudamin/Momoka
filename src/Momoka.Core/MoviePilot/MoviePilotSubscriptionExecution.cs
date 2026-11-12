using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

/// <summary>订阅搜索执行状态的用户可见展示词，与网页端 subscriptionExecution.ts的九种一一对应。</summary>
public enum MoviePilotExecutionLabel { Queued, Searching, Waiting, Submitting, Submitted, NoResult, Failed, Cancelling, Cancelled }

/// <summary>展示色调；视图把它映射成笔刷或 InfoBar 严重级，协议侧不引用 UI 类型。</summary>
public enum MoviePilotExecutionTone { Info, Primary, Success, Secondary, Error, Warning }

/// <summary>展示所需的执行状态字段；订阅级状态与批次状态都满足（网页端 SubscriptionExecutionLike）。</summary>
public interface IMoviePilotExecutionStatus
{
    string State { get; }
    string Phase { get; }
    /// <summary>等待站点时的下次继续时间；只有订阅级状态会返回，批次恒为空。</summary>
    string NextRunAt { get; }
    /// <summary>订阅级取 submitted_count；批次取 download_counts.submitted。</summary>
    int SubmittedCount { get; }
}

/// <summary>订阅列表里一条订阅的当前搜索执行状态（execution_status 投影）；服务器没给就是 null。</summary>
public sealed record MoviePilotSubscriptionExecution : IMoviePilotExecutionStatus
{
    public required string State { get; init; }
    public string Phase { get; init; } = "";
    public string UpdatedAt { get; init; } = "";
    public string Source { get; init; } = "";
    public string BatchId { get; init; } = "";
    public string NextRunAt { get; init; } = "";
    public string Error { get; init; } = "";
    public bool CanCancel { get; init; }
    public int SubmittedCount { get; init; }

    public static MoviePilotSubscriptionExecution? Parse(JsonElement data) => data.ValueKind != JsonValueKind.Object ? null : new()
    {
        State = MoviePilotSubscription.Text(data, "state"),
        Phase = MoviePilotSubscription.Text(data, "phase"),
        UpdatedAt = MoviePilotSubscription.Text(data, "updated_at"),
        Source = MoviePilotSubscription.Text(data, "source"),
        BatchId = MoviePilotSubscription.Text(data, "batch_id"),
        NextRunAt = MoviePilotSubscription.Text(data, "next_run_at"),
        Error = MoviePilotSubscription.Text(data, "error"),
        CanCancel = MoviePilotSubscription.Member(data, "can_cancel").ValueKind == JsonValueKind.True,
        SubmittedCount = MoviePilotSubscription.Number(data, "submitted_count") ?? 0
    };
}

/// <summary>一次订阅搜索批次的用户可见进度（subscribe/execution/batches）。</summary>
public sealed record MoviePilotSubscriptionBatch : IMoviePilotExecutionStatus
{
    public required string BatchId { get; init; }
    internal string ConnectionStamp { get; init; } = "";
    public string Source { get; init; } = "";
    public required string State { get; init; }
    public string Phase { get; init; } = "";
    public int TotalCount { get; init; }
    public int ProcessedCount { get; init; }
    public int FinishedCount { get; init; }
    public int FailedCount { get; init; }
    public int CancelledCount { get; init; }
    public int SkippedCount { get; init; }
    public string CreatedAt { get; init; } = "";
    public string UpdatedAt { get; init; } = "";
    public int? CurrentSubscriptionId { get; init; }
    public int? CurrentSiteId { get; init; }
    public string Error { get; init; } = "";
    public bool CanCancel { get; init; }
    public int SubmittedCount { get; init; }
    public int DownloadsOmitted { get; init; }
    /// <summary>download_counts 与 downloads 的原样 JSON，只用于批次签名比对，不向 UI 摊开明细。</summary>
    internal string DownloadCountsText { get; init; } = "{}";
    internal string DownloadsText { get; init; } = "[]";
    public string NextRunAt => "";

    public static IReadOnlyList<MoviePilotSubscriptionBatch> ParseList(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array)
            throw new MoviePilotException("MoviePilot 订阅搜索批次格式不正确，请刷新重试");
        return data.EnumerateArray().Select(Parse).Where(batch => batch is not null)
            .Cast<MoviePilotSubscriptionBatch>().DistinctBy(batch => batch.BatchId).ToArray();
    }

    private static MoviePilotSubscriptionBatch? Parse(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object || MoviePilotSubscription.Text(item, "batch_id") is not { Length: > 0 } id) return null;
        var counts = MoviePilotSubscription.Member(item, "download_counts");
        var downloads = MoviePilotSubscription.Member(item, "downloads");
        return new()
        {
            BatchId = id,
            Source = MoviePilotSubscription.Text(item, "source"),
            State = MoviePilotSubscription.Text(item, "state"),
            Phase = MoviePilotSubscription.Text(item, "phase"),
            TotalCount = MoviePilotSubscription.Number(item, "total_count") ?? 0,
            ProcessedCount = MoviePilotSubscription.Number(item, "processed_count") ?? 0,
            FinishedCount = MoviePilotSubscription.Number(item, "finished_count") ?? 0,
            FailedCount = MoviePilotSubscription.Number(item, "failed_count") ?? 0,
            CancelledCount = MoviePilotSubscription.Number(item, "cancelled_count") ?? 0,
            SkippedCount = MoviePilotSubscription.Number(item, "skipped_count") ?? 0,
            CreatedAt = MoviePilotSubscription.Text(item, "created_at"),
            UpdatedAt = MoviePilotSubscription.Text(item, "updated_at"),
            CurrentSubscriptionId = MoviePilotSubscription.Number(item, "current_subscription_id"),
            CurrentSiteId = MoviePilotSubscription.Number(item, "current_site_id"),
            Error = MoviePilotSubscription.Text(item, "error"),
            CanCancel = MoviePilotSubscription.Member(item, "can_cancel").ValueKind == JsonValueKind.True,
            SubmittedCount = MoviePilotSubscription.Number(counts, "submitted") ?? 0,
            DownloadsOmitted = MoviePilotSubscription.Number(item, "downloads_omitted") ?? 0,
            DownloadCountsText = counts.ValueKind == JsonValueKind.Object ? counts.GetRawText() : "{}",
            DownloadsText = downloads.ValueKind == JsonValueKind.Array ? downloads.GetRawText() : "[]"
        };
    }
}

/// <summary>
/// 执行状态的展示判断，逐条移植网页端 subscriptionExecution.ts 与 SubscribeCard/SubscribeListView 的可见性规则。
/// 后端阶段比用户需要区分的多：内部准备、秒级站点间隔、排队原因都归并成少数几种；跳过不展示，
/// 只有站点要等较久时才给出继续时间。
/// </summary>
public static class MoviePilotExecutionText
{
    /// <summary>站点等待超过该时长才提示继续时间；更短的等待属于正常搜索节奏，显示为搜索中。</summary>
    public static readonly TimeSpan VisibleWait = TimeSpan.FromSeconds(120);

    /// <summary>终态以 state 为准，进行中以 phase 为准，避免残留阶段把已结束任务显示成进行中。</summary>
    public static string Stage(IMoviePilotExecutionStatus status) =>
        IsTerminal(status.State) ? status.State : status.Phase.Length > 0 ? status.Phase : status.State;

    public static bool IsTerminal(string state) => state is "completed" or "failed" or "cancelled" or "skipped";

    public static bool IsActiveState(string state) => state is "queued" or "scheduled" or "running" or "matching"
        or "searching" or "waiting_subscription" or "waiting_site_budget" or "preparing" or "submitting" or "cancelling";

    /// <summary>批次是否仍有可继续的工作；终态不被残留阶段重新激活。</summary>
    public static bool IsActive(IMoviePilotExecutionStatus status) =>
        !IsTerminal(status.State) && (IsActiveState(status.State) || IsActiveState(status.Phase));

    /// <summary>后台自动检查的来源；卡片不为它们显示执行态（空来源不算后台）。</summary>
    public static bool IsBackgroundSource(string source) => source is "new" or "fallback";

    /// <summary>批次横幅只展示用户主动发起的搜索，后台自动检查保持安静运行。</summary>
    public static bool IsUserRequestedSource(string source) => source is "manual" or "targeted";

    /// <summary>站点要等待超过 <see cref="VisibleWait"/> 时返回继续时间，否则返回 null。</summary>
    public static DateTimeOffset? ResumeAt(IMoviePilotExecutionStatus status, DateTimeOffset now)
    {
        if (Stage(status) != "waiting_site_budget" || status.NextRunAt.Length == 0) return null;
        return DateTimeOffset.TryParse(status.NextRunAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var resume)
            && resume - now > VisibleWait ? resume : null;
    }

    /// <summary>用户可见的展示词；跳过的任务没有可行动信息，返回 null 不展示。</summary>
    public static MoviePilotExecutionLabel? Label(IMoviePilotExecutionStatus status, DateTimeOffset now)
    {
        var stage = Stage(status);
        if (stage == "skipped") return null;
        if (stage == "completed") return status.SubmittedCount > 0 ? MoviePilotExecutionLabel.Submitted : MoviePilotExecutionLabel.NoResult;
        if (ResumeAt(status, now) is not null) return MoviePilotExecutionLabel.Waiting;
        return stage switch
        {
            "queued" or "scheduled" or "waiting_subscription" => MoviePilotExecutionLabel.Queued,
            "preparing" or "submitting" => MoviePilotExecutionLabel.Submitting,
            "failed" => MoviePilotExecutionLabel.Failed,
            "cancelling" => MoviePilotExecutionLabel.Cancelling,
            "cancelled" => MoviePilotExecutionLabel.Cancelled,
            _ => MoviePilotExecutionLabel.Searching
        };
    }

    /// <summary>展示词文案；等待继续只给本地时分，保持卡片状态紧凑。无展示词时为空串。</summary>
    public static string LabelText(IMoviePilotExecutionStatus status, DateTimeOffset now) => Label(status, now) switch
    {
        MoviePilotExecutionLabel.Queued => "排队中",
        MoviePilotExecutionLabel.Searching => "搜索中",
        MoviePilotExecutionLabel.Waiting => ResumeAt(status, now)!.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) + " 再搜",
        MoviePilotExecutionLabel.Submitting => "添加下载",
        MoviePilotExecutionLabel.Submitted => "已添加下载",
        MoviePilotExecutionLabel.NoResult => "暂无资源",
        MoviePilotExecutionLabel.Failed => "搜索失败",
        MoviePilotExecutionLabel.Cancelling => "停止中",
        MoviePilotExecutionLabel.Cancelled => "已停止",
        _ => ""
    };

    public static MoviePilotExecutionTone Tone(MoviePilotExecutionLabel label) => label switch
    {
        MoviePilotExecutionLabel.Queued or MoviePilotExecutionLabel.Waiting => MoviePilotExecutionTone.Info,
        MoviePilotExecutionLabel.Searching or MoviePilotExecutionLabel.Submitting => MoviePilotExecutionTone.Primary,
        MoviePilotExecutionLabel.Submitted => MoviePilotExecutionTone.Success,
        MoviePilotExecutionLabel.NoResult or MoviePilotExecutionLabel.Cancelled => MoviePilotExecutionTone.Secondary,
        MoviePilotExecutionLabel.Failed => MoviePilotExecutionTone.Error,
        _ => MoviePilotExecutionTone.Warning
    };

    /// <summary>卡片上终态的短暂反馈窗口；跳过不展示，故没有窗口。</summary>
    public static TimeSpan? CardTerminalWindow(string state) => state switch
    {
        "completed" => TimeSpan.FromSeconds(5),
        "cancelled" => TimeSpan.FromSeconds(10),
        "failed" => TimeSpan.FromSeconds(60),
        _ => null
    };

    /// <summary>批次横幅上终态的短暂反馈窗口；全部完成或全部跳过的批次不展示。</summary>
    public static TimeSpan? BatchTerminalWindow(string state) => state switch
    {
        "failed" => TimeSpan.FromSeconds(60),
        "cancelled" => TimeSpan.FromSeconds(10),
        _ => null
    };

    /// <summary>
    /// 卡片此刻该显示哪份执行状态：后台来源与跳过不显示；终态按重要程度短暂显示后恢复卡片原始信息。
    /// 终态先看 state 再看 phase（与网页端一致）；更新时间解析不出来时按刚发生处理，宁可多显示一会儿。
    /// </summary>
    public static MoviePilotSubscriptionExecution? VisibleCardExecution(MoviePilotSubscriptionExecution? execution, DateTimeOffset now)
    {
        if (execution is null || execution.Source.Length > 0 && IsBackgroundSource(execution.Source)) return null;
        if (Label(execution, now) is null) return null;
        var terminal = CardTerminalWindow(execution.State) is not null ? execution.State
            : CardTerminalWindow(execution.Phase) is not null ? execution.Phase : null;
        if (terminal is null) return execution;
        var elapsed = DateTimeOffset.TryParse(execution.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var updated)
            && now > updated ? now - updated : TimeSpan.Zero;
        return elapsed < CardTerminalWindow(terminal) ? execution : null;
    }

    /// <summary>结束批次只保留短暂反馈；更新时间解析不出来时保持可见，避免旧失败长期占据页面。</summary>
    public static bool IsRecentTerminalBatch(MoviePilotSubscriptionBatch batch, IReadOnlySet<string> dismissed, DateTimeOffset now)
    {
        if (BatchTerminalWindow(batch.State) is not { } window || dismissed.Contains(batch.BatchId)) return false;
        return !DateTimeOffset.TryParse(batch.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var updated)
            || now - updated < window;
    }

    /// <summary>横幅批次：用户主动发起的里先找进行中的，再找最近结束的；没有就是 null。</summary>
    public static MoviePilotSubscriptionBatch? SelectVisibleBatch(IEnumerable<MoviePilotSubscriptionBatch> batches,
        IReadOnlySet<string> dismissed, DateTimeOffset now)
    {
        var requested = batches.Where(batch => IsUserRequestedSource(batch.Source)).ToArray();
        return requested.FirstOrDefault(batch => IsActive(batch))
            ?? requested.FirstOrDefault(batch => IsRecentTerminalBatch(batch, dismissed, now));
    }

    /// <summary>
    /// 列表里是否有正在执行的卡片（决定是否按 15 秒节奏补刷完整列表）。与网页端一致：
    /// 只看 state/phase 是否活动词，不做终态屏蔽 —— 这里问的是「要不要继续刷新」，不是「要不要展示」。
    /// </summary>
    public static bool HasActiveCardExecution(IEnumerable<MoviePilotSubscription> subscriptions) =>
        subscriptions.Any(subscription => subscription.Execution is { } execution
            && (execution.Source.Length == 0 || !IsBackgroundSource(execution.Source))
            && (IsActiveState(execution.State) || IsActiveState(execution.Phase)));

    /// <summary>批次横幅的进度百分比；总数未知时为 0。</summary>
    public static int BatchProgressPercent(MoviePilotSubscriptionBatch batch) => batch.TotalCount <= 0 ? 0
        : Math.Min(100, (int)Math.Round(batch.ProcessedCount * 100.0 / batch.TotalCount, MidpointRounding.AwayFromZero));

    /// <summary>总数未知且仍在活动的批次用不定进度条，不伪造百分比。</summary>
    public static bool BatchProgressIndeterminate(MoviePilotSubscriptionBatch batch) =>
        batch.TotalCount == 0 && IsActiveState(batch.State);

    public static MoviePilotExecutionTone BatchTone(MoviePilotSubscriptionBatch batch) =>
        IsActive(batch) ? batch.Phase == "waiting_subscription" ? MoviePilotExecutionTone.Warning : MoviePilotExecutionTone.Info
        : batch.State == "failed" ? MoviePilotExecutionTone.Error : MoviePilotExecutionTone.Secondary;

    /// <summary>
    /// 批次快照签名：只有影响进度展示的字段变了才算变化（网页端 executionBatchSignature 的字段序）。
    /// download_counts/downloads 以原样 JSON 文本参与比对，内容不变则签名不变。
    /// </summary>
    public static string Signature(IEnumerable<MoviePilotSubscriptionBatch> batches) =>
        JsonSerializer.Serialize(batches.Select(batch => new object?[]
        {
            batch.BatchId, batch.Source, batch.State, batch.Phase, batch.TotalCount, batch.ProcessedCount,
            batch.FinishedCount, batch.FailedCount, batch.CancelledCount, batch.SkippedCount, batch.CreatedAt,
            batch.CurrentSubscriptionId, batch.CurrentSiteId, batch.UpdatedAt, batch.Error, batch.CanCancel,
            batch.DownloadCountsText, batch.DownloadsText, batch.DownloadsOmitted
        }));
}

/// <summary>手工「搜索当前媒体类型订阅」的轻量回执；旧后端成功但不带数据时为 null。</summary>
public sealed record MoviePilotSubscriptionSearchSubmission(int TargetCount, int QueuedCount, int OngoingCount)
{
    public static MoviePilotSubscriptionSearchSubmission? Parse(JsonElement data) => data.ValueKind != JsonValueKind.Object ? null
        : new(MoviePilotSubscription.Number(data, "target_count") ?? 0,
            MoviePilotSubscription.Number(data, "queued_count") ?? 0,
            MoviePilotSubscription.Number(data, "ongoing_count") ?? 0);
}

/// <summary>搜索回执对应的提示；IsSuccess 区分成功与告知两种语气（网页端 toast.success / toast.info）。</summary>
public sealed record MoviePilotSubscriptionSearchNotice(string Message, bool IsSuccess);

public static class MoviePilotSubscriptionSearchText
{
    /// <summary>网页端 runSubscriptionMaintenance 的回执提示梯，逐分支对应。</summary>
    public static MoviePilotSubscriptionSearchNotice SearchAllNotice(MoviePilotSubscriptionSearchSubmission? submission) => submission switch
    {
        null => new("当前媒体类型订阅搜索已安排！", true),
        { TargetCount: 0 } => new("当前媒体类型没有需要搜索的订阅", false),
        { QueuedCount: 0 } => new($"{submission.OngoingCount} 个当前媒体类型订阅已经在搜索中，请稍候", false),
        { OngoingCount: > 0 } => new($"已安排 {submission.QueuedCount} 个当前媒体类型订阅搜索，另有 {submission.OngoingCount} 个正在处理中", true),
        _ => new($"已安排 {submission.QueuedCount} 个当前媒体类型订阅，系统会自动依次搜索", true)
    };
}
