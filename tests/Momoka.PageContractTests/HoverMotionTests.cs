using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 悬停放大（2026-11-13）：卡片整卡抬一档、预览面板换弹簧撑开。两处都参考 WinUI Gallery 的
/// <c>Samples/XamlCompInterop</c>（固定提交 6de91fe）。这里钉住参数与实现路径，防止被悄悄改回
/// 「整卡不放大」或退回一段 keyframe；实际手感、遮挡和减弱动效下的落定仍靠运行截图与探针。
/// </summary>
internal static class HoverMotionTests
{
    internal static void Register()
    {
        Test("悬浮预览：封面和徽标播放动图，过渡层不提前播放且保留动态封面", () =>
        {
            var source = Read("src/Momoka.Shell/Views/CardPreview.xaml.cs");
            Assert.Contains("_previewAnimation = new AnimatedPoster(PreviewImage, preview: true);", source);
            Assert.DoesNotContain("new AnimatedPoster(TransitionImage", source);
            Assert.Contains("new AnimatedPoster(LogoImage, preview: true)", source);
            Assert.Contains("ReferenceEquals(_previewAnimation.OriginalSource, artwork)", source);
            Assert.Contains("_pendingArtwork ?? TransitionImage.Source", source);
            Assert.Contains("if (Poster is { } poster && PosterCache.IsAnimated(poster))",
                Read("src/Momoka.Shell/Views/CardItem.cs"));
            Assert.Contains("PreviewImage.Source = TransitionImage.Source = LogoImage.Source = null;", source);
            var loader = Read("src/Momoka.Shell/Media/PosterLoader.cs");
            Assert.Contains("DecodePixelWidth = PosterDecodeSize.Width(bytes, logicalWidth)", loader);
        });
        Test("悬停放大：卡片整卡抬到 1.03，抬起用 BackEase 过冲", () =>
        {
            var source = Read("src/Momoka.Shell/Views/PosterCard.xaml.cs");
            Assert.Contains("private const double HoverScale = 1.03;", source);
            Assert.Contains("var lifted = _hoverDwell.IsReady || _previewOpen;", source);
            Assert.Contains("var active = _hoverDwell.IsReady || keyboardFocused || _previewOpen;", source);
            Assert.Contains("new BackEase", source);
            Assert.Contains("XamlCompInterop", source);
            Assert.Contains("6de91fe", source);
        });

        Test("悬停放大：鼠标移动与计时都核对真实封面位置，保留按钮即时反馈", () =>
        {
            var source = Read("src/Momoka.Shell/Views/PosterCard.xaml.cs");
            Assert.Contains("Root.AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);", source);
            Assert.Contains("e.Pointer.PointerDeviceType != PointerDeviceType.Mouse", source);
            Assert.Contains("Art.TransformToVisual(content)", source);
            Assert.Contains("position = (cursor.X, cursor.Y);", source);
            var tickStart = source.IndexOf("private void OnHoverDwellTick(", StringComparison.Ordinal);
            var tickEnd = source.IndexOf("private void OpenPreview(", tickStart, StringComparison.Ordinal);
            var tick = source[tickStart..tickEnd];
            Assert.Contains("AnotherPopupOpen() || !TryReadCoverPointer(out var position)", tick);
            Assert.Contains("CancelHoverDwell();", tick);
            Assert.Contains("_hoverDwell.Observe(position.X, position.Y, now);", tick);
            Assert.Contains("_hoverDwell.TryComplete(now)", tick);
            Assert.Contains("Hover.IsHitTestVisible = _hovered;", source);
            Assert.DoesNotContain("ArmPreview()", source);
            Assert.DoesNotContain("PreviewDwellMs = 1000", source);
            var watchStart = source.IndexOf("_watch = new HoverWatch(", StringComparison.Ordinal);
            var watchEnd = source.IndexOf("_focus = new FocusWatch(", watchStart, StringComparison.Ordinal);
            Assert.Contains("CancelHoverDwell();", source[watchStart..watchEnd]);

            foreach (var (start, end) in new[]
            {
                ("private static void OnCardChanged(", "private void OnLoaded("),
                ("private void OnUnloaded(", "private void AnimateCard("),
                ("private void ResetMotion(", "internal Task PlayRemovalAsync(")
            })
            {
                var first = source.IndexOf(start, StringComparison.Ordinal);
                var last = source.IndexOf(end, first, StringComparison.Ordinal);
                Assert.Contains("CancelHoverDwell();", source[first..last]);
            }
        });

        Test("悬停放大：指针按下和所有动作统一抑制，鼠标点击焦点不推近封面", () =>
        {
            var source = Read("src/Momoka.Shell/Views/PosterCard.xaml.cs");
            var pressStart = source.IndexOf("private void OnPointerPressed(", StringComparison.Ordinal);
            var pressEnd = source.IndexOf("private void OnPointerReleased(", pressStart, StringComparison.Ordinal);
            Assert.Contains("SuppressHoverDwell();", source[pressStart..pressEnd]);
            var raise = source[source.IndexOf("private void Raise(", StringComparison.Ordinal)..];
            Assert.True(raise.IndexOf("SuppressHoverDwell();", StringComparison.Ordinal)
                < raise.IndexOf("ActionRequested?.Invoke", StringComparison.Ordinal));
            Assert.Contains("FocusManager.GetFocusedElement(root) is Control { FocusState: FocusState.Keyboard }", source);
            Assert.Contains("if (!TryReadCoverPointer(out _)) _hoverDwell.Reset();", source);
        });

        Test("悬停预览：Popup独立于缩放卡片，窗口坐标不再次乘祖先缩放", () =>
        {
            var source = Read("src/Momoka.Shell/Views/PosterCard.xaml.cs");
            Assert.DoesNotContain("Root.Children.Add(_previewHost);", source);
            Assert.Contains("width, rootWidth, PreviewMargin) + art.Left", source);
            Assert.Contains("height, rootHeight, PreviewMargin) + art.Top", source);
        });

        Test("悬停预览：演职人员的人物卡不弹预览面板", () =>
        {
            var source = Read("src/Momoka.Shell/Views/PosterCard.xaml.cs");
            var start = source.IndexOf("private bool PreviewEligible(", StringComparison.Ordinal);
            var end = source.IndexOf("private void StartHoverDwellTimer(", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start, "找不到 PreviewEligible");
            Assert.Contains("card.Item.Type != EmbyItemType.Person", source[start..end],
                "演职人员卡不是导航卡，必须按人物类型再挡一次预览");
        });

        Test("主页命中探针：显式打开分隔线后量边界，保留原主页无通栏线检查", () =>
        {
            var source = Read("src/Momoka.Shell/ShellNavigationProbe.HomeHeader.cs");
            Assert.Contains("!initialRule && rule.Visibility == Visibility.Collapsed", source);
            Assert.True(source.IndexOf("head.ShowRule = true;", StringComparison.Ordinal)
                < source.IndexOf("!HitsScope(Center(rule))", StringComparison.Ordinal));
            Assert.Contains("head.ShowRule = initialRule;", source);
            Assert.Contains("负向检查没有检测到整行点击回归", source);
            Assert.Contains("Get<Grid>(card, \"Root\").IsHitTestVisible = false;", source);
            Assert.Contains("Call(card, \"ResetMotion\");", source);
            Assert.Contains("对齐检查未检出旧的居中布局", source);
        });
        Test("外壳队列：初始空ViewModel安全隐藏，独占Detach后保留队列", () =>
        {
            var source = Read("src/Momoka.Shell/Views/ShellPage.xaml.cs");
            var start = source.IndexOf("bool IShellActions.CanQueueVideo(", StringComparison.Ordinal);
            var end = source.IndexOf("/// <summary>进播放前", start, StringComparison.Ordinal);
            var queue = source[start..end];
            Assert.DoesNotContain("Player.Attached", queue);
            Assert.Contains("Player.ViewModel?.CanQueue(item) == true", queue);
            Assert.Contains("Player.ViewModel?.Queue(item, next) == true", queue);
            Assert.Contains("Player.ViewModel?.HasPendingQueue == true", queue);
            Assert.Contains("Player.ViewModel?.CanResumeQueue == true", queue);
            Assert.Contains("Player.ViewModel is not { CanResumeQueue: true } player", queue);
            Assert.Contains("Player.ViewModel?.ClearQueue()", queue);
            Assert.Contains("Player.ViewModel is { } player", queue);
            var page = Read("src/Momoka.Shell/Views/PlayerPage.xaml.cs");
            var detach = page[page.IndexOf("internal void Detach()", StringComparison.Ordinal)..];
            detach = detach[..detach.IndexOf("// ---- 输出尺寸", StringComparison.Ordinal)];
            Assert.DoesNotContain("ViewModel = null", detach);
        });
        Test("整理菜单探针：保留真实菜单验收并记录激活结果，不强求前台", () =>
        {
            var source = Read("src/Momoka.Shell/ShellNavigationProbe.SeriesResourceBrowser.cs");
            Assert.DoesNotContain("UntilAsync(() => Native.GetForegroundWindow() == handle)", source);
            Assert.Contains("mp-series-menu-activation.txt", source);
            Assert.Contains("UntilAsync(() => menu.IsOpen", source);
            Assert.Contains("整理清单菜单超出窗口可用边界", source);
            Assert.Contains("菜单缺少运行时UIA句柄或名称", source);
        });

        Test("标题栏自检：切换前后取同一真实拖动点，避开按钮洞", () =>
        {
            var run = Read("src/Momoka.Shell/ShellSelfCheck.Run.cs");
            var frame = Read("src/Momoka.Shell/ShellSelfCheck.Frame.cs");
            Assert.DoesNotContain("HitAt(window, 8, 16)", run);
            Assert.Contains("CaptionDragPoint(window)", run);
            Assert.Contains("window.TitleBarMetrics.Height / scale - 2", frame);
            Assert.Contains("captionBefore.Hit == Native.HitCaption", frame);
            Assert.Contains("HitAt(window, captionBefore.Point.X, captionBefore.Point.Y) == Native.HitCaption", frame);
            Assert.Contains("centre == Native.HitClient", frame);
            Assert.Contains("beyond == Native.HitCaption", frame);
        });
        Test("订阅封面悬浮：主页两排与订阅列表共用同一条静止计时和预览面板", () =>
        {
            var preview = Read("src/Momoka.Shell/Views/CardPreview.xaml.cs");
            Assert.Contains("internal void ShowSubscription(MoviePilotSubscriptionCard card)", preview);
            Assert.Contains("SubscriptionBlock.Visibility = Visibility.Visible;", preview);
            Assert.Contains("PlayButton.Visibility = Visibility.Collapsed;", preview);
            Assert.Contains("card.ProgressCount", preview);
            Assert.Contains("card.SubscriberName", preview);
            Assert.Contains("查看{card.Title}订阅详情", preview);
            // 同日后续用户令「去掉 订阅状态 订阅中 那一行」：状态行连标签带值整行删除，不再按值显隐。
            Assert.DoesNotContain("SubscriptionStateRow", Read("src/Momoka.Shell/Views/CardPreview.xaml"));
            Assert.DoesNotContain("SubscriptionStateText", preview);
            Assert.DoesNotContain("CardPreview_SubscriptionState.Text", Read("src/Momoka.Shell/Strings/zh-Hans/Resources.resw"));
            Assert.DoesNotContain("CardPreview_SubscriptionState.Text", Read("src/Momoka.Shell/Strings/en-US/Resources.resw"));
            Assert.Contains("CardPreview_SubscriptionUpdated.Text", Read("src/Momoka.Shell/Strings/zh-Hans/Resources.resw"));
            Assert.Contains("CardPreview_SubscriptionUpdated.Text", Read("src/Momoka.Shell/Strings/en-US/Resources.resw"));

            // 2026-11-24 用户令「右上角添加一个编辑订阅按钮」：媒体预播放键的位置在订阅模式换成编辑键，
            // 标题按它的实际宽度让位；点它抛出 EditRequested，由宿主接上同一条订阅编辑窗口。
            Assert.Contains("AutomationProperties.AutomationId=\"CardPreviewEditSubscription\"", Read("src/Momoka.Shell/Views/CardPreview.xaml"));
            Assert.Contains("x:Name=\"EditSubscriptionSlot\"", Read("src/Momoka.Shell/Views/CardPreview.xaml"));
            Assert.Contains("CardPreview_EditSubscription.Content", Read("src/Momoka.Shell/Strings/zh-Hans/Resources.resw"));
            Assert.Contains("CardPreview_EditSubscription.Content", Read("src/Momoka.Shell/Strings/en-US/Resources.resw"));
            Assert.Contains("internal event EventHandler? EditRequested;", preview);
            Assert.Contains("EditSubscriptionSlot.Visibility = Visibility.Visible;", preview);
            Assert.Contains("EditSubscriptionButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));", preview);
            Assert.Contains("EditSubscriptionButton.DesiredSize.Width + HeadingActionGap", preview);
            Assert.Contains("EditSubscriptionSlot.Visibility = Visibility.Collapsed;", preview);
            Assert.Contains("EditRequested?.Invoke", preview);

            var poster = Read("src/Momoka.Shell/Views/MoviePilotSubscriptionPoster.xaml.cs");
            Assert.Contains("private readonly PosterHoverDwell _previewDwell = new();", poster);
            Assert.Contains("AnotherPopupOpen()", poster);
            Assert.Contains("CardPreviewPlacement.Horizontal(", poster);
            Assert.Contains("new Popup", poster);
            Assert.Contains("IsLightDismissEnabled = true,", poster);
            Assert.Contains("PreviewRequested?.Invoke", poster);
            Assert.Contains("DetailsRequested?.Invoke", poster);
            Assert.Contains("_preview.EditRequested += (_, _) =>", poster);
            Assert.Contains("EditRequested?.Invoke", poster);
            Assert.Contains("PreviewScrollWatch", poster);
            Assert.Contains("new PreviewCloseDelay(() => ClosePreview(), () => _hovered || _previewHovered)", poster);
            var shared = Read("src/Momoka.Shell/Views/PreviewPopupSupport.cs");
            Assert.Contains("viewer.ViewChanged += OnScrolled;", shared);
            Assert.Contains("if (keep()) return;", shared);
            Assert.Contains("GraceMilliseconds = 180", shared);
            Assert.Contains("internal static bool AnotherOpen(XamlRoot? root, Popup? own)", shared);

            var view = Read("src/Momoka.Shell/Views/MoviePilotSubscriptionsView.xaml");
            Assert.Contains("PreviewRequested=\"OnPosterPreviewRequested\"", view);
            Assert.Contains("DetailsRequested=\"OnPosterDetailsRequested\"", view);
            Assert.Contains("EditRequested=\"OnPosterEditRequested\"", view);
            Assert.Contains("private void OnPosterEditRequested(", Read("src/Momoka.Shell/Views/MoviePilotSubscriptionsView.xaml.cs"));
            var page = Read("src/Momoka.Shell/Views/LibraryPage.xaml.cs");
            Assert.Contains("OnSubscriptionMatchPreviewRequested", page);
            Assert.Contains("ViewModel.OpenMatchingSubscription(card);", page[page.IndexOf("OnSubscriptionMatchPreviewDetails", StringComparison.Ordinal)..]);
            Assert.Contains("OnSubscriptionMatchPreviewEdit", page);
            Assert.Contains("EditRequested=\"OnSubscriptionMatchPreviewEdit\"", Read("src/Momoka.Shell/Views/LibraryPage.xaml"));
            var model = Read("src/Momoka.Shell/ViewModels/MoviePilotSubscriptionsViewModel.cs");
            Assert.Contains("public Task LoadPreviewBackdropAsync(MoviePilotSubscriptionCard card)", model);
        });
        Test("悬停放大：预览面板用弹簧驱动 Progress，不再用关键帧", () =>
        {
            var source = Read("src/Momoka.Shell/Views/CardPreview.xaml.cs");
            Assert.Contains("CreateSpringScalarAnimation()", source);
            Assert.Contains("spring.Target = \"Progress\";", source);
            Assert.Contains("spring.DampingRatio", source);
            Assert.Contains("spring.Period", source);
            Assert.Contains("6de91fe", source);
            Assert.DoesNotContain("CreateScalarKeyFrameAnimation", source);
        });
        Test("悬停预览：媒体库补齐资料并在面板内静音循环播放预告", () =>
        {
            var poster = Read("src/Momoka.Shell/Views/PosterCard.xaml.cs");
            Assert.Contains("OpenPreviewAfterDetailsAsync", poster);
            Assert.Contains("EnsurePreviewDetailsAsync(card, CancellationToken.None)", poster);
            Assert.Contains("client.GetItemAsync(card.Item.Id, token, EmbyFields.Preview)", poster);
            Assert.Contains("card.ApplyPreviewDetails(detail);", poster);
            Assert.Contains("StoreTrailerPreview.CreateAsync(subject, cts.Token)", poster);
            Assert.Contains("preview.PlayTrailer(trailer);", poster);

            Assert.Contains("if (!ShellPrefs.Trailers.HoverAutoplay) return;", poster);
            var preview = Read("src/Momoka.Shell/Views/CardPreview.xaml.cs");
            Assert.Contains("internal void PlayTrailer(StoreTrailerPreview preview)", preview);
            Assert.Contains("IsMuted = preferences.HoverMuted,", preview);
            Assert.Contains("IsLoopingEnabled = preferences.HoverLoop,", preview);
            Assert.Contains("AutoPlay = true", preview);
            Assert.Contains("AreTransportControlsEnabled = false", preview);
            Assert.Contains("internal void StopTrailer()", preview);
            Assert.Contains("StopTrailer();", preview[preview.IndexOf("internal void Reset(", StringComparison.Ordinal)..]);

            var fields = Read("src/Momoka.Core/Emby/ItemQuery.cs");
            var start = fields.IndexOf("public const string Preview", StringComparison.Ordinal);
            var end = fields.IndexOf(';', start);
            var previewFields = fields[start..end];
            foreach (var name in new[] { "Overview", "Genres", "CommunityRating", "ProductionYear", "PremiereDate" })
                Assert.Contains(name, previewFields);
        });
        Test("悬停预告：设置页「界面 → 预告片」四项与当场生效的接线", () =>
        {
            var settings = Read("src/Momoka.Shell/ViewModels/SettingsViewModel.cs");
            Assert.Contains("Sections.Add(TrailerCard());", settings);
            Assert.Contains("private SettingSection TrailerCard()", settings);
            Assert.Contains("new SettingSection(\"界面\", \"预告片\"", settings);
            foreach (var label in new[] { "封面悬停自动播放", "悬停预告片静音", "悬停预告片循环", "详情页自动播放" })
                Assert.Contains($"Toggle(\"{label}\"", settings);
            Assert.Contains("ShellPrefs.ApplyTrailers(Settings.Trailer);", settings);
            Assert.Contains("rows.RemoveAll(row => row.Label is \"悬停预告片静音\" or \"悬停预告片循环\");", settings);

            var prefs = Read("src/Momoka.Shell/ShellPrefs.cs");
            Assert.Contains("public static TrailerSettings Trailers { get; private set; } = new();", prefs);
            Assert.Contains("public static void ApplyTrailers(TrailerSettings trailer)", prefs);
            Assert.Contains("TrailerChanged?.Invoke(Trailers);", prefs);

            var app = Read("src/Momoka.Shell/App.xaml.cs");
            Assert.Contains("ShellPrefs.ApplyTrailers(settings.Settings.Trailer);", app);

            var preview = Read("src/Momoka.Shell/Views/CardPreview.xaml.cs");
            Assert.Contains("ShellPrefs.TrailerChanged += OnTrailerPreferenceChanged;", preview);
            Assert.Contains("if (!preferences.HoverAutoplay)", preview);
            Assert.Contains("player.IsMuted = preferences.HoverMuted;", preview);
            Assert.Contains("player.IsLoopingEnabled = preferences.HoverLoop;", preview);

            var detail = Read("src/Momoka.Shell/Views/DetailPage.StoreTrailer.cs");
            Assert.Contains("!ShellPrefs.Trailers.DetailAutoplay", detail);
            Assert.Contains("ShellPrefs.TrailerChanged += OnTrailerPreferencesChanged;", detail);
            var subscription = Read("src/Momoka.Shell/Views/MoviePilotSubscriptionPage.Trailer.cs");
            Assert.Contains("!ShellPrefs.Trailers.DetailAutoplay", subscription);
            Assert.Contains("ShellPrefs.TrailerChanged += OnTrailerPreferencesChanged;", subscription);
        });
        Test("悬停详情：徽标与片名带着面板里的最后位置飞进详情页", () =>
        {
            var preview = Read("src/Momoka.Shell/Views/CardPreview.xaml.cs");
            Assert.Contains("internal PreviewHeadingCapture? CaptureHeading(double offsetX, double offsetY, EmbyItem item)", preview);
            Assert.Contains("element.TransformToVisual(Panel)", preview);
            Assert.Contains("TitleText.FontSize", preview);
            Assert.Contains("ItemArtwork.Plate(item)", preview);

            var poster = Read("src/Momoka.Shell/Views/PosterCard.xaml.cs");
            var raise = poster[poster.IndexOf("private void Raise(", StringComparison.Ordinal)..];
            var capture = raise.IndexOf("CaptureHeading(", StringComparison.Ordinal);
            Assert.True(capture >= 0 && capture < raise.IndexOf("SuppressHoverDwell();", StringComparison.Ordinal),
                "位置只能在面板收起之前量");
            Assert.Contains("internal PreviewHeadingCapture? TakePreviewHeading()", poster);

            Assert.Contains("PreviewHeadingTransition.Prepare(Frame, poster);", Read("src/Momoka.Shell/Views/HomePage.xaml.cs"));
            Assert.Contains("PreviewHeadingTransition.Prepare(Frame, poster);", Read("src/Momoka.Shell/Views/LibraryPage.xaml.cs"));
            var detail = Read("src/Momoka.Shell/Views/DetailPage.xaml.cs");
            Assert.Contains("PreviewHeadingTransition.Attach(this, request.Item);", detail);
            Assert.Contains("StopPreviewHeadingMotion();", detail);

            var flight = Read("src/Momoka.Shell/Views/DetailPage.PreviewHeading.cs");
            Assert.Contains("PreviewHeadingPlacement.Scale(capture.TitleFontSize, TitleText.FontSize)", flight);
            Assert.Contains("SeedConnectedLayoutWidth();", flight);
            Assert.Contains("ForceConnectedLayout();", flight);
            Assert.Contains("HomeMotion.AnimationsEnabled", flight);
            Assert.Contains("StopConnectedPlateArrival();", flight);

            var transition = Read("src/Momoka.Shell/Views/PreviewHeadingTransition.cs");
            Assert.Contains("ReferenceEquals(Item.Source?.UserStateOwner, other.Source?.UserStateOwner)", transition);
            Assert.Contains("Capture.Matches(request.Item)", transition);
        });
        Test("悬浮预览：右上角操作键共用主题色样式，侧栏预览的「查看更多」在标题行右端", () =>
        {
            // 2026-11-27 用户令「查看更多移到右上角、按钮用主题色、圆角与封面悬浮一致」与「封面悬浮右上角的按钮用主题色」。
            // 两块浮层的同一颗键因此共用一份几何；底色交给强调键模板，标记里不另写颜色 —— 换主题时跟着 ThemeHost 走。
            var styles = Read("src/Momoka.Shell/Theme/Styles.xaml");
            var shared = styles[styles.IndexOf("x:Key=\"EgPreviewActionButtonStyle\"", StringComparison.Ordinal)..];
            shared = shared[..shared.IndexOf("</Style>", StringComparison.Ordinal)];
            Assert.Contains("BasedOn=\"{StaticResource EgPrimaryButtonStyle}\"", shared);
            Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"{StaticResource EgMediaActionCornerRadius}\" />", shared);

            var card = Read("src/Momoka.Shell/Views/CardPreview.xaml");
            Assert.DoesNotContain("EgSecondaryButtonStyle", card);
            const string action = "Style=\"{StaticResource EgPreviewActionButtonStyle}\"";
            var first = card.IndexOf(action, StringComparison.Ordinal);
            Assert.True(first >= 0 && card.IndexOf(action, first + 1, StringComparison.Ordinal) > first,
                "封面悬浮的播放键与编辑订阅键没有共用主题色样式");

            // 「查看更多」在库名那一行的最后一列，不再占封面下面整行；聚合模式关掉时整颗收起。
            var shell = Read("src/Momoka.Shell/Views/ShellPage.xaml");
            var more = shell[shell.IndexOf("x:Name=\"LibraryPreviewMore\"", StringComparison.Ordinal)..];
            more = more[..more.IndexOf("/>", StringComparison.Ordinal)];
            Assert.Contains("Grid.Column=\"1\"", more);
            Assert.Contains(action, more);
            Assert.DoesNotContain("Grid.Row=\"2\"", more);
            Assert.DoesNotContain("HorizontalAlignment=\"Stretch\"", more);
        });
    }

    private static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory, "找不到方案根目录");
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }
}
