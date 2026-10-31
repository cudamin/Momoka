using System.Reflection;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class MediaManagementCardTests
{
    internal static void Register()
    {
        foreach (var action in Enum.GetValues<MoviePilotHistoryAction>())
            Test("媒体卡片：默认全选与操作范围 " + action, () =>
            {
                var plan = Plan(action);
                var row = MoviePilotHistoryRecordRow.Create(plan.Histories[0], plan);
                Assert.True(row.Media.Selected);
                Assert.True(row.Selectable);
                Assert.Equal("长标题 S01E02", row.Title);
                Assert.Equal("电视剧 / 2026", row.Subtitle);
                Assert.Equal("本地", row.SourceStorage);
                Assert.Equal("[字幕组] 第02集 HEVC.mkv", row.SourceName);
                Assert.Equal("/Downloads/很长的目录/", row.SourceDirectory);
                Assert.Equal("D:\\媒体库\\测试剧\\", row.DestinationDirectory);
                Assert.Equal("测试剧.S01E02.mkv", row.DestinationName);
                Assert.Equal(plan.Smart ? "按助手配置处理" : plan.DeleteSource ? "将删除" : "保留", row.SourceStatus);
                Assert.Equal(plan.Smart ? "按助手配置处理" : plan.DeleteDestination ? "将删除" : "保留", row.DestinationStatus);
                Assert.False(row.Complete);
            });
        Test("媒体卡片：部分结果保留原身份与取消勾选，不把缺回执当未发送", () =>
        {
            var plan = Plan(MoviePilotHistoryAction.DeleteAll);
            var original = MoviePilotHistoryRecordRow.Create(plan.Histories[0], plan);
            original.Media.Selected = false;
            var result = new MoviePilotHistoryResult(false, "", [], [], 1, "连接中断");
            var row = MoviePilotHistoryRecordRow.Create(plan.Histories[0], plan, result, original.Media);
            Assert.True(ReferenceEquals(original.Media, row.Media));
            Assert.False(row.Media.Selected);
            Assert.False(row.Selectable);
            Assert.True(row.NeedsReview);
            Assert.Equal("待核对", row.SourceStatus);
            Assert.Contains("未取得回执", row.Message);
            Assert.False(row.Message.Contains("未发送", StringComparison.Ordinal));
        });
        foreach (var status in new[] { "failed", "not_requested", "already_missing", "deleted" })
            Test("媒体卡片：分项结果 " + status, () =>
            {
                var plan = Plan(MoviePilotHistoryAction.DeleteAll);
                var complete = status is "already_missing" or "deleted";
                var receipt = new MoviePilotHistoryReceipt(1, status, "deleted", complete ? "deleted" : "retained", "失败原因") { Complete = complete };
                var row = MoviePilotHistoryRecordRow.Create(plan.Histories[0], plan, new(false, "", [], [receipt], 1));
                Assert.Equal(complete, row.Complete);
                Assert.Equal(!complete, row.NeedsReview);
                Assert.Equal(status switch { "failed" => "删除失败", "not_requested" => "未执行", "already_missing" => "已不存在", _ => "已删除" }, row.SourceStatus);
                Assert.Equal(complete ? "" : "失败原因", row.Message);
            });
        Test("媒体卡片：智能任务接收不冒充整理完成", () =>
        {
            var plan = Plan(MoviePilotHistoryAction.SmartReorganize);
            var row = MoviePilotHistoryRecordRow.Create(plan.Histories[0], plan, new(true, "task", [1], [], 1));
            Assert.Equal("已提交", row.State);
            Assert.Equal("待助手处理", row.SourceStatus);
            Assert.False(row.Complete);
            Assert.Contains("尚未确认完成", row.Message);
        });
    }

    private static MoviePilotHistoryPlan Plan(MoviePilotHistoryAction action)
    {
        var history = new MoviePilotTransferHistory
        {
            Id = 1, Title = "长标题", Type = "电视剧", Year = "2026", Seasons = "S01", Episodes = "E02",
            SourcePath = "/Downloads/很长的目录/[字幕组] 第02集 HEVC.mkv", TargetPath = "D:\\媒体库\\测试剧\\测试剧.S01E02.mkv",
            SourceStorage = "local", TargetStorage = "local", Mode = "softlink", Date = "2026-10-31 08:00:00", Success = true
        };
        return (MoviePilotHistoryPlan)Activator.CreateInstance(typeof(MoviePilotHistoryPlan), BindingFlags.Instance | BindingFlags.NonPublic,
            null, ["长标题", action, true, "fixture", new[] { history }], null)!;
    }
}
