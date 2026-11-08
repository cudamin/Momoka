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
        Test("弹幕：新增外观字段从旧配置补齐，非法字体和档位不能进入渲染", () =>
        {
            var settings = JsonSerializer.Deserialize<DanmakuSettings>("""{"FontFamily":"bad\\\\font{tag}","BorderStyle":99,"DensityMode":-1}""")!;
            settings.Normalize();
            var json = JsonSerializer.SerializeToElement(settings);
            Assert.True(json.TryGetProperty("ScaleWithWindow", out var scale), "缺少随窗口缩放偏好");
            Assert.True(scale.GetBoolean());
            Assert.True(json.GetProperty("PreventOverlap").GetBoolean());
            Assert.True(json.GetProperty("SpeedSyncPlayback").GetBoolean());
            Assert.Equal("Microsoft YaHei", json.GetProperty("FontFamily").GetString());
            Assert.Equal(0, json.GetProperty("BorderStyle").GetInt32());
            Assert.Equal(0, json.GetProperty("DensityMode").GetInt32());
        });

        Test("弹幕：只恢复显示外观，来源、启用、匹配和屏蔽词不改变", () =>
        {
            var settings = JsonSerializer.Deserialize<DanmakuSettings>("""{"Enabled":true,"Show":false,"Source":1,"DanmuApiBaseUrl":"http://127.0.0.1:9321","ProtectedDanmuApiToken":"cipher","AutoMatch":false,"BlockedWords":"spoiler","Opacity":20,"Bold":true,"FontFamily":"Arial","DensityMode":2}""")!;
            var restore = typeof(DanmakuSettings).GetMethod("RestoreAppearance");
            Assert.True(restore is not null, "缺少仅恢复外观的操作");
            restore!.Invoke(settings, null);
            var json = JsonSerializer.SerializeToElement(settings);
            Assert.True(settings.Enabled);
            Assert.False(settings.Show);
            Assert.False(settings.AutoMatch);
            Assert.Equal("spoiler", settings.BlockedWords);
            Assert.Equal("http://127.0.0.1:9321", settings.DanmuApiBaseUrl);
            Assert.Equal("cipher", settings.ProtectedDanmuApiToken);
            Assert.Equal(70d, settings.Opacity);
            Assert.False(json.GetProperty("Bold").GetBoolean());
            Assert.Equal("Microsoft YaHei", json.GetProperty("FontFamily").GetString());
            Assert.Equal(0, json.GetProperty("DensityMode").GetInt32());
        });

        Test("弹幕：外观消息把用户选择原样下发给独立叠加层，备份恢复也保留新偏好", () =>
        {
            var live = new AppSettings();
            live.Danmaku.ScaleWithWindow = false;
            live.Danmaku.AvoidSubtitles = true;
            live.Danmaku.PreventOverlap = false;
            live.Danmaku.SpeedSyncPlayback = false;
            live.Danmaku.FontFamily = "Arial";
            live.Danmaku.Bold = true;
            live.Danmaku.BorderStyle = 2;
            live.Danmaku.DensityMode = 1;
            var restored = new AppSettings();
            SettingsPreferences.Apply(restored, SettingsPreferences.ToBackupDocument(live));
            using var document = JsonDocument.Parse(MpvDanmaku.RenderOptions(restored.Danmaku, 2.5));
            var json = document.RootElement;
            Assert.False(json.GetProperty("scaleWithWindow").GetBoolean());
            Assert.True(json.GetProperty("avoidSubtitles").GetBoolean());
            Assert.False(json.GetProperty("preventOverlap").GetBoolean());
            Assert.False(json.GetProperty("speedSyncPlayback").GetBoolean());
            Assert.Equal("Arial", json.GetProperty("fontFamily").GetString());
            Assert.True(json.GetProperty("bold").GetBoolean());
            Assert.Equal(2, json.GetProperty("borderStyle").GetInt32());
            Assert.Equal(1, json.GetProperty("densityMode").GetInt32());
            Assert.Equal(2.5, json.GetProperty("delay").GetDouble());
        });

        Test("弹幕：旧设置无字段默认关闭，非法和非有限外观值归一化", () =>
        {
            var settings = SettingsMigration.Normalize(new AppSettings { Danmaku = null! });
            Assert.False(settings.Danmaku.Enabled);
            Assert.True(settings.Danmaku.AutoMatch);
            Assert.False(settings.Danmaku.ShowInput);
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

        Test("弹幕：旧配置里的自定义凭据字段读回后被忽略，不再影响可用性", () =>
        {
            // 2026-10-27 起应用凭据全部内置；旧 settings.json 里残留的 appId／protectedAppSecret／
            // credentialSource 不属于当前模型，反序列化时忽略，读回来也不能让服务变得可用。
            const string legacy = """
                {"SchemaVersion":2,"Danmaku":{"Enabled":true,"CredentialSource":2,"AppId":"legacy-app","ProtectedAppSecret":"legacy-secret","Opacity":35}}
                """;
            var settings = SettingsMigration.FromJson(legacy, new PrefixProtector());
            Assert.True(settings.Danmaku.Enabled);
            Assert.Equal(35d, settings.Danmaku.Opacity);
            Assert.True(DandanPlayMatching.IsConfigured(settings));
            Assert.DoesNotContain("legacy-app", JsonSerializer.Serialize(settings));
            Assert.DoesNotContain("legacy-secret", JsonSerializer.Serialize(settings));
        });

        Test("弹幕：备份只带外观，启用开关不导出、不被恢复覆盖，也不换对象", () =>
        {
            var live = new AppSettings();
            live.Danmaku.Enabled = true;
            live.Danmaku.Opacity = 35;
            live.Danmaku.ShowInput = true;
            live.Danmaku.BlockedWords = "测试,spoiler";
            var reference = live.Danmaku;
            var backup = SettingsPreferences.ToBackupDocument(live);
            Assert.False(backup.Danmaku.Enabled);
            Assert.Equal(35d, backup.Danmaku.Opacity);
            Assert.True(backup.Danmaku.ShowInput);
            SettingsPreferences.Apply(live, new AppSettings());
            Assert.True(ReferenceEquals(reference, live.Danmaku));
            Assert.True(live.Danmaku.Enabled);
            Assert.Equal(70d, live.Danmaku.Opacity);
            Assert.False(live.Danmaku.ShowInput);
        });

        Test("弹幕：服务开启且内置后端即可用，外部后端与关闭都不算", () =>
        {
            var settings = new AppSettings();
            Assert.False(DandanPlayMatching.IsConfigured(settings));
            settings.Danmaku.Enabled = true;
            foreach (var pipeline in new[] { VideoPipelineKind.Integrated, VideoPipelineKind.Standalone })
            {
                settings.Mpv.Pipeline = pipeline;
                Assert.True(DandanPlayMatching.IsConfigured(settings));
            }

            settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
            Assert.False(DandanPlayMatching.IsConfigured(settings));
            settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
            settings.Danmaku.Enabled = false;
            Assert.False(DandanPlayMatching.IsConfigured(settings));
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

        Test("弹弹play：关闭服务或没有凭据来源时零HTTP请求", () =>
        {
            InFixture(fixture =>
            {
                fixture.Settings.Danmaku.Enabled = false;
                Assert.Throws<DandanPlayException>(() => fixture.Client.SearchAsync("测试", null).GetAwaiter().GetResult());
                Assert.Throws<DandanPlayException>(() => fixture.Client.SendCommentAsync(1, 0, "测试").GetAwaiter().GetResult());
                Assert.Equal(0, fixture.Transport.Requests.Count);
            });
            InFixture(fixture =>
            {
                var error = Assert.Catch<DandanPlayException>(() => fixture.Client.GetCommentsAsync(1, true).GetAwaiter().GetResult());
                Assert.Contains("内置弹幕凭据", error.Message);
                Assert.Equal(0, fixture.Transport.Requests.Count);
            }, withoutCredentials: true);
        });

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
                ("Invalid Timestamp", "Windows 时间"), ("Invalid AppId", "内置应用凭据无效"),
                ("Invalid Signature", "内置密钥或签名无效"), ("Invalid AppSecret", "内置密钥或签名无效"),
                ("Missing Authentication Headers", "请求缺少应用认证头"), ("fixture-secret", "检查系统时间"),
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
            // 缓存按凭据身份隔离：端点换了一枚应用（或密钥）之后不能沿用上一份结果。
            fixture.Credentials.AppId = "other-fixture-app";
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

        Test("R14-001：删除被拒的凭据缓存不复活，落盘失败仍使用新身份", () => InBuiltInFixture(fixture =>
        {
            var credentials = 0;
            fixture.Transport.Response = request => request.Uri.Host == "credential.test"
                ? CredentialReply("builtin-app", ++credentials == 1 ? "old-secret" : "new-secret", fixture.Now)
                : Reply(Search);
            Assert.Equal("old-secret", fixture.BuiltIn!.GetAsync().GetAwaiter().GetResult().Secret);
            var cache = Path.Combine(Path.GetDirectoryName(fixture.Cache)!, "builtin-credential.bin");
            using (var locked = new FileStream(cache, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fixture.BuiltIn.Invalidate();
                Assert.True(File.Exists(cache));
                Assert.Equal("new-secret", fixture.BuiltIn.GetAsync().GetAwaiter().GetResult().Secret);
                Assert.Equal(2, credentials);
                Assert.Equal("new-secret", fixture.BuiltIn.GetAsync().GetAwaiter().GetResult().Secret);
                Assert.Equal(2, credentials);
            }
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

        Test("弹弹play：内置凭据错误密钥与过期都被拒绝，且都不发弹幕请求", () => InBuiltInFixture(fixture =>
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
            Assert.Equal(1, fixture.Transport.Requests.Count);
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
            var invalidText = JsonSerializer.Serialize(new { context = new string('a', 32), requestId = "recoverable", text = "bad\u0080" });
            Assert.Null(MpvDanmaku.ParseSend(invalidText));
            var parseEnvelope = typeof(MpvDanmaku).GetMethod("ParseSendEnvelope");
            Assert.NotNull(parseEnvelope);
            var envelope = parseEnvelope!.Invoke(null, [invalidText]);
            Assert.NotNull(envelope);
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

        Test("弹幕：来源默认两个都用，未知值归一，地址与令牌限长", () =>
        {
            var settings = SettingsMigration.Normalize(new AppSettings { Danmaku = null! });
            Assert.Equal(DanmakuSourceMode.Both, settings.Danmaku.Source);
            Assert.Equal("", settings.Danmaku.DanmuApiBaseUrl);
            Assert.Equal("", settings.Danmaku.ProtectedDanmuApiToken);

            settings.Danmaku.Source = (DanmakuSourceMode)99;
            settings.Danmaku.DanmuApiBaseUrl = "  http://127.0.0.1:9321  ";
            settings.Danmaku.ProtectedDanmuApiToken = new string('x', 9000);
            SettingsMigration.Normalize(settings);
            Assert.Equal(DanmakuSourceMode.Both, settings.Danmaku.Source);
            Assert.Equal("http://127.0.0.1:9321", settings.Danmaku.DanmuApiBaseUrl);
            Assert.Equal(8192, settings.Danmaku.ProtectedDanmuApiToken.Length);
        });

        Test("弹幕：备份不导出 danmu_api 地址与令牌，恢复也不覆盖本机连接", () =>
        {
            var live = new AppSettings();
            live.Danmaku.Enabled = true;
            live.Danmaku.Source = DanmakuSourceMode.DanmuApi;
            live.Danmaku.DanmuApiBaseUrl = "http://127.0.0.1:9321";
            live.Danmaku.ProtectedDanmuApiToken = "cipher-token";
            live.Danmaku.Opacity = 35;

            var backup = SettingsPreferences.ToBackupDocument(live);
            Assert.Equal(DanmakuSourceMode.Both, backup.Danmaku.Source);
            Assert.Equal("", backup.Danmaku.DanmuApiBaseUrl);
            Assert.Equal("", backup.Danmaku.ProtectedDanmuApiToken);
            Assert.Equal(35d, backup.Danmaku.Opacity);

            SettingsPreferences.Apply(live, new AppSettings());
            Assert.True(live.Danmaku.Enabled);
            Assert.Equal(DanmakuSourceMode.DanmuApi, live.Danmaku.Source);
            Assert.Equal("http://127.0.0.1:9321", live.Danmaku.DanmuApiBaseUrl);
            Assert.Equal("cipher-token", live.Danmaku.ProtectedDanmuApiToken);
            Assert.Equal(70d, live.Danmaku.Opacity);
        });

        Test("弹幕：来源包含关系、发送能力与可用性", () =>
        {
            Assert.True(DanmakuSourceRules.Includes(DanmakuSourceMode.DandanPlay, DanmakuSource.DandanPlay));
            Assert.False(DanmakuSourceRules.Includes(DanmakuSourceMode.DandanPlay, DanmakuSource.DanmuApi));
            Assert.True(DanmakuSourceRules.Includes(DanmakuSourceMode.DanmuApi, DanmakuSource.DanmuApi));
            Assert.True(DanmakuSourceRules.Includes(DanmakuSourceMode.Both, DanmakuSource.DandanPlay));
            Assert.True(DanmakuSourceRules.Includes(DanmakuSourceMode.Both, DanmakuSource.DanmuApi));
            Assert.True(DanmakuSourceRules.CanSend(DanmakuSourceMode.DandanPlay));
            Assert.False(DanmakuSourceRules.CanSend(DanmakuSourceMode.DanmuApi));
            Assert.True(DanmakuSourceRules.CanSend(DanmakuSourceMode.Both));

            var settings = new AppSettings();
            Assert.False(DanmakuSourceRules.IsAvailable(settings));
            settings.Danmaku.Enabled = true;
            settings.Danmaku.Source = DanmakuSourceMode.DandanPlay;
            Assert.True(DanmakuSourceRules.IsAvailable(settings));
            settings.Danmaku.Source = DanmakuSourceMode.DanmuApi;
            Assert.False(DanmakuSourceRules.IsAvailable(settings));
            settings.Danmaku.DanmuApiBaseUrl = "http://127.0.0.1:9321";
            Assert.True(DanmakuSourceRules.IsAvailable(settings));
            settings.Danmaku.Source = DanmakuSourceMode.Both;
            Assert.True(DanmakuSourceRules.IsAvailable(settings));
            settings.Danmaku.DanmuApiBaseUrl = "";
            Assert.True(DanmakuSourceRules.IsAvailable(settings));
            settings.Danmaku.Source = DanmakuSourceMode.DanmuApi;
            Assert.False(DanmakuSourceRules.IsAvailable(settings));
            settings.Mpv.Backend = MpvBackendKind.ExternalMpv;
            settings.Danmaku.DanmuApiBaseUrl = "http://127.0.0.1:9321";
            Assert.False(DanmakuSourceRules.IsAvailable(settings));
        });

        Test("弹幕：danmu_api 地址只接受 http/https 完整地址，令牌作为独立路径段", () =>
        {
            Assert.True(DanmuApiEndpoint.TryCreate("http://127.0.0.1:9321", "", out var baseUri, out var error), error);
            Assert.Equal("http://127.0.0.1:9321/", baseUri.AbsoluteUri);
            Assert.True(DanmuApiEndpoint.TryCreate("http://127.0.0.1:9321/", "abc-123", out baseUri, out error), error);
            Assert.Equal("http://127.0.0.1:9321/abc-123/", baseUri.AbsoluteUri);
            Assert.True(DanmuApiEndpoint.TryCreate("https://example.test/danmu", "a/b?c", out baseUri, out error), error);
            Assert.Equal("https://example.test/danmu/a%2Fb%3Fc/", baseUri.AbsoluteUri);

            foreach (var bad in new[] { "", "127.0.0.1:9321", "ftp://host", "http://user:pass@host",
                "http://host/?q=1", "http://host/#frag", "http:///missing" })
                Assert.False(DanmuApiEndpoint.TryCreate(bad, "", out _, out _), bad);
            Assert.False(DanmuApiEndpoint.TryCreate("http://host", "a\nb", out _, out _));
            Assert.False(DanmuApiEndpoint.TryCreate("http://host", new string('x', 513), out _, out _));
        });

        Test("弹幕：danmu_api 令牌用 DPAPI 保护，空值清除，解不开要明说", () =>
        {
            var protector = new PrefixProtector();
            var protectedText = DanmuApiToken.Protect(protector, " abc ");
            Assert.Equal("cipher:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("abc")), protectedText);
            Assert.Equal("abc", DanmuApiToken.Unprotect(protector, protectedText, out var error));
            Assert.Equal("", error);
            Assert.Equal("", DanmuApiToken.Protect(protector, "  "));
            Assert.Equal("", DanmuApiToken.Unprotect(null, protectedText, out error));
            Assert.Contains("无法解密", error);
        });

        Test("danmu_api：搜索走令牌路径、不签名、不碰内置凭据", () => InFixture(fixture =>
        {
            UseDanmuApi(fixture);
            fixture.Transport.Response = _ => Reply(Search);
            var result = fixture.Client.SearchAsync("测试作品", "1", DanmakuSource.DanmuApi).GetAwaiter().GetResult();
            Assert.Equal(2, result.Episodes.Count);
            var request = fixture.Transport.Requests.Single();
            Assert.Equal("danmu.test", request.Uri.Host);
            Assert.Equal(9321, request.Uri.Port);
            Assert.Equal("/fixture-token/api/v2/search/episodes", request.Uri.AbsolutePath);
            Assert.Contains(Uri.EscapeDataString("测试作品"), request.Uri.Query);
            Assert.Contains("&episode=1", request.Uri.Query);
            Assert.False(request.Headers.ContainsKey("X-AppId"));
            Assert.False(request.Headers.ContainsKey("X-Timestamp"));
            Assert.False(request.Headers.ContainsKey("X-Signature"));
            Assert.False(request.Headers.ContainsKey("Authorization"));
            Assert.DoesNotContain("fixture-secret", request.Uri.AbsoluteUri);
        }));

        Test("danmu_api：弹幕不签名，缓存按来源与地址隔离", () => InFixture(fixture =>
        {
            UseDanmuApi(fixture);
            fixture.Settings.Danmaku.Source = DanmakuSourceMode.Both;
            fixture.Transport.Response = request => request.Uri.Host == "danmu.test"
                ? Reply("""{"count":1,"comments":[{"p":"1,1,0,[youku]","m":"来自 danmu_api"}]}""")
                : Reply(Comments);
            var dandan = fixture.Client.GetCommentsAsync(230001, false, DanmakuSource.DandanPlay).GetAwaiter().GetResult();
            var custom = fixture.Client.GetCommentsAsync(230001, false, DanmakuSource.DanmuApi).GetAwaiter().GetResult();
            Assert.Equal(3, dandan.Count);
            Assert.Equal("来自 danmu_api", custom.Single().Text);
            Assert.Equal(2, fixture.Transport.Requests.Count);
            Assert.True(fixture.Transport.Requests[0].Headers.ContainsKey("X-Signature"));
            Assert.False(fixture.Transport.Requests[1].Headers.ContainsKey("X-Signature"));
            fixture.Client.GetCommentsAsync(230001, false, DanmakuSource.DandanPlay).GetAwaiter().GetResult();
            fixture.Client.GetCommentsAsync(230001, false, DanmakuSource.DanmuApi).GetAwaiter().GetResult();
            Assert.Equal(2, fixture.Transport.Requests.Count);
            fixture.Settings.Danmaku.DanmuApiBaseUrl = "http://other.test:9321";
            fixture.Client.GetCommentsAsync(230001, false, DanmakuSource.DanmuApi).GetAwaiter().GetResult();
            Assert.Equal(3, fixture.Transport.Requests.Count);
        }));

        Test("danmu_api：地址无效、令牌解不开或来源未包含时不发请求", () => InFixture(fixture =>
        {
            fixture.Settings.Danmaku.Source = DanmakuSourceMode.DanmuApi;
            var error = Assert.Catch<DandanPlayException>(() =>
                fixture.Client.SearchAsync("测试", null, DanmakuSource.DanmuApi).GetAwaiter().GetResult());
            Assert.Contains("服务地址", error.Message);
            Assert.Equal(0, fixture.Transport.Requests.Count);

            fixture.Settings.Danmaku.DanmuApiBaseUrl = "http://danmu.test:9321";
            fixture.Settings.Danmaku.ProtectedDanmuApiToken = "not-a-cipher";
            error = Assert.Catch<DandanPlayException>(() =>
                fixture.Client.SearchAsync("测试", null, DanmakuSource.DanmuApi).GetAwaiter().GetResult());
            Assert.Contains("无法解密", error.Message);
            Assert.Equal(0, fixture.Transport.Requests.Count);

            fixture.Settings.Danmaku.ProtectedDanmuApiToken = "";
            fixture.Settings.Danmaku.Source = DanmakuSourceMode.DandanPlay;
            error = Assert.Catch<DandanPlayException>(() =>
                fixture.Client.SearchAsync("测试", null, DanmakuSource.DanmuApi).GetAwaiter().GetResult());
            Assert.Contains("来源", error.Message);
            Assert.Equal(0, fixture.Transport.Requests.Count);
        }));

        Test("danmu_api：401、403、404、限额与5xx只给来源提示，不回显正文", () => InFixture(fixture =>
        {
            UseDanmuApi(fixture);
            foreach (var (status, text) in new[]
            {
                (HttpStatusCode.Unauthorized, "鉴权"), (HttpStatusCode.Forbidden, "鉴权"),
                (HttpStatusCode.NotFound, "重新搜索"), (HttpStatusCode.TooManyRequests, "频繁"),
                (HttpStatusCode.BadGateway, "HTTP 502")
            })
            {
                fixture.Transport.Requests.Clear();
                fixture.Transport.Response = _ => Reply("fixture-secret", status);
                var error = Assert.Catch<DandanPlayException>(() =>
                    fixture.Client.SearchAsync("测试", null, DanmakuSource.DanmuApi).GetAwaiter().GetResult());
                Assert.Contains(text, error.Message);
                Assert.DoesNotContain("fixture-secret", error.Message);
                Assert.Equal(1, fixture.Transport.Requests.Count);
            }
        }));

        Test("danmu_api：弹幕请求不跟随跳转", () => InFixture(fixture =>
        {
            UseDanmuApi(fixture);
            fixture.Transport.Response = _ => Redirect("http://other.test/steal");
            Assert.Throws<DandanPlayException>(() =>
                fixture.Client.GetCommentsAsync(1, true, DanmakuSource.DanmuApi).GetAwaiter().GetResult());
            Assert.Equal(1, fixture.Transport.Requests.Count);
        }));

        Test("danmu_api：match 发送 fileName，只取第一条匹配，未匹配返回空", () => InFixture(fixture =>
        {
            UseDanmuApi(fixture);
            fixture.Transport.Response = _ => Reply("""
                {"success":true,"errorCode":0,"isMatched":true,"matches":[
                  {"episodeId":10198,"animeTitle":"测试作品(2025)","episodeTitle":"第1集"},
                  {"episodeId":2,"animeTitle":"另一个","episodeTitle":"第1集"}]}
                """);
            var match = fixture.Client.MatchAsync("测试作品 (2025) S01E01").GetAwaiter().GetResult();
            Assert.Equal(10198L, match!.EpisodeId);
            Assert.Equal("测试作品(2025)", match.AnimeTitle);
            var request = fixture.Transport.Requests.Single();
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/fixture-token/api/v2/match", request.Uri.AbsolutePath);
            Assert.False(request.Headers.ContainsKey("X-AppId"));
            Assert.False(request.Headers.ContainsKey("X-Signature"));
            using (var body = JsonDocument.Parse(request.Body))
                Assert.Equal("测试作品 (2025) S01E01", body.RootElement.GetProperty("fileName").GetString());

            fixture.Transport.Requests.Clear();
            fixture.Transport.Response = _ => Reply("""{"success":true,"errorCode":0,"isMatched":false,"matches":[]}""");
            Assert.Null(fixture.Client.MatchAsync("测试作品").GetAwaiter().GetResult());
            Assert.Equal(1, fixture.Transport.Requests.Count);
        }));

        Test("danmu_api：缓存文件名和内容不含令牌", () => InFixture(fixture =>
        {
            UseDanmuApi(fixture);
            fixture.Transport.Response = _ => Reply(Search);
            fixture.Client.SearchAsync("测试", "1", DanmakuSource.DanmuApi).GetAwaiter().GetResult();
            var file = Directory.GetFiles(fixture.Cache).Single();
            Assert.DoesNotContain("fixture-token", Path.GetFileName(file));
            Assert.DoesNotContain("fixture-token", File.ReadAllText(file));
        }));

        Test("弹幕：danmu_api 自动匹配文件名带年份，续季不自动", () =>
        {
            var episode = new EmbyItem
            {
                Type = EmbyItemType.Episode,
                SeriesName = "凡人修仙传",
                IndexNumber = 1,
                ParentIndexNumber = 1,
                ProductionYear = 2020
            };
            Assert.True(DanmakuMatchRules.CanAutoMatch(episode));
            Assert.Equal("凡人修仙传 (2020) S01E01", DanmakuMatchRules.MatchFileName(episode));
            var noYear = new EmbyItem
            {
                Type = EmbyItemType.Episode,
                SeriesName = "凡人修仙传",
                IndexNumber = 2,
                ParentIndexNumber = 1
            };
            Assert.Equal("凡人修仙传 S01E02", DanmakuMatchRules.MatchFileName(noYear));

            var movie = new EmbyItem { Type = EmbyItemType.Movie, Name = "测试电影", ProductionYear = 2024 };
            Assert.True(DanmakuMatchRules.CanAutoMatch(movie));
            Assert.Equal("测试电影 (2024)", DanmakuMatchRules.MatchFileName(movie));

            foreach (var unavailable in new[]
            {
                new EmbyItem { Type = EmbyItemType.Episode, SeriesName = "测试", IndexNumber = 1, ParentIndexNumber = 2 },
                new EmbyItem { Type = EmbyItemType.Episode, SeriesName = "测试", IndexNumber = 1, ParentIndexNumber = 0 },
                new EmbyItem { Type = EmbyItemType.Episode, SeriesName = "测试", ParentIndexNumber = 1 },
                new EmbyItem { Type = EmbyItemType.Series, Name = "测试" }
            })
                Assert.False(DanmakuMatchRules.CanAutoMatch(unavailable), unavailable.Type);
        });

        Test("弹幕：danmu_api 自动匹配优先使用所属剧的年份", () =>
        {
            var episode = new EmbyItem
            {
                Type = EmbyItemType.Episode,
                SeriesName = "凡人修仙传",
                IndexNumber = 184,
                ParentIndexNumber = 1,
                ProductionYear = 2026
            };
            Assert.Equal("凡人修仙传 (2020) S01E184", DanmakuMatchRules.MatchFileName(episode, 2020));
            Assert.Equal("凡人修仙传 (2026) S01E184", DanmakuMatchRules.MatchFileName(episode));
            Assert.Equal("凡人修仙传 (2026) S01E184", DanmakuMatchRules.MatchFileName(episode, 0));

            var movie = new EmbyItem { Type = EmbyItemType.Movie, Name = "测试电影", ProductionYear = 2024 };
            Assert.Equal("测试电影 (2024)", DanmakuMatchRules.MatchFileName(movie, 2020));
        });

        Test("弹幕：danmu_api 匹配结果核对显式集号", () =>
        {
            var episode = new EmbyItem
            {
                Type = EmbyItemType.Episode,
                SeriesName = "测试",
                IndexNumber = 1,
                ParentIndexNumber = 1
            };
            Assert.True(DanmakuMatchRules.Accepts(episode, new DandanPlayEpisode(1, "测试", "【youku】 第1集")));
            Assert.False(DanmakuMatchRules.Accepts(episode, new DandanPlayEpisode(1, "测试", "【bilibili1】 第22话 魔道争锋1")));
            Assert.True(DanmakuMatchRules.Accepts(episode, new DandanPlayEpisode(1, "测试", "第1话 标题")));
            Assert.True(DanmakuMatchRules.Accepts(episode, new DandanPlayEpisode(1, "测试", "魔道争锋1")));
            Assert.True(DanmakuMatchRules.Accepts(episode, new DandanPlayEpisode(1, "测试", "EP01 标题")));
            Assert.False(DanmakuMatchRules.Accepts(episode, null));
            Assert.Equal(22, DanmakuMatchRules.ExplicitEpisodeNumber("第22话 魔道争锋1"));
            Assert.Equal(1, DanmakuMatchRules.ExplicitEpisodeNumber("【youku】 第1集"));
            Assert.Null(DanmakuMatchRules.ExplicitEpisodeNumber("2020年"));
            var movie = new EmbyItem { Type = EmbyItemType.Movie, Name = "测试" };
            Assert.True(DanmakuMatchRules.Accepts(movie, new DandanPlayEpisode(1, "测试", "第22话")));
        });

        Test("弹幕：合并两源按时间排序并保留四万条上限", () =>
        {
            var first = new[] { new DanmakuComment(10, 1, 0xFFFFFF, "A"), new DanmakuComment(30, 1, 0xFFFFFF, "C") };
            var second = new[] { new DanmakuComment(20, 1, 0xFFFFFF, "B"), new DanmakuComment(40, 1, 0xFFFFFF, "D") };
            var combined = DanmakuComments.Combine(first, second);
            Assert.Equal("10,20,30,40", string.Join(',', combined.Select(comment => comment.Time)));
            var many = Enumerable.Range(0, 50000).Select(index => new DanmakuComment(index, 1, 0xFFFFFF, $"C{index}"));
            Assert.Equal(40000, DanmakuComments.Combine(many).Count);
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

    private static void UseDanmuApi(Fixture fixture, string address = "http://danmu.test:9321", string token = "fixture-token")
    {
        fixture.Settings.Danmaku.Source = DanmakuSourceMode.DanmuApi;
        fixture.Settings.Danmaku.DanmuApiBaseUrl = address;
        fixture.Settings.Danmaku.ProtectedDanmuApiToken =
            token.Length == 0 ? "" : DanmuApiToken.Protect(fixture.Protector, token);
    }

    private static void InFixture(Action<Fixture> action, bool withoutCredentials = false)
    {
        using var fixture = new Fixture(withoutCredentials: withoutCredentials);
        action(fixture);
    }

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
        public FixedCredentialSource Credentials { get; } = new("fixture-app", "fixture-secret");
        public PrefixProtector Protector { get; } = new();
        public DandanPlayBuiltInCredentialSource? BuiltIn { get; }
        public DandanPlayClient Client { get; }

        /// <param name="builtIn">走真实的内置凭据来源（假端点），用于验证解密、换新与 401 重取。</param>
        /// <param name="withoutCredentials">不给任何凭据来源，用于验证「没有内置凭据」时的明确报错。</param>
        public Fixture(bool builtIn = false, bool withoutCredentials = false)
        {
            Settings.Danmaku.Enabled = true;
            if (builtIn)
                BuiltIn = new(Protector, Path.Combine(_root, "builtin-credential.bin"),
                    "https://credential.test/credential", BuiltInKey, Transport, () => Now);
            IDandanPlayCredentialSource? source = null;
            if (!withoutCredentials) source = BuiltIn is null ? Credentials : BuiltIn;
            Client = new(Settings, Cache, Transport, () => Now, source, Protector);
        }

        public void Dispose()
        {
            Client.Dispose();
            BuiltIn?.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// 固定凭据来源：产品里只有内置端点那一种实现，这里替掉它，好让签名、缓存与重定向的判据不被
    /// 「每条请求前先走一次假端点」带偏。
    /// </summary>
    private sealed class FixedCredentialSource(string appId, string secret) : IDandanPlayCredentialSource
    {
        public string AppId { get; set; } = appId;
        public string Secret { get; set; } = secret;
        public Task<DandanPlayCredential> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DandanPlayCredential(AppId, Secret, "fixture-kid"));
        public void Invalidate() { }
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
}
