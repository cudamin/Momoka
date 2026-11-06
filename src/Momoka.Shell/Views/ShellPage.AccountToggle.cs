using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private VisualState? _accountOffState;
    private DoubleAnimation? _accountOffAnimation;

    private void EnsureAccountToggleOffState()
    {
        // WinUI 2.3.9 的 Off 是空态；快速拨动后关菜单，On 的动画值可能留在 X=20。
        // 只补当前模板的关态终值，保留原 On、过渡及可访问性，不直接改滑块基础位置。
        AggregateModeItem.ApplyTemplate();
        var root = VisualTreeHelper.GetChildrenCount(AggregateModeItem) > 0
            ? VisualTreeHelper.GetChild(AggregateModeItem, 0) as FrameworkElement : null;
        var off = root is null ? null : VisualStateManager.GetVisualStateGroups(root)
            .FirstOrDefault(group => group.Name == "ToggleStates")?.States.FirstOrDefault(state => state.Name == "Off");
        var knob = root is null ? null : Descendants(root).OfType<FrameworkElement>()
            .FirstOrDefault(element => element.Name == "SwitchKnob")?.RenderTransform as TranslateTransform;
        if (off is null || knob is null) return;
        off.Storyboard ??= new Storyboard();
        if (ReferenceEquals(off, _accountOffState) && _accountOffAnimation is not null
            && off.Storyboard.Children.Contains(_accountOffAnimation)) return;
        var reset = new DoubleAnimation { To = 0, Duration = TimeSpan.Zero };
        // 代码追加的时间线不在平台 XAML 的模板名称作用域内，直接绑定当前模板的变换对象。
        Storyboard.SetTarget(reset, knob);
        Storyboard.SetTargetProperty(reset, "X");
        off.Storyboard.Children.Add(reset);
        _accountOffState = off;
        _accountOffAnimation = reset;
    }
}
