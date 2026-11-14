using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

public sealed partial class LibraryPage
{
    private AutoSuggestBox? _titleSearchInput;
    internal AutoSuggestBox ActiveSearchInput => _titleSearchInput ?? SearchInput;

    internal bool SearchInputFocused
    {
        get
        {
            // AutoSuggestBox 的实际焦点在模板 TextBox，不能只读外层 FocusState。
            var input = ActiveSearchInput;
            if (input.XamlRoot is null) return false;
            for (var focused = FocusManager.GetFocusedElement(input.XamlRoot) as DependencyObject;
                focused is not null; focused = VisualTreeHelper.GetParent(focused))
                if (ReferenceEquals(focused, input)) return true;
            return false;
        }
    }

    /// <summary>标题框可见时接管输入；窄窗或离页解除，页内输入保留原词和来源。</summary>
    internal void SetTitleSearchInput(AutoSuggestBox? input)
    {
        if (_released || _request?.IsSearch != true) input = null;
        if (ReferenceEquals(_titleSearchInput, input)) return;
        var focused = SearchInputFocused;
        // 保留尚未等到原生 TextChanged 的输入草稿，缩放不会回写成 VM 中的旧词。
        if (!_released && (_titleSearchInput is not null || focused)
            && !string.Equals(ActiveSearchInput.Text, ViewModel.SearchText, StringComparison.Ordinal))
            _ = SearchTypingAsync(ActiveSearchInput.Text ?? string.Empty);
        CancelImeSubmit();
        _searchEnterDown = false;
        if (_titleSearchInput is { } previous)
        {
            previous.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnSearchKeyDown));
            previous.RemoveHandler(UIElement.KeyUpEvent, new KeyEventHandler(OnSearchKeyUp));
            previous.LostFocus -= OnSearchLostFocus;
        }
        _titleSearchInput = input;
        if (input is not null)
        {
            input.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnSearchKeyDown), handledEventsToo: true);
            input.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler(OnSearchKeyUp), handledEventsToo: true);
            input.LostFocus += OnSearchLostFocus;
        }
        SyncTitleSearchText();
        PlaceSearchSites();
        ApplyHeaderLayout();
        SearchInput.Text = ViewModel.SearchText;
        if (focused && !_released)
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_released && ReferenceEquals(_titleSearchInput, input))
                    ActiveSearchInput.Focus(FocusState.Programmatic);
            });
    }

    private void SyncTitleSearchText()
    {
        if (_titleSearchInput is not { } input) return;
        if (!string.Equals(input.Text, ViewModel.SearchText, StringComparison.Ordinal))
            input.Text = ViewModel.SearchText;
        input.PlaceholderText = SearchInput.PlaceholderText;
    }

    private void OnSearchLostFocus(object sender, RoutedEventArgs args)
    {
        _searchEnterDown = false;
        CancelImeSubmit();
    }

    internal void SubmitTitleSearch(string text)
    {
        CancelImeSubmit();
        _ = SubmitSearchAsync(text);
    }
}
