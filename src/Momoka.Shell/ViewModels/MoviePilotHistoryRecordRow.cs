using System.Globalization;
using Momoka.MoviePilot;

namespace Momoka.Shell.ViewModels;

/// <summary>确认与回执共用原记录身份；没有回执的行也保留，不能推断请求未发送。</summary>
public sealed class MoviePilotHistoryRecordRow
{
    public MoviePilotSeriesTransferRow Media { get; }
    public long Id { get; }
    public string Title { get; }
    public string Metadata { get; }
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public string SourceName => FileName(SourcePath);
    public string DestinationName => FileName(DestinationPath);
    public string SourceDirectory => Directory(SourcePath);
    public string DestinationDirectory => Directory(DestinationPath);
    public string SourceStorage => Storage(Media.History.SourceStorage);
    public string DestinationStorage => Storage(Media.History.TargetStorage);
    public string Subtitle => string.Join(" / ", new[] { Media.History.Type, Media.History.Year }.Where(value => value.Length > 0));
    public string SourceStatus { get; }
    public string DestinationStatus { get; }
    public string HistoryStatus { get; }
    public string State { get; }
    public string Message { get; }
    public bool NeedsReview { get; }
    public bool Complete { get; }
    public bool Selectable { get; }
    public bool Positive => Complete || State is "整理成功" or "已提交";
    public string ResultSteps => $"源文件：{SourceStatus}\n媒体库：{DestinationStatus}\n整理记录：{HistoryStatus}";

    public static MoviePilotHistoryRecordRow Create(MoviePilotTransferHistory history,
        MoviePilotHistoryPlan plan, MoviePilotHistoryResult? result = null, MoviePilotSeriesTransferRow? media = null) =>
        new(history, plan, result, media);

    private MoviePilotHistoryRecordRow(MoviePilotTransferHistory history, MoviePilotHistoryPlan plan,
        MoviePilotHistoryResult? result, MoviePilotSeriesTransferRow? media)
    {
        var receipt = result?.Receipts.FirstOrDefault(value => value.Id == history.Id);
        var accepted = result?.AcceptedIds.Contains(history.Id) == true;
        var episode = history.Seasons + history.Episodes;
        var review = result is not null && (result.Smart ? !accepted : receipt?.Complete != true);
        Media = media ?? new MoviePilotSeriesTransferRow(history) { Selected = true };
        Id = history.Id;
        Title = episode.Length > 0 ? history.Title + " " + episode : history.Title;
        Metadata = "#" + history.Id.ToString(CultureInfo.InvariantCulture);
        SourcePath = history.SourcePath;
        DestinationPath = history.TargetPath;
        SourceStatus = Status(plan.Smart, plan.DeleteSource, result, receipt?.Source);
        DestinationStatus = Status(plan.Smart, plan.DeleteDestination, result, receipt?.Destination);
        HistoryStatus = result is null ? plan.Smart ? "保留" : "将删除" : result.Smart ? "保留" : receipt?.History switch
        {
            "deleted" => "已删除",
            "not_found" => "已不存在",
            "retained" => "保留待核对",
            _ => "待核对"
        };
        State = result is null ? history.Success ? "整理成功" : "整理失败"
                : result.Smart ? accepted ? "已提交" : "待核对" : review ? "待核对" : "已完成";
        Message = result is null ? "" : result.Smart
                ? accepted ? "已交给智能助手，文件处理尚未确认完成。" : "未取得提交回执，请到 MoviePilot 核对。"
                : receipt is null ? "未取得回执，请到 MoviePilot 核对；不要重复提交。" : review ? receipt.Message : "";
        NeedsReview = review;
        Selectable = result is null;
        Complete = result is { Smart: false } && receipt?.Complete == true;
    }

    private static string Status(bool smart, bool deleting, MoviePilotHistoryResult? result, string? status) =>
        result is null ? smart ? "按助手配置处理" : deleting ? "将删除" : "保留"
        : result.Smart ? "待助手处理" : status switch
        {
            "deleted" => "已删除",
            "already_missing" => "已不存在",
            "not_requested" => deleting ? "未执行" : "保留",
            "failed" => "删除失败",
            _ => "待核对"
        };

    private static string FileName(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        return normalized.Length == 0 ? "路径未返回" : normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    private static string Directory(string path)
    {
        var end = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return end < 0 ? "" : path[..(end + 1)];
    }

    private static string Storage(string storage) => storage switch { "local" => "本地", "" => "存储未知", _ => storage };
}
