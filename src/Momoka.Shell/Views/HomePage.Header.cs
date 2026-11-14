using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class HomePage
{
    private readonly HomeHeaderScrollState _headerScroll = new();
    private bool _restoringHeaderScroll;
    private bool _reflowingHeader;
    private int _headerRestoreCorrelation = -1;
    internal HomeHeaderMode HeaderMode => _headerScroll.Mode;

    internal void FocusHeaderScroll()
    {
        // 与 SettingsPage.FocusSettingsCards 同一策略：让视口自身接焦点，不把深处页面拉回首个按钮。
        var tabStop = Scroller.IsTabStop;
        Scroller.IsTabStop = true;
        try { Scroller.Focus(FocusState.Programmatic); }
        finally { Scroller.IsTabStop = tabStop; }
    }

    private void ResetHeaderScroll()
    {
        _headerScroll.Reset(BannerImmersive, Scroller.VerticalOffset);
        PaintInk();
    }

    // Gallery 6de91feccadfbb0a796b71caedead5a2284f730e:
    // Samples/ItemsView/ItemsViewPage.xaml.cs 的 ScrollView.ViewChanged 接线和 VerticalOffset 读取。
    // 样例没有方向隐藏操作条；方向滞回由本页拥有，横向货架不参与。
    private void OnHeaderViewChanged(ScrollView sender, object args)
    {
        if (_released) return;
        if (_reflowingHeader || _restoringHeaderScroll || _restoredScroll is not null)
            _headerScroll.Reset(BannerImmersive, sender.VerticalOffset);
        else
            _headerScroll.Update(BannerImmersive, sender.VerticalOffset);
        PaintInk();
    }

    private void OnHeaderScrollCompleted(ScrollView sender, ScrollingScrollCompletedEventArgs args)
    {
        if (_released || !_restoringHeaderScroll || args.CorrelationId != _headerRestoreCorrelation) return;
        _restoringHeaderScroll = false;
        ResetHeaderScroll();
    }
}
