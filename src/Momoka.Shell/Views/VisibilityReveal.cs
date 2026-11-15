using Microsoft.UI.Xaml;
using System.Runtime.CompilerServices;

namespace Momoka.Shell.Views;

/// <summary>
/// 绑定的 <see cref="UIElement.Visibility"/> 从 Collapsed 变回 Visible 时的短入场：由下方 8 DIP 淡入。
/// <para>
/// 只做入场，退出仍由绑定立即收起 —— 属性已经被绑定改成 Collapsed，没有可播的退出帧。
/// 动画总开关关闭、元素没有 XamlRoot 或第一次上树时不播，直接停在最终状态；
/// 与 <see cref="SurfaceMotion"/> 共用同一条「只动 Opacity 与 RenderTransform.Y」的路径。
/// </para>
/// <para>
/// 不要用在自己在管 Opacity／RenderTransform 的元素上（PosterCard、HomeBanner、Shimmer 等），
/// 两边会互相抢同一组属性。它服务的是那些由视图模型 Visibility 直接控制的普通面板。
/// </para>
/// </summary>
public static class VisibilityReveal
{
    internal const int EnterMilliseconds = 160;
    internal const double EnterOffset = 8;

    private static readonly ConditionalWeakTable<FrameworkElement, Watch> Watches = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(VisibilityReveal),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject target, bool value)
    {
        target.SetValue(IsEnabledProperty, value);
        // 生产环境里 DP 元数据回调也会走到 Apply；这里直接接一次，让测试替身不必实现元数据回调，
        // Apply 本身按元素去重，重复调用不会多挂一个订阅。
        if (target is FrameworkElement element) Apply(element, value);
    }

    public static bool GetIsEnabled(DependencyObject target) =>
        target.GetValue(IsEnabledProperty) is true;

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is FrameworkElement element) Apply(element, args.NewValue is true);
    }

    private static void Apply(FrameworkElement element, bool enabled)
    {
        if (enabled)
        {
            Watches.GetValue(element, static owner => new Watch(owner));
        }
        else if (Watches.TryGetValue(element, out var watch))
        {
            watch.Dispose();
            Watches.Remove(element);
        }
    }

    private sealed class Watch : IDisposable
    {
        private readonly FrameworkElement _element;
        private readonly SurfaceMotion _motion;
        private readonly long _token;
        private bool _visible;

        internal Watch(FrameworkElement element)
        {
            _element = element;
            _motion = new SurfaceMotion(element);
            _visible = element.Visibility == Visibility.Visible;
            _token = element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnVisibilityChanged);
        }

        private void OnVisibilityChanged(DependencyObject sender, DependencyProperty property)
        {
            if (_element.Visibility == Visibility.Visible)
            {
                if (_visible) return;
                _visible = true;
                _motion.Enter(EnterMilliseconds, EnterOffset);
            }
            else
            {
                _visible = false;
                _motion.Collapse();
            }
        }

        public void Dispose() =>
            _element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, _token);
    }
}
