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
            Assert.Equal(DanmakuCredentialSource.BuiltIn, settings.Danmaku.CredentialSource);
            Assert.Equal("dav282msq5", settings.Danmaku.AppId);
            Assert.Equal("", settings.Danmaku.ProtectedAppSecret);
            Assert.True(settings.Danmaku.AutoMatch);
            Assert.True(settings.Danmaku.ShowInput);
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
            live.Danmaku.CredentialSource = DanmakuCredentialSource.Custom;
            live.Danmaku.AppId = "fixture-app";
            live.Danmaku.ProtectedAppSecret = "fixture-protected-secret";
            live.Danmaku.Opacity = 35;
            live.Danmaku.ShowInput = false;
            live.Danmaku.BlockedWords = "测试,spoiler";
            var reference = live.Danmaku;
            var backup = SettingsPreferences.ToBackupDocument(live);
            var json = JsonSerializer.Serialize(backup);
            Assert.DoesNotContain("fixture-app", json);
            Assert.DoesNotContain("fixture-protected-secret", json);
            Assert.False(backup.Danmaku.Enabled);
            Assert.Equal("", backup.Danmaku.AppId);
            Assert.Equal(DanmakuCredentialSource.Auto, backup.Danmaku.CredentialSource);
            Assert.Equal(35d, backup.Danmaku.Opacity);
            Assert.False(backup.Danmaku.ShowInput);
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.True(ReferenceEquals(reference, live.Danmaku));
            Assert.True(live.Danmaku.Enabled);
            Assert.Equal(DanmakuCredentialSource.Custom, live.Danmaku.CredentialSource);
            Assert.Equal("fixture-app", live.Danmaku.AppId);
            Assert.Equal("fixture-protected-secret", live.Danmaku.ProtectedAppSecret);
            Assert.Equal(70d, live.Danmaku.Opacity);
            Assert.True(live.Danmaku.ShowInput);
        });

        Test("弹幕：内置来源无需本机密钥，自定义来源必须齐备且不清偏好", () =>
        {
            var settings = new AppSettings();
            Assert.False(DandanPlayMatching.IsConfigured(settings));
            settings.Danmaku.Enabled = true;
            settings.Danmaku.CredentialSource = DanmakuCredentialSource.BuiltIn;
            foreach (var pipeline in new[] { VideoPipelineKind.Integrated, VideoPipelineKind.Standalone })
            {
                settings.Mpv.Pipeline = pipeline;
                Assert.True(DandanPlayMatching.IsConfigured(settings));
            }

            settings.Danmaku.CredentialSource = DanmakuCredentialSource.Custom;
            settings.Danmaku.AppId = "fixture-app";
            settings.Danmaku.ProtectedAppSecret = "";
            Assert.False(DandanPlayMatching.IsConfigured(settings));
            settings.Danmaku.ProtectedAppSecret = "fixture-secret";
            Assert.True(DandanPlayMatching.IsConfigured(settings));

            settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
            Assert.False(DandanPlayMatching.IsConfigured(settings));
            Assert.Equal("fixture-secret", settings.Danmaku.ProtectedAppSecret);
        });

        Test("弹幕：旧配置按本机密钥落到自定义，否则落到内置", () =>
        {
            var legacy = new AppSettings();
            legacy.Danmaku.ProtectedAppSecret = "fixture-secret";
            SettingsMigration.Normalize(legacy);
            Assert.Equal(DanmakuCredentialSource.Custom, legacy.Danmaku.CredentialSource);

            var fresh = SettingsMigration.Normalize(new AppSettings());
            Assert.Equal(DanmakuCredentialSource.BuiltIn, fresh.Danmaku.CredentialSource);

            var invalid = new AppSettings();
            invalid.Danmaku.CredentialSource = (DanmakuCredentialSource)99;
            SettingsMigration.Normalize(invalid);
            Assert.Equal(DanmakuCredentialSource.BuiltIn, invalid.Danmaku.CredentialSource);
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
            var search = fixture.Client.SearchAsync("测试 / a&b", "2").GetAwaiter().GetResult();
            Assert.False(search.HasMore);
            var episodes = search.Episodes;
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

        Test("弹弹play：官方截断或本地上限都不把不完整候选认作唯一", () => InFixture(fixture =>
        {
            fixture.Transport.Response = _ => Reply("""{"success":true,"hasMore":true,"animes":[{"animeTitle":"测试作品","episodes":[{"episodeId":230001,"episodeTitle":"第1话"}]}]}""");
            var search = fixture.Client.SearchAsync("测试作品", "1").GetAwaiter().GetResult();
            Assert.True(search.HasMore);
            Assert.Equal(1, search.Episodes.Count);
            var item = new EmbyItem { Type = EmbyItemType.Movie, Name = "测试作品" };
            Assert.Null(DandanPlayMatching.UniqueMatch(item, search.Episodes, search.HasMore));
            Assert.True(fixture.Client.SearchAsync("测试作品", "1").GetAwaiter().GetResult().HasMore);
            Assert.Equal(1, fixture.Transport.Requests.Count);
            foreach (var count in new[] { 1000, 1001 })
            {
                fixture.Transport.Response = _ => Reply(JsonSerializer.Serialize(new
                {
                    hasMore = false,
                    animes = new[] { new { animeTitle = "测试作品", episodes = Enumerable.Range(1, count).Select(id => new { episodeId = id, episodeTitle = "第1话" }) } },
                }));
                search = fixture.Client.SearchAsync($"测试{count}", null).GetAwaiter().GetResult();
                Assert.Equal(1000, search.Episodes.Count);
                Assert.Equal(count > 1000, search.HasMore);
            }
        }));

        Test("弹弹play：官方鉴权错误只映射白名单，未知头不会回显", () => InFixture(fixture =>
        {
            foreach (var pair in new[]
            {
                ("Invalid Timestamp", "Windows 时间"), ("Invalid AppId", "AppId 或对应密钥"),
                ("Invalid Signature", "密钥或签名"), ("Invalid AppSecret", "密钥或签名"),
                ("Missing Authentication Headers", "请求缺少应用认证头"), ("fixture-secret", "应用授权和系统时间"),
            })
            {
                fixture.Transport.Response = _ =>
                {
                    var response = Reply("fixture-secret", HttpStatusCode.Forbidden);
                    response.Headers.Add("X-Error-Message", pair.Item1);
                    return response;
                };
                var error = Assert.Catch<DandanPlayException>(() => fixture.Client.SearchAsync("测试", null).GetAwaiter().GetResult());
                Assert.Contains(pair.Item2, error.Message);
                Assert.DoesNotContain("fixture-secret", error.Message);
            }
        }));

        Test("弹弹play：并发重复请求复用缓存，搜索和弹幕各只发一次", () => InFixture(fixture =>
        {
            fixture.Transport.Response = request => Reply(request.Uri.AbsolutePath.Contains("search", StringComparison.Ordinal) ? Search : Comments);
            using var ready = new CountdownEvent(8);
            using var release = new ManualResetEventSlim();
            var requests = Enumerable.Range(0, 8).Select(index => Task.Factory.StartNew(() =>
            {
                ready.Signal();
                release.Wait();
                if (index % 2 == 0) fixture.Client.SearchAsync("测试作品", "1").GetAwaiter().GetResult();
                else fixture.Client.GetCommentsAsync(230001, true).GetAwaiter().GetResult();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            try { Assert.True(ready.Wait(TimeSpan.FromSeconds(10))); }
            finally { release.Set(); }
            Task.WhenAll(requests).GetAwaiter().GetResult();
            Assert.Equal(2, fixture.Transport.Requests.Count);
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

        Test("弹弹play：无本机密钥时取内置凭据并签名，密钥不落明文", () => InBuiltInFixture(fixture =>
        {
            fixture.Transport.Response = request => request.Uri.Host == "credential.test"
                ? CredentialReply("builtin-app", "builtin-secret", fixture.Now) : Reply(Search);
            var search = fixture.Client.SearchAsync("测试", "1").GetAwaiter().GetResult();
            Assert.Equal(2, search.Episodes.Count);
            Assert.Equal(2, fixture.Transport.Requests.Count);
            Assert.Equal("credential.test", fixture.Transport.Requests[0].Uri.Host);
            var request = fixture.Transport.Requests[1];
            Assert.Equal("builtin-app", request.Headers["X-AppId"]);
            Assert.Equal(DandanPlayClient.Signature("builtin-app", "builtin-secret",
                fixture.Now.ToUnixTimeSeconds(), request.Uri.AbsolutePath), request.Headers["X-Signature"]);
            Assert.DoesNotContain("builtin-secret", request.Uri.AbsoluteUri);
            var credentialCache = Path.Combine(Path.GetDirectoryName(fixture.Cache)!, "builtin-credential.bin");
            Assert.True(File.Exists(credentialCache));
            Assert.DoesNotContain("builtin-secret", File.ReadAllText(credentialCache));
            Assert.DoesNotContain("builtin-secret", string.Join('\n', Directory.GetFiles(fixture.Cache).Select(Path.GetFileName)));
        }));

        Test("弹弹play：Worker 生成的密文能被客户端解密并使用", () => InBuiltInFixture(fixture =>
        {
            const long exp = 1_800_000_000;
            const string data = "83OQe7wrl+TN/WXfNXPCIDZ8wyHNtJoXD7Xanw3DVe/4iwz6UMo4HSVejwdiKk3HD0Gs3vo35V8A1s7IirYDFQ6nu3kvchzE9V5V/oHYBU1TrLpX3thJMMrSdDEBkVGEZw==";
            fixture.Now = DateTimeOffset.FromUnixTimeSeconds(exp - 3600);
            fixture.Transport.Response = request => request.Uri.Host == "credential.test"
                ? Reply(JsonSerializer.Serialize(new { v = 1, kid = "interop-kid", exp, data }))
                : Reply(Search);
            fixture.Client.SearchAsync("测试", null).GetAwaiter().GetResult();
            var api = fixture.Transport.Requests.Last();
            Assert.Equal("interop-app", api.Headers["X-AppId"]);
            Assert.Equal(DandanPlayClient.Signature("interop-app", "interop-secret",
                fixture.Now.ToUnixTimeSeconds(), api.Uri.AbsolutePath), api.Headers["X-Signature"]);
        }));

        Test("弹弹play：内置凭据缓存到期前不重取，到期后重取", () => InBuiltInFixture(fixture =>
        {
            var credentials = 0;
            fixture.Transport.Response = request =>
            {
                if (request.Uri.Host != "credential.test") return Reply(Search);
                credentials++;
                return CredentialReply("builtin-app", "builtin-secret", fixture.Now);
            };
            fixture.Client.SearchAsync("测试", "1").GetAwaiter().GetResult();
            fixture.Client.SearchAsync("测试", "1").GetAwaiter().GetResult();
            Assert.Equal(1, credentials);
            Assert.Equal(2, fixture.Transport.Requests.Count);
            fixture.Now = fixture.Now.AddDays(8);
            fixture.Client.SearchAsync("测试", "1").GetAwaiter().GetResult();
            Assert.Equal(2, credentials);
            Assert.Equal(4, fixture.Transport.Requests.Count);
        }));

        Test("弹弹play：内置凭据到期但端点失败时使用未过期缓存", () => InBuiltInFixture(fixture =>
        {
            var credentials = 0;
            fixture.Transport.Response = request =>
            {
                if (request.Uri.Host != "credential.test") return Reply(Search);
                credentials++;
                return credentials == 1
                    ? CredentialReply("builtin-app", "builtin-secret", fixture.Now,
                        expires: fixture.Now.AddHours(2).ToUnixTimeSeconds())
                    : Reply("down", HttpStatusCode.BadGateway);
            };
            fixture.Client.SearchAsync("测试", "1").GetAwaiter().GetResult();
            fixture.Now = fixture.Now.AddHours(1).AddMinutes(30);
            fixture.Client.SearchAsync("其他", "1").GetAwaiter().GetResult();
            Assert.Equal(2, credentials);
            Assert.Equal(4, fixture.Transport.Requests.Count);
        }));

        Test("弹弹play：内置凭据 401 换一份再试一次，发送不自动重试", () => InBuiltInFixture(fixture =>
        {
            var credentials = 0;
            var apiCalls = 0;
            fixture.Transport.Response = request =>
            {
                if (request.Uri.Host == "credential.test")
                {
                    credentials++;
                    return CredentialReply("builtin-app", credentials == 1 ? "secret-one" : "secret-two", fixture.Now);
                }
                apiCalls++;
                return apiCalls == 1 ? Reply("denied", HttpStatusCode.Unauthorized) : Reply(Search);
            };
            fixture.Client.SearchAsync("测试", "1").GetAwaiter().GetResult();
            Assert.Equal(2, credentials);
            Assert.Equal(2, apiCalls);
            var second = fixture.Transport.Requests.Last();
            Assert.Equal(DandanPlayClient.Signature("builtin-app", "secret-two",
                fixture.Now.ToUnixTimeSeconds(), second.Uri.AbsolutePath), second.Headers["X-Signature"]);

            fixture.Transport.Requests.Clear();
            var posts = 0;
            fixture.Transport.Response = request =>
            {
                if (request.Uri.Host == "credential.test")
                    return CredentialReply("builtin-app", "secret-three", fixture.Now);
                posts++;
                return Reply("denied", HttpStatusCode.Unauthorized);
            };
            Assert.Throws<DandanPlayException>(() => fixture.Client.SendCommentAsync(230001, 1, "测试").GetAwaiter().GetResult());
            Assert.Equal(1, posts);
        }));

        Test("弹弹play：内置凭据端点失败时不请求弹幕接口", () => InBuiltInFixture(fixture =>
        {
            fixture.Transport.Response = request => request.Uri.Host == "credential.test"
                ? Reply("missing", HttpStatusCode.NotFound) : Reply(Search);
            var error = Assert.Catch<DandanPlayException>(() => fixture.Client.SearchAsync("测试", null).GetAwaiter().GetResult());
            Assert.Contains("内置弹幕凭据", error.Message);
            Assert.Equal(1, fixture.Transport.Requests.Count);
            Assert.Equal("credential.test", fixture.Transport.Requests[0].Uri.Host);
        }));

        Test("弹弹play：内置凭据错误密钥与过期都被拒绝，自定义凭据不访问内置端点", () => InBuiltInFixture(fixture =>
        {
            fixture.Transport.Response = _ => CredentialReply("builtin-app", "builtin-secret", fixture.Now,
                key: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
            var error = Assert.Catch<DandanPlayException>(() => fixture.Client.SearchAsync("测试", null).GetAwaiter().GetResult());
            Assert.Contains("无法解密", error.Message);
            Assert.Equal(1, fixture.Transport.Requests.Count);

            fixture.Transport.Requests.Clear();
            fixture.BuiltIn!.Invalidate();
            fixture.Transport.Response = _ => CredentialReply("builtin-app", "builtin-secret", fixture.Now,
                expires: fixture.Now.AddMinutes(-1).ToUnixTimeSeconds());
            error = Assert.Catch<DandanPlayException>(() => fixture.Client.SearchAsync("测试", null).GetAwaiter().GetResult());
            Assert.Contains("过期", error.Message);

            using var custom = new Fixture();
            custom.Transport.Response = _ => Reply(Search);
            custom.Client.SearchAsync("测试", null).GetAwaiter().GetResult();
            Assert.False(custom.Transport.Requests.Any(request => request.Uri.Host == "credential.test"));
        }));

        Test("弹弹play：应用发送使用签名POST及官方字段，确认后清除两种关联缓存", () => InFixture(fixture =>
        {
            fixture.Transport.Response = request => Reply(request.Method == HttpMethod.Post ? """{"success":true,"errorCode":0,"cid":1234567890123}""" : Comments);
            fixture.Client.GetCommentsAsync(230001, false).GetAwaiter().GetResult();
            fixture.Client.GetCommentsAsync(230001, true).GetAwaiter().GetResult();
            var cid = fixture.Client.SendCommentAsync(230001, 12.5, " 中文测试 ").GetAwaiter().GetResult();
            Assert.Equal(1234567890123L, cid);
            Assert.Equal(0, Directory.GetFiles(fixture.Cache).Length);
            var request = fixture.Transport.Requests.Last();
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v2/comment/230001/app", request.Uri.AbsolutePath);
            Assert.Equal("", request.Uri.Query);
            Assert.Equal(DandanPlayClient.Signature("fixture-app", "fixture-secret", fixture.Now.ToUnixTimeSeconds(), request.Uri.AbsolutePath), request.Headers["X-Signature"]);
            Assert.False(request.Headers.ContainsKey("Authorization"));
            using var body = JsonDocument.Parse(request.Body);
            Assert.Equal("中文测试", body.RootElement.GetProperty("comment").GetString());
            Assert.Equal(12.5, body.RootElement.GetProperty("time").GetDouble());
            Assert.Equal(1, body.RootElement.GetProperty("mode").GetInt32());
            Assert.Equal(16777215, body.RootElement.GetProperty("color").GetInt32());
            Assert.Equal("Momoka", body.RootElement.GetProperty("userName").GetString());
            Assert.DoesNotContain("fixture-secret", request.Body);
            fixture.Client.GetCommentsAsync(230001, true).GetAwaiter().GetResult();
            Assert.Equal(4, fixture.Transport.Requests.Count);
        }));

        Test("弹弹play：发送拒绝非法内容时间、未选库与已关闭服务", () => InFixture(fixture =>
        {
            foreach (var text in new[] { "", "   ", new string('字', 101), "一\n二" })
                Assert.Throws<DandanPlayException>(() => fixture.Client.SendCommentAsync(1, 0, text).GetAwaiter().GetResult());
            foreach (var time in new[] { -1d, double.NaN, double.PositiveInfinity, 86401d })
                Assert.Throws<DandanPlayException>(() => fixture.Client.SendCommentAsync(1, time, "测试").GetAwaiter().GetResult());
            Assert.Throws<DandanPlayException>(() => fixture.Client.SendCommentAsync(0, 1, "测试").GetAwaiter().GetResult());
            fixture.Settings.Danmaku.Enabled = false;
            Assert.Throws<DandanPlayException>(() => fixture.Client.SendCommentAsync(1, 1, "测试").GetAwaiter().GetResult());
            Assert.Equal(0, fixture.Transport.Requests.Count);
        }));

        Test("弹弹play：发送失败不跟随跳转、不重试且未知结果不伪装成功", () => InFixture(fixture =>
        {
            foreach (var response in new Func<HttpResponseMessage>[]
            {
                () => Redirect("https://comment.dandanplay.net/write"),
                () => Reply("fixture-secret", HttpStatusCode.BadGateway),
                () => Reply("fixture-secret", HttpStatusCode.TooManyRequests),
                () => Reply("""{"success":false,"errorCode":4,"errorMessage":"fixture-secret"}"""),
                () => Reply("""{"success":true,"errorCode":0}"""),
                () => Reply("""{"success":true,"errorCode":"bad","cid":1}"""),
                () => Reply("[]"),
                () => throw new HttpRequestException("fixture-secret"),
            })
            {
                fixture.Transport.Requests.Clear();
                fixture.Transport.Response = _ => response();
                var error = Assert.Catch<DandanPlayException>(() => fixture.Client.SendCommentAsync(1, 0, "测试").GetAwaiter().GetResult());
                Assert.DoesNotContain("fixture-secret", error.Message);
                Assert.Equal(1, fixture.Transport.Requests.Count);
            }
        }));

        Test("弹幕：发送时间扣除当前延迟，负位置保持未知", () =>
        {
            Assert.Equal(90d, DandanPlayClient.CommentTime(100, 10));
            Assert.Equal(110d, DandanPlayClient.CommentTime(100, -10));
            Assert.Equal(0d, DandanPlayClient.CommentTime(5, 10));
            Assert.True(double.IsNaN(DandanPlayClient.CommentTime(-1, 0)));
            Assert.True(double.IsNaN(DandanPlayClient.CommentTime(1, double.NaN)));
        });

        Test("弹弹play：超大发送回执提示结果未确认且不重试", () => InFixture(fixture =>
        {
            fixture.Transport.Response = _ =>
            {
                var response = Reply("{}");
                response.Content.Headers.ContentLength = DandanPlayClient.MaximumResponseBytes + 1L;
                return response;
            };
            var error = Assert.Catch<DandanPlayException>(() => fixture.Client.SendCommentAsync(1, 0, "测试").GetAwaiter().GetResult());
            Assert.Contains("结果未确认", error.Message);
            Assert.Equal(1, fixture.Transport.Requests.Count);
        }));

        Test("弹幕：原生发送消息严格校验长度和上下文，不执行文本中的命令", () =>
        {
            var payload = JsonSerializer.Serialize(new { context = new string('a', 32), requestId = "1", text = "中文; quit" });
            Assert.Equal("中文; quit", MpvDanmaku.ParseSend(payload)!.Text);
            Assert.NotNull(VideoWindowContract.Parse([MpvDanmaku.Send, payload]));
            foreach (var invalid in new[] { "[]", "{}", "bad", new string('a', 2049),
                JsonSerializer.Serialize(new { context = "old", requestId = "1", text = "测试" }),
                JsonSerializer.Serialize(new { context = new string('a', 32), requestId = "1", text = new string('字', 101) }) })
                Assert.Null(MpvDanmaku.ParseSend(invalid));
        });

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

    private static void InBuiltInFixture(Action<Fixture> action) { using var fixture = new Fixture(builtIn: true); action(fixture); }

    private const string BuiltInKey = "yYvsbrP9o55bxzdEeh8kQVD9Gf25+rjX/BIndmDBcd8=";

    private static HttpResponseMessage CredentialReply(string appId, string secret, DateTimeOffset now,
        string key = BuiltInKey, string kid = "fixture-kid", long? expires = null)
    {
        var exp = expires ?? now.AddDays(7).ToUnixTimeSeconds();
        var plain = JsonSerializer.Serialize(new { appId, appSecret = secret, exp });
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Convert.FromBase64String(key), 16))
            aes.Encrypt(nonce, plainBytes, cipher, tag, Encoding.UTF8.GetBytes($"momoka-danmaku-credential-v1|{kid}|{exp}"));
        var data = Convert.ToBase64String([.. nonce, .. cipher, .. tag]);
        return Reply(JsonSerializer.Serialize(new { v = 1, kid, exp, data }));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "momoka-danmaku-tests-" + Guid.NewGuid().ToString("N"));
        public DateTimeOffset Now { get; set; } = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        public AppSettings Settings { get; } = new();
        public Transport Transport { get; } = new();
        public string Cache => Path.Combine(_root, "cache");
        public DandanPlayBuiltInCredentialSource? BuiltIn { get; }
        public DandanPlayClient Client { get; }

        public Fixture(bool builtIn = false)
        {
            Settings.Danmaku.Enabled = true;
            if (builtIn)
            {
                Settings.Danmaku.CredentialSource = DanmakuCredentialSource.BuiltIn;
                BuiltIn = new(new PrefixProtector(), Path.Combine(_root, "builtin-credential.bin"),
                    "https://credential.test/credential", BuiltInKey, Transport, () => Now);
            }
            else
            {
                Settings.Danmaku.CredentialSource = DanmakuCredentialSource.Custom;
                Settings.Danmaku.AppId = "fixture-app";
                Settings.Danmaku.ProtectedAppSecret = "fixture-secret";
            }
            Client = new(Settings, new(PassthroughSecretProtector.Instance), Cache, Transport, () => Now, BuiltIn);
        }

        public void Dispose()
        {
            Client.Dispose();
            BuiltIn?.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private sealed record Request(Uri Uri, IReadOnlyDictionary<string, string> Headers, HttpMethod Method, string Body);

    private sealed class Transport : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];
        public Func<Request, HttpResponseMessage> Response { get; set; } = _ => Reply(Search);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = new Request(request.RequestUri!, request.Headers.ToDictionary(pair => pair.Key, pair => string.Join(',', pair.Value), StringComparer.OrdinalIgnoreCase), request.Method, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(snapshot);
            return Response(snapshot);
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
