using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Momoka.Shell.Views;

/// <summary>
/// 所有弹窗共用的「点击外部取消」行为，由 Theme/Styles.xaml 的弹窗基础样式启用。
/// 新增弹窗类型必须接同一条样式；DialogChromeTests 检查类型清单及本地覆盖。
/// <para>
/// WinUI 的 ContentDialog 模板里，遮罩 <c>SmokeLayerBackground</c> 会被框架搬到另一个 Popup
/// （microsoft-ui-xaml <c>0a03f68d586d1c100f753ca3221597c3ee550f4a</c> 的
/// <c>dxaml/xcp/dxaml/lib/ContentDialog_Partial.cpp</c>，<c>PrepareSmokeLayer</c>；还原的 WinUI 2.3.9
/// generic.xaml 用同名部件）。
/// 按在遮罩上的指针根本不经过 ContentDialog 元素，只挂弹窗本体收不到那一下。这里在显示时找出那个
/// 独立 Popup 里的遮罩单独订阅，关掉时退订。
/// </para>
/// <para>
/// 遮罩铺满整个内容区、又永远压在弹窗层之下：能收到按下就说明弹窗表面没接住这一下，直接算「外面」，
/// 不去跨树换算坐标。经模板根进来的按下才量 <c>BackgroundElement</c> 的矩形（弹窗元素自己铺满窗口，
/// 它的边界不是表面）。内部下拉、菜单是各自打开的 Popup，按下不经过这两处，点它们不会关掉父弹窗。
/// </para>
/// <para>
/// 只走 <see cref="ContentDialog.Hide()"/>，结果是 None，不替用户保存、确认或执行背后的按钮；
/// 表自己写在 <c>Closing</c> 里的保护（例如字幕写入在飞）照样拦得住。
/// public 类型与访问器只为 XAML 编译器读取附加属性元数据，不作为业务接口。
/// </para>
/// </summary>
public static class DialogDismiss
{
    private static readonly ConditionalWeakTable<ContentDialog, OutsideClickSubscription> Subscriptions = new();

    public static readonly DependencyProperty ClosesOnOutsideClickProperty = DependencyProperty.RegisterAttached(
        "ClosesOnOutsideClick",
        typeof(bool),
        typeof(DialogDismiss),
        new PropertyMetadata(false, OnClosesOnOutsideClickChanged));

    public static void SetClosesOnOutsideClick(DependencyObject target, bool value) =>
        target.SetValue(ClosesOnOutsideClickProperty, value);

    public static bool GetClosesOnOutsideClick(DependencyObject target) =>
        (bool)target.GetValue(ClosesOnOutsideClickProperty);

    private static void OnClosesOnOutsideClickChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ContentDialog dialog) return;
        // 样式可能先被隐式那条应用、再被某张表自己的 Style 换掉；重复打开也只留一个订阅者。
        if (args.NewValue is true)
            Subscriptions.GetValue(dialog, static owner => new OutsideClickSubscription(owner));
        else if (Subscriptions.TryGetValue(dialog, out var subscription))
        {
            subscription.Dispose();
            Subscriptions.Remove(dialog);
        }
    }

    private sealed class OutsideClickSubscription : IDisposable
    {
        private readonly ContentDialog _dialog;
        private readonly PointerEventHandler _pressed;
        private UIElement? _templateRoot;
        private UIElement? _overlay;

        internal OutsideClickSubscription(ContentDialog dialog)
        {
            _dialog = dialog;
            _pressed = OnPointerPressed;
            dialog.Opened += OnOpened;
            dialog.Loaded += OnLoaded;
            dialog.Closed += OnClosed;
            if (dialog.IsLoaded) Attach();
        }

        private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args) => Attach();

        /// <summary>
        /// 弹窗挪进 Popup 时，<c>Unloaded</c> 会在 <c>Opened</c> 之后紧接着到（2026-10-30 实测），
        /// 拿它退订等于刚接上就摘掉、点外面毫无反应。改在 <c>Loaded</c> 重新找一次，只在关掉时退订。
        /// </summary>
        private void OnLoaded(object sender, RoutedEventArgs args) => Attach();

        private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args) => Detach();

        private void Attach()
        {
            Detach();
            // 子控件可能已处理按下；外面那一下仍要认出来，里面那一下绝不能被改成取消。
            _templateRoot = TemplatePart(_dialog, "LayoutRoot") ?? (UIElement)_dialog;
            _templateRoot.AddHandler(UIElement.PointerPressedEvent, _pressed, handledEventsToo: true);
            _overlay = Overlay(_dialog);
            _overlay?.AddHandler(UIElement.PointerPressedEvent, _pressed, handledEventsToo: true);
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
        {
            var outside = ReferenceEquals(sender, _overlay)
                || ShouldDismissAt(_dialog, args.GetCurrentPoint(_dialog).Position);
            if (!outside || !GetClosesOnOutsideClick(_dialog)) return;
            // 先吞掉按下，再走可能同步触发 Closing 的出口；即便写入保护拒绝关闭也不能点穿。
            args.Handled = true;
            TryDismissFromBackdrop(_dialog);
        }

        private void Detach()
        {
            _templateRoot?.RemoveHandler(UIElement.PointerPressedEvent, _pressed);
            _overlay?.RemoveHandler(UIElement.PointerPressedEvent, _pressed);
            _templateRoot = null;
            _overlay = null;
        }

        public void Dispose()
        {
            Detach();
            _dialog.Opened -= OnOpened;
            _dialog.Loaded -= OnLoaded;
            _dialog.Closed -= OnClosed;
        }
    }

    /// <summary>尝试取消；true 代表这次外部点击已处理，Closing 仍可拒绝关闭。</summary>
    internal static bool TryDismiss(ContentDialog dialog, Point point)
    {
        return ShouldDismissAt(dialog, point) && TryDismissFromBackdrop(dialog);
    }

    /// <summary>
    /// 按在遮罩上：遮罩铺满内容区且在弹窗层之下，这一下能落到它身上就说明表面没接住，直接取消。
    /// 与 <see cref="TryDismiss"/> 同一条出口，<c>Closing</c> 的保护继续生效。
    /// </summary>
    internal static bool TryDismissFromBackdrop(ContentDialog dialog)
    {
        if (!GetClosesOnOutsideClick(dialog)) return false;
        dialog.Hide();
        return true;
    }

    /// <summary>这个点算不算「在弹窗外面」。表面还没排过版（宽高为 0）时一律算里面，不乱关。</summary>
    internal static bool ShouldDismissAt(ContentDialog dialog, Point point)
    {
        if (Surface(dialog) is not { ActualWidth: > 0, ActualHeight: > 0 } surface) return false;
        if (surface.TransformToVisual(dialog) is not { } transform) return false;
        var bounds = transform.TransformBounds(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight));
        return !bounds.Contains(point);
    }

    /// <summary>模板里画底色、圆角和投影的那颗 Border；它才是「弹窗本体」的矩形。</summary>
    internal static Border? Surface(ContentDialog dialog) => TemplatePart(dialog, "BackgroundElement") as Border;

    /// <summary>
    /// 框架搬出去、铺满内容区的那层遮罩。每个 XamlRoot 同时只允许一张 ContentDialog，
    /// 所以按名字找得到的就是这张表的遮罩；内部 Flyout/ComboBox 的 popup 不叫这个名字。
    /// </summary>
    internal static FrameworkElement? Overlay(ContentDialog dialog) => dialog.XamlRoot is { } root
        ? VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Select(popup => popup.Child)
            .OfType<FrameworkElement>().FirstOrDefault(child => child.Name == "SmokeLayerBackground")
        : null;

    /// <summary>按名字取模板部件（弹窗的模板命名域在控件外面，只能走可视树）。</summary>
    internal static FrameworkElement? TemplatePart(ContentDialog dialog, string name) =>
        Descendants(dialog).OfType<FrameworkElement>().FirstOrDefault(element => element.Name == name);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            {
                var child = VisualTreeHelper.GetChild(node, index);
                yield return child;
                queue.Enqueue(child);
            }
        }
    }
}
