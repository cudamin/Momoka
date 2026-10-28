using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Momoka.Shell.Views;

/// <summary>Source selector with the ListViewBase peer missing from the pinned Toolkit control.</summary>
public sealed partial class SearchSourceTabs : Segmented
{
    protected override AutomationPeer OnCreateAutomationPeer() => new SourceTabsAutomationPeer(this);

    private sealed partial class SourceTabsAutomationPeer(SearchSourceTabs owner) : ListViewBaseAutomationPeer(owner)
    {
        // ListViewBase has no concrete item peer. Data peers preserve native single-selection,
        // item names and SelectionItemPattern instead of exposing only the visual containers.
        protected override ItemAutomationPeer OnCreateItemAutomationPeer(object item) =>
            new ListViewItemDataAutomationPeer(item, this);
    }
}
