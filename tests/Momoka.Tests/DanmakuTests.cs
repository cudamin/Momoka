using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Emby;
using Momoka.Mpv;
using Momoka.Playback;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class DanmakuTests
{
    private const string Search = """{"success":true,"errorCode":0,"animes":[{"animeId":23,"animeTitle":"测试作品","episodes":[{"episodeId":230001,"episodeTitle":"第1话"},{"episodeId":230002,"episodeTitle":"第2话"}]}]}""";
    private const string Comments = """{"count":3,"comments":[{"p":"12.5,1,16777215,user","m":"滚动测试"},{"p":"1,5,255,user","m":"顶部测试"},{"p":"2,4,16777215,user","m":"底部测试"}]}""";

    internal static void Register()
    {
        Test("弹幕：旧设置无字段默认关闭，非法和非有限外观值归一化", () =>
        {
            var settings = SettingsMigration.Normalize(new AppSettings { Danmaku = null! });
            Assert.False(settings.Danmaku.Enabled);
            Assert.Equal("dav282msq5", settings.Danmaku.AppId);
            Assert.Equal("", settings.Danmaku.ProtectedAppSecret);
            Assert.True(settings.Danmaku.AutoMatch);
            settings.Danmaku.Opacity = double.NaN;
            settings.Danmaku.FontSize = double.PositiveInfinity;
            settings.Danmaku.Speed = -1;
            settings.Danmaku.Area = 200;
            settings.Danmaku.Density = 1000;
            settings.Danmaku.BlockedWords = new string('字', 4500);
            SettingsMigration.Normalize(settings);
            Assert.Equal(70d, settings.Danmaku.Opacity);
            Assert.Equal(36d, settings.Danmaku.FontSize);
            Assert.Equal(0.5, settings.Danmaku.Speed);
            Assert.Equal(100d, settings.Danmaku.Area);
            Assert.Equal(100, settings.Danmaku.Density);
            Assert.Equal(4000, settings.Danmaku.BlockedWords.Length);
        });

        Test("弹幕：密钥只存保护器产物，失败不覆盖旧密钥", () =>
        {
            var preferences = new DanmakuSettings();
            var credentials = new DandanPlayCredentials(new PrefixProtector());
            credentials.SetSecret(preferences, "  fixture-secret  ");
            Assert.Equal("cipher:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("fixture-secret")), preferences.ProtectedAppSecret);
            Assert.Equal("fixture-secret", credentials.GetSecret(preferences));
            var previous = preferences.ProtectedAppSecret;
            Assert.Throws<CryptographicException>(() => new DandanPlayCredentials(new BrokenProtector()).SetSecret(preferences, "other"));
            Assert.Equal(previous, preferences.ProtectedAppSecret);
            credentials.SetSecret(preferences, "");
            Assert.Equal("", preferences.ProtectedAppSecret);
        });

        Test("弹幕：跨用户或损坏的 DPAPI 密文回到重新输入，不泄露异常正文", () =>
        {
            var settings = new DanmakuSettings { ProtectedAppSecret = "broken" };
            Assert.Equal("", new DandanPlayCredentials(new BrokenProtector()).GetSecret(settings));
            Assert.Equal("", new DandanPlayCredentials(new PrefixProtector()).GetSecret(settings));
        });

        Test("弹幕：备份只带外观，连接与密钥不导出、不覆盖、不换对象", () =>
        {
            var live = new AppSettings();
            live.Danmaku.Enabled = true;
            live.Danmaku.AppId = "fixture-app";
            live.Danmaku.ProtectedAppSecret = "fixture-protected-secret";
            live.Danmaku.Opacity = 35;
            live.Danmaku.BlockedWords = "测试,spoiler";
            var reference = live.Danmaku;
            var backup = SettingsPreferences.ToBackupDocument(live);
            var json = JsonSerializer.Serialize(backup);
            Assert.DoesNotContain("fixture-app", json);
            Assert.DoesNotContain("fixture-protected-secret", json);
            Assert.False(backup.Danmaku.Enabled);
            Assert.Equal("", backup.Danmaku.AppId);
            Assert.Equal(35d, backup.Danmaku.Opacity);
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.True(ReferenceEquals(reference, live.Danmaku));
            Assert.True(live.Danmaku.Enabled);
            Assert.Equal("fixture-app", live.Danmaku.AppId);
            Assert.Equal("fixture-protected-secret", live.Danmaku.ProtectedAppSecret);
            Assert.Equal(70d, live.Danmaku.Opacity);
        });

        Test("弹幕：内置两管线可用，外部播放器与未配置服务不可用且不清偏好", () =>
        {
            var settings = new AppSettings();
            Assert.False(DandanPlayMatching.IsConfigured(settings));
            settings.Danmaku.Enabled = true;
            settings.Danmaku.AppId = "fixture-app";
            settings.Danmaku.ProtectedAppSecret = "fixture-secret";
            foreach (var pipeline in new[] { VideoPipelineKind.Integrated, VideoPipelineKind.Standalone })
            {
                settings.Mpv.Pipeline = pipeline;
                Assert.True(DandanPlayMatching.IsConfigured(settings));
            }
            settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
            Assert.False(DandanPlayMatching.IsConfigured(settings));
            Assert.Equal("fixture-secret", settings.Danmaku.ProtectedAppSecret);
        });

        Test("弹幕：参数只认有限时间、三种普通模式、24位颜色与限长文本", () =>
        {
            Assert.Equal(new DanmakuComment(1.5, 1, 0xFFFFFF, "你好"), DanmakuComments.Parse("1.5,1,16777215,user", "你好"));
            foreach (var value in new[] { "NaN,1,0,u", "Infinity,1,0,u", "-1,1,0,u", "86401,1,0,u", "1,7,0,u", "1,1,-1,u", "1,1,16777216,u", "bad", "1,1,x,u" })
                Assert.Null(DanmakuComments.Parse(value, "文字"), value);
            Assert.Null(DanmakuComments.Parse("1,1,0,u", "\r\n"));
            Assert.Equal(300, DanmakuComments.Parse("1,1,0,u", new string('字', 500))!.Text.Length);
            Assert.Equal("abc", DanmakuComments.Parse("1,1,0,u", "a\0b\nc")!.Text);
        });

        Test("弹幕：屏蔽词、颜色、模式、重复合并与时间排序共用一套规则", () =>
        {
            DanmakuComment C(double time, string text, int mode = 1, int color = 0xFFFFFF) => new(time, mode, color, text);
            var settings = new DanmakuSettings { BlockedWords = "剧透，SPOILER\n广告", Top = false, Bottom = false, Color = false };
            var comments = new[] { C(10, "正常"), C(1, "正常"), C(2, "spoiler!"), C(3, "剧透一行"), C(4, "顶", 5), C(5, "底", 4), C(6, "彩色", color: 1), C(11, "正常"), C(12, "另一句") };
            var result = DanmakuComments.Filter(comments, settings);
            Assert.Equal("1,11,12", string.Join(',', result.Select(comment => comment.Time)));
            settings.Deduplicate = false;
            Assert.Equal(4, DanmakuComments.Filter(comments, settings).Count);
            settings.Scroll = false;
            Assert.Equal(0, DanmakuComments.Filter(comments, settings).Count);
        });

        Test("弹幕：解析和筛选保留40,000条上限", () =>
        {
            var comments = Enumerable.Range(0, 41000).Select(index => new DanmakuComment(index, 1, 0xFFFFFF, $"评论{index}"));
            Assert.Equal(40000, DanmakuComments.Filter(comments, new()).Count);
        });

        Test("弹幕：唯一精确标题可自动选中，歧义、续季和特别篇必须手选", () =>
        {
            var item = new EmbyItem { Type = EmbyItemType.Episode, SeriesName = "测试作品", Name = "第1集", IndexNumber = 1, ParentIndexNumber = 1 };
            var episode = new DandanPlayEpisode(230001, "测试作品", "第1话");
            Assert.Equal("测试作品", DandanPlayMatching.Title(item));
            Assert.Equal("1", DandanPlayMatching.EpisodeNumber(item));
            Assert.Equal(episode, DandanPlayMatching.UniqueMatch(item, [episode]));
            Assert.Null(DandanPlayMatching.UniqueMatch(item, [episode, episode with { EpisodeId = 230002 }]));
            Assert.Null(DandanPlayMatching.UniqueMatch(item, [episode with { AnimeTitle = "测试作品第二季" }]));
            item.ParentIndexNumber = 2;
            Assert.Null(DandanPlayMatching.UniqueMatch(item, [episode]));
            item.ParentIndexNumber = 0;
            Assert.Null(DandanPlayMatching.UniqueMatch(item, [episode]));
            Assert.Null(DandanPlayMatching.EpisodeNumber(item));
        });

        Test("弹弹play：签名按官方串联顺序，不含查询参数", () =>
        {
            var actual = DandanPlayClient.Signature("fixture-app", "fixture-secret", 1735660800, "/api/v2/comment/123450001");
            var expected = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("fixture-app1735660800/api/v2/comment/123450001fixture-secret")));
            Assert.Equal(expected, actual);
        });

        Test("弹弹play：关闭或缺少密钥零HTTP请求", () => InFixture(fixture =>
        {
            fixture.Settings.Danmaku.Enabled = false;
            Assert.Throws<DandanPlayException>(() => fixture.Client.SearchAsync("测试", null).GetAwaiter().GetResult());
            fixture.Settings.Danmaku.Enabled = true;
            fixture.Settings.Danmaku.ProtectedAppSecret = "";
            Assert.Throws<DandanPlayException>(() => fixture.Client.GetCommentsAsync(1, true).GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Transport.Requests.Count);
        }));

        Test("弹弹play：搜索路径编码、集数、签名与平展剧集都按真实规格", () => InFixture(fixture =>
        {
            fixture.Transport.Response = _ => Reply(Search);
            var episodes = fixture.Client.SearchAsync("测试 / a&b", "2").GetAwaiter().GetResult();
            Assert.Equal(2, episodes.Count);
            Assert.Equal(230001L, episodes[0].EpisodeId);
            Assert.Equal("测试作品 · 第1话", episodes[0].Label);
            var request = fixture.Transport.Requests.Single();
            Assert.Equal("/api/v2/search/episodes", request.Uri.AbsolutePath);
            Assert.Contains(Uri.EscapeDataString("测试 / a&b"), request.Uri.Query);
            Assert.Contains("&episode=2", request.Uri.Query);
            Assert.Equal("fixture-app", request.Headers["X-AppId"]);
            Assert.Equal(DandanPlayClient.Signature("fixture-app", "fixture-secret", fixture.Now.ToUnixTimeSeconds(), request.Uri.AbsolutePath), request.Headers["X-Signature"]);
            Assert.False(request.Headers.ContainsKey("X-AppSecret"));
            Assert.False(request.Headers.ContainsKey("Authorization"));
            Assert.DoesNotContain("fixture-secret", request.Uri.AbsoluteUri);
        }));

        Test("弹弹play：非法关键词或集数在发请求前拒绝", () => InFixture(fixture =>
        {
            foreach (var title in new[] { "", "a", new string('a', 121) })
                Assert.Throws<DandanPlayException>(() => fixture.Client.SearchAsync(title, null).GetAwaiter().GetResult());
            foreach (var episode in new[] { "0", "-1", "a", "1&token=x", "1234567" })
                Assert.Throws<DandanPlayException>(() => fixture.Client.SearchAsync("测试", episode).GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Transport.Requests.Count);
        }));

        Test("弹弹play：官方CDN跳转不传鉴权头，返回弹幕排序且含关联参数", () => InFixture(fixture =>
        {
            fixture.Transport.Response = request => request.Uri.Host == "api.dandanplay.net"
                ? Redirect("https://comment.dandanplay.net/fixture.json") : Reply(Comments);
            var comments = fixture.Client.GetCommentsAsync(230001, true).GetAwaiter().GetResult();
            Assert.Equal(3, comments.Count);
            Assert.Equal(1d, comments[0].Time);
            Assert.Contains("withRelated=true", fixture.Transport.Requests[0].Uri.Query);
            var second = fixture.Transport.Requests[1];
            Assert.False(second.Headers.ContainsKey("X-AppId"));
            Assert.False(second.Headers.ContainsKey("X-Timestamp"));
            Assert.False(second.Headers.ContainsKey("X-Signature"));
            Assert.False(second.Headers.ContainsKey("X-AppSecret"));
        }));

        Test("弹弹play：同源跳转重新按目标路径签名", () => InFixture(fixture =>
        {
            fixture.Transport.Response = request => request.Uri.AbsolutePath.StartsWith("/api/v2/", StringComparison.Ordinal)
                ? Redirect("/cache/fixture.json") : Reply(Comments);
            fixture.Client.GetCommentsAsync(230001, false).GetAwaiter().GetResult();
            var request = fixture.Transport.Requests[1];
            Assert.Equal(DandanPlayClient.Signature("fixture-app", "fixture-secret", fixture.Now.ToUnixTimeSeconds(), "/cache/fixture.json"), request.Headers["X-Signature"]);
        }));

        Test("弹弹play：阻止非官方、HTTP、用户名、非标准端口与相似域名跳转", () =>
        {
            foreach (var url in new[] { "http://comment.dandanplay.net/1", "https://dandanplay.net.evil.test/1", "https://evil.test/1", "https://user@api.dandanplay.net/1", "https://api.dandanplay.net:8443/1", "https://127.0.0.1/1" })
            {
                Assert.False(DandanPlayClient.SafeRedirect(new Uri(url)), url);
                InFixture(fixture =>
                {
                    fixture.Transport.Response = _ => Redirect(url);
                    Assert.Throws<DandanPlayException>(() => fixture.Client.GetCommentsAsync(1, true).GetAwaiter().GetResult());
                    Assert.Equal(1, fixture.Transport.Requests.Count);
                });
            }
        });

        Test("弹弹play：搜索不跟随跳转，弹幕跳转最多三次", () => InFixture(fixture =>
        {
            fixture.Transport.Response = _ => Redirect("https://comment.dandanplay.net/loop");
            Assert.Throws<DandanPlayException>(() => fixture.Client.SearchAsync("测试", null).GetAwaiter().GetResult());
            Assert.Equal(1, fixture.Transport.Requests.Count);
            fixture.Transport.Requests.Clear();
            Assert.Throws<DandanPlayException>(() => fixture.Client.GetCommentsAsync(1, true).GetAwaiter().GetResult());
            Assert.Equal(4, fixture.Transport.Requests.Count);
        }));

        Test("弹弹play：同关键词与弹幕库缓存两小时，过期与换凭据必须重取", () => InFixture(fixture =>
        {
            fixture.Transport.Response = request => Reply(request.Uri.AbsolutePath.Contains("search", StringComparison.Ordinal) ? Search : Comments);
            fixture.Client.SearchAsync("测试", "1").GetAwaiter().GetResult();
            fixture.Client.SearchAsync("测试", "1").GetAwaiter().GetResult();
            fixture.Client.GetCommentsAsync(230001, true).GetAwaiter().GetResult();
            fixture.Client.GetCommentsAsync(230001, true).GetAwaiter().GetResult();
            Assert.Equal(2, fixture.Transport.Requests.Count);
            fixture.Now = fixture.Now.AddHours(3);
            fixture.Client.SearchAsync("测试", "1").GetAwaiter().GetResult();
            Assert.Equal(3, fixture.Transport.Requests.Count);
            fixture.Settings.Danmaku.AppId = "other-fixture-app";
            fixture.Client.SearchAsync("测试", "1").GetAwaiter().GetResult();
            Assert.Equal(4, fixture.Transport.Requests.Count);
        }));

        Test("弹弹play：缓存损坏重取，磁盘结果数量有界且不含明文密钥", () => InFixture(fixture =>
        {
            fixture.Transport.Response = _ => Reply(Search);
            fixture.Client.SearchAsync("测试", "1").GetAwaiter().GetResult();
            var file = Directory.GetFiles(fixture.Cache).Single();
            Assert.DoesNotContain("fixture-secret", file);
            Assert.DoesNotContain("fixture-secret", File.ReadAllText(file));
            File.WriteAllText(file, "broken-json");
            File.SetLastWriteTimeUtc(file, fixture.Now.UtcDateTime);
            fixture.Client.SearchAsync("测试", "1").GetAwaiter().GetResult();
            Assert.Equal(2, fixture.Transport.Requests.Count);
            for (var index = 0; index < 20; index++) fixture.Client.SearchAsync($"测试{index}", null).GetAwaiter().GetResult();
            Assert.True(Directory.GetFiles(fixture.Cache).Length <= 16);
        }));

        Test("弹弹play：401、403、限额、业务错误与非法JSON不回显正文、不缓存失败", () => InFixture(fixture =>
        {
            foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.BadGateway })
            {
                fixture.Transport.Response = _ => Reply("fixture-secret", status);
                var error = Assert.Catch<DandanPlayException>(() => fixture.Client.SearchAsync("测试", null).GetAwaiter().GetResult());
                Assert.DoesNotContain("fixture-secret", error.Message);
            }
            foreach (var body in new[] { "invalid-json", "[]", """{"success":false,"errorMessage":"fixture-secret","animes":[]}""", """{"errorCode":42,"animes":[]}""" })
            {
                fixture.Transport.Response = _ => Reply(body);
                var error = Assert.Catch<DandanPlayException>(() => fixture.Client.SearchAsync("测试", null).GetAwaiter().GetResult());
                Assert.DoesNotContain("fixture-secret", error.Message);
            }
            Assert.False(Directory.Exists(fixture.Cache));
        }));

        Test("弹弹play：限长读取在超大声明前停止", () => InFixture(fixture =>
        {
            fixture.Transport.Response = _ =>
            {
                var response = Reply(Comments);
                response.Content.Headers.ContentLength = DandanPlayClient.MaximumResponseBytes + 1L;
                return response;
            };
            Assert.Throws<DandanPlayException>(() => fixture.Client.GetCommentsAsync(1, true).GetAwaiter().GetResult());
        }));

        Test("弹弹play：用户取消仍抛取消，不被翻译成网络错误", () => InFixture(fixture =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => fixture.Client.SearchAsync("测试", null, cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Transport.Requests.Count);
        }));

        Test("弹幕：加载收据有文件序号、传输序号与条数，协议拒绝非法值", () =>
        {
            Assert.Equal(MpvDanmaku.Ready, VideoWindowContract.Parse([MpvDanmaku.Ready, "2"])?.Key);
            Assert.Equal(MpvDanmaku.Loaded, VideoWindowContract.Parse([MpvDanmaku.Loaded, "2:42:100"])?.Key);
            Assert.Null(VideoWindowContract.Parse([MpvDanmaku.Ready, "0"]));
            Assert.Null(VideoWindowContract.Parse([MpvDanmaku.Loaded, "2:-1:100"]));
            Assert.Null(VideoWindowContract.Parse([MpvDanmaku.Loaded, "2:42:x"]));
            Assert.Equal(MpvDanmaku.Menu, VideoWindowContract.Parse([MpvDanmaku.Menu, "open"])?.Key);
            Assert.Null(VideoWindowContract.Parse([MpvDanmaku.Menu, "anything"]));
        });

        Test("新增快捷键：旧用户自定义绑定优先，新增默认键不抢旧键", () =>
        {
            var bindings = new Dictionary<string, string> { ["toggle-mute"] = "D" };
            var resolved = ShortcutCatalog.Resolve(bindings);
            Assert.Equal("toggle-mute", ShortcutCatalog.Lookup(bindings, new("D", false, false, false)));
            Assert.True(resolved["toggle-danmaku"].IsEmpty);
            Assert.Equal(ShortcutCatalog.Actions.Count, resolved.Count);
            var rebound = ShortcutCatalog.Rebind(bindings, "toggle-danmaku", new("K", false, false, false));
            Assert.Null(rebound.Conflict);
            Assert.Equal("toggle-danmaku", ShortcutCatalog.Lookup(rebound.Bindings, new("K", false, false, false)));
            Assert.Equal("toggle-mute", ShortcutCatalog.Lookup(rebound.Bindings, new("D", false, false, false)));
        });

        Test("新增快捷键：两管线共用截图、轨道、逐帧与统计命令", () =>
        {
            Assert.Equal("screenshot,scaled+subtitles", string.Join(',', AdditionalShortcuts.Command("screenshot")!));
            Assert.Equal("cycle,sid,down", string.Join(',', AdditionalShortcuts.Command("subtitle-previous")!));
            Assert.Equal("cycle,aid", string.Join(',', AdditionalShortcuts.Command("audio-next")!));
            Assert.Equal("frame-back-step", AdditionalShortcuts.Command("frame-backward")![0]);
            Assert.Equal("seek,0,absolute+exact", string.Join(',', AdditionalShortcuts.Command("seek-start")!));
            Assert.Null(AdditionalShortcuts.Command("toggle-danmaku"));
            var native = VideoWindowShortcuts.Bindings(new Dictionary<string, string>());
            Assert.Equal("screenshot", native["s"]);
            Assert.Equal("frame-backward", native["Shift+e"]);
            Assert.Equal("toggle-danmaku", native["d"]);
            Assert.Equal("show-playback-info", native["Ctrl+i"]);
        });
    }

    private static HttpResponseMessage Reply(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Redirect(string url)
    {
        var result = Reply("", HttpStatusCode.Redirect);
        result.Headers.Location = new Uri(url, UriKind.RelativeOrAbsolute);
        return result;
    }

    private static void InFixture(Action<Fixture> action) { using var fixture = new Fixture(); action(fixture); }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "momoka-danmaku-tests-" + Guid.NewGuid().ToString("N"));
        public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        public AppSettings Settings { get; } = new();
        public Transport Transport { get; } = new();
        public string Cache => Path.Combine(_root, "cache");
        public DandanPlayClient Client { get; }

        public Fixture()
        {
            Settings.Danmaku.Enabled = true;
            Settings.Danmaku.AppId = "fixture-app";
            Settings.Danmaku.ProtectedAppSecret = "fixture-secret";
            Client = new(Settings, new(PassthroughSecretProtector.Instance), Cache, Transport, () => Now);
        }

        public void Dispose()
        {
            Client.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private sealed record Request(Uri Uri, IReadOnlyDictionary<string, string> Headers);

    private sealed class Transport : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];
        public Func<Request, HttpResponseMessage> Response { get; set; } = _ => Reply(Search);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = new Request(request.RequestUri!, request.Headers.ToDictionary(pair => pair.Key, pair => string.Join(',', pair.Value), StringComparer.OrdinalIgnoreCase));
            Requests.Add(snapshot);
            return Task.FromResult(Response(snapshot));
        }
    }

    private sealed class PrefixProtector : ISecretProtector
    {
        public string Protect(string plainText) => "cipher:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
        public string Unprotect(string cipherText) => Encoding.UTF8.GetString(Convert.FromBase64String(cipherText[7..]));
    }

    private sealed class BrokenProtector : ISecretProtector
    {
        public string Protect(string plainText) => throw new CryptographicException("fixture-secret");
        public string Unprotect(string cipherText) => throw new CryptographicException("fixture-secret");
    }
}
