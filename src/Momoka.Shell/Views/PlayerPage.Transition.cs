using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Diagnostics;
using Momoka.Playback;
using Momoka.Shell.Interop;
using Momoka.Shell.Windowing;

namespace Momoka.Shell.Views;

public sealed partial class PlayerPage
{
    private bool _onStage;
    private bool _inputSuspended;
    private bool _enterAnimating;
    private bool _startupHandoverPending;
    private VideoFrameOverlay? _startupCover;
    private bool _startupCoverSettled;
    private int _presentationGeneration;
    private PlayerMotion.Transition? _pageTransition;
    private DispatcherQueueTimer? _poseDriver;
    private const int PoseTickMilliseconds = 16;

    /// <summary>
    /// 变换由同一颗钟逐拍写入，反向时从当前姿势续接。不能用 Storyboard 驱动 RenderTransform：
    /// 岛在进场中途重新布局时，该动画曾停在起点且不发 Completed，控制条因此被推出窗口。
    /// <para>
    /// 只管「小窗里的淡入淡出」这一种转场。起播开了自动全屏的那条不走这里 —— 它「一上来就整屏」，
    /// 由 <see cref="EnterFullscreenAtOnce"/> 直接整屏、纯色层盖住长大那一拍（用户令 2026-09-25）。
    /// </para>
    /// </summary>
    private void TransitionPage(bool entering, Action landed, Action<double>? progress = null)
    {
        var now = Now;
        var from = _pageTransition?.At(now)
            ?? (entering ? PlayerMotion.Pose.Entering : PlayerMotion.Pose.Visible);
        CancelPageTransition();

        if (!HomeMotion.AnimationsEnabled || XamlRoot is null)
        {
            ApplyPagePose(PlayerMotion.Pose.Visible);
            progress?.Invoke(1);
            landed();
            return;
        }

        var transition = PlayerMotion.Page(entering, from, now);
        _pageTransition = transition;
        ApplyPagePose(transition.From);
        _enterAnimating = entering;

        var driver = DispatcherQueue.CreateTimer();
        driver.IsRepeating = true;
        driver.Interval = TimeSpan.FromMilliseconds(PoseTickMilliseconds);
        _poseDriver = driver;
        driver.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_poseDriver, driver)) { driver.Stop(); return; }

            var elapsed = Now - transition.StartedAt;
            if (elapsed < transition.Duration)
            {
                progress?.Invoke(Math.Clamp(elapsed / (double)transition.Duration, 0, 1));
                ApplyPagePose(transition.At(Now));
                return;
            }

            CancelPageTransition();
            ApplyPagePose(PlayerMotion.Pose.Visible);
            progress?.Invoke(1);
            landed();
        };
        driver.Start();
        Log.Debug(Category, $"播放页{(entering ? "进场" : "退场")}：{transition.Duration}ms");
    }

    /// <summary>先提交不透明加载层，再改窗口；首帧淡入必须等这一交接完成，不能靠两个计时器碰运气。</summary>
    private async Task CompletePlayerEntranceAsync()
    {
        if (!_onStage || _window is not { } window) return;
        var generation = _windowChangeGeneration;
        var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(this).Compositor;
        try
        {
            await compositor.RequestCommitAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(350));
            if (!Current()) return;
            VideoFrameOverlay.Flush();
            _shell?.ShowPlayer(true);
            window.VideoVisible = true;
            EnterAutoFullscreen();
            await _windowChange;
            if (!Current()) return;
            await compositor.RequestCommitAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(350));
            VideoFrameOverlay.Flush();
        }
        catch (Exception error)
        {
            Log.Warn(Category, "起播窗口交接未能确认呈现，保留加载层等待首帧", error);
            if (Current())
            {
                _shell?.ShowPlayer(true);
                window.VideoVisible = true;
                EnterAutoFullscreen();
            }
        }
        finally
        {
            if (Current())
            {
                _startupHandoverPending = false;
                if (!ViewModel.CoverUp) HideCoverPlate();
            }
        }

        bool Current() => generation == _windowChangeGeneration && _onStage && _window == window;
    }

    /// <summary>
    /// 「一上来就整屏」（用户令 2026-09-25）：起播开了自动全屏时，不在小窗里淡入再跳全屏，而是当拍
    /// 直接把窗口整屏、播放页从第一帧就整屏，最贴近「播放中按 F 切全屏」那种即时感。
    /// <para>
    /// 窗口从浏览几何长到整屏那一拍，岛的合成提交晚 <c>SetWindowPos</c> 一两拍；浏览页此刻也还停在小窗
    /// 尺寸。两者都用加载遮罩同色的纯色顶层层盖住（<see cref="VideoFrameOverlay"/> 由 DWM 直接合成、不吃
    /// 岛的滞后），等整屏加载层提交上屏再撤，于是从头到尾看不到「先小窗、再放大」。
    /// </para>
    /// </summary>
    private void EnterFullscreenAtOnce()
    {
        if (_window is not { } window) return;
        CancelPageTransition();

        // 播放页立刻不透明（不走淡入），铺满整屏的黑舞台＋加载层。
        ApplyPagePose(PlayerMotion.Pose.Visible);

        // 纯色层先盖满整个显示器：盖住「浏览页还在小窗」这一瞬，也盖住紧接着窗口长大的那一拍。
        // 2026-09-25 起铺的是遮罩那张背景图（图没到手才退回纯色，见 StartupCover）—— 从前一律铺近黑，
        // 用户看到的因此是「先全屏黑一片、再切到背景图」。
        var cover = StartupCover(window.Handle);
        _startupCover = cover;
        _startupCoverSettled = false;
        _startupHandoverPending = true;
        try
        {
            // 任务栏在覆盖层之前收下并等确认（2026-10-07 用户报「加载页面任务栏闪一下」）：覆盖层一上屏，
            // 整块显示器（任务栏那一块也在内）就是加载图，而主任务栏画在一切 topmost 之上 —— SW_HIDE 若
            // 排在覆盖层之后（从前全屏落地才收），加载图先铺满、任务栏站在上面，用户看到它闪一下才消失。
            // 此时窗口还不是全屏，收与退都走窗口层的预收/回退一对，进场失败不会把任务栏押在半路。
            window.HideTrayForImminentFullscreen();
            cover.Show(VideoFrameOverlay.FullscreenRect(window.Handle), topmost: true);
            VideoFrameOverlay.Flush();
            _shell?.ShowPlayer(true);
            window.VideoVisible = true;
            window.Fullscreen = true;
            if (!window.Fullscreen) window.CancelImminentFullscreenTray();
            ApplyTitleScale(true);
            _ = RevealAfterInstantFullscreenAsync(cover);
        }
        catch
        {
            CancelStartupCover();
            window.CancelImminentFullscreenTray();
            throw;
        }
    }

    /// <summary>
    /// 等岛、加载层和当前图片在真实客户区稳定出帧，再将原生背景交给 XAML。
    /// 固定两次 Rendering（包括超时）不能证明新布局已呈现；冷态时旧布局还会在后续通知里回来。
    /// RequestCommitAsync 在此岛上曾不兑现，故用连续实际帧、尺寸核对与合成余量共同判定。
    /// </summary>
    private async Task RevealAfterInstantFullscreenAsync(VideoFrameOverlay cover)
    {
        var started = Now;
        var generation = _presentationGeneration;
        var window = _window;
        var ready = false;
        try
        {
            if (window is null) return;
            SynchronizePlaybackLayout();
            var previous = (window.ClientSize, XamlRoot?.RasterizationScale, ViewModel.CoverBackdrop);
            var stableSince = Now;
            var frames = 0;
            while (Current() && Now - started < StartupCoverCeilingMilliseconds)
            {
                var rendered = await WaitFramesAsync(1, RevealFrameMilliseconds);
                if (!Current()) return;
                var current = (window.ClientSize, XamlRoot?.RasterizationScale, ViewModel.CoverBackdrop);
                if (rendered == 0 || !StartupCoverFits(window) || current != previous)
                {
                    frames = 0;
                    stableSince = Now;
                    previous = current;
                    continue;
                }
                if (frames == 0) stableSince = Now;
                if (++frames < 3 || Now - stableSince < HandoffSettleMilliseconds) continue;
                VideoFrameOverlay.Flush();
                VideoFrameOverlay.Flush();
                ready = true;
                break;
            }
            if (Current() && !ready)
                Log.Warn(Category, $"起播整屏加载层未在 {StartupCoverCeilingMilliseconds}ms 内稳定出帧，撤应急覆盖层；"
                    + $"客户区={window.ClientSize}，岛={XamlRoot?.Size}，加载层={Cover.ActualWidth}x{Cover.ActualHeight}");
        }
        catch (Exception error)
        {
            Log.Warn(Category, "起播加载层交接检查失败，撤应急覆盖层", error);
        }
        finally
        {
            cover.Dispose();
            // 退出或下一次进场可以先接管；迟到的旧任务只释放自己那一层。
            if (ReferenceEquals(_startupCover, cover))
            {
                _startupCoverSettled = ready;
                _startupCover = null;
                _startupHandoverPending = false;
                if (_onStage && Attached && !ViewModel.CoverUp) HideCoverPlate();
            }
            VideoFrameOverlay.Flush();
            Log.Debug(Category, $"起播整屏覆盖层撤下：盖了 {Now - started}ms，布局稳定={ready}");
        }

        bool Current() => generation == _presentationGeneration && ReferenceEquals(_startupCover, cover)
            && _onStage && Attached && _window == window && window is { Fullscreen: true }
            && window.Handle != IntPtr.Zero && !Native.IsIconic(window.Handle);
    }

    private bool StartupCoverFits(HostWindow window)
    {
        if (XamlRoot is not { RasterizationScale: > 0 } root
            || Visibility != Visibility.Visible || Cover.Visibility != Visibility.Visible
            || Opacity != 1 || Cover.Opacity != 1
            || PageTransform.ScaleX != 1 || PageTransform.ScaleY != 1 || PageTransform.TranslateY != 0
            || CoverArtwork.Background is not ImageBrush brush
            || !ReferenceEquals(brush.ImageSource, ViewModel.CoverBackdrop)) return false;
        var (width, height) = window.ClientSize;
        var raster = root.RasterizationScale;
        // 连岛本身也要追上；只看强制 Arrange 后的控件尺寸会把暂时排对误认成稳定。
        if (!Matches(root.Size.Width, root.Size.Height)) return false;
        return Fills(Root) && Fills(Cover) && Fills(CoverArtwork);

        bool Matches(double w, double h) => width > 0 && height > 0
            && Math.Abs(w * raster - width) <= 1 && Math.Abs(h * raster - height) <= 1;
        bool Fills(FrameworkElement element)
        {
            var origin = element.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
            return Matches(element.ActualWidth, element.ActualHeight)
                && Math.Abs(origin.X * raster) <= 1 && Math.Abs(origin.Y * raster) <= 1;
        }
    }

    private void CancelStartupCover()
    {
        var cover = _startupCover;
        _startupCover = null;
        cover?.Dispose();
        if (cover is not null) _startupHandoverPending = false;
    }

    // 仅防故障时原生顶层窗口滞留；到此上限算交接失败，不能记作已出帧。
    private const int StartupCoverCeilingMilliseconds = 2000;

    /// <summary>整屏进场的撤层余量：渲染被饿时宁可覆盖层多活一拍，也不让「撤层」抢在「上屏」前面。</summary>
    private const int RevealFrameMilliseconds = 80;

    /// <summary>
    /// 等合成器出过 <paramref name="frames"/> 帧。没有渲染回调的场合（窗口被最小化、显示器熄了）也要走得动，
    /// 所以每一帧都兜一张 <paramref name="perFrameMilliseconds"/> 的网 —— 网宽由调用方按「覆盖层的寿命」定。
    /// </summary>
    /// <returns>实际收到的渲染回调数；超时不算一帧。</returns>
    private static async Task<int> WaitFramesAsync(int frames, int perFrameMilliseconds = 32)
    {
        var count = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnRendering(object? sender, object args)
            {
                CompositionTarget.Rendering -= OnRendering;
                rendered.TrySetResult();
            }

            CompositionTarget.Rendering += OnRendering;
            try
            {
                await Task.WhenAny(rendered.Task, Task.Delay(perFrameMilliseconds));
                if (rendered.Task.IsCompletedSuccessfully) count++;
            }
            finally { CompositionTarget.Rendering -= OnRendering; }
        }
        return count;
    }

    private void ApplyPagePose(PlayerMotion.Pose pose)
    {
        Opacity = pose.Opacity;
        PageTransform.ScaleX = PageTransform.ScaleY = pose.Scale;
        PageTransform.TranslateY = pose.OffsetY;
    }

    private void CancelPageTransition()
    {
        var driver = _poseDriver;
        _poseDriver = null;
        _pageTransition = null;
        _enterAnimating = false;
        EndBrowseWait();
        driver?.Stop();
    }

    /// <summary>
    /// 给 <paramref name="target"/> 的 <paramref name="property"/> 挂一条 DoubleAnimation。只许喂
    /// <b>Opacity 这类它真跑得动</b>的属性——换集遮罩的淡出（<see cref="PlayerPage.Cover.cs"/>）用它；
    /// <b>RenderTransform 别走这条路</b>，整页进退的 pose 由 <see cref="TransitionPage"/> 的 16ms 计时器
    /// 逐拍驱动，理由写在那里（2026-09-18/19 两次实机翻车：SetTarget 变换对象冻在起点、属性路径指向
    /// 元素也到不了终点）。
    /// </summary>
    private static void Animate(Storyboard board, DependencyObject target, string property,
        double from, double to, int milliseconds)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        board.Children.Add(animation);
    }

    /// <summary>
    /// 这一次 <see cref="CompletePlayerExit"/> 要不要顺手把窗口还回浏览几何。
    /// <para>
    /// 由 <c>LeavePlayer</c> 立起（它才是「真的退出一场播放」那条路）。<b>不能只看「跑到了落定拍」</b>：
    /// <c>PlayerPage.Detach</c> 那条路是 <c>LeavePlayer(); StopPlayerMotion(); CompletePlayerExit();</c>
    /// 三连，页面可能根本没上过台 —— 那时把窗口掰回上一次记下的浏览几何是无事生非（用户只是从浏览页
    /// 点了一部片子走独占模式）。落定拍消费掉它，所以一次退场只会换来一次还原。
    /// </para>
    /// </summary>
    private bool _restoreBrowseOnExit;

    /// <summary>
    /// 这一趟退场<b>不留溶解</b>：片子停下之前从播放页走的那条路（<see cref="ReturnToBrowseBeforeStopAsync"/>）
    /// 已经把那最后一帧交在一块不随窗口变形的覆盖层上，本页收姿势那 240ms 溶解只剩一块空舞台在淡 ——
    /// 用户要的是「直接切回主页」（2026-09-25 用户令），所以那一趟当拍收摊。立起与消费都在
    /// <see cref="LeavePlayer"/> 一处，别的收摊路（Detach、真收摊）看不到它。
    /// </summary>
    private bool _exitSnap;

    /// <summary>
    /// 这一趟退场落定没有 —— 等在 <see cref="ReturnToBrowseBeforeStopAsync"/> 那一头的人拿它当信号（停播放
    /// 排在动画之后，见那一段）。非空即「正等」，由 <see cref="CompletePlayerExit"/> 放行并清掉。
    /// </summary>
    private TaskCompletionSource? _exitLanded;

    /// <summary>
    /// 退场等待浏览页报就绪的那颗表；非空即「最后一帧还按着，溶解没起」。
    /// <para>
    /// <b>为什么要等（2026-09-20 晚用户令「退出的时候不第一时间去掉画面，等背景图加载出来之后再无缝替换」）。</b>
    /// <c>LeavePlayer</c> 的 ① 已经把浏览页放回屏上，但它那张背景图（主页轮播、详情页背景）要一次网络往返
    /// 才到；溶解全长只有 <see cref="PlayerMotion.ExitMilliseconds"/>，图没到就溶解完，露出来的是那张图的
    /// 位置上的空白 —— 用户截图里那块发灰的空页面。所以先按住最后一帧不放，等浏览页把内容画出来（或等到
    /// <see cref="ExitReadyWaitMilliseconds"/> 那个上限）再起同一条溶解轨道。
    /// </para>
    /// <para>
    /// <b>为什么兜一个上限。</b>就绪是个外部条件 —— 服务器慢、图挂了、或者这次退场根本没有背景图要等 ——
    /// 没有上限的话「等就绪」会变成「永远不退场」，而屏上停着的是一张按着不放的旧画面加一个已经收摊的
    /// 输入层。上限到点就当它已就绪：宁可露出那块灰，也不能卡在退场里出不来。
    /// </para>
    /// </summary>
    private DispatcherQueueTimer? _browseWait;

    /// <summary>
    /// 等浏览页就绪的宽限。240ms 是溶解全长，等它三倍多的余量：一次正常的背景图往返在本机是一两帧到
    /// 几十毫秒，给它 800ms 是「图确实在路上」与「这张图不会来了」之间那条线。
    /// </summary>
    private const int ExitReadyWaitMilliseconds = 800;

    /// <summary>
    /// ③「等背景图加载出来之后再无缝替换」：按住最后一帧，等浏览页报就绪（或等到宽限上限）才起溶解。
    /// <para>
    /// 判定只用 <c>ContentHost</c> 的呈现尺寸 —— 浏览页被 <c>ShowPlayer(false)</c> 放回来的同一拍就
    /// <c>UpdateLayout()</c> 强制过一版，所以「有尺寸」在这里等于「这一拍真的排过版了」。这比去问每一张
    /// 背景图「你的 Source 到了没有」简单且够用：那些图各自是哪个控件、什么时候算到，是浏览页自己的事，
    /// 这一页管的是「别在它还是空的时候把画面收掉」。宽限到点同样放行（见 <c>_browseWait</c> 的注释）。
    /// </para>
    /// <para>
    /// 落定拍走 <see cref="CompletePlayerExit"/>，它会把 <c>_browseWait</c> 一并收掉 —— 提前退场、换向、
    /// <c>Detach</c> 那几条路都从这里过，所以「按着不放」的标记不会漏到下一趟。
    /// </para>
    /// </summary>
    private void ExitWhenBrowseReady()
    {
        _browseWait?.Stop();
        _browseWait = null;

        var wait = DispatcherQueue.CreateTimer();
        wait.Interval = TimeSpan.FromMilliseconds(PoseTickMilliseconds);
        wait.IsRepeating = true;
        // 宽限用的是本页那颗钟（`Now` ＝ Environment.TickCount64），与 pose 轨道、光标那几处同一个
        // 时间源；DispatcherQueueTimer 自己没有 Elapsed，所以截止时刻在这儿先算好。
        var deadline = Now + ExitReadyWaitMilliseconds;
        wait.Tick += (_, _) =>
        {
            // 退场等待期间页面已离台且输入已暂停；重新进场或收摊才取消等待。
            if (_onStage || !_inputSuspended || !_restoreBrowseOnExit)
            {
                EndBrowseWait();
                return;
            }

            // 就绪问的是「此刻框里那一页自己报没报就绪」——没有页面的场合（探针那条没导航过的路）
            // 一律算就绪，于是立即溶解，与探针/自检同一条路。判据只此一处（见 BrowseReady），
            // 退场淡出那一条也读它。
            var ready = BrowseReady;

            if (!ready && Now < deadline) return;

            EndBrowseWait();
            if (ready) Log.Info(Category, "退场：浏览页已就绪，开始溶解");
            else Log.Warn(Category, $"退场：等浏览页就绪超时（{ExitReadyWaitMilliseconds}ms），照常溶解");
            BeginPlayerExitDissolve();
        };

        _browseWait = wait;
        wait.Start();
    }

    /// <summary>收掉等待表与它的标记，起溶解与收摊两条路都要过这里。</summary>
    private void EndBrowseWait()
    {
        _browseWait?.Stop();
        _browseWait = null;
    }

    /// <summary>
    /// 溶解真正起跑的那一拍：整页姿势逐拍走到落定，画面的压暗与它同拍（<see cref="DrivePlayerExit"/> 由
    /// <c>progress</c> 回调驱动）。与 <c>LeavePlayer</c> 当初排在 ② 的是同一条轨道，只是现在它可以在
    /// 浏览页就绪之后才起。
    /// </summary>
    private void BeginPlayerExitDissolve() =>
        TransitionPage(
            entering: false,
            landed: CompletePlayerExit,
            progress: DrivePlayerExit);

    private void CompletePlayerExit()
    {
        EndBrowseWait();
        Visibility = Visibility.Collapsed;
        Stage.Visibility = Visibility.Collapsed;
        EndPlayerExit();
        CompleteCoverExit();
        ApplyPagePose(PlayerMotion.Pose.Visible);
        _inputSuspended = false;

        // 画面先放掉，窗口再动 —— 反过来的话，最后那一帧会以旧尺寸出现在新窗口里。
        _videoTarget.RetainLastFrame = false;

        // **窗口到这里才改尺寸**（2026-09-20 晚，用户令「就不能直接切到主页，然后再调整窗口大小吗」）。
        // 走到这一拍，屏上只剩已经排好版的浏览页，于是：
        //   ① 「新窗口尺寸 + 旧内容」那一拍再也吐不出「定在旧尺寸上的最后一帧」；
        //   ② 合成器若仍旧先吐一拍旧内容，旧内容是**浏览页**，垫底那一条是窗口底色 #16181C ——
        //      与浏览页同一个量级的深色（2026-09-20 实测两者底边条亮度都是 32），中间那条硬分界
        //      也就不存在了。
        if (_restoreBrowseOnExit)
        {
            _restoreBrowseOnExit = false;
            _window?.RestorePlayerToBrowse();
            if (_window is not null) _window.FreeSizing = false;
        }

        // **窗口几何到这一拍才算落定**，播放层这一位因此收在这里、而且收在下面那次强制布局之前：
        // 上面那两次 SetWindowPos（退全屏 → 还浏览几何）给浏览页的量测全是中间态，中途放开门，主页
        // 会先按播放几何翻一趟档、再按浏览几何翻回来 —— 用户看到的就是媒体库上下各跑一次 260ms 的动画
        // （2026-09-25 实测日志：视口 867 → 压上轮播、13ms 后视口 1271 → 回默认）。放开门之后的这一次
        // SynchronizePlaybackLayout 会给主页一次尺寸变化，该判的档在那一拍一次判完。
        _window?.SetPlayerLayer(false);

        // Mica 到这一拍才打开：整页与退场底都收干净了，底下是已经画好的浏览页，开它谁也看不见。
        // 早开一步就会在窗口改尺寸的那几拍里漏出一块壁纸采样面（2026-09-20 用户第三张截图）。
        RestoreBrowseBackdrop();

        // 客户区刚变过，这一拍读到的才是「浏览页该排多高、岛该多大」。
        SynchronizePlaybackLayout();

        // **退场落定**：等在 <see cref="ReturnToBrowseBeforeStopAsync"/> 那一头的人放行 —— 停播放要排在
        // 动画走完之后，留帧与那层 SpriteVisual 在动画期间才活着（见那一段）。幂等：Detach 一类不经这条
        // 路进来的收摊，这里放的是空的。
        var landed = _exitLanded;
        _exitLanded = null;
        landed?.TrySetResult();
    }

    /// <summary>
    /// 用户关闭播放页时直接交回现成的浏览页。必须先呈现再停 mpv：保留交换链引用并不能阻止
    /// mpv 的 stop/quit 把里面的像素清空。
    /// <para>
    /// <b>2026-09-25 第三轮起，这一趟不让画面跟着窗口缩</b>（用户令「退出播放窗口化的一瞬间会有视频的残留
    /// 画面，不能直接切回主页吗」）。第二轮这一趟走的是「等浏览页就绪 → 最后一帧逐拍溶解」，而窗口在那之前
    /// 就已经缩回浏览几何 —— 画面跟着缩进小窗的那一截，用户读作「残留画面」。现在那一帧交在一块<b>不随窗口
    /// 变形</b>的覆盖层上（见 ①②），本页当拍收摊。
    /// </para>
    /// <para>
    /// <b>2026-11-08 起，撤层之前先把它淡出去</b>（用户令「退出时采用淡出等对应的离场效果」，与进场同一个
    /// 280ms）：覆盖层改铺到新客户区之后按常量 alpha 逐拍淡到全透明，露出早就排好版的主页。淡出<b>不等</b>
    /// 它走完 —— 见 ④，停播放要跟着用户那一下走（声音不能陪着动画再响三百毫秒），而屏上已经由这一层接着，
    /// mpv 那一刀清像素伤不到它。
    /// </para>
    /// </summary>
    internal async Task ReturnToBrowseBeforeStopAsync()
    {
        if (!_onStage || !Attached || _window is not { Handle: var handle } window || handle == IntPtr.Zero) return;
        var context = ViewModel.CaptureInteraction();
        var generation = _presentationGeneration;
        // 这一趟是不是从全屏退出来的 —— 全屏退出不做淡出（理由见 ②）。必须在 RestorePlayerToBrowse 之前读：
        // 那一句会把窗口（连同全屏状态）一次还成浏览几何。
        var wasFullscreen = window.Fullscreen;
        bool Current() => generation == _presentationGeneration && _window == window
            && window.Handle == handle && Attached && ViewModel.IsCurrentInteraction(context);

        // ① 先抓一帧，铺成一块**不随窗口变形**的原客户区覆盖层（2026-09-25 用户令「退出播放窗口化的一瞬间会有
        //    视频的残留画面，不能直接切回主页吗」）。从前这一趟是让画面跟着窗口一起缩、缩完再逐拍溶解 ——
        //    全屏视频缩进小窗的那一截就是用户说的「残留画面」。覆盖层由 DWM 直接合成、定位在屏幕上，
        //    窗口怎么缩它都不动，于是「窗口化」这件事发生在一张静止的画底下，谁也看不见。
        //    抓不到帧照常往下走：屏上剩的是退场底（没有画面可残留，也就没有这一条）。
        VideoFrameOverlay? handoff = null;
        try
        {
            if (_videoTarget.HasAttachedVisual)
            {
                try
                {
                    var handoffBounds = VideoFrameOverlay.ClientRect(handle);
                    var frame = await _videoTarget.CaptureFrameAsync().WaitAsync(TimeSpan.FromMilliseconds(750));
                    if (!Current() || !_onStage) return;
                    if (frame is not null)
                    {
                        handoff = new VideoFrameOverlay(handle, frame);
                        handoff.Show(handoffBounds, topmost: true);
                        VideoFrameOverlay.Flush();
                    }
                }
                catch (Exception error)
                {
                    Log.Warn(Category, "退场抓帧不成，这一趟没有覆盖层遮挡", error);
                }
            }
            if (!Current() || !_onStage) return;

            // The overlay stays at screen coordinates while the host returns to its browse size.
            _videoTarget.RetainLastFrame = false;
            window.RestorePlayerToBrowse();
            window.FreeSizing = false;

            // ② 窗口已经还回浏览几何：把覆盖层改铺到**新的**客户区（2026-11-08）。
            //
            //    播放时的客户区与浏览几何通常只差高度（`AspectLock.Fit` 保持宽度、按中心缩放），所以按 contain
            //    摆进新客户区的画面，在屏上的位置与尺寸分毫不动 —— 变的只是它四周多出来的那两道。那两道补成
            //    舞台色（见 ExitSurroundColor）：透空的话屏上就是「一条悬空的画面带正在淡出」，补上才是
            //    「播放页还在，只是化了」。
            //
            //    这一句与上面那次 SetWindowPos 在同一个同步块里，中间没有 await，因此不会有一帧露出没铺满的
            //    窗口。从前这一趟不留过渡（用户令 2026-09-25「不能直接切回主页吗」），两帧后撤层，屏上是
            //    「最后一帧 → 主页」的硬切；现在撤层之前先把它淡出去（见 ④）。
            //
            //    **全屏退出不走这一段**（用户 2026-11-08 选「只做窗口模式」）：那一趟覆盖层是整块显示器，
            //    每拍整块重传不划算；更要紧的是窗口要从显示器缩回浏览几何，画面在屏上的位置必然变，
            //    「原样停着再淡」这个前提根本不存在 —— 那种情况留在原地的还是一层不透明的层，两帧后撤层。
            var fading = handoff is not null && !wasFullscreen && HomeMotion.AnimationsEnabled;
            var fadeBounds = fading ? VideoFrameOverlay.ClientRect(handle) : default;
            var surround = fading ? ExitSurroundColor : 0u;
            if (fading)
            {
                handoff!.Show(fadeBounds, topmost: true, surround: surround);
                VideoFrameOverlay.Flush();
            }

            var landed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _exitLanded = landed;
            _exitSnap = true;
            LeavePlayer();
            generation = _presentationGeneration;

            try
            {
                await landed.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                Log.Warn(Category, "退场没在 3 秒内落定，照常停播放");
            }

            // Layout and composition commit independently; the browse page gets its own frames committed
            // before the backend is allowed to clear the pixels it was feeding.
            await WaitFramesAsync(2);

            // ④ 淡出（**不等它走完**）。停播放不能排在 280ms 之后：画面这一刻已经冻在覆盖层上，mpv 那一刀清
            //    像素伤不到屏上，而声音得跟着用户那一下停 —— 让它陪着动画再响三百毫秒才是这一趟真正会被听出来
            //    的地方。淡出自己按 generation 收尾（换片、卸载、收摊都会让它下一拍停手并撤层）。
            if (fading)
            {
                _exitFadeTask = FadeExitAsync(handoff, fadeBounds, surround, generation);
                handoff = null;
            }
            VideoFrameOverlay.Flush();
            // 浏览页已经呈现，保持这一实例；撤层后再导航会先露出静态主页，再迟到地补播入场。
        }
        finally
        {
            handoff?.Dispose();
        }
    }

    /// <summary>
    /// 退场那一层自己淡出去：先等浏览页就绪（≤<c>ExitReadyWaitMilliseconds</c>），再整段匀速淡到全透明并撤层。
    /// <para>
    /// <b>调用方不等它</b>（见 <c>ReturnToBrowseBeforeStopAsync</c> 的 ④）：停播放要跟着用户那一下走，
    /// 而屏上已经由这一层接着。每拍只有两件事 —— 把整层的不透明度递给分层窗口（<c>SetLayeredWindowAttributes</c>，
    /// 像素不重画也不重传，见 <c>VideoFrameOverlay.Fade</c>）、等一拍；曲线与时长都在 <c>PlayerMotion</c> 里，
    /// 与进场同一个数。
    /// </para>
    /// <para>
    /// 换片、卸载、收摊都会让这次退场的 <paramref name="generation"/> 过期：循环下一拍就停手，<c>finally</c>
    /// 里撤层。屏上不会留下一层按着旧画面不放的窗口 —— 它不属于任何一棵 XAML 树，没人替它收。
    /// 「系统动画关掉时不做」那一问由调用方判（它同时决定要不要改铺，见 ②）。
    /// </para>
    /// </summary>
    private async Task FadeExitAsync(VideoFrameOverlay? overlay, NativeRect bounds, uint surround, int generation)
    {
        var waitedReady = 0L;
        try
        {
            if (overlay is null) return;
            _exitFade = overlay;
            bool Current() => generation == _presentationGeneration && Attached && _window is not null;

            // ③ 等浏览页把自己的内容画出来再淡。背景图要一次网络往返，图没到就淡完，露出来的是那块发灰的
            //    空页面（2026-09-20 用户截图）。宽限到点照常淡出 —— 宁可露出那块灰，也不能卡在退场里出不来。
            var readyStarted = Now;
            var readyDeadline = readyStarted + ExitReadyWaitMilliseconds;
            while (!BrowseReady && Now < readyDeadline && Current()) await WaitExitTickAsync().ConfigureAwait(true);
            waitedReady = Now - readyStarted;

            var started = Now;
            while (Current())
            {
                var progress = Math.Clamp((Now - started) / (double)PlayerMotion.ExitMilliseconds, 0, 1);
                overlay.Show(bounds, topmost: true,
                    alpha: PlayerMotion.ExitFadeAlpha(progress), surround: surround);
                if (progress >= 1) break;
                await WaitExitTickAsync().ConfigureAwait(true);
            }
            Log.Debug(Category, $"退场淡出 {Now - started}ms（等浏览页就绪 {waitedReady}ms，"
                + $"层 {bounds.Width}x{bounds.Height}）");
        }
        catch (Exception error)
        {
            // 层画不动了：当拍撤掉。停在半透明上比硬切更糟 —— 那会把一块半透明的旧画面留在主页上。
            Log.Warn(Category, "退场淡出没走完，直接撤层", error);
        }
        finally
        {
            if (ReferenceEquals(_exitFade, overlay)) _exitFade = null;
            overlay?.Dispose();
            VideoFrameOverlay.Flush();
        }
    }

    private void StopPlayerMotion()
    {
        _inputSuspended = true;
        // 收摊这条路绕过 CompletePlayerExit，播放层这一位要在这里还 —— 一笔没人还的账就是「主页从此不再翻档」。
        _window?.SetPlayerLayer(false);
        CancelPageTransition();
        CancelExitFade();
        ResetCover();
        ApplyPagePose(PlayerMotion.Pose.Visible);
        EndPlayerExit();
        // 这条路会绕过 CompletePlayerExit，Mica 的那一句要在这里补上，否则「没走完退场就收摊」之后
        // 浏览态会一直少一层底（窗口露 BaseBrush）。
        RestoreBrowseBackdrop();
        _videoTarget.RetainLastFrame = false;
    }
}
