using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Shell.Interop;
using Momoka.Shell.Media;
using Windows.Foundation;

namespace Momoka.Shell.Views;

/// <summary>
/// 折叠侧栏的媒体库悬停预览：指针停在图标栏里某个库的图标上时，从栏右边滑出一块小面板，显示库名和几张
/// 媒体封面；聚合模式下多一颗「查看更多」，点它进这个库的完整页面。
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
        if (!CanPreviewLibrary || !_libraries.TryGetValue(tag, out var request)) return;

        CancellationTokenSource? load = null;
        try
        {
            _previewAnchor = anchor;
            _previewTag = tag;
            _previewLoad?.Cancel();
            _previewLoad?.Dispose();
            _previewLoad = null;

            ResetPreviewCovers();
            LibraryPreviewTitle.Text = request.Title;
            LibraryPreviewMeta.Text = PreviewMeta(tag);
            LibraryPreviewMeta.Visibility = LibraryPreviewMeta.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            LibraryPreviewMore.Visibility = Settings.AggregateMode ? Visibility.Visible : Visibility.Collapsed;
            LibraryPreviewEmpty.Visibility = Visibility.Collapsed;
            LibraryPreviewLoading.IsActive = false;
            AutomationProperties.SetName(LibraryPreviewPanel, $"媒体库预览：{request.Title}");

            LibraryPreviewPanel.Visibility = Visibility.Visible;
            PlaceLibraryPreview();
            StartPreviewMotion();

            load = new CancellationTokenSource();
            _previewLoad = load;
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

    /// <summary>库名下面那一行：多账号（聚合）时写来源服务器，其余时候留空藏起来。</summary>
    private string PreviewMeta(string tag)
    {
        if (_session is not { } session || session.Sources.Count <= 1) return "";
        return _libraryViews.FirstOrDefault(view => HomeLayout.LibraryKey(view) == tag)?.Source?.ServerName ?? "";
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
        for (var index = 0; index < picks.Length; index++)
        {
            var item = picks[index];
            var (frame, picture, placeholder) = CreatePreviewCover(coverWidth, index, item, load);
            LibraryPreviewCovers.Children.Add(frame);
            if (_previewImages is not null && PreviewImageType(item) is { } imageType)
                _ = LoadPreviewCoverAsync(picture, placeholder, item, item.Source ?? scope, imageType,
                    Math.Max(56, (int)Math.Round(coverWidth)), load);
        }

        LibraryPreviewLoading.IsActive = false;
        PlaceLibraryPreview();
    }

    private void ResetPreviewCovers()
    {
        foreach (var child in LibraryPreviewCovers.Children.OfType<FrameworkElement>())
        {
            var content = child is Button button ? button.Content : (child as Border)?.Child;
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

    private (FrameworkElement Frame, Image Picture, FontIcon Placeholder) CreatePreviewCover(double width, int column,
        EmbyItem item, CancellationTokenSource load)
    {
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
        // Gallery ButtonPage.xaml Example2 / Button_Click，固定提交 6de91feccadfbb0a796b71caedead5a2284f730e：
        // 保留 Button + 图片内容 + Click + 无障碍名称；使用本地画框样式和既有播放入口。
        FrameworkElement frame;
        if (CanPlayPreviewItem(item))
        {
            var button = new Button
            {
                Width = width,
                Height = Math.Round(width * 1.5),
                Style = (Style)Application.Current.Resources["EgArtworkTileStyle"],
                BorderThickness = new Thickness(0),
                Content = content,
                Tag = item
            };
            AutomationProperties.SetAutomationId(button, "SidebarLibraryPreviewPlay_" + column);
            AutomationProperties.SetName(button, $"播放：{item.Name}");
            button.Click += (_, _) => OnLibraryPreviewCoverClicked(button, item, load);
            frame = button;
        }
        else
        {
            frame = new Border
            {
                Width = width,
                Height = Math.Round(width * 1.5),
                CornerRadius = (CornerRadius)Application.Current.Resources["EgPosterCornerRadius"],
                Background = (Brush)Application.Current.Resources["EgSurfaceAltBrush"],
                Child = content
            };
            AutomationProperties.SetAccessibilityView(frame, AccessibilityView.Raw);
        }
        Grid.SetColumn(frame, column);
        return (frame, picture, placeholder);
    }

    private static bool CanPlayPreviewItem(EmbyItem item) => !item.IsDisplayOnly
        && (item.IsPlayable || item.Type is EmbyItemType.Series or EmbyItemType.Season);

    private void OnLibraryPreviewCoverClicked(Button button, EmbyItem item, CancellationTokenSource load)
    {
        if (!IsCurrentPreview(load) || !LibraryPreviewCovers.Children.Contains(button)
            || item.Source is not { IsAvailable: true } || !CanPlayPreviewItem(item)) return;
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
