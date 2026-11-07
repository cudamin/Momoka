using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Playback;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task DiagnosticsInitialTitleAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "diagnostics-initial-title"));
        paths.EnsureCreated();
        var playback = new PlaybackService(fixture.Session, fixture.Settings,
            () => throw new InvalidOperationException("标题探针禁止创建播放后端"), null!, allowPlayback: false);
        await using var handle = new DiagnosticsTitleHandle();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var current = typeof(PlaybackService).GetField("_current", flags)!;
        var generation = typeof(PlaybackService).GetField("_playbackGeneration", flags)!;
        var publish = typeof(PlaybackService).GetMethod("RaiseNowPlaying", flags)!;
        // Only a managed status owner is installed; Start/Play/monitor/commands are never called.
        current.SetValue(playback, handle);
        generation.SetValue(playback, 1L);
        publish.Invoke(playback, [new EmbyItem { Name = "首次进入前的匿名片名", Type = EmbyItemType.Movie }, 1L]);
        var page = new DiagnosticsPage();
        _root.Children.Add(page);
        try
        {
            page.ViewModel.Attach(fixture.Actions, fixture.SettingsService, fixture.Session, fixture.Capabilities,
                playback, paths, fixture.Dispatcher, new DiagnosticsProbeClipboard(), fixture.Launcher, new RingBufferLogSink(4));
            await page.ViewModel.ReloadAsync();
            ResizeProbeClient(760, 680);
            await LayoutAsync(page);
            AssertTitle("首次进入前的匿名片名");
            await SaveProbeScreenAsync("diagnostics-initial-title-screen.png");
            generation.SetValue(playback, 2L);
            publish.Invoke(playback, [new EmbyItem { Name = "当前匿名片名", Type = EmbyItemType.Movie }, 2L]);
            publish.Invoke(playback, [new EmbyItem { Name = "迟到旧匿名片名", Type = EmbyItemType.Movie }, 1L]);
            await SettleAsync();
            await LayoutAsync(page);
            AssertTitle("当前匿名片名");
            await SaveProbeScreenAsync("diagnostics-current-title-screen.png");
            Require(fixture.Actions.Plays == 0 && handle.Calls == 0, "诊断标题探针启动了播放或访问后端");
        }
        finally
        {
            page.Release();
            _root.Children.Remove(page);
            current.SetValue(playback, null);
            ResizeInspect(1420, 980);
        }

        void AssertTitle(string expected)
        {
            Require(page.ViewModel.NowPlaying == expected, "诊断首次读取或迟到旧标题覆盖了当前片名");
            Require(Descendants(Get<StackPanel>(page, "PlaybackSummary")).OfType<TextBlock>()
                .Any(text => text.Text == expected && text.ActualHeight > 0), "当前片名没有进入真实诊断文字控件");
        }
    }

    private sealed class DiagnosticsTitleHandle : IPlaybackHandle
    {
        internal int Calls { get; private set; }
        public bool HasControlChannel => false;
        public bool IsPaused => false;
        public event Action<bool>? PauseChanged { add { } remove { } }
        private Task<T> Reject<T>() { Calls++; throw new InvalidOperationException("匿名标题边界禁止后端读取或播放"); }
        public Task<double?> GetPositionAsync(CancellationToken token) => Reject<double?>();
        public Task<PlaybackExit> WaitForExitAsync(CancellationToken token) => Reject<PlaybackExit>();
        public Task StopAsync() => Reject<bool>();
        public Task ShowMessageAsync(string text) => Reject<bool>();
        public Task SetPropertyAsync(string name, object? value, CancellationToken token) => Reject<bool>();
        public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken token) => Reject<IReadOnlyList<MpvTrack>>();
        public Task<double?> GetNumberAsync(string name, CancellationToken token) => Reject<double?>();
        public Task<string?> GetTextAsync(string name, CancellationToken token) => Reject<string?>();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
