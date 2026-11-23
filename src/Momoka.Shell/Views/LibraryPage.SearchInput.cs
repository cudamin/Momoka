using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class LibraryPage
{
    private TextBox? _searchTextBox;
    private bool _searchComposing;

    private void OnSearchInputGotFocus(object sender, RoutedEventArgs args)
    {
        // 事件接到实际编辑框。模板重建后，下一次获得焦点会换到新的编辑框。
        if (_released || args.OriginalSource is not TextBox { Name: "TextBox" } field
            || ReferenceEquals(field, _searchTextBox)) return;
        DetachSearchTextBox();
        _searchTextBox = field;
        field.TextCompositionStarted += OnSearchCompositionStarted;
        field.TextCompositionEnded += OnSearchCompositionEnded;
        field.LostFocus += OnSearchEditLostFocus;
    }

    private void DetachSearchTextBox()
    {
        if (_searchTextBox is not { } field) return;
        field.TextCompositionStarted -= OnSearchCompositionStarted;
        field.TextCompositionEnded -= OnSearchCompositionEnded;
        field.LostFocus -= OnSearchEditLostFocus;
        _searchTextBox = null;
        _searchComposing = false;
    }

    private void OnSearchEditLostFocus(object sender, RoutedEventArgs args) => _searchComposing = false;

    private void OnSearchCompositionStarted(TextBox sender, TextCompositionStartedEventArgs args)
    {
        _searchComposing = true;
        CancelTyping();
    }

    private void OnSearchCompositionEnded(TextBox sender, TextCompositionEndedEventArgs args)
    {
        _searchComposing = false;
        // 文本可能已在组字期间变化，确认后用最终文字恢复媒体库原有的输入防抖。
        ApplySearchEdit(sender.Text);
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // IME 可以消费按下而只留下回车抬起。仅接受控件原生的查询提交事件。
        if (!_searchComposing) _ = SubmitSearchAsync(args.QueryText ?? sender.Text ?? string.Empty);
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && args.CheckCurrent())
            ApplySearchEdit(sender.Text);
    }

    private void ApplySearchEdit(string? text)
    {
        if (!_searchComposing && !string.Equals(text, ViewModel.SearchText, StringComparison.Ordinal))
            _ = SearchTypingAsync(text ?? string.Empty);
    }
}
