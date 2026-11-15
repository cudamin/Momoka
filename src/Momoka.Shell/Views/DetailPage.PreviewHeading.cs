using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Momoka.Emby;
using Momoka.Infrastructure;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace Momoka.Shell.Views;

public sealed partial class DetailPage
{
    /// <summary>从悬停面板飞进来的时长；和关联封面的平台补间落在同一档。</summary>
    private const int PreviewHeadingFlightMs = 320;

    private PreviewHeadingFlight? _previewHeadingFlight;

    /// <summary>
    /// 悬停预览里那枚徽标和片名移进这一页（2026-11-16 用户令）。源面板点击时就收起了，所以这里不搬元素：
    /// 让这一页自己的 <see cref="DetailPage.TitlePlate"/> 与 <see cref="DetailPage.TitleText"/> 从面板量到的
    /// 矩形起跑 —— 同一个名字、同一张徽标图，屏上读作同一件东西移了过来。
    /// <para>
    /// 先接住面板那张已经解好的徽标（<see cref="ViewModels.DetailViewModel.UseConnectedPlate"/>）再抢排一次：
    /// 徽标有没有、多高，决定了片名最终落在哪一行，落点必须在起跑前量准。任何一格量不出来就整段跳过，
    /// 页面照常呈现，不为动效推迟导航。
    /// </para>
    /// </summary>
    internal bool BeginPreviewHeadingMotion(PreviewHeadingCapture capture)
    {
        StopPreviewHeadingMotion();
        if (ViewModel.CurrentItem is not { } current || !capture.Matches(current)) return false;

        // 徽标图与面板手上那张是同一版才接；接不上的那一版照旧等这一页自己的图。
        var currentPlate = capture.PlateArtwork is { } artwork && ItemArtwork.Plate(current) == artwork;
        if (currentPlate && capture.PlatePicture is { } picture && ViewModel.PlateImage is null)
            ViewModel.UseConnectedPlate(capture.Item, picture);

        if (!HomeMotion.AnimationsEnabled) return false;
        if (XamlRoot?.Content is not UIElement root) return false;

        // 详情页平时要等第一次布局才量得到标题落点；这一趟和关联封面一样，先在导航事务里把它排出来。
        SeedConnectedLayoutWidth();
        ForceConnectedLayout();
        if (!HeadingBounds(TitleText, root, out var titleTarget)) return false;

        var board = new Storyboard();
        var elements = new List<PreviewHeadingElement>(2);
        // 片名比的是字号：折行行数不影响起飞大小。徽标是一张图，按高度比。
        Append(TitleText, capture.Title, titleTarget,
            PreviewHeadingPlacement.Scale(capture.TitleFontSize, TitleText.FontSize));

        if (currentPlate && capture.Plate is { } plateSource
            && HeadingBounds(TitlePlate, root, out var plateTarget))
        {
            // 这一层的透明度从这一拍起由飞行管：「图到了」的淡入不再在半路把它拉一遍。
            StopConnectedPlateArrival();
            Append(TitlePlate, plateSource, plateTarget,
                PreviewHeadingPlacement.Scale(plateSource.Height, plateTarget.Height));
        }

        if (elements.Count == 0) return false;
        var flight = new PreviewHeadingFlight(this, board, elements.ToArray());
        _previewHeadingFlight = flight;
        if (flight.Start()) return true;
        _previewHeadingFlight = null;
        return false;

        void Append(FrameworkElement element, Rect source, Rect target, double scale)
        {
            var (translateX, translateY) = PreviewHeadingPlacement.Offset(
                source.Left, source.Top, target.Left, target.Top);
            var transform = new CompositeTransform
            {
                ScaleX = scale,
                ScaleY = scale,
                TranslateX = translateX,
                TranslateY = translateY,
            };
            element.RenderTransformOrigin = new Point(0, 0);
            element.RenderTransform = transform;
            var previousOpacity = element.Opacity;
            element.Opacity = 0;
            elements.Add(new PreviewHeadingElement(element, previousOpacity));

            Animate(transform, "ScaleX", scale, 1);
            Animate(transform, "ScaleY", scale, 1);
            Animate(transform, "TranslateX", translateX, 0);
            Animate(transform, "TranslateY", translateY, 0);
            Animate(element, "Opacity", 0, 1);
        }

        void Animate(DependencyObject target, string property, double from, double to)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(PreviewHeadingFlightMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            board.Children.Add(animation);
        }
    }

    /// <summary>离开这一页时落定：标题和徽标回到不透明、无变换的静止样子。</summary>
    internal void StopPreviewHeadingMotion() => _previewHeadingFlight?.Stop();

    /// <summary>
    /// 目标元素此刻在窗口内容里的矩形；没排过版、被祖先收起或量不出面积都不算落点。
    /// 和关联封面的 <see cref="CardDetailTransition.ArrangedImage"/> 同一套判据，只是这里看的是任意元素。
    /// </summary>
    private static bool HeadingBounds(FrameworkElement element, UIElement root, out Rect bounds)
    {
        bounds = default;
        if (element.XamlRoot is null || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
        try
        {
            var box = element.TransformToVisual(root).TransformBounds(
                new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            if (box.Width <= 0 || box.Height <= 0) return false;
            bounds = box;
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>一个飞进来的元素和它原来的不透明度；落定时要原样还回去。</summary>
    private readonly record struct PreviewHeadingElement(FrameworkElement Element, double Opacity);

    /// <summary>
    /// 一段在飞的标题/徽标。落定、离页、应用或系统关动画、超时都走 <see cref="Stop"/>；
    /// 停的时候把元素恢复成静止样子，免得半透明或停在半路。
    /// </summary>
    private sealed class PreviewHeadingFlight
    {
        private readonly DetailPage _page;
        private readonly Storyboard _board;
        private readonly PreviewHeadingElement[] _elements;
        private readonly UISettings _settings = new();
        private readonly DispatcherQueueTimer _timeout;
        private EventHandler<object>? _completed;
        private bool _done;

        internal PreviewHeadingFlight(DetailPage page, Storyboard board, PreviewHeadingElement[] elements)
        {
            _page = page;
            _board = board;
            _elements = elements;
            _timeout = page.DispatcherQueue.CreateTimer();
            _timeout.Interval = TimeSpan.FromMilliseconds(PreviewHeadingFlightMs + 800);
            _timeout.IsRepeating = false;
        }

        internal bool Start()
        {
            _completed = (_, _) => Stop();
            _board.Completed += _completed;
            _timeout.Tick += OnTimeout;
            HomeMotion.PreferenceChanged += OnPreferenceChanged;
            try { _settings.AnimationsEnabledChanged += OnSystemPreferenceChanged; }
            catch (Exception)
            {
                // 系统订阅失败仍按应用偏好播放；停的时候这类旧委托已经不可能挂上，不用假装退订成功。
            }
            _page.Unloaded += OnPageUnloaded;
            try { _board.Begin(); }
            catch (Exception)
            {
                // 离树或页面正在销毁时平台可能拒绝启动；标题与徽标立即回到静止样子。
                Stop();
                return false;
            }
            _timeout.Start();
            return true;
        }

        internal void Stop()
        {
            if (_done) return;
            _done = true;
            _timeout.Stop();
            _timeout.Tick -= OnTimeout;
            if (_completed is not null) _board.Completed -= _completed;
            _completed = null;
            HomeMotion.PreferenceChanged -= OnPreferenceChanged;
            try { _settings.AnimationsEnabledChanged -= OnSystemPreferenceChanged; }
            catch (Exception) { /* 本地资格已撤；平台退订失败不挡住终值恢复。 */ }
            _page.Unloaded -= OnPageUnloaded;
            try { _board.Stop(); }
            catch (Exception) { /* 停不下来也照常写回终值，不能让标题停在半透明。 */ }
            foreach (var element in _elements)
            {
                element.Element.RenderTransform = null;
                element.Element.Opacity = element.Opacity;
            }
            if (ReferenceEquals(_page._previewHeadingFlight, this)) _page._previewHeadingFlight = null;
        }

        private void OnTimeout(object? sender, object args) => Stop();

        private void OnPreferenceChanged()
        {
            if (!HomeMotion.AnimationsEnabled) Stop();
        }

        private void OnSystemPreferenceChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
        {
            void ApplyCurrentPreference()
            {
                if (!HomeMotion.AnimationsEnabled) Stop();
            }
            if (_page.DispatcherQueue.HasThreadAccess) ApplyCurrentPreference();
            else _page.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, ApplyCurrentPreference);
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs args)
        {
            // 回收/重开的幻影 Unloaded 不能把仍在树上的页面停在半路。
            if (_page.XamlRoot is not null) return;
            Stop();
        }
    }
}
