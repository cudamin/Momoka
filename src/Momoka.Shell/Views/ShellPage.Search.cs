using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    // Gallery MainWindow.xaml.cs / OnControlsSearchBoxTextChanged and QuerySubmitted,
    // 14a4a1a2b8ddc527dc4a7d5f7e743d7c2bc97db7 (MIT): only user input starts a query.
    // 本地适配：首次聚焦打开搜索页；后续输入交给原页，保留来源、防抖及返回栈。
    private LibraryPage? TitleSearchPage => ContentFrame.Content is LibraryPage page
        && page.SearchState.IsSearch ? page : null;

    private void OnTitleSearchGotFocus(object sender, RoutedEventArgs args)
    {
        if (TitleSearchPage is null) OpenSearch();
    }

    private void OnTitleSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput || !args.CheckCurrent()) return;
        // WinUI 的 TextChanged 会延迟到达：Enter 已提交的同词不能再清结果，离页后也不能重新导航。
        if (TitleSearchPage is not { } page) return;
        var text = sender.Text ?? string.Empty;
        if (string.Equals(text, page.ViewModel.SearchText, StringComparison.Ordinal)) return;
        _ = page.SearchTypingAsync(text);
    }

    private void OnTitleSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var text = args.QueryText ?? sender.Text ?? string.Empty;
        if (TitleSearchPage is null) OpenSearch();
        TitleSearchPage?.SubmitTitleSearch(text);
    }
}
