using System.Net;
using System.Text.Json;
using Momoka.Emby;
using Momoka.Shell;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static int Main()
    {
        Test("键盘结果定位按子卡片身份，不依赖原生容器DataContext", () =>
        {
            var correct = new object();
            var wrong = new object();
            var cards = new Dictionary<object, string?> { [correct] = KeyboardJourneyProbeServer.MovieId, [wrong] = "other" };
            Assert.True(ReferenceEquals(correct, KeyboardJourneyProbeRules.FindResult([wrong, correct], container => cards[container], KeyboardJourneyProbeServer.MovieId)));
            Assert.Null(KeyboardJourneyProbeRules.FindResult([wrong], container => cards[container], KeyboardJourneyProbeServer.MovieId));
            Assert.Null(KeyboardJourneyProbeRules.FindResult([correct], _ => null, KeyboardJourneyProbeServer.MovieId));
        });
        foreach (var host in new[] { "keyboard-a.invalid", "keyboard-b.invalid" })
            Test("键盘匿名源公开信息和目录 " + host, () => TestHarnessRun(async () =>
            {
                var server = new KeyboardJourneyProbeServer();
                using var info = await server.ReplyAsync(new(HttpMethod.Get, "https://" + host + "/emby/System/Info/Public"), default);
                var publicInfo = JsonSerializer.Deserialize<PublicSystemInfo>(await info.Content.ReadAsStringAsync())!;
                Assert.Equal(host, publicInfo.Id);
                Assert.Equal(MediaServerKind.Emby, publicInfo.ServerKind);
                using var views = await server.ReplyAsync(new(HttpMethod.Get, "https://" + host + "/emby/Users/keyboard-user/Views"), default);
                Assert.Equal("keyboard-library", JsonSerializer.Deserialize<ItemsResult>(await views.Content.ReadAsStringAsync())!.Items.Single().Id);
            }));
        Test("键盘匿名源只有正确人工账号密码能够登录", () => TestHarnessRun(async () =>
        {
            var server = new KeyboardJourneyProbeServer();
            using var correct = Authentication(KeyboardJourneyProbeServer.Username, KeyboardJourneyProbeServer.Password);
            using var response = await server.ReplyAsync(correct, default);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var auth = JsonSerializer.Deserialize<AuthenticationResult>(await response.Content.ReadAsStringAsync())!;
            Assert.Equal(KeyboardJourneyProbeServer.Username, auth.User!.Id);
            Assert.False(auth.User.Policy!.IsAdministrator);
            using var wrong = Authentication(KeyboardJourneyProbeServer.Username, "wrong");
            using var rejected = await server.ReplyAsync(wrong, default);
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.Equal(2, server.AuthenticationPosts);
        }));
        Test("键盘搜索只从第二匿名来源返回固定影片，详情不包含媒体URL", () => TestHarnessRun(async () =>
        {
            var server = new KeyboardJourneyProbeServer();
            foreach (var (host, count) in new[] { ("keyboard-a.invalid", 0), ("keyboard-b.invalid", 1) })
            {
                using var response = await server.ReplyAsync(new(HttpMethod.Get, "https://" + host + "/emby/Users/keyboard-user/Items?SearchTerm=keyboard-query"), default);
                var page = JsonSerializer.Deserialize<ItemsResult>(await response.Content.ReadAsStringAsync())!;
                Assert.Equal(count, page.TotalRecordCount);
                Assert.Equal(count, page.Items.Count);
            }
            using var detail = await server.ReplyAsync(new(HttpMethod.Get, "https://keyboard-b.invalid/emby/Users/keyboard-user/Items/keyboard-movie"), default);
            var item = JsonSerializer.Deserialize<EmbyItem>(await detail.Content.ReadAsStringAsync())!;
            Assert.Equal(KeyboardJourneyProbeServer.MovieId, item.Id);
            Assert.True(item.MediaSources.All(source => string.IsNullOrEmpty(source.Path)));
        }));
        Test("键盘匿名源拒绝真实目标、未知写入和取消", () => TestHarnessRun(async () =>
        {
            var server = new KeyboardJourneyProbeServer();
            await RejectAsync(() => server.ReplyAsync(new(HttpMethod.Get, "https://example.com/Items"), default));
            await RejectAsync(() => server.ReplyAsync(new(HttpMethod.Post, "https://keyboard-a.invalid/Items/a"), default));
            await RejectAsync(() => server.ReplyAsync(new(HttpMethod.Get, "https://keyboard-a.invalid/unknown"), default));
            Assert.Equal(1, server.RejectedWrites);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try { using var response = await server.ReplyAsync(new(HttpMethod.Get, KeyboardJourneyProbeServer.Address + "/Items"), canceled.Token); throw new AssertionException("取消仍执行"); }
            catch (OperationCanceledException) { }
        }));
        Test("键盘流程读数拒绝虚假输入、缺失焦点/名称/角色和错误字段名", () =>
        {
            KeyboardJourneyProbeRules.RequireFocus("服务器地址", "Edit", true);
            KeyboardJourneyProbeRules.RequireName("服务器地址", "服务器地址");
            KeyboardJourneyProbeRules.RequireKeyProgress(10, 11);
            foreach (var action in new Action[]
            {
                () => KeyboardJourneyProbeRules.RequireFocus("", "Edit", true),
                () => KeyboardJourneyProbeRules.RequireFocus("服务器地址", "", true),
                () => KeyboardJourneyProbeRules.RequireFocus("服务器地址", "Edit", false),
                () => KeyboardJourneyProbeRules.RequireName("错误名称夹具", "服务器地址"),
                () => KeyboardJourneyProbeRules.RequireKeyProgress(10, 10)
            }) Reject(action);
        });
        Test("键盘最终回执拒绝丢查询、错来源、真播放和多登录/写入", () =>
        {
            KeyboardJourneyProbeRules.RequireFinal(KeyboardJourneyProbeServer.Keyword, true, 0, 1, 0);
            foreach (var action in new Action[]
            {
                () => KeyboardJourneyProbeRules.RequireFinal("", true, 0, 1, 0),
                () => KeyboardJourneyProbeRules.RequireFinal(KeyboardJourneyProbeServer.Keyword, false, 0, 1, 0),
                () => KeyboardJourneyProbeRules.RequireFinal(KeyboardJourneyProbeServer.Keyword, true, 1, 1, 0),
                () => KeyboardJourneyProbeRules.RequireFinal(KeyboardJourneyProbeServer.Keyword, true, 0, 2, 0),
                () => KeyboardJourneyProbeRules.RequireFinal(KeyboardJourneyProbeServer.Keyword, true, 0, 1, 1)
            }) Reject(action);
        });
        return Run();
    }

    private static HttpRequestMessage Authentication(string user, string password) => new(HttpMethod.Post, "https://keyboard-a.invalid/emby/Users/AuthenticateByName")
    { Content = new StringContent(JsonSerializer.Serialize(new { UserName = user, Pw = password })) };
    private static void TestHarnessRun(Func<Task> action)
    {
        var running = action();
        while (!running.IsCompleted) Thread.Sleep(1);
        if (running.IsFaulted) throw running.Exception!.InnerException!;
        if (running.IsCanceled) throw new OperationCanceledException();
    }
    private static void Reject(Action action)
    {
        try { action(); throw new AssertionException("接受错误读数"); }
        catch (InvalidOperationException) { }
    }
    private static async Task RejectAsync(Func<Task<HttpResponseMessage>> action)
    {
        try { using var response = await action(); throw new AssertionException("接受禁止请求"); }
        catch (InvalidOperationException) { }
    }
}
