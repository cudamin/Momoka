using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Momoka.Shell.Views;

/// <summary>
/// 「点弹窗外面就关掉它」：在弹窗表面以外、主窗口以内按下指针，这张表就退出（2026-10-29 用户令，
/// 当时的话是「项目中所有的弹窗和以后新增的弹窗，都要支持点击弹窗外部主窗口内时自动退出」）。
/// <para>
/// 为什么是附加属性挂在样式上：<see cref="ContentDialog"/> 没有「点外面」这个开关，而 WinUI 的隐式样式
/// 按类型精确匹配（见 <c>Theme/Styles.xaml</c> 里 <c>EgDialogSurfaceStyle</c> 那段），从它派生的每一张表
/// 都各接一行样式。把这个属性放进 <c>EgDialogSurfaceStyle</c>，现有各表与以后照规矩接样式的新表都自动
/// 带上；忘了接样式的新表由 <c>DialogChromeTests</c> 拦住，而不是靠谁记得加一行。
/// </para>
/// <para>
/// 「外面」怎么量：<see cref="ContentDialog"/> 元素本身铺满整个窗口 —— 模板根那颗 <c>LayoutRoot</c> 画的就是
/// 遮罩，底和圆角在居中的 <c>BackgroundElement</c> 上（<c>CornerRadius</c>/<c>Background</c> 都是
/// TemplateBinding 到它）。所以不能拿弹窗元素的边界当表面，取模板里那颗 <c>BackgroundElement</c> 的矩形。
/// 按在遮罩上时指针事件照常冒泡到弹窗元素，判定因此成立；弹窗内部的下拉、菜单是另一层 popup，事件不经过
/// 这里，点它们不会误关。
/// </para>
/// <para>
/// 关的时候走 <see cref="ContentDialog.Hide()"/>，与关闭按钮同一条出口：表自己写在 <c>Closing</c> 里的
/// 「写入在飞时不许关」照样拦得住（字幕管理就是这么做的）。
/// </para>
/// <para>
/// 类型与那对访问器是 public 的，因为 XAML 编译器只从公开元数据里认附加属性（<c>Setter
/// Property="local:DialogDismiss.ClosesOnOutsideClick"</c>）；这不是给外部用的接口，产品里只有样式表引用它。
/// </para>
/// </summary>
public static class DialogDismiss
{
    /// <summary>为 true 时，弹窗表面以外的按下会关掉这张表。由 <c>EgDialogSurfaceStyle</c> 统一打开。</summary>
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
        // 样式可能先被隐式那条应用、再被某张表自己的 Style 换掉；先摘再挂，重复应用也只有一个处理者。
        dialog.PointerPressed -= OnPointerPressed;
        if (args.NewValue is true) dialog.PointerPressed += OnPointerPressed;
    }

    private static void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not ContentDialog dialog) return;
        // 这一下只用来关窗：标成已处理，遮罩底下的东西不该同时收到它。
        if (TryDismiss(dialog, args.GetCurrentPoint(dialog).Position)) args.Handled = true;
    }

    /// <summary>按在这个点上会不会关掉这张表。真的关了才算 <c>true</c>；探针走同一条路。</summary>
    internal static bool TryDismiss(ContentDialog dialog, Point point)
    {
        if (!ShouldDismissAt(dialog, point)) return false;
        // 同一张表重复 Hide 是安全的：框架在 m_hideInProgress / 已关闭时直接返回。
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
