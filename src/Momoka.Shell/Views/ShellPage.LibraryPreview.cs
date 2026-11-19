using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Music;
using Momoka.Shell.Interop;
using Momoka.Shell.Media;
using Windows.Foundation;

namespace Momoka.Shell.Views;

/// <summary>
/// 折叠侧栏的媒体库悬停预览：指针停在图标栏里某个库的图标上时，从栏右边滑出一块小面板，显示库名和几张
/// 媒体封面；聚合模式下多一颗「查看更多」，点它进这个库的完整页面。点封面走统一的 OpenItem 下钻，能够播放的
/// 条目在鼠标悬停时浮出遮罩和独立播放键。侧栏那两条 MoviePilot 订阅入口（订阅·电影 / 订阅·电视剧）用的是
/// 同一块面板：三张订阅海报，点海报进那条订阅的详情页（2026-11-18 用户令「给侧边栏的订阅也加悬浮」）。
/// <para>
/// 只在<b>图标栏</b>（<c>Sidebar.IsPaneOpen == false</c>）这一档发生，因为展开时库名本来就写在栏里，再弹一块
/// 同名浮层只是重复。面板左沿由代码钉在栏右边，永远不压住那 56 宽的一列 —— 这是用户点名要的
/// 「不要遮挡侧边栏」。
/// </para>
/// <para>
/// 鼠标连续静止半秒才展开，点击后直到真正离开图标才允许重开。指针从图标移到面板跨过栏沿的小缝时，
/// 收起留一小段宽限；面板自己的指针进入会取消收起。网络与图片读取走既有的 client / 图片缓存。
/// </para>
/// </summary>
public sealed partial class ShellPage
{
    private const int PreviewCoverCount = 3;
    private const double PreviewMaxWidth = 360;
    private const double PreviewMinWidth = 160;
    private const double PreviewCoverGap = 10;
    private const double PreviewPadding = 16;
    private const double PreviewCoverShadeOpacity = 0.78;

    private DispatcherQueueTimer? _previewOpenTimer;
    private DispatcherQueueTimer? _previewCloseTimer;
    private ScaleTransform? _previewScale;
    private Storyboard? _previewStoryboard;
    private CancellationTokenSource? _previewLoad;
    private EmbyImageStore? _previewImages;
    private NavigationViewItem? _previewAnchor;
    private NavigationViewItem? _previewHoverAnchor;
    private readonly Dictionary<NavigationViewItem, PosterHoverDwell> _previewDwells = [];
    private string? _previewTag;

    private bool CanPreviewLibrary => _settings is not null && SidebarShown && !Sidebar.IsPaneOpen;

    private void InitializeLibraryPreview()
    {
        _previewOpenTimer = DispatcherQueue.CreateTimer();
        _previewOpenTimer.IsRepeating = false;
        _previewOpenTimer.Interval = TimeSpan.FromMilliseconds(PosterHoverDwell.DelayMs);
        _previewOpenTimer.Tick += OnPreviewOpenTick;
        // NavigationViewItem 会处理按下事件；仍监听这一拍，在导航/捕获变化之前取消悬停。
        Sidebar.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSidebarPreviewPointerPressed), true);

        _previewCloseTimer = DispatcherQueue.CreateTimer();
        _previewCloseTimer.IsRepeating = false;
        _previewCloseTimer.Interval = TimeSpan.FromMilliseconds(160);
        _previewCloseTimer.Tick += (_, _) =>
        {
            _previewCloseTimer?.Stop();
            HideLibraryPreview();
        };

        // 订阅那两条是 XAML 里的常驻项，接线只做一次；库项每次重建栏时重新接（见 RebuildSidebarLibraries）。
        foreach (var item in new[] { SidebarMoviePilotMovies, SidebarMoviePilotSeries })
        {
            AttachSidebarPreviewItem(item);
            TrackPreviewItem(item);
        }
    }

    /// <summary>
    /// 图标栏里一项要接的那套悬停接线：占住框架自带的那颗「库名」提示（半秒后弹出的预览面板标题写的就是
    /// 同一个名字，两颗一起出现就是用户 2026-11-14 报的「显示了两个 电影」），再接三只指针事件。
    /// <para>
    /// 提示之所以挂一颗零尺寸、透明的空提示：NavigationViewItem 只在条目还没有自定义提示时才把 Content
    /// 塞进 ToolTip（WinUI 的 UpdateNavigationViewItemToolTip）。订阅那两项以前会弹「订阅·电视剧」那颗
    /// 提示，现在和库项一样让位给预览面板。
    /// </para>
    /// </summary>
    private void AttachSidebarPreviewItem(NavigationViewItem item)
    {
        ToolTipService.SetToolTip(item, new ToolTip
        {
            Background = null,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0)
        });
        item.PointerEntered += OnSidebarLibraryPointerEntered;
        item.PointerExited += OnSidebarLibraryPointerExited;
        item.AddHandler(PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnSidebarLibraryPointerMoved), true);
    }

    /// <summary>每项一份静止计时；重建栏清空表之后由库项与订阅项各自登记回来。</summary>
    private void TrackPreviewItem(NavigationViewItem item)
    {
        if (!_previewDwells.ContainsKey(item)) _previewDwells.Add(item, new PosterHoverDwell());
    }

    private void OnSidebarLibraryPointerEntered(object sender, PointerRoutedEventArgs e)
        => OnSidebarLibraryPointerMoved(sender, e);

    private void OnSidebarLibraryPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not NavigationViewItem { Tag: string tag } item || !CanPreviewLibrary
            || e is null || e.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
        var buttons = e.GetCurrentPoint(item).Properties;
        if (buttons.IsLeftButtonPressed || buttons.IsRightButtonPressed || buttons.IsMiddleButtonPressed)
        {
            SuppressLibraryPreview(item);
            return;
        }
        if (!TryReadSidebarPreviewPointer(item, out var position)) return;

        // 一个库的点击抑制只在真正移开后解除；布局/捕获产生的同坐标 Exit/Enter 不能解除。
        foreach (var (other, state) in _previewDwells)
        {
            if (!ReferenceEquals(other, item) && state.IsSuppressed && !TryReadSidebarPreviewPointer(other, out _))
                state.Reset();
        }
        if (!_previewDwells.TryGetValue(item, out var dwell)) return;
        if (!ReferenceEquals(_previewHoverAnchor, item))
        {
            if (_previewHoverAnchor is { } previous && _previewDwells.TryGetValue(previous, out var old)) old.Cancel();
            _previewOpenTimer?.Stop();
            _previewHoverAnchor = item;
        }

        _previewCloseTimer?.Stop();

        // 面板已经摆着同一个库时只续命，不重播展开动画。
        if (LibraryPreviewPanel.Visibility == Visibility.Visible && _previewTag == tag) return;
        if (dwell.Observe(position.X, position.Y, Environment.TickCount64)) StartPreviewOpenTimer(dwell);
    }

    private void StartPreviewOpenTimer(PosterHoverDwell dwell)
    {
        _previewOpenTimer?.Stop();
        if (_previewOpenTimer is null) return;
        _previewOpenTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, dwell.RemainingMilliseconds(Environment.TickCount64)));
        _previewOpenTimer.Start();
    }

    private void OnPreviewOpenTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_previewHoverAnchor is not { Tag: string tag } anchor
            || !_previewDwells.TryGetValue(anchor, out var dwell) || !dwell.IsPending) return;
        if (!CanPreviewLibrary || !TryReadSidebarPreviewPointer(anchor, out var position))
        {
            dwell.Cancel();
            _previewHoverAnchor = null;
            SchedulePreviewClose();
            return;
        }
        var now = Environment.TickCount64;
        dwell.Observe(position.X, position.Y, now);
        if (!dwell.TryComplete(now))
        {
            StartPreviewOpenTimer(dwell);
            return;
        }
        _ = ShowLibraryPreviewAsync(anchor, tag);
    }

    private bool TryReadSidebarPreviewPointer(NavigationViewItem item, out (int X, int Y) position)
        => TryReadPreviewPointer(item, out position);

    private bool TryReadPreviewPointer(FrameworkElement target, out (int X, int Y) position)
    {
        position = default;
        if (XamlRoot is not { Content: UIElement content } root || !ReferenceEquals(target.XamlRoot, root)
            || target.ActualWidth <= 0 || target.ActualHeight <= 0)
            return false;
        if (!Native.GetCursorPos(out var cursor)) return false;
        position = (cursor.X, cursor.Y);
        var id = root.ContentIslandEnvironment?.AppWindowId;
        var handle = id is { } window ? Win32Interop.GetWindowFromWindowId(window) : IntPtr.Zero;
        if (handle == IntPtr.Zero || root.RasterizationScale <= 0 || !Native.ScreenToClient(handle, ref cursor)) return false;
        try
        {
            var bounds = target.TransformToVisual(content).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
            return bounds.Contains(new Point(cursor.X / root.RasterizationScale, cursor.Y / root.RasterizationScale));
        }
        catch (ArgumentException)
        {
            // 重建媒体库时，旧条目的退出事件可能晚于视觉树脱离。
            return false;
        }
    }

    private void OnSidebarLibraryPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not NavigationViewItem item || TryReadSidebarPreviewPointer(item, out _)) return;
        if (_previewDwells.TryGetValue(item, out var dwell)) dwell.Reset();
        if (ReferenceEquals(_previewHoverAnchor, item))
        {
            _previewOpenTimer?.Stop();
            _previewHoverAnchor = null;
        }
        SchedulePreviewClose();
    }

    private void OnSidebarPreviewPointerPressed(object sender, PointerRoutedEventArgs e) => SuppressLibraryPreview();

    private void SuppressLibraryPreview(NavigationViewItem? invoked = null)
    {
        foreach (var item in new[] { invoked, _previewHoverAnchor, _previewAnchor })
        {
            if (item is not null && _previewDwells.TryGetValue(item, out var dwell)
                && TryReadSidebarPreviewPointer(item, out _)) dwell.Suppress();
        }
        HideLibraryPreview();
    }

    private void OnLibraryPreviewPointerEntered(object sender, PointerRoutedEventArgs e) => _previewCloseTimer?.Stop();

    private void OnLibraryPreviewPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // 封面变为按钮后，其捕获/子元素退出同样会冒泡，鼠标仍在面板里就不收起。
        if (!TryReadPreviewPointer(LibraryPreviewPanel, out _)) SchedulePreviewClose();
    }

    private void SchedulePreviewClose()
    {
        if (LibraryPreviewPanel.Visibility != Visibility.Visible) return;
        _previewCloseTimer?.Stop();
        _previewCloseTimer?.Start();
    }

    private async Task ShowLibraryPreviewAsync(NavigationViewItem anchor, string tag)
    {
        if (!CanPreviewLibrary) return;
        if (HomeLayout.IsMoviePilot(tag))
        {
            await ShowSubscriptionPreviewAsync(anchor, tag);
            return;
        }
        if (!_libraries.TryGetValue(tag, out var request)) return;

        CancellationTokenSource? load = null;
        try
        {
            // 预览标题照栏里那一项写：聚合模式（两个及以上在线账号）时它是「用户名 · 库名」，单账号时就是库名。
            // 副标题（服务器名）2026-11-16 按用户令去掉，来源已经写在同一行里。
            load = PresentLibraryPreview(anchor, tag, anchor.Content as string ?? request.Title);
            await FillLibraryPreviewAsync(request, tag, load).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (load is not null && IsCurrentPreview(load)) ShowPreviewEmpty("预览读取失败，稍后再试");
            Log.Debug("媒体库预览", $"读取预览失败（{request.Title}）：{error.Message}");
        }
    }

    /// <summary>
    /// 把面板摆出来、立起这一趟的取消边界并返回它；两个数据源（媒体库与订阅）共用这一段。
    /// </summary>
    private CancellationTokenSource PresentLibraryPreview(NavigationViewItem anchor, string tag, string label)
    {
        _previewAnchor = anchor;
        _previewTag = tag;
        _previewLoad?.Cancel();
        _previewLoad?.Dispose();
        _previewLoad = null;

        ResetPreviewCovers();
        LibraryPreviewTitle.Text = label;
        LibraryPreviewMore.Visibility = Settings.AggregateMode ? Visibility.Visible : Visibility.Collapsed;
        LibraryPreviewEmpty.Visibility = Visibility.Collapsed;
        LibraryPreviewLoading.IsActive = false;
        AutomationProperties.SetName(LibraryPreviewPanel, $"媒体库预览：{label}");

        LibraryPreviewPanel.Visibility = Visibility.Visible;
        PlaceLibraryPreview();
        StartPreviewMotion();

        var load = new CancellationTokenSource();
        _previewLoad = load;
        return load;
    }

    /// <summary>
    /// 侧栏两条订阅入口的预览（2026-11-18 用户令「给侧边栏的订阅也加悬浮」）：三张订阅海报，点海报进那条
    /// 订阅的详情页；「查看更多」与其他库项同一条规则（聚合模式才出现），点它进订阅列表。
    /// </summary>
    private async Task ShowSubscriptionPreviewAsync(NavigationViewItem anchor, string tag)
    {
        if (_moviePilotService is not { Available: true }) return;
        var label = anchor.Content as string
            ?? (tag == HomeLayout.MoviePilotSeries ? "订阅·电视剧" : "订阅·电影");
        CancellationTokenSource? load = null;
        try
        {
            load = PresentLibraryPreview(anchor, tag, label);
            await FillSubscriptionPreviewAsync(tag, load).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (load is not null && IsCurrentPreview(load)) ShowPreviewEmpty("预览读取失败，稍后再试");
            Log.Debug("订阅预览", $"读取订阅预览失败（{label}）：{error.Message}");
        }
    }

    private async Task FillSubscriptionPreviewAsync(string tag, CancellationTokenSource load)
    {
        if (_moviePilotService is not { Available: true } service)
        {
            ShowPreviewEmpty("暂时读不到订阅");
            return;
        }

        LibraryPreviewLoading.IsActive = true;
        IReadOnlyList<MoviePilotSubscription> subscriptions;
        try
        {
            subscriptions = await service.SubscriptionsAsync(load.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception error)
        {
            if (IsCurrentPreview(load)) ShowPreviewEmpty("预览读取失败，稍后再试");
            Log.Debug("订阅预览", $"读取订阅列表失败：{error.Message}");
            return;
        }

        if (!IsCurrentPreview(load)) return;

        var series = tag == HomeLayout.MoviePilotSeries;
        var picks = subscriptions
            .Where(item => item.IsSeries == series && SubscriptionPoster(item) is not null)
            .Take(PreviewCoverCount)
            .ToArray();
        if (picks.Length == 0)
        {
            LibraryPreviewLoading.IsActive = false;
            ShowPreviewEmpty(series ? "还没有电视剧订阅" : "还没有电影订阅");
            return;
        }

        // 图片走 MoviePilot 自己的图片代理（服务器够得着图源，客户端不一定），失败只留占位。
        var fetch = service.CaptureImageFetcher();
        var coverWidth = PreviewCoverWidth();
        for (var index = 0; index < picks.Length; index++)
        {
            var subscription = picks[index];
            var (cell, picture, placeholder) = CreateSubscriptionCover(coverWidth, index, subscription, load);
            LibraryPreviewCovers.Children.Add(cell);
            _ = LoadSubscriptionCoverAsync(fetch, picture, placeholder, subscription,
                Math.Max(56, (int)Math.Round(coverWidth)), load);
        }

        LibraryPreviewLoading.IsActive = false;
        PlaceLibraryPreview();
    }

    /// <summary>订阅海报的地址：只认绝对的 http(s) 地址，其余按缺图处理。</summary>
    private static string? SubscriptionPoster(MoviePilotSubscription subscription)
        => Uri.TryCreate(subscription.Media.PosterUrl, UriKind.Absolute, out var url) && url.Scheme is "http" or "https"
            ? url.AbsoluteUri
            : null;

    private (FrameworkElement Cell, Image Picture, FontIcon Placeholder) CreateSubscriptionCover(double width, int column,
        MoviePilotSubscription subscription, CancellationTokenSource load)
    {
        // 订阅海报沿用影视封面那一档 2:3；点它是打开订阅详情，不是播放。
        var height = Math.Round(width * 1.5);
        var placeholder = new FontIcon
        {
            Glyph = "\uE8B2",
            FontSize = (double)Application.Current.Resources["EgTitleFontSize"],
            Foreground = (Brush)Application.Current.Resources["EgTextFaintBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var picture = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
        var content = new Grid();
        content.Children.Add(placeholder);
        content.Children.Add(picture);
        var button = new Button
        {
            Width = width,
            Height = height,
            Style = (Style)Application.Current.Resources["EgArtworkTileStyle"],
            BorderThickness = new Thickness(0),
            Content = content,
            Tag = subscription
        };
        AutomationProperties.SetAutomationId(button, "SidebarSubscriptionPreviewOpen_" + column);
        AutomationProperties.SetName(button, $"打开订阅：{subscription.Title}");
        button.Click += (_, _) => OnSubscriptionPreviewCoverClicked(button, subscription, load);

        var title = new TextBlock
        {
            Text = subscription.Title,
            Style = (Style)Application.Current.Resources["EgCaptionStyle"],
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["EgTextBrush"],
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap
        };
        ToolTipService.SetToolTip(title, subscription.Title);

        var cell = new StackPanel { Width = width, Spacing = 6 };
        cell.Children.Add(button);
        cell.Children.Add(title);
        Grid.SetColumn(cell, column);
        return (cell, picture, placeholder);
    }

    private void OnSubscriptionPreviewCoverClicked(Button button, MoviePilotSubscription subscription, CancellationTokenSource load)
    {
        if (!IsCurrentPreview(load)
            || !LibraryPreviewCovers.Children.OfType<StackPanel>().Any(cell => cell.Children.Contains(button))) return;
        SuppressLibraryPreview();
        OpenSubscription(subscription);
    }

    private async Task LoadSubscriptionCoverAsync(Func<string, CancellationToken, Task<byte[]>> fetch,
        Image picture, FontIcon placeholder, MoviePilotSubscription subscription, int width, CancellationTokenSource load)
    {
        try
        {
            if (SubscriptionPoster(subscription) is not { } poster) return;
            var bytes = await fetch(poster, load.Token).ConfigureAwait(true);
            var bitmap = await PosterLoader.DecodeAsync(bytes, width).ConfigureAwait(true);
            if (bitmap is null || !IsCurrentPreview(load)) return;

            picture.Source = bitmap;
            picture.Visibility = Visibility.Visible;
            placeholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Log.Debug("订阅预览", $"海报读取失败（{subscription.Title}）：{error.Message}");
        }
    }

    private async Task FillLibraryPreviewAsync(LibraryRequest request, string tag, CancellationTokenSource load)
    {
        var library = _libraryViews.FirstOrDefault(view => HomeLayout.LibraryKey(view) == tag);
        var scope = library?.Source ?? (_session is { IsSignedIn: true } session ? session.Capture() : null);
        if (scope is not { IsAvailable: true } || library is null)
        {
            ShowPreviewEmpty("暂时读不到这个库");
            return;
        }

        LibraryPreviewLoading.IsActive = true;
        List<EmbyItem> items;
        try
        {
            items = await scope.ExecuteAsync(async (client, token) =>
            {
                // 音乐库（含飞牛音乐）列专辑：Items/Latest 对飞牛音乐直接回空，预览于是永远说「没有封面」。
                if (HomeLatest.UsesMusicQuery(library))
                {
                    var page = await client.GetItemsAsync(HomeLatest.MusicQuery(library.Id, 12), token).ConfigureAwait(false);
                    return page.Items;
                }
                if (HomeLatest.UsesSeriesQuery(library))
                {
                    var page = await client.GetItemsAsync(HomeLatest.SeriesQuery(library.Id, 12), token).ConfigureAwait(false);
                    return page.Items;
                }
                return await client.GetLatestAsync(library.Id, 12, token, HomeLatest.LibraryTypes(library)).ConfigureAwait(false);
            }, load.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception error)
        {
            if (IsCurrentPreview(load)) ShowPreviewEmpty("预览读取失败，稍后再试");
            Log.Debug("媒体库预览", $"读取最近添加失败（{request.Title}）：{error.Message}");
            return;
        }

        if (!IsCurrentPreview(load)) return;

        var picks = items.Where(Previewable).Take(PreviewCoverCount).ToArray();
        if (picks.Length == 0)
        {
            LibraryPreviewLoading.IsActive = false;
            ShowPreviewEmpty("这个库里还没有可预览的封面");
            return;
        }

        _previewImages ??= _services?.GetService<EmbyImageStore>();
        var coverWidth = PreviewCoverWidth();
        var square = MusicCatalog.IsLibrary(request.CollectionType);
        for (var index = 0; index < picks.Length; index++)
        {
            var item = picks[index];
            var (cell, picture, placeholder) = CreatePreviewCover(coverWidth, index, item, load, square);
            LibraryPreviewCovers.Children.Add(cell);
            if (_previewImages is not null && PreviewImageType(item) is { } imageType)
                _ = LoadPreviewCoverAsync(picture, placeholder, item, item.Source ?? scope, imageType,
                    Math.Max(56, (int)Math.Round(coverWidth)), load);
        }

        LibraryPreviewLoading.IsActive = false;
        PlaceLibraryPreview();
    }

    private void ResetPreviewCovers()
    {
        foreach (var cell in LibraryPreviewCovers.Children.OfType<StackPanel>())
        {
            var frame = cell.Children.OfType<FrameworkElement>().FirstOrDefault();
            var content = frame switch
            {
                Button button => button.Content as UIElement,
                Border border => border.Child,
                Grid cover => cover.Children.OfType<Button>().FirstOrDefault()?.Content as UIElement,
                _ => null
            };
            if (content is not Grid grid) continue;
            foreach (var picture in grid.Children.OfType<Image>()) picture.Source = null;
        }
        LibraryPreviewCovers.Children.Clear();
    }

    private static bool Previewable(EmbyItem item) =>
        !string.IsNullOrEmpty(item.Name) && PreviewImageType(item) is not null;

    private static string? PreviewImageType(EmbyItem item)
    {
        foreach (var type in PreviewImageTypes)
        {
            if (EmbyImageStore.TagFor(item, type) is { Length: > 0 }) return type;
        }
        return null;
    }

    private static readonly string[] PreviewImageTypes =
        [EmbyImageStore.Primary, EmbyImageStore.Thumb, EmbyImageStore.Backdrop];

    private (FrameworkElement Cell, Image Picture, FontIcon Placeholder) CreatePreviewCover(double width, int column,
        EmbyItem item, CancellationTokenSource load, bool square = false)
    {
        // 音乐预览沿用音乐页的方形唱片，影视预览保留 2:3 海报。
        var height = square ? width : Math.Round(width * 1.5);
        var placeholder = new FontIcon
        {
            Glyph = square ? "\uE8D6" : "\uE8B2",
            FontSize = (double)Application.Current.Resources["EgTitleFontSize"],
            Foreground = (Brush)Application.Current.Resources["EgTextFaintBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var picture = new Image { Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
        var shade = new Border
        {
            Background = (Brush)Application.Current.Resources["EgScrimBrush"],
            IsHitTestVisible = false,
            Opacity = 0
        };
        var content = new Grid();
        content.Children.Add(placeholder);
        content.Children.Add(picture);
        content.Children.Add(shade);
        // Gallery ButtonPage.xaml Example2 / Button_Click，固定提交 6de91feccadfbb0a796b71caedead5a2284f730e：
        // 保留 Button + 图片内容 + Click + 无障碍名称。详情面与播放键沿用 PosterCard.xaml 的兄弟结构，
        // 不把一颗 Button 嵌进另一颗；悬停遮罩与播放键分别复用 EgScrimBrush 和 EgOverlayPlayButtonStyle。
        FrameworkElement frame;
        if (!item.IsDisplayOnly)
        {
            var details = new Button
            {
                Width = width,
                Height = height,
                Style = (Style)Application.Current.Resources["EgArtworkTileStyle"],
                BorderThickness = new Thickness(0),
                Content = content,
                Tag = item
            };
            AutomationProperties.SetAutomationId(details, "SidebarLibraryPreviewOpen_" + column);
            AutomationProperties.SetName(details, $"查看{item.Name}详情");
            details.Click += (_, _) => OnLibraryPreviewCoverClicked(details, item, load);

            var cover = new Grid { Width = width, Height = height };
            cover.Children.Add(details);
            Button? play = null;
            if (CanPlayPreviewItem(item))
            {
                play = new Button
                {
                    Style = (Style)Application.Current.Resources["EgOverlayPlayButtonStyle"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                    IsTabStop = false,
                    Opacity = 0,
                    Tag = item,
                    Content = new FontIcon
                    {
                        Glyph = "\uE768",
                        Style = (Style)Application.Current.Resources["EgPlayGlyphStyle"]
                    }
                };
                play.Resources["ButtonBackgroundPointerOver"] = Application.Current.Resources["EgPlayGlassHoverBrush"];
                play.Resources["ButtonBackgroundPressed"] = Application.Current.Resources["EgPlayGlassPressedBrush"];
                play.Resources["ButtonForegroundPointerOver"] = Application.Current.Resources["EgTextOnAccentBrush"];
                play.Resources["ButtonForegroundPressed"] = Application.Current.Resources["EgTextOnAccentBrush"];
                AutomationProperties.SetAutomationId(play, "SidebarLibraryPreviewPlay_" + column);
                AutomationProperties.SetName(play, $"播放：{item.Name}");
                ToolTipService.SetToolTip(play, $"播放：{item.Name}");
                play.Click += (_, _) => OnLibraryPreviewPlayClicked(play, item, load);
                cover.Children.Add(play);
            }

            void SetHover(bool hovered)
            {
                shade.Opacity = hovered ? PreviewCoverShadeOpacity : 0;
                if (play is null) return;
                play.Opacity = hovered ? 1 : 0;
                play.IsHitTestVisible = hovered;
            }

            cover.PointerEntered += (_, e) =>
            {
                if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse) SetHover(true);
            };
            cover.PointerExited += (_, e) =>
            {
                // 与 PosterCard 相同：子按钮的捕获/退出也会冒泡，指针仍在封面里时不能把按钮藏起来。
                var point = e.GetCurrentPoint(cover).Position;
                if (point.X >= 0 && point.Y >= 0 && point.X < cover.Width && point.Y < cover.Height) return;
                SetHover(false);
            };
            frame = cover;
        }
        else
        {
            frame = new Border
            {
                Width = width,
                Height = height,
                CornerRadius = (CornerRadius)Application.Current.Resources["EgPosterCornerRadius"],
                Background = (Brush)Application.Current.Resources["EgSurfaceAltBrush"],
                Child = content
            };
            AutomationProperties.SetAccessibilityView(frame, AccessibilityView.Raw);
        }
        // 封面下面那一行片名（2026-11-14 用户令「给封面下方加上片名」）：面板里只有图的时候认不出是哪一部。
        // 两行封顶、再长就省略，完整名字交给指针提示；字号 / 颜色沿用正文卡片标题那一条的缩格版。
        var title = new TextBlock
        {
            Text = item.Name,
            Style = (Style)Application.Current.Resources["EgCaptionStyle"],
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["EgTextBrush"],
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap
        };
        ToolTipService.SetToolTip(title, item.Name);

        var cell = new StackPanel { Width = width, Spacing = 6 };
        cell.Children.Add(frame);
        cell.Children.Add(title);
        Grid.SetColumn(cell, column);
        return (cell, picture, placeholder);
    }

    private static bool CanPlayPreviewItem(EmbyItem item) => !item.IsDisplayOnly
        && (item.IsPlayable || item.Type is EmbyItemType.Series or EmbyItemType.Season);

    private bool IsCurrentPreviewCoverButton(Button button, EmbyItem item, CancellationTokenSource load) =>
        IsCurrentPreview(load) && item.Source is { IsAvailable: true }
        && LibraryPreviewCovers.Children.OfType<StackPanel>()
            .Select(cell => cell.Children.OfType<Grid>().FirstOrDefault())
            .Any(cover => cover?.Children.Contains(button) == true);

    private void OnLibraryPreviewCoverClicked(Button button, EmbyItem item, CancellationTokenSource load)
    {
        if (!IsCurrentPreviewCoverButton(button, item, load) || item.IsDisplayOnly) return;
        SuppressLibraryPreview();
        OpenItem(item);
    }

    private void OnLibraryPreviewPlayClicked(Button button, EmbyItem item, CancellationTokenSource load)
    {
        if (!IsCurrentPreviewCoverButton(button, item, load) || !CanPlayPreviewItem(item)) return;
        SuppressLibraryPreview();
        _ = PlayAsync(item);
    }

    private async Task LoadPreviewCoverAsync(Image picture, FontIcon placeholder, EmbyItem item,
        EmbySessionScope scope, string imageType, int width, CancellationTokenSource load)
    {
        try
        {
            var tag = EmbyImageStore.TagFor(item, imageType)!;
            var bitmap = await _previewImages!.GetDecodedAsync(scope, EmbyImageStore.ImageItemId(item, imageType),
                imageType, tag, EmbyImageStore.RequestWidth(width),
                bytes => PosterLoader.DecodeAsync(bytes, width), load.Token).ConfigureAwait(true);
            if (bitmap is null || !IsCurrentPreview(load)) return;

            picture.Source = bitmap;
            picture.Visibility = Visibility.Visible;
            placeholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Log.Debug("媒体库预览", $"封面读取失败（{item.Name}）：{error.Message}");
        }
    }

    private void ShowPreviewEmpty(string text)
    {
        LibraryPreviewLoading.IsActive = false;
        LibraryPreviewEmpty.Text = text;
        LibraryPreviewEmpty.Visibility = Visibility.Visible;
        PlaceLibraryPreview();
    }

    private bool IsCurrentPreview(CancellationTokenSource load) =>
        !load.IsCancellationRequested && ReferenceEquals(_previewLoad, load)
        && LibraryPreviewPanel.Visibility == Visibility.Visible;

    private double PreviewCoverWidth()
    {
        var width = LibraryPreviewPanel.Width;
        return Math.Max(56, Math.Floor((width - PreviewPadding * 2 - PreviewCoverGap * 2) / PreviewCoverCount));
    }

    /// <summary>
    /// 面板站在图标栏右边、与指针所在那一项同高：左沿由栏宽算出来（永不压栏），上下夹在标题栏和窗口底之间。
    /// 宽度跟着窗口收，三张封面始终在同一行里放得下。
    /// </summary>
    private void PlaceLibraryPreview()
    {
        if (_previewAnchor is not { } anchor || XamlRoot is null) return;

        var rail = Math.Max(SidebarHost.ActualWidth, Sidebar.CompactPaneLength);
        var available = ActualWidth - rail - 24;
        var width = Math.Max(PreviewMinWidth, Math.Min(PreviewMaxWidth, available));
        LibraryPreviewPanel.Width = width;
        LibraryPreviewPanel.MaxHeight = Math.Max(160, ActualHeight - HomePage.ChromeHeight - 16);

        var anchorTop = HomePage.ChromeHeight + 8;
        try
        {
            anchorTop = anchor.TransformToVisual(this).TransformPoint(new Point()).Y;
        }
        catch (ArgumentException)
        {
            // 图标还没进视图树：用标题栏下沿，下一次布局后由进入事件重新摆。
        }

        // 量之前先清掉上一次摆位留下的 Margin：WinUI 的 DesiredSize 含 Margin（WPF 不含），
        // 上一次的 top 会把这次量到的高度抬高一整个顶边距 —— 上限因此塌到标题栏下沿，第二次摆位
        // （封面读完那一次）就把面板从鼠标所在那一项甩到内容区最上面。
        LibraryPreviewPanel.Margin = default;
        LibraryPreviewPanel.Measure(new Size(width, double.PositiveInfinity));
        var height = Math.Min(LibraryPreviewPanel.DesiredSize.Height, LibraryPreviewPanel.MaxHeight);
        var top = Math.Clamp(anchorTop - 8, HomePage.ChromeHeight + 8,
            Math.Max(HomePage.ChromeHeight + 8, ActualHeight - height - 8));
        LibraryPreviewPanel.Margin = new Thickness(rail + 8, top, 0, 0);
    }

    private void StartPreviewMotion()
    {
        _previewStoryboard?.Stop();
        _previewStoryboard = null;
        _previewScale ??= new ScaleTransform();
        LibraryPreviewPanel.RenderTransform = _previewScale;

        if (!HomeMotion.AnimationsEnabled || LibraryPreviewPanel.XamlRoot is null)
        {
            _previewScale.ScaleX = 1;
            LibraryPreviewPanel.Opacity = 1;
            return;
        }

        _previewScale.ScaleX = 0.72;
        LibraryPreviewPanel.Opacity = 0;
        var storyboard = new Storyboard();
        var scale = new DoubleAnimation
        {
            From = 0.72,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scale, _previewScale);
        Storyboard.SetTargetProperty(scale, "ScaleX");
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, LibraryPreviewPanel);
        Storyboard.SetTargetProperty(fade, "Opacity");
        storyboard.Children.Add(scale);
        storyboard.Children.Add(fade);
        _previewStoryboard = storyboard;
        storyboard.Begin();
    }

    private void HideLibraryPreview()
    {
        _previewOpenTimer?.Stop();
        foreach (var dwell in _previewDwells.Values) dwell.Cancel();
        _previewHoverAnchor = null;
        _previewCloseTimer?.Stop();
        _previewStoryboard?.Stop();
        _previewStoryboard = null;
        _previewLoad?.Cancel();
        _previewLoad?.Dispose();
        _previewLoad = null;
        _previewAnchor = null;
        _previewTag = null;

        LibraryPreviewPanel.Visibility = Visibility.Collapsed;
        LibraryPreviewPanel.Opacity = 1;
        if (_previewScale is not null) _previewScale.ScaleX = 1;
        LibraryPreviewLoading.IsActive = false;
        LibraryPreviewEmpty.Visibility = Visibility.Collapsed;
        LibraryPreviewMore.Visibility = Visibility.Collapsed;
        ResetPreviewCovers();
    }

    private void OnLibraryPreviewMoreClicked(object sender, RoutedEventArgs e)
    {
        if (_previewTag is not { } tag) return;
        SuppressLibraryPreview();
        GoTo(tag);
        SyncSidebar();
    }
}
