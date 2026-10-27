using System.Text;
using Momoka.Configuration;
using Momoka.Danmaku;
using Momoka.Infrastructure;
using Momoka.Mpv;
using Momoka.Playback;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;
using Momoka.Shell.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>假播放句柄只记录命令；不创建 mpv、不读取媒体或真实帐号，也不上报观看记录。</summary>
    private async Task PlayerSkipAsync()
    {
        using var fixture = CreateFixture();
        fixture.Settings.Mpv.Pipeline = VideoPipelineKind.Integrated;
        fixture.Settings.Mpv.Backend = MpvBackendKind.BuiltInLibMpv;
        fixture.Settings.Playback.SkipSections = SkipSectionMode.Ask;
        var paths = new AppPaths(Path.Combine(_options.Paths.Root, "player-skip"));
        paths.EnsureCreated();
        using var client = new DandanPlayClient(fixture.Settings, Path.Combine(paths.Root, "cache"), new RejectNetwork());
        var shaders = new ShaderGroupResolver(fixture.Settings.Shaders);
        var playback = new PlaybackService(fixture.Session, fixture.Settings, () => throw new InvalidOperationException("跳过探针禁止创建播放后端"),
            new PlaybackPlanner(fixture.Settings, shaders, paths.ShaderCacheDirectory, paths.ScreenshotDirectory), allowPlayback: false);
        var vm = new PlayerViewModel(playback, fixture.SettingsService, fixture.Session, fixture.Images, shaders, fixture.Dispatcher, client);
        var handle = new SkipProbeHandle();
        Field(playback, "_current", handle);
        var page = new PlayerPage();
        page.Attach(vm, new ShellPage(), _window!);
        _root.Children.Add(page);
        page.ShowChromeForShot(null);
        var button = Get<Button>(page, "SkipButton");
        var row = Get<StackPanel>(page, "SkipRow");
        var text = Get<TextBlock>(page, "SkipText");
        var skips = Get<SkipCoordinator>(vm, "_skips");
        var opening = new SkipSection(SkipSectionKind.Opening, 30, 120, "片头");
        var ending = new SkipSection(SkipSectionKind.Ending, 1400, 1500, "片尾");
        var report = new List<string>();
        try
        {
            // 前一条播放用例可能留下窄窗口；通用 OSD 间距检查按足够容纳长标题的窗口档执行。
            ResizeProbeClient(1100, 680);
            _window!.SynchronizeContentLayout();
            await LayoutAsync(page);
            // 负向覆盖：检查不能把重新塞入的提示文字或竖排内容误认为已简化。同步还原，不提交一帧坏界面。
            var extraTip = new TextBlock { Text = "不应出现在按钮里的提示" };
            row.Children.Add(extraTip);
            try { Require(!page.SkipContentIsSimple, "单行检查没有发现额外提示"); }
            finally { row.Children.Remove(extraTip); }
            var orientation = row.Orientation;
            row.Orientation = Orientation.Vertical;
            try { Require(!page.SkipContentIsSimple, "单行检查没有发现竖排内容"); }
            finally { row.Orientation = orientation; }
            Require(page.SkipContentIsSimple, "负向夹具还原后按钮仍不是单行");
            var mapping = page.ProbeSkipAndChapters();
            Require(mapping.Ok, mapping.Detail);
            var narration = page.ProbeNarration();
            Require(narration.Ok, narration.Detail);
            var clearance = page.ProbeClearance();
            Require(clearance.Ok, clearance.Detail);

            vm.Title = "跳过按钮 · 离线验证";
            vm.Subtitle = "仅图标和标题 · 没有提示文字或倒计时";
            foreach (var (width, height, section) in new[] { (1100, 680, opening), (760, 500, ending) })
            {
                ResizeProbeClient(width, height);
                page.ShowChromeForShot(null);
                Arm(section);
                await SettlePictureAsync();
                CheckLayout(fullscreen: false);
                await SaveSkipShotAsync($"player-skip-{width}");
                if (width == 1100)
                {
                    Require(VisualStateManager.GoToState(button, "PointerOver", false), "跳过按钮没有悬停态");
                    await SaveSkipShotAsync("player-skip-hover");
                    Require(VisualStateManager.GoToState(button, "Pressed", false), "跳过按钮没有按下态");
                    await SaveSettledFrameAsync(button, "player-skip-pressed-render.png");
                    VisualStateManager.GoToState(button, "Normal", false);
                }
            }

            _window!.ToggleMaximize();
            await UntilAsync(() => _window.IsMaximized);
            Arm(opening);
            await SettlePictureAsync();
            CheckLayout(fullscreen: true);
            await SaveSkipShotAsync("player-skip-maximized");

            _window.Fullscreen = true;
            Arm(ending);
            await SettlePictureAsync();
            CheckLayout(fullscreen: true);
            await SaveSkipShotAsync("player-skip-fullscreen");

            // Invoke 走真实 Button.Command；回车和 Esc 走页面与键盘共用的处理器，只记录假句柄命令。
            Arm(opening);
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) ?? new ButtonAutomationPeer(button);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => handle.Commands.Any(command => command.StartsWith("seek|", StringComparison.Ordinal)));
            Require(handle.Commands.Contains("seek|120|absolute+exact") && !vm.SkipOffered, "点击没有跳到片头结尾并收起按钮");
            Require(!handle.Commands.Any(command => command.Contains("pause", StringComparison.Ordinal)), "跳过按钮错误地暂停了播放");

            Arm(ending);
            ((IWin32KeySink)page).Handle((int)VirtualKey.Enter);
            // 片尾落点保留现有规则：停在总时长前半秒，由播放器自然走到 EOF。
            Require(handle.Commands.Contains("seek|1499.5|absolute+exact") && !vm.SkipOffered, "回车没有接受片尾提示或改变了原有 EOF 落点");
            Arm(opening);
            ((IWin32KeySink)page).Handle((int)VirtualKey.Escape);
            Require(!vm.SkipOffered && handle.Commands.Count == 0 && _window.Fullscreen, "Esc 没有只取消提示，或误退出全屏/执行命令");
            vm.ApplySkipOffer();
            Require(!vm.SkipOffered, "取消后仍在同一区间又冒出提示");

            Arm(opening);
            SetStatus(31, paused: true);
            vm.ApplySkipOffer();
            Require(vm.SkipOffered, "暂停错误地撤掉了提示");
            SetStatus(46.1);
            vm.ApplySkipOffer();
            Require(!vm.SkipOffered && button.Visibility == Visibility.Collapsed, "移除倒计时条后十五秒时限失效");
            Arm(opening);
            Field(playback, "_current", null);
            vm.ApplySkipOffer();
            Require(button.Visibility == Visibility.Collapsed, "没有可控制播放时还显示跳过按钮");
            report.Add("点击、回车确认、Esc 取消、暂停保持、十五秒过期和无播放隐藏均通过；未创建播放后端。");
            File.WriteAllLines(Path.Combine(_options.Paths.LogDirectory, "player-skip-layout.txt"), report, new UTF8Encoding(false));
        }
        finally
        {
            _window!.Fullscreen = false;
            if (_window.IsMaximized) _window.ToggleMaximize();
            Field(playback, "_current", null);
            Field(vm, "_playerUp", false);
            page.Detach();
            _root.Children.Remove(page);
            page.ReleaseVideoSurface();
            await vm.ShutdownAsync();
        }

        async Task SettlePictureAsync()
        {
            _window!.SynchronizeContentLayout();
            await LayoutAsync(page);
            // 窗口几何与 XAML 属性都能先于屏幕提交完成；两张真实渲染回调之后再抓屏。
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var frames = 0;
            void OnRendering(object? sender, object args)
            {
                if (++frames >= 2) ready.TrySetResult();
            }
            CompositionTarget.Rendering += OnRendering;
            try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
            finally { CompositionTarget.Rendering -= OnRendering; }
        }

        async Task SaveSkipShotAsync(string name)
        {
            await SaveSettledFrameAsync(page, name + "-render.png");
            await SettlePictureAsync();
            await SaveProbeScreenAsync(name + "-screen.png");
        }

        void Arm(SkipSection section)
        {
            handle.Commands.Clear();
            skips.Begin([section], 1500);
            Field(vm, "_playerUp", true);
            SetStatus(section.Start + 1);
            vm.ApplySkipOffer();
        }

        void SetStatus(double position, bool paused = false)
        {
            handle.Status = new PlayerStatus { Position = position, Duration = 1500, Loaded = true, Paused = paused };
            Call(vm, "ApplyStatus", handle.Status);
        }

        void CheckLayout(bool fullscreen)
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) ?? new ButtonAutomationPeer(button);
            var bounds = Bounds(button);
            var bar = Bounds(Get<Grid>(page, "Bar"));
            var (clientWidth, clientHeight) = _window!.ClientSize;
            var raster = page.XamlRoot.RasterizationScale;
            Require(Math.Abs(page.ActualWidth * raster - clientWidth) <= 1 && Math.Abs(page.ActualHeight * raster - clientHeight) <= 1,
                "截图前播放页尚未铺满真实客户区");
            var padX = fullscreen ? 23.4 : 18;
            var padY = fullscreen ? 27.3 : 21;
            Require(page.SkipContentIsSimple && row.ActualHeight > 0, "按钮不是有尺寸的图标/标题单行");
            Require(peer.GetName() == text.Text && peer.GetAutomationId() == "SkipButton", "按钮动态读屏名称或自动化句柄丢失");
            Require(bounds.Left >= 0 && bounds.Right <= page.ActualWidth + 0.5 && bounds.Bottom <= bar.Top + 0.5, "跳过按钮超出画面或压住控制条");
            // 位置与独占一致（elements/SkipButton.lua）：底边离**控制行上沿**一个 padY，右边离画面右沿也是这个数
            // （窄窗为让开音量条会左移，那一支由下面那条避让检查兜住）。量的是行顶而不是控制条顶 ——
            // 行顶上还压着这一行自己的内缩（窗口 8 / 大档 10）。
            var rowTop = Bounds(Get<Grid>(page, "TransportRow")).Top;
            var gap = rowTop - bounds.Bottom;
            Require(Math.Abs(gap - padY) <= 0.5, $"跳过按钮离控制行的距离不是独占的 {padY}（实测 {gap:0.##}）");
            Require(bounds.Right <= page.ActualWidth - padY + 0.5, "跳过按钮比独占模式更靠右");
            var rail = Bounds(Get<FrameworkElement>(page, "Rail"));
            Require(!(bounds.Left < rail.Right && rail.Left < bounds.Right && bounds.Top < rail.Bottom && rail.Top < bounds.Bottom),
                "窄/矮窗口的跳过按钮压住音量条");
            Require(Math.Abs(button.Padding.Left - padX) < 0.01 && Math.Abs(button.Padding.Top - padY) < 0.01, "窗口/全屏放大内边距不正确");
            Require(Math.Abs(button.CornerRadius.TopLeft - (fullscreen ? 2.6 : 2)) < 0.01, "没有使用独占模式的小圆角");
            Require(Math.Abs(button.ActualHeight - row.ActualHeight - 2 * padY - button.BorderThickness.Top - button.BorderThickness.Bottom) < 1,
                "删掉提示和倒计时后仍留下第二行空白");
            report.Add($"{page.ActualWidth:0}×{page.ActualHeight:0} {(fullscreen ? "大档" : "窗口")} {text.Text} 按钮 {bounds.Width:0.##}×{bounds.Height:0.##} 离控制行 {gap:0.##}；单行、位置、避让、圆角、缩放和 UIA 名称通过。");
        }

        Rect Bounds(FrameworkElement element)
        {
            var point = element.TransformToVisual(page).TransformPoint(new Point());
            return new(point.X, point.Y, element.ActualWidth, element.ActualHeight);
        }
    }

    private sealed class SkipProbeHandle : IPlaybackHandle, IPlayerControl
    {
        public List<string> Commands { get; } = [];
        public PlayerStatus Status { get; set; } = new();
        public bool HasControlChannel => true;
        public bool? PictureInHostWindow => true;
        public bool IsPaused => Status.Paused;
        public event Action<bool>? PauseChanged { add { } remove { } }
        public event Action<PlayerStatus>? StatusChanged { add { } remove { } }
        public event Action<IReadOnlyList<MpvTrack>>? TracksChanged { add { } remove { } }
        public Task<double?> GetPositionAsync(CancellationToken cancellationToken) => Task.FromResult<double?>(Status.Position);
        public Task<PlaybackExit> WaitForExitAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("禁止播放");
        public Task StopAsync() => Task.CompletedTask;
        public Task ShowMessageAsync(string text) => Task.CompletedTask;
        public Task SetPropertyAsync(string name, object? value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<MpvTrack>> GetTracksAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MpvTrack>>([]);
        public Task<double?> GetNumberAsync(string name, CancellationToken cancellationToken) => Task.FromResult<double?>(null);
        public Task<string?> GetTextAsync(string name, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<bool> CommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Commands.Add(string.Join('|', arguments));
            return Task.FromResult(true);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
