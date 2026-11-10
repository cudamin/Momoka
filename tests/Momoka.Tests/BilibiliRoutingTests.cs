using System.Net;
using Momoka.Configuration;
using Momoka.Danmaku;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class BilibiliRoutingTests
{
    internal static void Register()
    {
        Test("弹幕分派：B站长分享链接和XML只使用B站传输，关闭后拒绝请求", async () =>
        {
            var settings = Settings();
            using var bilibili = new BilibiliClient(settings, PassthroughSecretProtector.Instance, new Reply());
            using var facade = Facade(settings, bilibili);
            var result = await facade.SearchAsync("https://www.bilibili.com/video/BV17x411w7KC/?share_source=" + new string('x', 150), null, DanmakuSource.Bilibili);
            Assert.Equal(401L, result.Episodes.Single().EpisodeId);
            var comments = await facade.GetCommentsAsync(401, true, DanmakuSource.Bilibili);
            Assert.Equal("匿名弹幕", comments.Single().Text);
            await facade.ShutdownAsync();
            Assert.Throws<OperationCanceledException>(() => facade.SearchAsync("BV17x411w7KC", null, DanmakuSource.Bilibili).GetAwaiter().GetResult());
        });
        Test("弹幕分派：关闭会取消并等待B站在途操作，借用客户端仍由容器管理", async () =>
        {
            var settings = Settings();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var bilibili = new BilibiliClient(settings, PassthroughSecretProtector.Instance, new Reply(async token =>
            {
                entered.TrySetResult();
                using var registration = token.Register(() => canceled.TrySetResult());
                await finish.Task;
                token.ThrowIfCancellationRequested();
            }));
            using var facade = Facade(settings, bilibili);
            var request = facade.SearchAsync("BV17x411w7KC", null, DanmakuSource.Bilibili);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var closing = facade.ShutdownAsync();
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(closing.IsCompleted);
            finish.SetResult();
            Assert.Throws<OperationCanceledException>(() => request.GetAwaiter().GetResult());
            await closing.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(7L, (await bilibili.ValidateSessionAsync("SESSDATA=anonymous")).UserId);
        });
    }

    private static AppSettings Settings()
    {
        var settings = new AppSettings();
        settings.Danmaku.Enabled = true;
        settings.Danmaku.Source = DanmakuSourceMode.Bilibili;
        settings.Danmaku.BilibiliUserId = 7;
        settings.Danmaku.ProtectedBilibiliCookie = "SESSDATA=anonymous";
        return settings;
    }

    private static DandanPlayClient Facade(AppSettings settings, BilibiliClient bilibili)
    {
        var constructor = typeof(DandanPlayClient).GetConstructors().Single();
        Assert.True(constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(BilibiliClient)), "缺少B站来源分派");
        return (DandanPlayClient)constructor.Invoke([settings, "unused", new Reject(), null, null, PassthroughSecretProtector.Instance, bilibili]);
    }

    private sealed class Reject : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new AssertionException("B站操作不得触发原两源HTTP");
    }

    private sealed class Reply(Func<CancellationToken, Task>? before = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (before is not null) await before(token);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath switch
                {
                    "/x/web-interface/nav" => """{"code":0,"data":{"isLogin":true,"mid":7,"uname":"匿名用户"}}""",
                    "/x/web-interface/view" => """{"code":0,"data":{"title":"匿名视频","pages":[{"page":1,"cid":401,"part":"正片"}]}}""",
                    "/401.xml" => """<i><d p="1,1,25,16777215,0,0,hash,1">匿名弹幕</d></i>""",
                    _ => throw new AssertionException("非预期地址")
                })
            };
        }
    }
}
