using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class StoreTrailerWebTests
{
    internal static void Register()
    {
        Test("网页字幕：生产消息解析保留两轨名称、选轨和通知", () =>
        {
            using var fixture = new Fixture();
            var changes = 0;
            fixture.Preview.CaptionsChanged += () => changes++;
            fixture.Core.RaiseMessage("captions:" + Captions("en"));
            Assert.Equal(1, changes);
            Assert.True(fixture.Preview.Captions.Select(track => (track.Language, track.Name))
                .SequenceEqual(new[] { ("zh", "匿名中文"), ("en", "Anonymous English") }));
            Assert.Equal("en", fixture.Preview.SelectedCaption);
        });
        Test("网页字幕：坏JSON和过大消息不覆盖已解析轨道", () =>
        {
            using var fixture = new Fixture();
            fixture.Core.RaiseMessage("captions:" + Captions("zh"));
            var changes = 0;
            fixture.Preview.CaptionsChanged += () => changes++;
            fixture.Core.RaiseMessage("captions:{");
            fixture.Core.RaiseMessage("captions:" + new string(' ', 16385));
            Assert.Equal(0, changes);
            Assert.Equal("zh", fixture.Preview.SelectedCaption);
            Assert.Equal(2, fixture.Preview.Captions.Count);
        });
        Test("网页字幕：空语言与重复轨拒绝，名称缺失回到语言码", () =>
        {
            using var fixture = new Fixture();
            fixture.Core.RaiseMessage("""captions:{"language":"","tracks":[null,{}, {"language":""},{"language":"zh"},{"language":"zh","name":"重复"},{"language":"en","name":"English"}]}""");
            Assert.Null(fixture.Preview.SelectedCaption);
            Assert.True(fixture.Preview.Captions.Select(track => (track.Language, track.Name))
                .SequenceEqual(new[] { ("zh", "zh"), ("en", "English") }));
        });
        Test("网页字幕：选择与关闭走生产WebMessage队列，回读才发轨道通知", () =>
        {
            using var fixture = new Fixture();
            fixture.Core.RaiseMessage("captions:" + Captions(""));
            var changes = 0;
            fixture.Preview.CaptionsChanged += () => changes++;
            fixture.Preview.SelectCaption("zh");
            Assert.Equal("caption:zh", fixture.Core.Commands[^1]);
            Assert.Equal(0, changes);
            fixture.Core.RaiseMessage("captions:" + Captions("zh"));
            Assert.Equal(1, changes);
            fixture.Preview.SelectCaption(null);
            Assert.Equal("caption:", fixture.Core.Commands[^1]);
            fixture.Core.RaiseMessage("captions:" + Captions(""));
            Assert.Null(fixture.Preview.SelectedCaption);
            Assert.Equal(2, changes);
        });
        Test("网页预告：生产播放通知按实际消息变化，重复状态不重复通知", () =>
        {
            using var fixture = new Fixture();
            var states = new List<bool>();
            fixture.Preview.PlaybackChanged += () => states.Add(fixture.Preview.IsPlaying);
            foreach (var message in new[] { "playing", "playing", "paused", "paused", "playing", "blocked" })
                fixture.Core.RaiseMessage(message);
            Assert.True(states.SequenceEqual(new[] { true, false, true, false }));
            Assert.False(fixture.Preview.IsPlaying);
        });
        Test("网页预告：错误来源、取消和释放后消息不改变生产状态", () =>
        {
            using var fixture = new Fixture();
            fixture.Core.RaiseMessage("captions:" + Captions("zh"), "https://outside.invalid/index.html");
            fixture.Core.RaiseMessage("playing", "https://momoka/other.html");
            Assert.Equal(0, fixture.Preview.Captions.Count);
            Assert.False(fixture.Preview.IsPlaying);
            fixture.Cancel.Cancel();
            fixture.Core.RaiseMessage("captions:" + Captions("zh"));
            Assert.Equal(0, fixture.Preview.Captions.Count);
            fixture.Preview.Dispose();
            var commands = fixture.Core.Commands.Count;
            fixture.Core.RaiseMessage("playing");
            fixture.Preview.Send("caption:zh");
            Assert.False(fixture.Preview.IsPlaying);
            Assert.Equal(commands, fixture.Core.Commands.Count);
            Assert.True(fixture.Preview.Element.Closed);
        });
        Test("网页预告：生产控制队列和弹窗下载守卫保留", () =>
        {
            using var fixture = new Fixture();
            Assert.True(fixture.Core.Commands.SequenceEqual(new[] { "mute", "pause" }));
            fixture.Preview.SetActive(true);
            Assert.Equal("play", fixture.Core.Commands[^1]);
            fixture.Preview.SetActive(false);
            Assert.Equal("pause", fixture.Core.Commands[^1]);
            fixture.Preview.Mute(false);
            Assert.Equal("unmute", fixture.Core.Commands[^1]);
            Assert.False(fixture.Core.IsMuted);
            Assert.False(fixture.Core.TryOpenWindow());
            Assert.False(fixture.Core.TryDownload());
        });
        Test("网页字幕探针：实际解析通知、原生菜单和独立文本轨回读仍接线", () =>
        {
            var probe = Read("src/Momoka.Shell/ShellNavigationProbe.StoreTrailerWeb.cs");
            foreach (var required in new[] { "preview.CaptionsChanged +=", "preview.PlaybackChanged +=", "preview.SelectedCaption == language",
                "((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();", "await closed.Task.WaitAsync", "GetProperty(\"apiCaption\")",
                "v.addTextTrack('subtitles'", "o.events.onApiChange()", "o.events.onStateChange({data:2})", "probe-pause",
                "captionChanges > previousChanges", "blockedUnknownRequests={blocked}" }) Assert.Contains(required, probe);
        });
    }

    private static string Captions(string language) =>
        $$"""{"language":"{{language}}","tracks":[{"language":"zh","name":"匿名中文"},{"language":"en","name":"Anonymous English"}]}""";

    private static string Read(string relative)
    {
        for (var root = new DirectoryInfo(AppContext.BaseDirectory); root is not null; root = root.Parent)
            if (File.Exists(Path.Combine(root.FullName, "Momoka.sln"))) return File.ReadAllText(Path.Combine(root.FullName, relative));
        throw new AssertionException("找不到本树测试源");
    }

    private sealed class Fixture : IDisposable
    {
        internal StoreYouTubePreview Preview { get; } = new();
        internal CancellationTokenSource Cancel { get; } = new();
        internal Microsoft.Web.WebView2.Core.CoreWebView2 Core => Preview.Element.TestCore;

        internal Fixture()
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "trailer-web-boundary", Guid.NewGuid().ToString("N"));
            Preview.StartAsync("Abc_123-xyz", folder, Cancel.Token, _ => { }).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Preview.Dispose();
            Cancel.Dispose();
        }
    }
}
