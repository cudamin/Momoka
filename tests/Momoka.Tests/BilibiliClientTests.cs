using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Danmaku;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class BilibiliClientTests
{
    private const string Cookie = "SESSDATA=fixture%2Csession; bili_jct=fixture-csrf; DedeUserID=7; buvid3=fixture-device";
    private const string Nav = """{"code":0,"message":"0","ttl":1,"data":{"isLogin":true,"mid":7,"uname":"测试帐号","wbi_img":{"img_url":"https://i0.hdslb.com/bfs/wbi/abcdefghijklmnopqrstuvwxyz012345.png","sub_url":"https://i0.hdslb.com/bfs/wbi/6789abcdefghijklmnopqrstuvwxyz01.png"}}}""";
    private const string Video = """{"code":0,"message":"0","ttl":1,"data":{"aid":170001,"bvid":"BV17x411w7KC","title":"测试视频","cid":11,"pages":[{"cid":11,"page":1,"part":"第一部分","duration":100},{"cid":12,"page":2,"part":"第二部分","duration":120}]}}""";
    private const string Season = """{"code":0,"message":"success","result":{"season_id":101,"title":"测试番剧","episodes":[{"id":201,"aid":301,"cid":401,"title":"1","long_title":"开始","badge":""},{"id":202,"aid":302,"cid":402,"title":"2","long_title":"继续","badge":""}],"section":[{"title":"特别篇","episodes":[{"id":203,"aid":303,"cid":403,"title":"SP","long_title":"番外"}]}]}}""";

    internal static void Register()
    {
        Test("B站：明确ep或分P链接优先于播放器预填集数", async () =>
        {
            foreach (var input in new[] { "https://www.bilibili.com/video/BV17x411w7KC/?p=2", "ep202" })
            {
                using var client = Client(new Handler((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath switch
                {
                    "/x/web-interface/nav" => Nav,
                    "/x/web-interface/view" => Video,
                    "/pgc/view/web/season" => Season,
                    _ => throw new AssertionException("不应请求其他地址")
                }))));
                var result = await client.SearchAsync(input, "1");
                Assert.Equal(input == "ep202" ? 402L : 12L, result.Episodes.Single().EpisodeId);
            }
        });
        Test("B站：登录确认只向固定nav发送白名单会话并读取帐号", () =>
        {
            using var handler = new Handler((request, _) =>
            {
                Assert.Equal("https://api.bilibili.com/x/web-interface/nav", request.RequestUri!.AbsoluteUri);
                Assert.Equal(Cookie, request.Headers.GetValues("Cookie").Single());
                Assert.Equal("https://www.bilibili.com/", request.Headers.Referrer!.AbsoluteUri);
                return Task.FromResult(Json(Nav));
            });
            using var client = Client(handler);
            var account = client.ValidateSessionAsync(Cookie).GetAwaiter().GetResult();
            Assert.Equal(7L, account.UserId);
            Assert.Equal("测试帐号", account.Name);
        });

        Test("B站：未登录或异常响应不接受帐号也不回显会话正文", () =>
        {
            foreach (var body in new[]
            {
                """{"code":-101,"message":"fixture%2Csession"}""",
                """{"code":0,"data":{"isLogin":false,"mid":7,"uname":"fixture%2Csession"}}""",
                """{"code":0,"data":{"isLogin":true,"mid":0,"uname":"测试"}}""",
                """{"code":"fixture","data":{}}""",
                """{"code":0,"data":["fixture"]}""",
                """{"code":0,"data":"fixture"}""",
                "<html>fixture%2Csession</html>"
            })
            {
                using var client = Client(new Handler((_, _) => Task.FromResult(Json(body))));
                var error = Assert.Catch<DandanPlayException>(() => client.ValidateSessionAsync(Cookie).GetAwaiter().GetResult());
                Assert.DoesNotContain("fixture", error.ToString());
            }
        });

        Test("B站：BV和官方分P链接直接展开cid，不请求用户URL", () =>
        {
            foreach (var input in new[] { "BV17x411w7KC", "https://www.bilibili.com/video/BV17x411w7KC/?p=2" })
            {
                using var client = Client(new Handler((request, _) =>
                {
                    if (request.RequestUri!.AbsolutePath == "/x/web-interface/nav") return Task.FromResult(Json(Nav));
                    Assert.Equal("/x/web-interface/view", request.RequestUri!.AbsolutePath);
                    Assert.Contains("bvid=BV17x411w7KC", request.RequestUri.Query);
                    return Task.FromResult(Json(Video));
                }));
                var result = client.SearchAsync(input, null).GetAwaiter().GetResult();
                Assert.False(result.HasMore);
                Assert.Equal(input.StartsWith("https", StringComparison.Ordinal) ? 1 : 2, result.Episodes.Count);
                Assert.Equal(input.StartsWith("https", StringComparison.Ordinal) ? 12L : 11L, result.Episodes[0].EpisodeId);
                Assert.Equal("测试视频", result.Episodes[0].AnimeTitle);
            }
        });

        Test("B站：ep链接核对准确id，ss展开正片与特别篇，集数只选准确编号", () =>
        {
            foreach (var (input, episode, expected) in new[] { ("ep202", (string?)null, new long[] { 402 }), ("ss101", (string?)null, new long[] { 401, 402, 403 }), ("ss101", "2", new long[] { 402 }) })
            {
                using var client = Client(new Handler((request, _) =>
                {
                    if (request.RequestUri!.AbsolutePath == "/x/web-interface/nav") return Task.FromResult(Json(Nav));
                    Assert.Equal("/pgc/view/web/season", request.RequestUri!.AbsolutePath);
                    Assert.Contains(input.StartsWith("ep", StringComparison.Ordinal) ? "ep_id=202" : "season_id=101", request.RequestUri.Query);
                    return Task.FromResult(Json(Season));
                }));
                var result = client.SearchAsync(input, episode).GetAwaiter().GetResult();
                Assert.Equal(string.Join(',', expected), string.Join(',', result.Episodes.Select(item => item.EpisodeId)));
                Assert.Equal("测试番剧", result.Episodes[0].AnimeTitle);
            }
        });

        Test("B站：作品关键词搜索两类WBI再按season_id展开，分页标记保留", () =>
        {
            var types = new HashSet<string>();
            using var client = Client(new Handler((request, _) =>
            {
                if (request.RequestUri!.AbsolutePath == "/x/web-interface/nav") return Task.FromResult(Json(Nav));
                var uri = request.RequestUri!;
                if (uri.AbsolutePath == "/x/web-interface/nav") return Task.FromResult(Json(Nav));
                if (uri.AbsolutePath == "/pgc/view/web/season") return Task.FromResult(Json(Season));
                Assert.Equal("/x/web-interface/wbi/search/type", uri.AbsolutePath);
                Assert.Contains("keyword=", uri.Query);
                Assert.Contains("w_rid=", uri.Query);
                Assert.Contains("wts=", uri.Query);
                types.Add(uri.Query.Contains("media_bangumi", StringComparison.Ordinal) ? "media_bangumi" : "media_ft");
                return Task.FromResult(Json("""{"code":0,"data":{"page":1,"pagesize":20,"numResults":21,"numPages":2,"result":[{"season_id":101,"title":"<em class=\"keyword\">测试</em>番剧"}]}}"""));
            }));
            var result = client.SearchAsync("测试番剧", "2").GetAwaiter().GetResult();
            Assert.Equal(2, types.Count);
            Assert.True(result.HasMore);
            Assert.Equal(1, result.Episodes.Count);
            Assert.Equal(402L, result.Episodes[0].EpisodeId);
        });

        Test("B站：WBI中文空格与特殊字符按固定上游向量签名，长分享链接只取编号", () =>
        {
            using var nav = JsonDocument.Parse(Nav);
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var key = (string)typeof(BilibiliClient).GetMethod("WbiKey", flags)!.Invoke(null, [nav.RootElement.GetProperty("data")])!;
            Assert.Equal("klscrix6pok5wdj91hfn7jgt32odmcfn", key);
            var query = (string)typeof(BilibiliClient).GetMethod("SignSearch", flags)!.Invoke(null, ["测试 A !()*", "media_bangumi", key, 1700000000L])!;
            Assert.Equal("keyword=%E6%B5%8B%E8%AF%95+A+%21%28%29%2A&page=1&page_size=20&search_type=media_bangumi&web_location=1550101&wts=1700000000&w_rid=65ebde501ff2929a6e5aef5306f9397e", query);
            using var client = Client(new Handler((request, _) =>
            {
                if (request.RequestUri!.AbsolutePath == "/x/web-interface/nav") return Task.FromResult(Json(Nav));
                Assert.Equal("?bvid=BV17x411w7KC", request.RequestUri.Query);
                return Task.FromResult(Json(Video));
            }));
            var result = client.SearchAsync("https://www.bilibili.com/video/BV17x411w7KC/?share_source=" + new string('a', 400), null).GetAwaiter().GetResult();
            Assert.Equal(2, result.Episodes.Count);
        });

        Test("B站：用户取消保留取消异常，已取消令牌和过长输入零请求", () =>
        {
            using var cancel = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var client = Client(new Handler(async (_, token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Json(Nav);
            }));
            var pending = client.ValidateSessionAsync(Cookie, cancel.Token);
            started.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            cancel.Cancel();
            Assert.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => client.GetCommentsAsync(401, cancel.Token).GetAwaiter().GetResult());
            Assert.Throws<DandanPlayException>(() => client.SearchAsync(new string('字', 121), null).GetAwaiter().GetResult());
        });

        Test("B站：XML时间模式颜色转换并排序，实体解码与超长文本沿用过滤", () =>
        {
            const string xml = """<?xml version="1.0" encoding="UTF-8"?><i><chatid>401</chatid><maxlimit>1000</maxlimit><d p="12.5,1,25,16777215,1700000000,0,userhash,1">滚动&amp;测试</d><d p="1,5,25,255,1700000000,0,userhash,2">顶部测试</d><d p="2,4,25,16777215,1700000000,0,userhash,3">底部测试</d><d p="3,7,25,16777215,1700000000,0,userhash,4">高级测试</d><d p="NaN,1,25,16777215,1700000000,0,userhash,5">非法</d><d p="4,1,25,16777216,1700000000,0,userhash,6">非法</d><d p="5,1">损坏</d></i>""";
            using var client = Client(new Handler((request, _) =>
            {
                if (request.RequestUri!.AbsolutePath == "/x/web-interface/nav") return Task.FromResult(Json(Nav));
                Assert.Equal("https://comment.bilibili.com/401.xml", request.RequestUri!.AbsoluteUri);
                Assert.False(request.Headers.Contains("Cookie"));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "text/xml") });
            }));
            var comments = client.GetCommentsAsync(401).GetAwaiter().GetResult();
            Assert.Equal(3, comments.Count);
            Assert.Equal(new DanmakuComment(1, 5, 255, "顶部测试"), comments[0]);
            Assert.Equal(new DanmakuComment(12.5, 1, 16777215, "滚动&测试"), comments[2]);
        });

        Test("B站：XML禁止DTD，拒绝超大正文和重定向，错误不回显响应", () =>
        {
            foreach (var response in new[]
            {
                new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.example/fixture%2Csession") } },
                new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("fixture%2Csession") },
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<!DOCTYPE i [<!ENTITY e SYSTEM 'file:///fixture'>]><i><d p='1,1,25,255,1,0,hash,1'>&e;</d></i>") },
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[32 * 1024 * 1024 + 1]) }
            })
            {
                using var client = Client(new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/x/web-interface/nav" ? Json(Nav) : response)));
                var error = Assert.Catch<DandanPlayException>(() => client.GetCommentsAsync(401).GetAwaiter().GetResult());
                Assert.DoesNotContain("fixture", error.ToString());
                Assert.DoesNotContain("evil.example", error.ToString());
            }
        });

        Test("B站：非法编号、非官方URL与非法集数零请求", () =>
        {
            using var client = Client(new Handler((_, _) => throw new AssertionException("不应访问HTTP")));
            foreach (var input in new[] { "https://evil.example/BV17x411w7KC", "https://www.bilibili.com.evil.example/bangumi/play/ep202", "https://user@www.bilibili.com/bangumi/play/ep202", "http://www.bilibili.com/bangumi/play/ep202", "https://www.bilibili.com:444/bangumi/play/ep202", "https://www.bilibili.com/video/not-an-id", "https://www.bilibili.com/bangumi/play/ep0" })
                Assert.Throws<DandanPlayException>(() => client.SearchAsync(input, null).GetAwaiter().GetResult());
            Assert.Throws<DandanPlayException>(() => client.SearchAsync("测试", "0").GetAwaiter().GetResult());
            Assert.Throws<DandanPlayException>(() => client.GetCommentsAsync(0).GetAwaiter().GetResult());
        });

        Test("B站：作品定位和匿名XML下载前核对nav帐号，失效或错帐号不会继续取数据", () =>
        {
            foreach (var input in new[] { "BV17x411w7KC", "ep202", "测试番剧", "comments" })
            {
                foreach (var nav in new[] { """{"code":-101,"message":"fixture%2Csession"}""", """{"code":0,"data":{"isLogin":true,"mid":8,"uname":"另一个帐号"}}""" })
                {
                    using var client = Client(new Handler((request, _) =>
                    {
                        Assert.Equal("/x/web-interface/nav", request.RequestUri!.AbsolutePath);
                        return Task.FromResult(Json(nav));
                    }));
                    Assert.Throws<DandanPlayException>(() =>
                    {
                        if (input == "comments") client.GetCommentsAsync(401).GetAwaiter().GetResult();
                        else client.SearchAsync(input, null).GetAwaiter().GetResult();
                    });
                }
            }
        });

        Test("B站：来源关闭、未选B站、外部后端和会话损坏不发HTTP", () =>
        {
            foreach (var configure in new Action<AppSettings>[]
            {
                settings => settings.Danmaku.Enabled = false,
                settings => settings.Danmaku.Source = DanmakuSourceMode.DandanPlay,
                settings => settings.Mpv.Backend = MpvBackendKind.ExternalMpv,
                settings => settings.Danmaku.ProtectedBilibiliCookie = "invalid",
            })
            {
                var settings = Settings();
                configure(settings);
                using var client = new BilibiliClient(settings, PassthroughSecretProtector.Instance, new Handler((_, _) => throw new AssertionException("不应访问HTTP")));
                Assert.Throws<DandanPlayException>(() => client.SearchAsync("测试番剧", null).GetAwaiter().GetResult());
                Assert.Throws<DandanPlayException>(() => client.GetCommentsAsync(401).GetAwaiter().GetResult());
            }
        });

        Test("B站：XML最多接收四万条，正常空池成功，嵌套元素和非XML拒绝", () =>
        {
            var xml = new StringBuilder("<i>");
            for (var index = 0; index < 40001; index++) xml.Append("<d p='1,1,25,16777215,1,0,hash,1'>测试</d>");
            xml.Append("</i>");
            foreach (var (body, count) in new[] { (xml.ToString(), 40000), ("<i><chatid>401</chatid></i>", 0) })
            {
                using var client = Client(new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/x/web-interface/nav" ? Json(Nav) : Json(body))));
                Assert.Equal(count, client.GetCommentsAsync(401).GetAwaiter().GetResult().Count);
            }
            foreach (var body in new[] { "<html>fixture</html>", "<i><d p='1,1,25,255,1,0,hash,1'><span>fixture</span></d></i>" })
            {
                using var client = Client(new Handler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/x/web-interface/nav" ? Json(Nav) : Json(body))));
                var error = Assert.Catch<DandanPlayException>(() => client.GetCommentsAsync(401).GetAwaiter().GetResult());
                Assert.DoesNotContain("fixture", error.ToString());
            }
        });

        Test("B站：关闭取消HTTP与排队请求，排空前不释放handler且关闭后不接新请求", () =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var handler = new Handler(async (_, token) =>
            {
                started.SetResult();
                using var registration = token.Register(() => canceled.TrySetResult());
                await finish.Task;
                token.ThrowIfCancellationRequested();
                return Json(Nav);
            });
            using var client = Client(handler);
            var first = client.ValidateSessionAsync(Cookie);
            started.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            var queued = client.ValidateSessionAsync(Cookie);
            var closing = client.ShutdownAsync();
            canceled.Task.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            Assert.False(closing.IsCompleted);
            Assert.False(handler.Disposed);
            finish.SetResult();
            Assert.Throws<OperationCanceledException>(() => first.GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => queued.GetAwaiter().GetResult());
            closing.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            Assert.True(handler.Disposed);
            Assert.Throws<OperationCanceledException>(() => client.GetCommentsAsync(401).GetAwaiter().GetResult());
        });
    }

    private static BilibiliClient Client(HttpMessageHandler handler)
    {
        return new(Settings(), PassthroughSecretProtector.Instance, handler);
    }

    private static AppSettings Settings()
    {
        var settings = new AppSettings();
        settings.Danmaku.Enabled = true;
        settings.Danmaku.ProtectedBilibiliCookie = Cookie;
        settings.Danmaku.BilibiliUserId = 7;
        settings.Danmaku.Source = DanmakuSourceMode.Bilibili;
        return settings;
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal bool Disposed { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
