using System.Globalization;
using System.Text.Json;

namespace Momoka.MoviePilot;

public enum MoviePilotHistoryAction
{
    DeleteRecord,
    DeleteSource,
    DeleteDestination,
    DeleteAll,
    SmartReorganize
}

public sealed record MoviePilotHistoryCapabilities(string ConnectionStamp, bool CanManage, bool CanSmartReorganize);

/// <summary>只读核对后封存的确认范围；不能用标题搜索结果直接授权删除或智能整理。</summary>
public sealed class MoviePilotHistoryPlan
{
    public const int MaxFiles = 1000;
    private int _claimed;
    internal MoviePilotHistoryPlan(string title, MoviePilotHistoryAction action, bool batch,
        string connectionStamp, IReadOnlyList<MoviePilotTransferHistory> histories, IReadOnlyList<string>? libraryPaths = null)
    {
        Title = title;
        Action = action;
        Batch = batch;
        ConnectionStamp = connectionStamp;
        Histories = Array.AsReadOnly(histories.ToArray());
        LibraryPaths = Array.AsReadOnly((libraryPaths ?? histories.Select(history => history.TargetPath).ToArray()).ToArray());
    }

    public string Title { get; }
    public MoviePilotHistoryAction Action { get; }
    public bool Batch { get; }
    public string ConnectionStamp { get; }
    public IReadOnlyList<MoviePilotTransferHistory> Histories { get; }
    /// <summary>当前条目的完整库文件范围；缩小勾选记录不能把未选版本从这个范围移除。</summary>
    public IReadOnlyList<string> LibraryPaths { get; }
    public bool Claimed => Volatile.Read(ref _claimed) != 0;
    public bool DeleteSource => Action is MoviePilotHistoryAction.DeleteSource or MoviePilotHistoryAction.DeleteAll;
    public bool DeleteDestination => Action is MoviePilotHistoryAction.DeleteDestination or MoviePilotHistoryAction.DeleteAll;
    public bool Smart => Action == MoviePilotHistoryAction.SmartReorganize;
    public string Heading => Smart ? Batch ? "智能批量整理" : "智能整理" : "媒体管理";
    public string ActionLabel => Action switch
    {
        MoviePilotHistoryAction.DeleteRecord => "仅删除转移记录",
        MoviePilotHistoryAction.DeleteSource => "删除转移记录和源文件",
        MoviePilotHistoryAction.DeleteDestination => "删除转移记录和媒体库文件",
        MoviePilotHistoryAction.DeleteAll => "删除转移记录、源文件和媒体库文件",
        _ => Batch ? "智能批量整理" : "智能整理"
    };
    public string Warning => Smart
        ? "确认后会自动交给 MoviePilot 智能助手重新识别、改名和整理。助手可能移动文件或清理旧目标；按 MP 现有配置执行，不会改为普通手动整理。"
        : DeleteSource || DeleteDestination
            ? "将直接在 MoviePilot 服务器删除下列文件及转移记录，无法在这里撤销。删除源文件可能影响做种及其他链接；删除媒体库文件会使对应内容不可播放。"
            : "只删除 MoviePilot 的转移记录，保留源文件和媒体库文件。后续将无法再通过这条记录重新整理。";
    public string Summary => $"「{Title}」 · {Histories.Count.ToString(CultureInfo.InvariantCulture)} 条记录\n{ActionLabel}";
    public string Details => string.Join("\n\n", Histories.Select(history =>
        $"{history.Display}\n源文件{(DeleteSource ? "（删除）" : Smart ? "" : "（保留）")}：{history.SourcePath}\n媒体库文件{(DeleteDestination ? "（删除）" : Smart ? "" : "（保留）")}：{history.TargetPath}"));

    /// <summary>只缩小已核对范围；合法选择消费原确认，保留原记录顺序与快照，不能派生多份可提交计划。</summary>
    public MoviePilotHistoryPlan SelectRecords(IReadOnlyCollection<long> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var selectedIds = ids.ToArray();
        var selected = selectedIds.ToHashSet();
        if (selectedIds.Length == 0 || selected.Count != selectedIds.Length)
            throw new MoviePilotOperationBlockedException("请选择至少一条整理记录，不能重复选择同一条记录");
        var histories = Histories.Where(history => selected.Contains(history.Id)).ToArray();
        if (histories.Length != selectedIds.Length)
            throw new MoviePilotOperationBlockedException("选择包含未核对的整理记录，请重新读取确认范围");
        // 与直接提交争用同一个消费位；并行派生或原计划提交只能有一方取得确认。
        Claim();
        return new MoviePilotHistoryPlan(Title, Action, Batch, ConnectionStamp, histories, LibraryPaths);
    }

    internal void Claim()
    {
        if (Interlocked.Exchange(ref _claimed, 1) != 0)
            throw new MoviePilotOperationBlockedException("这次确认已使用，请先核对 MoviePilot 状态，不能重复提交");
    }
}

public sealed record MoviePilotHistoryReceipt(long Id, string Source, string Destination, string History, string Message)
{
    public bool Removed => History is "deleted" or "not_found";
    public bool Complete { get; init; }
    public string Display => $"#{Id} · 源文件：{Step(Source)} · 媒体库文件：{Step(Destination)} · 记录：{History switch { "deleted" => "已删除", "not_found" => "已不存在", _ => "保留待核对" }}{(Message.Length == 0 ? "" : "\n" + Message)}";
    private static string Step(string status) => status switch
    {
        "deleted" => "已删除",
        "already_missing" => "已不存在",
        "not_requested" => "未请求删除",
        "failed" => "失败",
        _ => "结果不明"
    };

    internal static MoviePilotHistoryReceipt Parse(long id, MoviePilotReply reply, MoviePilotHistoryPlan plan)
    {
        var data = reply.Data;
        var source = MoviePilotTransfer.Text(MoviePilotTransferHistory.Member(data, "source"), "status");
        var destination = MoviePilotTransfer.Text(MoviePilotTransferHistory.Member(data, "destination"), "status");
        var history = MoviePilotTransfer.Text(data, "history");
        if (source.Length == 0 && destination.Length == 0 && history.Length == 0 && reply.Success)
            return new(id, "unknown", "unknown", "unknown", "服务器已接收；此版本未返回分项结果，请到 MP 核对");
        if (!Valid(source) || !Valid(destination) || history is not ("deleted" or "retained" or "not_found") ||
            !plan.DeleteSource && source is not "not_requested" || !plan.DeleteDestination && destination is not "not_requested")
            throw new MoviePilotException("删除回执缺少可核对的分项结果；请到 MoviePilot 核对，勿重复发送");
        return new(id, source, destination, history,
            string.Join("；", new[] { reply.Message, MoviePilotTransfer.Text(data, "message"),
                MoviePilotTransfer.Text(MoviePilotTransferHistory.Member(data, "source"), "message"),
                MoviePilotTransfer.Text(MoviePilotTransferHistory.Member(data, "destination"), "message") }
                .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal)))
        {
            Complete = history is "deleted" or "not_found" &&
                (!plan.DeleteSource || source is "deleted" or "already_missing") &&
                (!plan.DeleteDestination || destination is "deleted" or "already_missing")
        };
    }

    private static bool Valid(string value) => value is "not_requested" or "deleted" or "already_missing" or "failed";
}

public sealed record MoviePilotHistoryResult(bool Smart, string ProgressKey, IReadOnlyList<long> AcceptedIds,
    IReadOnlyList<MoviePilotHistoryReceipt> Receipts, int RequestedCount, string Problem = "")
{
    /// <summary>只返回明确完成的库文件删除；源步骤失败仍保留已完成目标，未知回执不推定文件消失。</summary>
    public IReadOnlyList<string> RemovedLibraryPaths(MoviePilotHistoryPlan plan)
    {
        if (Smart || !plan.DeleteDestination) return [];
        var removed = new List<string>();
        foreach (var receipt in Receipts.Where(receipt => receipt.Destination is "deleted" or "already_missing"))
        {
            var history = plan.Histories.FirstOrDefault(history => history.Id == receipt.Id);
            if (history is null || string.IsNullOrWhiteSpace(history.TargetPath) ||
                removed.Any(path => MoviePilotTransfer.SamePath(path, history.TargetPath))) continue;
            removed.Add(history.TargetPath);
        }
        return removed.AsReadOnly();
    }

    public bool RemovesSubject(MoviePilotHistoryPlan plan)
    {
        var removed = RemovedLibraryPaths(plan);
        return plan.LibraryPaths.Count > 0 && plan.LibraryPaths.All(path => !string.IsNullOrWhiteSpace(path) &&
            removed.Any(target => MoviePilotTransfer.SamePath(path, target)));
    }

    public bool NeedsReview => Problem.Length > 0 || Receipts.Any(receipt => !receipt.Complete);
    public string Summary => Smart
        ? $"已交给 MoviePilot 智能助手处理 {AcceptedIds.Count} 条整理记录；提交成功不代表文件已整理完成，请在 MP 查看进度和整理历史。"
        : $"本次请求 {RequestedCount} 条记录，已确认移除 {Receipts.Count(receipt => receipt.Removed)} 条。{(NeedsReview ? "其余结果待核对；已完成的操作不会回滚，请到 MP 核对，不要整批重试。" : "")}";
}
