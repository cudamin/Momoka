using System.Net;
using System.Reflection;
using System.Text.Json;
using Momoka.AniBt;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.MoviePilot;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class AniBtReceiptStateTests
{
    public static void Register()
    {
        foreach (var (state, expected, success) in new[]
        {
            ("accepted", "已接收", true),
            ("skipped", "已跳过", true),
            ("manual_review", "人工复核", false),
            ("failed", "失败", false),
            ("retry_wait", "等待重试", false),
            ("future_state", "状态未确认", false),
            ("", "状态未确认", false)
        })
        {
            Test("R038 实际 Submit：" + state + " 不冒充完成或请求扫描", () =>
            {
                using var fixture = new ReceiptFixture(new FileCase(State: state));
                var row = fixture.Submit().Single();
                Assert.Equal(success, row.Success, "保留正式 CompleteReceipt 的已处理语义，不拿它推断完成");
                Assert.Contains(expected, row.Message);
                Assert.DoesNotContain("已重新整理到", row.Message);
                Assert.DoesNotContain("已完成整理", row.Message);
                Assert.Contains("MoviePilot", row.Message);
                Assert.Contains("核对", row.Message);
                fixture.AssertTraffic(1, 0, 1);
                fixture.AssertReplayBlocked();
            });
        }

        Test("R038 实际 Submit：completed 才报完成且一次请求扫描，不保证收录或移除", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase());
            var row = fixture.Submit().Single();
            Assert.True(row.Success);
            Assert.Contains("已完成整理", row.Message);
            Assert.Contains("已重新整理到 " + ReceiptFixture.NewTarget(0), row.Message);
            Assert.Contains("已请求 Emby 扫描媒体库", row.Message);
            Assert.Contains("收录", row.Message);
            Assert.Contains("移除", row.Message);
            Assert.Contains("由服务器决定", row.Message);
            Assert.DoesNotContain("会在扫描完成后移除", row.Message);
            fixture.AssertTraffic(1, 1, 1);
            fixture.AssertReplayBlocked();
        });

        Test("R038 实际 Submit：空成功信封只算接收，目标仍待确认", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase());
            fixture.ResetReplies("""{"success":true,"data":{}}""");
            var row = fixture.Submit().Single();
            Assert.True(row.Success);
            Assert.Contains("已接收", row.Message);
            Assert.Contains("完成状态待确认", row.Message);
            Assert.DoesNotContain("已重新整理到", row.Message);
            fixture.AssertTraffic(1, 0, 1);
            fixture.AssertReplayBlocked();
        });

        Test("R038 实际 Submit：成功标志与 completed 矛盾仍待核对，不扫描", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(LineSuccess: false));
            var row = fixture.Submit().Single();
            Assert.False(row.Success);
            Assert.Contains("待核对", row.Message);
            Assert.DoesNotContain("已完成整理", row.Message);
            fixture.AssertTraffic(1, 0, 1);
        });

        Test("R038 实际 Submit：completed 无目标路径不猜路径影响", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase());
            fixture.ResetReplies(fixture.Reply(0, "completed", target: ""));
            var row = fixture.Submit().Single();
            Assert.True(row.Success);
            Assert.Contains("已完成整理", row.Message);
            Assert.Contains("目标路径", row.Message);
            Assert.Contains("核对", row.Message);
            fixture.AssertTraffic(1, 0, 1);
        });

        Test("R038 实际 Submit：仅目标的唯一 completed 回执仍可核对旧路径影响", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase());
            fixture.ResetReplies(fixture.Reply(0, "completed", source: ""));
            var row = fixture.Submit().Single();
            Assert.True(row.Success);
            Assert.Contains("已完成整理", row.Message);
            fixture.AssertTraffic(1, 1, 1);
        });

        Test("R038 实际 Submit：失败信封中的明确 completed 不丢失已完成项与扫描", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(EnvelopeSuccess: false));
            var row = fixture.Submit().Single();
            Assert.False(row.Success, "整体回执失败保留，明确完成的文件仍要如实报告");
            Assert.Contains("已完成整理", row.Message);
            fixture.AssertTraffic(1, 1, 1);
        });

        Test("R038 实际 Submit：同源多个冲突回执保留逐行结果，不能凭其中 completed 扫描", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase());
            fixture.ResetReplies(fixture.Reply(0, "accepted", extraState: "completed"));
            var row = fixture.Submit().Single();
            Assert.False(row.Success);
            Assert.Contains("已接收", row.Message);
            Assert.Contains("已完成整理", row.Message);
            Assert.Contains("人工复核", row.Message);
            Assert.Contains("勿重复提交", row.Message);
            fixture.AssertTraffic(1, 0, 1);
        });

        Test("R038 实际 Submit：不属于预览文件的 completed 不能触发本批扫描", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase());
            fixture.ResetReplies(fixture.Reply(0, "completed", source: "/unselected/other.mkv"));
            var row = fixture.Submit().Single();
            Assert.False(row.Success);
            Assert.Contains("人工复核", row.Message);
            fixture.AssertTraffic(1, 0, 1);
        });

        Test("R038 实际 Submit：混合批次保留各状态，accepted/skipped 不停发，整批仅扫描一次", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(State: "accepted"), new FileCase(State: "skipped"), new FileCase(),
                new FileCase(State: "manual_review"), new FileCase(State: "future_state"), new FileCase());
            var rows = fixture.Submit();
            Assert.Equal(6, rows.Count);
            Assert.Equal(4, rows.Count(row => row.Success), "UI 已处理总数不是已移动或已完成总数");
            Assert.Contains("已接收", rows[0].Message);
            Assert.Contains("已跳过", rows[1].Message);
            Assert.Contains("已完成整理", rows[2].Message);
            Assert.Contains("人工复核", rows[3].Message);
            Assert.Contains("状态未确认", rows[4].Message);
            Assert.Contains("已完成整理", rows[5].Message);
            foreach (var index in new[] { 0, 1, 3, 4 })
            {
                Assert.DoesNotContain("已重新整理到", rows[index].Message);
                Assert.DoesNotContain("已请求 Emby 扫描", rows[index].Message);
            }
            Assert.Contains("已请求 Emby 扫描", rows[2].Message);
            Assert.Contains("已请求 Emby 扫描", rows[5].Message);
            fixture.AssertTraffic(6, 1, 6);
            fixture.AssertReplayBlocked();
        });

        foreach (var mode in new[] { "copy", "link", "softlink" })
        {
            Test("R038 实际 Submit：" + mode + " 保留原源且没有成功旧目标清理，不全扫", () =>
            {
                using var fixture = new ReceiptFixture(new FileCase(Mode: mode, HistorySuccess: false, TransferType: mode));
                var row = fixture.Submit().Single();
                Assert.True(row.Success);
                Assert.Contains("已完成整理", row.Message);
                Assert.DoesNotContain("已请求 Emby 扫描", row.Message);
                fixture.AssertTraffic(1, 0, 1);
            });

            Test("R038 实际 Submit：成功 " + mode + " 原记录重整清理旧目标，不能只按方式略过扫描", () =>
            {
                using var fixture = new ReceiptFixture(new FileCase(Mode: mode, TransferType: mode));
                var row = fixture.Submit().Single();
                Assert.True(row.Success);
                Assert.Contains("旧目标", row.Message);
                Assert.Contains(ReceiptFixture.OldTarget(0), row.Message);
                fixture.AssertTraffic(1, 1, 1);
            });

            Test("R038 实际 Submit：" + mode + " 重整回原路径且保留源，不作不必要全扫", () =>
            {
                using var fixture = new ReceiptFixture(new FileCase(Mode: mode, TransferType: mode, Target: ReceiptFixture.OldTarget(0)));
                Assert.True(fixture.Submit().Single().Success);
                fixture.AssertTraffic(1, 0, 1);
            });
        }

        Test("R038 实际 Submit：原 mode=move 但本次 copy，不按旧 mode 猜源被移走", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(HistorySuccess: false, TransferType: "copy"));
            Assert.True(fixture.Submit().Single().Success);
            fixture.AssertTraffic(1, 0, 1);
        });

        Test("R038 实际 Submit：目的目录未指定方式，成功旧目标的清理仍需核对", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(Mode: "softlink", TransferType: ""));
            Assert.Contains("旧目标", fixture.Submit().Single().Message);
            fixture.AssertTraffic(1, 1, 1);
        });

        Test("R038 实际 Submit：move 在原路径完成，不宣称旧路径需要移除", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(Target: ReceiptFixture.OldTarget(0)));
            Assert.True(fixture.Submit().Single().Success);
            fixture.AssertTraffic(1, 0, 1);
        });

        Test("R038 实际 Submit：目标是远程存储也不能漏掉本地成功旧目标的清理扫描", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(Mode: "softlink", TransferType: "copy", TargetStorage: "remote"));
            var row = fixture.Submit().Single();
            Assert.True(row.Success);
            Assert.Contains("旧目标", row.Message);
            fixture.AssertTraffic(1, 1, 1);
        });

        Test("R038 实际 Submit：同路径字符串但目标存储改变，旧本地目标仍不能当未变", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(Mode: "softlink", TransferType: "copy",
                Target: ReceiptFixture.OldTarget(0), TargetStorage: "remote"));
            var row = fixture.Submit().Single();
            Assert.True(row.Success);
            Assert.Contains("旧目标", row.Message);
            fixture.AssertTraffic(1, 1, 1);
        });

        Test("R038 实际 Submit：远程目标但复制保留源且无成功旧目标清理，不扫描", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(Mode: "copy", TransferType: "copy",
                HistorySuccess: false, TargetStorage: "remote"));
            Assert.True(fixture.Submit().Single().Success);
            fixture.AssertTraffic(1, 0, 1);
        });

        Test("R038 远程对象相对路径完成仍核本地成功旧目标清理", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(Mode: "softlink", TransferType: "copy",
                TargetStorage: "remote", Target: "folder/anonymous-1.mkv"));
            var row = fixture.Submit().Single();
            Assert.True(row.Success);
            Assert.Contains("已完成整理", row.Message);
            Assert.Contains("旧目标", row.Message);
            fixture.AssertTraffic(1, 1, 1);
            fixture.AssertReplayBlocked();
        });
        Test("R038 远程对象相对路径无成功旧目标时不扩大扫描", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(Mode: "copy", TransferType: "copy",
                HistorySuccess: false, TargetStorage: "remote", Target: "folder/anonymous-1.mkv"));
            Assert.True(fixture.Submit().Single().Success);
            fixture.AssertTraffic(1, 0, 1);
        });
        Test("R038 本地完成目标仍须是绝对路径才确认扫描影响", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(Mode: "softlink", TransferType: "copy",
                TargetStorage: "local", Target: "folder/anonymous-1.mkv"));
            Assert.True(fixture.Submit().Single().Success);
            fixture.AssertTraffic(1, 0, 1);
        });
        Test("R038 实际 Submit：扫描失败仍保留完成整理与手动恢复，不能许可整理重试", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase());
            fixture.Transport.Fail("Library/Refresh", HttpStatusCode.Forbidden);
            var row = fixture.Submit().Single();
            Assert.True(row.Success);
            Assert.Contains("已完成整理", row.Message);
            Assert.Contains("扫描请求失败", row.Message);
            Assert.Contains("手动扫描媒体库", row.Message);
            Assert.Contains("勿重复", row.Message);
            fixture.AssertTraffic(1, 1, 1);
            fixture.AssertReplayBlocked();
        });

        Test("R038 实际 Submit：canContinue 在扫描前拒绝，不向旧媒体库请求扫描", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase());
            var checks = 0;
            var row = fixture.Submit(canContinue: () => ++checks < 3).Single();
            Assert.True(row.Success);
            Assert.Contains("已完成整理", row.Message);
            Assert.Contains("媒体库身份已改变", row.Message);
            Assert.Contains("手动扫描", row.Message);
            fixture.AssertTraffic(1, 0, 1);
        });

        Test("R038 实际 Submit：完成后在扫描前取消，仍保留完成且手动恢复", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase());
            using var cancellation = new CancellationTokenSource();
            var checks = 0;
            var row = fixture.Submit(cancellation.Token, () =>
            {
                if (++checks == 3) cancellation.Cancel();
                return true;
            }).Single();
            Assert.True(row.Success);
            Assert.Contains("已完成整理", row.Message);
            Assert.Contains("扫描请求已取消", row.Message);
            Assert.Contains("手动扫描媒体库", row.Message);
            fixture.AssertTraffic(1, 0, 1);
        });

        Test("R038 实际 Submit：canContinue 在只读校验中拒绝，整批零写入", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(), new FileCase());
            var checks = 0;
            Assert.Throws<OperationCanceledException>(() => fixture.Submit(canContinue: () => ++checks < 2));
            fixture.AssertTraffic(0, 0, 1);
        });

        Test("R038 实际 Submit：最后一份预览不可整理，前面的文件也不能先写", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(), new FileCase());
            fixture.ResetReplies(badPreview: 1);
            Assert.Throws<MoviePilotOperationBlockedException>(() => fixture.Submit());
            fixture.AssertTraffic(0, 0, 2);
        });

        Test("R038 实际 Submit：预先取消只做内存拒绝，零预览零实际请求", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase());
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => fixture.Submit(cancellation.Token));
            fixture.AssertTraffic(0, 0, 0);
        });

        Test("R038 实际 Submit：只读校验后取消但尚未发送，不伪造完成或扫描", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase());
            using var cancellation = new CancellationTokenSource();
            var checks = 0;
            var row = fixture.Submit(cancellation.Token, () =>
            {
                if (++checks == 2) cancellation.Cancel();
                return true;
            }).Single();
            Assert.False(row.Success);
            Assert.Contains("取消", row.Message);
            fixture.AssertTraffic(0, 0, 1);
        });

        Test("R038 实际 Submit：首文件已完成后身份改变，后续未发且不扫描旧媒体库", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(), new FileCase(), new FileCase());
            var checks = 0;
            var rows = fixture.Submit(canContinue: () => ++checks < 5);
            Assert.True(rows[0].Success);
            Assert.Contains("已完成整理", rows[0].Message);
            Assert.False(rows[1].Success);
            Assert.Contains("取消或连接改变", rows[1].Message);
            Assert.False(rows[2].Success);
            Assert.Contains("没有提交", rows[2].Message);
            fixture.AssertTraffic(1, 0, 3);
        });

        Test("R038 实际 Submit：实际请求丢失回执，停止后续写入且不能靠重新预览重发", () =>
        {
            using var fixture = new ReceiptFixture(new FileCase(), new FileCase());
            fixture.Transport.When("transfer/manual?", () =>
            {
                if (fixture.Writes.Count > 0) throw new HttpRequestException("匿名整理回执丢失");
            });
            var rows = fixture.Submit();
            Assert.False(rows[0].Success);
            Assert.Contains("勿重复提交", rows[0].Message);
            Assert.False(rows[1].Success);
            Assert.Contains("没有提交", rows[1].Message);
            fixture.AssertTraffic(1, 0, 2);
            fixture.Transport.When("transfer/manual?", () => { });
            fixture.AssertReplayBlocked();
        });
    }

    private sealed record FileCase(string State = "completed", string Mode = "move", bool HistorySuccess = true,
        string TransferType = "move", bool LineSuccess = true, bool EnvelopeSuccess = true, string? Target = null,
        string TargetStorage = "local");

    /// <summary>
    /// 实际 AniBtService、MoviePilotService、EmbyClient，沿用正式 StubTransport；只安装匿名内存缓存会话。
    /// 计划使用服务真实解析的占位历史，源、目标只是 JSON 字符串，绝不创建或整理媒体文件。
    /// </summary>
    private sealed class ReceiptFixture : IDisposable
    {
        private static readonly AniBtAnime Anime = new("anonymous-anime", 42, "匿名动画");
        private readonly FileCase[] _cases;
        private readonly IReadOnlyList<MoviePilotTransferHistory> _histories;
        private readonly StubTransport _aniBtTransport = new();
        private readonly AniBtClient _aniBtClient;
        private readonly MoviePilotClient _moviePilotClient;
        private readonly MoviePilotService _moviePilot;
        private readonly EmbyHttp _embyHttp;
        private readonly EmbyClient _emby;
        private readonly AniBtService _service;
        private readonly RejectSecretAccess _secrets = new();
        public StubTransport Transport { get; } = new();
        public AniBtScrapePlan Plan { get; }

        public ReceiptFixture(params FileCase[] cases)
        {
            _cases = cases;
            var settings = new AppSettings();
            settings.MoviePilot.Enabled = true;
            settings.MoviePilot.Url = "http://192.0.2.10:3001";
            settings.MoviePilot.Username = "anonymous-fixture";
            settings.MoviePilot.ProtectedPassword = "not-a-secret-fixture";
            _moviePilotClient = new MoviePilotClient(Transport);
            _moviePilot = new MoviePilotService(_moviePilotClient, new MoviePilotCredentials(_secrets), settings);
            InstallAnonymousSession();
            Transport.Answer("media/source", """[{"name":"Bangumi","media_source":"bangumi","media_types":["电视剧"]}]""")
                .Answer("media/42?", """{"title":"匿名动画","media_source":"bangumi","media_id":"42","type":"电视剧"}""")
                .Answer("history/transfer", HistoryReply())
                .Answer("Library/Refresh", "");
            _histories = TransferSubmissionFixture.Complete(_moviePilot.FindTransferHistoriesAsync("匿名动画", CancellationToken.None));

            ResetReplies();
            _aniBtClient = new AniBtClient(_aniBtTransport);
            _embyHttp = new EmbyHttp(Transport);
            _emby = new EmbyClient(_embyHttp, new EmbyConnection(new Uri("http://192.0.2.10:8096/"),
                "anonymous-token", "anonymous-user", "anonymous-fixture", "Fixture", DeviceIdentity.Create("anonymous-device", "1.0")));
            _service = new AniBtService(_aniBtClient, _moviePilot);
            Plan = NewPlan();
        }

        private void InstallAnonymousSession()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var identity = typeof(MoviePilotService).GetMethod("CaptureIdentity", flags)!.Invoke(_moviePilot, null)!;
            var sessionType = typeof(MoviePilotService).GetNestedType("CachedSession", BindingFlags.NonPublic)!;
            var cached = Activator.CreateInstance(sessionType, flags | BindingFlags.Public, null,
                [identity, new MoviePilotSession(new Uri("http://192.0.2.10:3001/"), "anonymous-token", "anonymous-fixture", true)], null);
            typeof(MoviePilotService).GetField("_session", flags)!.SetValue(_moviePilot, cached);
            typeof(MoviePilotService).GetField("_availableIdentity", flags)!.SetValue(_moviePilot, identity);
            Assert.True(_moviePilot.Available, "必须预置可用缓存会话，不能调用测试连接或登录来准备夹具");
        }

        private AniBtScrapePlan NewPlan() => new(_histories.Select(history => new AniBtScrapeRow(history.TargetPath,
            MoviePilotTransfer.PathName(history.SourcePath), new AniBtMatch([Anime], Anime, "auto", "high", "fixture", 1), history)).ToArray(),
            MoviePilotTransferRequest.TypeSeries, _moviePilot.CaptureAniBtConnection(),
            new EmbyItem { Id = "anonymous-episode", Type = EmbyItemType.Episode, SeriesId = "anonymous-series" }, 1, "");

        public IReadOnlyList<AniBtScrapeResult> Submit(CancellationToken cancellationToken = default, Func<bool>? canContinue = null) =>
            Submit(Plan, cancellationToken, canContinue);

        private IReadOnlyList<AniBtScrapeResult> Submit(AniBtScrapePlan plan, CancellationToken cancellationToken = default,
            Func<bool>? canContinue = null) => TransferSubmissionFixture.Complete(_service.SubmitAsync(_emby, plan,
                Enumerable.Repeat(Anime, _cases.Length).ToArray(), cancellationToken, canContinue));

        public static string OldTarget(int index) => $"/library/old/anonymous-{index + 1}.mkv";
        public static string NewTarget(int index) => $"/library/new/anonymous-{index + 1}.mkv";
        private string Target(int index) => _cases[index].Target ?? NewTarget(index);

        private string HistoryReply() => JsonSerializer.Serialize(new
        {
            list = _cases.Select((file, index) => new
            {
                id = index + 1,
                title = "匿名动画",
                type = MoviePilotTransferRequest.TypeSeries,
                media_source = "bangumi",
                media_id = "42",
                mode = file.Mode,
                status = file.HistorySuccess,
                src_fileitem = new { storage = "local", type = "file", path = $"/downloads/anonymous-{index + 1}.mkv" },
                dest_fileitem = new { storage = "local", type = "file", path = OldTarget(index) }
            }).ToArray(),
            total = _cases.Length
        });

        public string Reply(int index, string state, string? source = null, string? target = null, string? extraState = null,
            bool preview = false, bool? success = null)
        {
            var lines = new[]
            {
                new { source = source ?? _histories[index].TransferPath, target = target ?? Target(index), state,
                    success = success ?? _cases[index].LineSuccess, message = "匿名逐文件回执" }
            }.ToList();
            if (extraState is not null) lines.Add(lines[0] with { state = extraState });
            return JsonSerializer.Serialize(new { success = preview || _cases[index].EnvelopeSuccess, data = new { items = lines } });
        }

        public void ResetReplies(string? reply = null, int badPreview = -1)
        {
            var previews = Enumerable.Range(0, _cases.Length)
                .Select(index => Reply(index, "preview", preview: true, success: index != badPreview));
            var actual = Enumerable.Range(0, _cases.Length).Select(index => reply ?? Reply(index, _cases[index].State));
            Transport.Sequence("transfer/manual?", previews.Concat(actual).Select(body => (HttpStatusCode.OK, body)).ToArray());
            // 每次只读校验会重新读取 FileItem 和目录；重设队列不能触碰生产防重账本。
            Transport.Sequence("storage/list", _histories.Select(history => (HttpStatusCode.OK,
                JsonSerializer.Serialize(new { success = true, data = new[] { history.TransferFile } }))).ToArray());
            Transport.Sequence("transfer/manual/target-path", _cases.Select(file => (HttpStatusCode.OK,
                JsonSerializer.Serialize(new
                {
                    success = true,
                    data = new { target_storage = file.TargetStorage, target_path = "/library/new", transfer_type = file.TransferType }
                }))).ToArray());
        }

        public IReadOnlyList<StubTransport.Sent> Writes => Transport.SentTo("transfer/manual?")
            .Where(request => !JsonSerializer.Deserialize<JsonElement>(request.Body).GetProperty("preview").GetBoolean()).ToArray();

        public void AssertTraffic(int writes, int scans, int previews)
        {
            Assert.Equal(writes, Writes.Count, "断言真实 preview:false 请求次数，不能只测复制算法或展示文案");
            Assert.Equal(scans, Transport.SentTo("Library/Refresh").Count, "断言实际扫描请求次数");
            Assert.Equal(previews, Transport.SentTo("transfer/manual?").Count - Writes.Count);
            var requests = Transport.SentTo("").ToList();
            var firstWrite = requests.ToList().FindIndex(request => request.Url.Contains("transfer/manual?", StringComparison.Ordinal) &&
                !JsonSerializer.Deserialize<JsonElement>(request.Body).GetProperty("preview").GetBoolean());
            if (firstWrite >= 0)
                Assert.Equal(_cases.Length, requests.Take(firstWrite).Count(request => request.Url.Contains("transfer/manual?", StringComparison.Ordinal)),
                    "所有文件先只读预览，才能出现第一个实际写入");
            foreach (var request in Writes)
            {
                Assert.Equal("POST", request.Method);
                Assert.Contains("background=false", request.Url);
                var body = JsonSerializer.Deserialize<JsonElement>(request.Body);
                Assert.True(body.GetProperty("reorganize").GetBoolean());
                Assert.False(body.GetProperty("from_history").GetBoolean());
                Assert.False(body.GetProperty("skip_success").GetBoolean());
                Assert.True(body.GetProperty("scrape").GetBoolean());
                Assert.Equal("bangumi", body.GetProperty("media_source").GetString());
                Assert.Equal("42", body.GetProperty("media_id").GetString());
                var index = body.GetProperty("logid").GetInt32() - 1;
                Assert.Equal(_cases[index].TransferType, body.TryGetProperty("transfer_type", out var mode) ? mode.GetString() : "");
                Assert.Equal(_cases[index].TargetStorage, body.GetProperty("target_storage").GetString());
            }
            foreach (var scan in Transport.SentTo("Library/Refresh"))
            {
                Assert.Equal("POST", scan.Method);
                Assert.Equal("", scan.Body);
                Assert.True(requests.IndexOf(scan) > requests.IndexOf(Writes.Last()), "扫描只能在实际整理回执之后请求");
            }
            AssertBoundary();
        }

        public void AssertReplayBlocked()
        {
            // 假 HTTP 的失联也会撤销 Available；只重装同身份匿名缓存，不触碰实际写入账本或走登录。
            InstallAnonymousSession();
            var writes = Writes.Count;
            var scans = Transport.SentTo("Library/Refresh").Count;
            ResetReplies();
            Assert.Throws<MoviePilotOperationBlockedException>(() => Submit());
            Assert.Equal(writes, Writes.Count, "同一计划只读重核后仍不能重新发送");
            ResetReplies();
            var repeated = Submit(NewPlan());
            Assert.False(repeated[0].Success);
            Assert.Contains("不能重复发送", repeated[0].Message, "新计划和新预览也不能绕过实际已发 history/file 账本");
            Assert.Equal(writes, Writes.Count);
            Assert.Equal(scans, Transport.SentTo("Library/Refresh").Count);
            AssertBoundary();
        }

        private void AssertBoundary()
        {
            Assert.Equal(0, _secrets.Calls);
            Assert.Equal(0, _aniBtTransport.SentTo("").Count, "AniBT 识别候选已预置，不能真的发 RSS 或匹配请求");
            foreach (var request in Transport.SentTo(""))
            {
                var path = new Uri(request.Url).AbsolutePath;
                Assert.True(path.EndsWith("/media/source", StringComparison.Ordinal) || path.EndsWith("/media/42", StringComparison.Ordinal) ||
                    path.EndsWith("/history/transfer", StringComparison.Ordinal) || path.EndsWith("/storage/list", StringComparison.Ordinal) ||
                    path.EndsWith("/transfer/manual/target-path", StringComparison.Ordinal) || path.EndsWith("/transfer/manual", StringComparison.Ordinal) ||
                    path.EndsWith("/Library/Refresh", StringComparison.Ordinal), "匿名假 HTTP 出现非 R038 入口：" + path);
            }
        }

        public void Dispose()
        {
            _moviePilot.Dispose();
            _moviePilotClient.Dispose();
            _aniBtClient.Dispose();
            _embyHttp.Dispose();
            Transport.Dispose();
            _aniBtTransport.Dispose();
        }
    }

    private sealed class RejectSecretAccess : ISecretProtector
    {
        public int Calls { get; private set; }
        public string Protect(string plainText)
        {
            Calls++;
            throw new AssertionException("R038 夹具禁止保护凭据");
        }
        public string Unprotect(string cipherText)
        {
            Calls++;
            throw new AssertionException("R038 夹具禁止解密或调用登录");
        }
    }
}
