using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>保留详情分区根、显隐和重排身份，只切换卡内标题与内容留白。</summary>
internal static class DetailSectionCard
{
    internal static void Apply(StackPanel panel, bool store, Thickness padding, double spacing = 10)
    {
        var header = (Border)panel.Children[0];
        var content = (StackPanel)panel.Children[1];
        panel.Style = Resource(store ? "EgStoreDetailCardStyle" : "EgBodyPanelStyle");
        panel.Padding = store ? default : padding;
        panel.Spacing = store ? 0 : spacing;
        header.Style = store ? Resource("EgStoreDetailCardHeaderStyle") : null;
        content.Style = store ? Resource("EgStoreDetailCardContentStyle") : null;
        content.Spacing = spacing;
        if (header.Child is ShelfHead head)
        {
            head.StoreCard = store;
            head.VerticalAlignment = store ? VerticalAlignment.Center : VerticalAlignment.Stretch;
        }
    }

    private static Style Resource(string key) => (Style)Application.Current.Resources[key];
}
