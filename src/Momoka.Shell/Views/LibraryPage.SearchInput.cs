using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Momoka.Shell.Interop;
using Windows.System;

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
        field.PreviewKeyDown += OnSearchEditKeyDown;
        field.TextCompositionStarted += OnSearchCompositionStarted;
        field.TextCompositionEnded += OnSearchCompositionEnded;
        field.LostFocus += OnSearchEditLostFocus;
    }

    private void DetachSearchTextBox()
    {
        if (_searchTextBox is not { } field) return;
        field.PreviewKeyDown -= OnSearchEditKeyDown;
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

    private void OnSearchEditKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Delete || _searchComposing || Native.CtrlHeld || Native.ShiftHeld || Native.AltHeld
            || sender is not TextBox field || !ReferenceEquals(args.OriginalSource, field) || field.IsReadOnly) return;

        // 在模板编辑框收到 Delete 时做一次前向删除。沿用文本框的选区写入和 TextChanged 通知，
        // 避免整串回写重置光标，修饰键和组字中的 Delete 仍由系统处理。
        if (field.SelectionLength == 0 && field.SelectionStart < field.Text.Length)
        {
            var start = field.SelectionStart;
            field.Select(start, StringInfo.GetNextTextElementLength(field.Text.AsSpan(start)));
        }
        if (field.SelectionLength > 0) field.SelectedText = string.Empty;
        args.Handled = true;
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
