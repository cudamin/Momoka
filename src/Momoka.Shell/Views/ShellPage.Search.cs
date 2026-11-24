using Microsoft.UI.Xaml;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private LibraryPage? TitleSearchPage => ContentFrame.Content is LibraryPage page
        && page.SearchState.IsSearch ? page : null;

    /// <summary>
    /// 点标题栏那颗搜索框：备好关联动画再进搜索页，框从标题栏移到内容区；搜索页开着时标题框收起
    /// （见 <see cref="SyncTitleSearch"/>），离开那一页再出现（2026-11-14 用户令）。
    /// </summary>
    private void OnTitleSearchGotFocus(object sender, RoutedEventArgs args)
    {
        if (TitleSearchPage is not null) return;
        TitleSearchTransition.Prepare(ContentFrame, TitleSearchBox);
        OpenSearch();
    }
}
