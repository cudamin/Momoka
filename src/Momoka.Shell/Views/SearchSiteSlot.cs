using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

/// <summary>
/// 把站点入口放进 <see cref="AutoSuggestBox"/> 模板里「输入框与放大镜之间」那一格。
/// <para>
/// 搜索页的框和「在 MoviePilot 搜索版本」窗口的框是同一颗原生控件：输入、清除、回车、键盘焦点都在它的
/// 模板里。站点入口要排在放大镜左边、又要在框的边框以内，只能等模板建出来以后往那个 Grid 里插一列，
/// 再把插点右边的格子依次右移。两处各写一遍的话，下一次模板或列序改动只会修好其中一处。
/// </para>
/// </summary>
internal static class SearchSiteSlot
{
    /// <summary>插好返回 true；模板还没建出来、或者已经插过，返回 false。</summary>
    internal static bool Place(AutoSuggestBox box, MoviePilotSitePicker picker)
    {
        if (Find<Button>(box, "QueryButton") is not { Parent: Grid host } query) return false;

        // WinUI 原生模板把 QueryButton 固定为 32 DIP；只拓宽点击区，不替换 Icon 或 Click，
        // AutoSuggestBox 仍负责回车、清除和 QuerySubmitted（模板重建后也要重新应用）。
        query.Width = 64;
        query.MinWidth = 64;
        query.Height = 32;
        query.IsTabStop = true;
        AutomationProperties.SetAccessibilityView(query, AccessibilityView.Content);
        AutomationProperties.SetAutomationId(query, "SearchSubmit");
        AutomationProperties.SetName(query, "搜索");
        ToolTipService.SetToolTip(query, "搜索");
        if (ReferenceEquals(picker.Parent, host)) return false;

        if (picker.Parent is Panel previous) previous.Children.Remove(picker);

        var column = Grid.GetColumn(query);
        host.ColumnDefinitions.Insert(column, new ColumnDefinition { Width = GridLength.Auto });
        foreach (var child in host.Children.OfType<FrameworkElement>())
        {
            var start = Grid.GetColumn(child);
            var span = Grid.GetColumnSpan(child);
            if (start >= column) Grid.SetColumn(child, start + 1);
            else if (start + span > column) Grid.SetColumnSpan(child, span + 1);
        }

        Grid.SetRow(picker, Grid.GetRow(query));
        Grid.SetColumn(picker, column);
        picker.Margin = new Thickness(4, 0, 2, 0);
        host.Children.Add(picker);

        return true;
    }

    /// <summary>视觉树里第一个这种东西。找不到就是 null —— 模板还没建出来不是错。</summary>
    private static T? Find<T>(DependencyObject node, string name) where T : FrameworkElement
    {
        if (node is T hit && hit.Name == name) return hit;

        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < children; index++)
            if (Find<T>(VisualTreeHelper.GetChild(node, index), name) is { } found) return found;

        return null;
    }
}
