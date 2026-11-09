using System.Net;
using Momoka.MoviePilot;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.SubscriptionFixture;

namespace Momoka.Tests;

internal static class SubscriptionActionsTests
{
    internal static async Task RunAsync()
    {
        using var fixture = new SubscriptionFixture();
        fixture.Http.Subscriptions.Add(Subscription(12));
        TaskCompletionSource<HttpResponseMessage?>? held = null;
        fixture.Http.Before = (request, _) =>
        {
            if (request.Path == "subscribe/share")
                return held?.Task ?? Task.FromResult<HttpResponseMessage?>(FakeMoviePilot.Reply(Json("""{"success":false,"message":"请先开启分享"}""")));
            if (request.Path == "subscribe/12/activity/event-1")
                return Task.FromResult<HttpResponseMessage?>(FakeMoviePilot.Reply(Json("""{"event_id":"event-1","kind":"search","status":"completed","details_loaded":true,"counts":{},"details":{"reason":"搜索完成"}}""")));
            if (request.Path == "subscribe/12/activity")
                return Task.FromResult<HttpResponseMessage?>(FakeMoviePilot.Reply(Json(request.Query.Contains("before=", StringComparison.Ordinal)
                    ? """{"items":[{"event_id":"event-1","kind":"search","status":"completed","details_loaded":false,"counts":{},"details":{}},{"event_id":"event-2","kind":"updated","status":"completed","details_loaded":false,"counts":{},"details":{}}],"next_cursor":null}"""
                    : """{"items":[{"event_id":"event-1","kind":"search","status":"completed","details_loaded":false,"counts":{},"details":{}}],"next_cursor":"next-1"}""")));
            return Task.FromResult<HttpResponseMessage?>(null);
        };
        await fixture.ConnectAsync();
        var subscription = (await fixture.Service.SubscriptionsAsync(CancellationToken.None)).Single();
        using var vm = new MoviePilotSubscriptionActionsViewModel();
        var owned = true;
        vm.Attach(fixture.Service, subscription, () => owned);
        Check(!vm.IsReady, "未读取活动前不能宣称空列表");
        var collection = vm.Activities;
        await vm.LoadActivityAsync(7, null);
        Check(vm.IsReady && vm.Activities.Count == 1 && vm.HasMore && !vm.NoticeOpen, "第一页活动或分页状态错误");
        Check((await vm.LoadDetailsAsync(vm.Activities[0]))?.Contains("搜索完成", StringComparison.Ordinal) == true, "未读取活动完整详情");
        await vm.LoadActivityAsync(7, null, more: true);
        Check(vm.Activities.Count == 2 && !vm.HasMore && ReferenceEquals(collection, vm.Activities), "分页重复条目或替换集合");
        Check(!await vm.ShareAsync("分享说明", "匿名分享人") && vm.NoticeOpen && !vm.Busy, "业务失败没有保留错误和重试状态");
        Check(fixture.Service.SubscriptionNeedsReview(subscription), "无明确回执的写入未保留核对状态");
        fixture.Service.ConfirmSubscriptionReviewed(subscription);
        held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var share = vm.ShareAsync("分享说明", "匿名分享人");
        Check(vm.Busy, "提交中未锁定");
        var count = fixture.Http.Writes;
        Check(!await vm.ShareAsync("分享说明", "匿名分享人") && fixture.Http.Writes == count, "重复点击再次提交");
        owned = false;
        held.SetResult(FakeMoviePilot.Reply(Json("""{"success":true}""")));
        Check(!await share, "旧归属迟到响应仍向弹窗报成功");
        vm.Cancel();
        await vm.LoadActivityAsync(7, null);
        Check(vm.Activities.Count == 0 && !vm.Busy, "关闭后仍载入活动");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
