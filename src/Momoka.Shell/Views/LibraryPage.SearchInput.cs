using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class LibraryPage
{
    private SearchInputGuard? _searchInputGuard;

    private void OnSearchInputGotFocus(object sender, RoutedEventArgs args)
    {
        if (_released) return;
        // 事件接到实际编辑框。模板重建后，下一次获得焦点会换到新的编辑框。
        // 退格与回车的输入法补偿和音乐库页共用 SearchInputGuard；这一页另接组字防抖与查询提交。
        (_searchInputGuard ??= new SearchInputGuard(CancelTyping, ApplySearchEdit,
            text => _ = SubmitSearchAsync(text))).Watch(args.OriginalSource);
    }

    private void DetachSearchTextBox()
    {
        _searchInputGuard?.Release();
        _searchInputGuard = null;
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // 原生查询与抬起补搜共用一次回车；这里返回 false 表示已经由另一边处理或仍在组字。
        if (_searchInputGuard?.TakeQuerySubmitted() == false) return;
        _ = SubmitSearchAsync(args.QueryText ?? sender.Text ?? string.Empty);
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && args.CheckCurrent()
            && _searchInputGuard?.IsComposing != true) ApplySearchEdit(sender.Text);
    }

    private void ApplySearchEdit(string? text)
    {
        if (!string.Equals(text, ViewModel.SearchText, StringComparison.Ordinal))
            _ = SearchTypingAsync(text ?? string.Empty);
    }
}
