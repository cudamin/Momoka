using System.Text.Json;
using Momoka.Configuration;
using Momoka.Mpv;
using Momoka.Translation;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        Test("字幕翻译：默认关闭，不建图层或发送原文", () => Run(async () =>
        {
            var session = new FakeSession();
            await SubtitleTranslationRuntime.RunAsync(session, new(), "placeholder-key", "not-used",
                (_, _, _, _) => throw new AssertionException("sent when off"), _ => throw new AssertionException("notice when off"), default);
            Assert.Equal(0, session.OverlayRequests);
        }));
        Test("字幕翻译：缺密钥只提示一次，保留原字幕", () => Run(async () =>
        {
            var session = new FakeSession(); var notices = new List<string>();
            await SubtitleTranslationRuntime.RunAsync(session, Enabled(), "", "not-used",
                (_, _, _, _) => throw new AssertionException("sent without key"), notices.Add, default);
            Assert.Equal(1, notices.Count); Assert.Contains("API 密钥", notices[0]); Assert.Equal(0, session.OverlayRequests);
        }));
        Test("字幕翻译：图层不可用不发请求", () => Run(async () =>
        {
            var session = new FakeSession { OverlayAvailable = false }; var notices = new List<string>();
            await SubtitleTranslationRuntime.RunAsync(session, Enabled(), "placeholder", "missing.lua",
                (_, _, _, _) => throw new AssertionException("sent without overlay"), notices.Add, default);
            Assert.Equal(1, notices.Count); Assert.Equal(0, session.Shown.Count);
        }));
        Test("字幕翻译：重复采样同一句只外发一次", () => Run(async () =>
        {
            var session = new FakeSession { Cue = Cue("hello") }; using var stop = new CancellationTokenSource(); var calls = 0;
            var work = Start(session, stop.Token, (_, _, _, _) => { calls++; return Task.FromResult("你好"); });
            await Until(() => session.Shown.Count == 1); await Task.Delay(450); stop.Cancel(); await work;
            Assert.Equal(1, calls); Assert.Equal("你好", session.Shown[0].Text);
        }));
        Test("字幕翻译：不同时间重复台词使用内存缓存", () => Run(async () =>
        {
            var session = new FakeSession { Cue = Cue("same", 1, 3) }; using var stop = new CancellationTokenSource(); var calls = 0;
            var work = Start(session, stop.Token, (_, _, _, _) => { calls++; return Task.FromResult("相同台词"); });
            await Until(() => session.Shown.Count == 1); session.Cue = null;
            var reads = session.Reads; await Until(() => session.Reads > reads + 1); session.Cue = Cue("same", 10, 13);
            await Until(() => session.Shown.Count == 2); stop.Cancel(); await work;
            Assert.Equal(1, calls); Assert.Equal(10d, session.Shown[1].Cue.Start!.Value);
        }));
        Test("字幕翻译：HTTP期间仍采样，旧句取消，迟到回复丢弃", () => Run(async () =>
        {
            var first = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var session = new FakeSession { Cue = Cue("first") }; using var stop = new CancellationTokenSource();
            CancellationToken firstToken = default; var calls = 0;
            var work = Start(session, stop.Token, (_, text, _, token) =>
            {
                Interlocked.Increment(ref calls); if (text == "first") { firstToken = token; return first.Task; }
                return Task.FromResult("第二句");
            });
            await Until(() => Volatile.Read(ref calls) == 1); session.Cue = Cue("second", 4, 8);
            await Until(() => firstToken.IsCancellationRequested); first.TrySetResult("迟到第一句");
            await Until(() => session.Shown.Count == 1); stop.Cancel(); await work;
            Assert.Equal("second", session.Shown[0].Cue.Text); Assert.Equal("第二句", session.Shown[0].Text); Assert.Equal(2, calls);
        }));
        Test("字幕翻译：停止或注销取消HTTP，旧文件不写下一集", () => Run(async () =>
        {
            var session = new FakeSession { Cue = Cue("hello") }; using var stop = new CancellationTokenSource();
            var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var cancelled = false;
            var work = Start(session, stop.Token, async (_, _, _, token) =>
            {
                began.TrySetResult(); try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { cancelled = true; throw; }
                return "never";
            });
            await began.Task.WaitAsync(TimeSpan.FromSeconds(3)); session.Available = false; session.Scope.Cancel();
            await work.WaitAsync(TimeSpan.FromSeconds(3)); Assert.True(cancelled); Assert.Equal(0, session.Shown.Count); Assert.Equal(0, session.StaleWrites);
        }));
        Test("字幕翻译：关闭清除独立图层", () => Run(async () =>
        {
            var session = new FakeSession { Cue = Cue("hello") }; using var stop = new CancellationTokenSource();
            var work = Start(session, stop.Token, (_, _, _, _) => Task.FromResult("你好"));
            await Until(() => session.Shown.Count == 1); var before = session.Clears; stop.Cancel(); await work; Assert.True(session.Clears > before);
        }));
        Test("字幕翻译：请求失败不泄漏响应，不重发同句", () => Run(async () =>
        {
            var session = new FakeSession { Cue = Cue("hello") }; var notices = new List<string>(); using var stop = new CancellationTokenSource(); var calls = 0;
            var work = Start(session, stop.Token, (_, _, _, _) => { calls++; return Task.FromException<string>(new HttpRequestException("secret-body-placeholder")); }, notices.Add);
            await Until(() => notices.Count == 1); await Task.Delay(650); stop.Cancel(); await work;
            Assert.Equal(1, calls); Assert.DoesNotContain("secret-body-placeholder", notices[0]); Assert.Equal(0, session.Shown.Count);
        }));
        Test("字幕翻译：图形字幕或空句没有云请求", () => Run(async () =>
        {
            var session = new FakeSession(); using var stop = new CancellationTokenSource(550); var calls = 0;
            await Start(session, stop.Token, (_, _, _, _) => { calls++; return Task.FromResult("never"); }); Assert.Equal(0, calls);
        }));
        Test("字幕翻译：OSD载荷按JSON数据隔离，外观归一", () =>
        {
            var cue = Cue("\"}, {\\pos(0,0)}\\N原文"); var json = MpvSubtitleTranslation.Payload(cue, "译文\n第二行", 100, -10);
            using var parsed = JsonDocument.Parse(json); Assert.Equal(cue.Text, parsed.RootElement.GetProperty("original").GetString());
            Assert.Equal(64, parsed.RootElement.GetProperty("fontSize").GetInt32()); Assert.Equal(10, parsed.RootElement.GetProperty("position").GetInt32());
        });
        Test("字幕翻译：备份不外带密钥端点模型，恢复不清本机连接", () =>
        {
            var settings = new AppSettings(); var live = settings.SubtitleTranslation;
            live.Enabled = true; live.Provider = SubtitleTranslationProvider.OpenAi; live.AiEndpoint = "https://private-ai.example/v1"; live.AiModel = "private-model";
            live.ProtectedGoogleApiKey = "google-cipher"; live.ProtectedMicrosoftApiKey = "microsoft-cipher"; live.ProtectedAiApiKey = "ai-cipher";
            live.MicrosoftRegion = "private-region"; live.SourceLanguage = "ja"; live.TargetLanguage = "en"; live.FontSize = 40; live.Position = 70;
            var backup = SettingsPreferences.ToBackupDocument(settings); var json = JsonSerializer.Serialize(backup);
            foreach (var secret in new[] { "google-cipher", "microsoft-cipher", "ai-cipher", "private-ai", "private-model", "private-region" }) Assert.DoesNotContain(secret, json);
            Assert.False(backup.SubtitleTranslation.Enabled); Assert.Equal("ja", backup.SubtitleTranslation.SourceLanguage);
            SettingsReset.Restore(settings); Assert.True(ReferenceEquals(live, settings.SubtitleTranslation)); Assert.True(live.Enabled);
            Assert.Equal("ai-cipher", live.ProtectedAiApiKey); Assert.Equal("private-model", live.AiModel); Assert.Equal("auto", live.SourceLanguage); Assert.Equal(32, live.FontSize);
        });
        Test("字幕翻译：旧配置JSON null保持默认关闭，外观钳制", () =>
        {
            var settings = SettingsMigration.FromJson("{\"SchemaVersion\":26,\"SubtitleTranslation\":null}", new FakeProtector());
            Assert.False(settings.SubtitleTranslation.Enabled); settings.SubtitleTranslation.FontSize = 1000; settings.SubtitleTranslation.Position = -100;
            SettingsMigration.Normalize(settings); Assert.Equal(64, settings.SubtitleTranslation.FontSize); Assert.Equal(10, settings.SubtitleTranslation.Position);
        });
        Test("字幕原生读数：map JSON可读，JSON字符串不能冒充cue", () =>
        {
            var value = SubtitleTranslationCue.FromNativeJson("{\"text\":\"原句\",\"sid\":\"2\",\"start\":1,\"end\":3}");
            Assert.NotNull(value);
            Assert.Equal("原句", value!.Text);
            Assert.Equal(1d, value.Start!.Value);
            Assert.Null(SubtitleTranslationCue.FromNativeJson("\"{\\\"text\\\":\\\"原句\\\"}\""));
        });
        Test("字幕原生读数：数组、坏JSON、空cue和关闭轨道拒绝", () =>
        {
            foreach (var input in new[] { "[]", "not-json", "\"\"", "{\"text\":\"\",\"sid\":\"2\"}", "{\"text\":\"x\",\"sid\":\"no\"}" })
                Assert.Null(SubtitleTranslationCue.FromNativeJson(input));
        });
        Test("字幕原生读数：非数字时间不抛错，未知时间保持空", () =>
        {
            var cue = SubtitleTranslationCue.FromNativeJson("{\"text\":\"x\",\"sid\":\"2\",\"start\":\"oops\",\"end\":null}");
            Assert.NotNull(cue); Assert.Null(cue!.Start); Assert.Null(cue.End);
        });
        foreach (var (name, work) in AsyncCases)
        {
            Exception? failure = null;
            try { await work(); } catch (Exception error) { failure = error; }
            var recorded = failure;
            TestHarness.Test(name, () => { if (recorded is not null) throw recorded; });
        }
        return TestHarness.Run();
    }

    private static SubtitleTranslationSettings Enabled() => new() { Enabled = true };
    private static SubtitleTranslationCue Cue(string text, double start = 1, double end = 3) => new(text, "2", start, end);
    private static Task Start(FakeSession session, CancellationToken token, Func<SubtitleTranslationSettings, string, string, CancellationToken, Task<string>> translate,
        Action<string>? notice = null) => SubtitleTranslationRuntime.RunAsync(session, Enabled(), "placeholder-key", "anonymous-script.lua", translate, notice ?? (_ => { }), token);
    private static Task Run(Func<Task> work) => work();
    private static readonly List<(string Name, Func<Task> Work)> AsyncCases = [];
    private static void Test(string name, Func<Task> work) => AsyncCases.Add((name, work));
    private static void Test(string name, Action work) => TestHarness.Test(name, work);
    private static async Task Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4); while (!ready())
        {
            if (DateTime.UtcNow > deadline) throw new AssertionException("fake translation timed out"); await Task.Delay(20);
        }
    }
    private sealed class FakeSession : ISubtitleTranslationPlaybackSession
    {
        public long Generation => 1; public bool Available = true; public bool IsCurrent => Available;
        public CancellationTokenSource Scope { get; } = new(); public CancellationToken Lifetime => Scope.Token;
        public volatile SubtitleTranslationCue? Cue; public bool OverlayAvailable = true;
        public int OverlayRequests; public int Clears; public int Reads; public int StaleWrites;
        public List<(SubtitleTranslationCue Cue, string Text)> Shown { get; } = [];
        public Task<bool> EnsureOverlayAsync(string scriptPath, CancellationToken cancellationToken) { OverlayRequests++; return Task.FromResult(IsCurrent && OverlayAvailable); }
        public Task<SubtitleTranslationCue?> ReadCueAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Interlocked.Increment(ref Reads); return Task.FromResult(IsCurrent ? Cue : null); }
        public Task<bool> ShowAsync(SubtitleTranslationCue cue, string text, int font, int position, CancellationToken cancellationToken)
        {
            if (!IsCurrent) { StaleWrites++; return Task.FromResult(false); }
            lock (Shown) Shown.Add((cue, text)); return Task.FromResult(true);
        }
        public Task ClearAsync(CancellationToken cancellationToken) { if (IsCurrent) Interlocked.Increment(ref Clears); return Task.CompletedTask; }
    }
    private sealed class FakeProtector : ISecretProtector
    {
        public string Protect(string value) => "cipher:" + value; public string Unprotect(string value) => value[7..];
    }
}
