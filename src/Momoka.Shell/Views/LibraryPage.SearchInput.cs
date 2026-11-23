using System.Globalization;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Momoka.Shell.Views;

public sealed partial class LibraryPage
{
    private TextBox? _searchTextBox;
    private bool _searchComposing;
    private bool _searchBackspaceHandled;

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
        field.PreviewKeyDown += OnSearchEditKeyDown;
        field.KeyUp += OnSearchEditKeyUp;
    }

    private void DetachSearchTextBox()
    {
        if (_searchTextBox is not { } field) return;
        field.TextCompositionStarted -= OnSearchCompositionStarted;
        field.TextCompositionEnded -= OnSearchCompositionEnded;
        field.LostFocus -= OnSearchEditLostFocus;
        field.PreviewKeyDown -= OnSearchEditKeyDown;
        field.KeyUp -= OnSearchEditKeyUp;
        _searchTextBox = null;
        ResetSearchEditState();
    }

    private void OnSearchEditLostFocus(object sender, RoutedEventArgs args) => ResetSearchEditState();

    private void ResetSearchEditState()
    {
        _searchComposing = false;
        _searchBackspaceHandled = false;
    }

    private void OnSearchEditKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Back) _searchBackspaceHandled = true;
    }

    private void OnSearchEditKeyUp(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Back) return;
        var handledByEditor = _searchBackspaceHandled;
        _searchBackspaceHandled = false;

        // 微软拼音有时只让退格的抬起到达 WinUI。按下已到达或仍在组字时继续由系统编辑。
        if (handledByEditor || _searchComposing || sender is not TextBox { IsReadOnly: false } field
            || IsSearchKeyDown(VirtualKey.Control) || IsSearchKeyDown(VirtualKey.Menu)
            || IsSearchKeyDown(VirtualKey.LeftWindows) || IsSearchKeyDown(VirtualKey.RightWindows)) return;

        var start = field.SelectionStart;
        var length = field.SelectionLength;
        if (length == 0)
        {
            if (start == 0) return;
            // 光标左边的一个可见字符可能由代理对或多个组合字符组成。
            var characters = StringInfo.ParseCombiningCharacters(field.Text);
            var index = Array.BinarySearch(characters, start);
            var previous = characters[(index < 0 ? ~index : index) - 1];
            field.Select(previous, start - previous);
        }
        field.SelectedText = string.Empty;
        args.Handled = true;
    }

    private static bool IsSearchKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private void OnSearchCompositionStarted(TextBox sender, TextCompositionStartedEventArgs args)
    {
        _searchComposing = true;
        CancelTyping();
    }

    private void OnSearchCompositionEnded(TextBox sender, TextCompositionEndedEventArgs args)
    {
        // 最后一位拼音被退格清空时，组字结束可以先于按键抬起。
        if (IsSearchKeyDown(VirtualKey.Back)) _searchBackspaceHandled = true;
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
