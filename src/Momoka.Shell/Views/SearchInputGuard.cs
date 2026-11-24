using System.Globalization;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Momoka.Shell.Views;

/// <summary>
/// 搜索框编辑区的输入法补偿，媒体库页和音乐库页共用这一份。
/// 微软拼音在 WinUI 的 AutoSuggestBox 里有时只把退格的抬起送到编辑框，按下那一下丢了，框里的文字和选区都不动；
/// 回车确认组字、组字末位被退格清空也有同样的漏送。这里只补系统没做的那一下，原生按键照常放行：
/// 按下已经到达、仍在组字，或者按着 Ctrl/Alt/Win 时都不重复补删、补搜。
/// </summary>
internal sealed class SearchInputGuard
{
    private readonly Action? _compositionStarted;
    private readonly Action<string>? _compositionFinished;
    private readonly Action<string>? _submit;

    private TextBox? _field;
    private bool _composing;
    private bool _backspaceHandled;
    private bool _enterConsumed;

    internal SearchInputGuard(Action? compositionStarted = null, Action<string>? compositionFinished = null,
        Action<string>? submit = null)
    {
        _compositionStarted = compositionStarted;
        _compositionFinished = compositionFinished;
        _submit = submit;
    }

    internal bool IsComposing => _composing;

    /// <summary>事件接到实际编辑框；模板重建后，下一次获得焦点会换到新的编辑框。</summary>
    internal void Watch(object? source)
    {
        if (source is not TextBox { Name: "TextBox" } field || ReferenceEquals(field, _field)) return;
        Detach();
        _field = field;
        field.TextCompositionStarted += OnCompositionStarted;
        field.TextCompositionEnded += OnCompositionEnded;
        field.LostFocus += OnLostFocus;
        field.PreviewKeyDown += OnKeyDown;
        field.KeyUp += OnKeyUp;
    }

    /// <summary>离页或换掉旧编辑框时解除订阅，遗留的组字与按键状态一并清掉。</summary>
    internal void Release()
    {
        Detach();
        Reset();
    }

    /// <summary>
    /// AutoSuggestBox 的 QuerySubmitted 与这里的抬起补搜共用一次回车：原生提交先到就由它消费，
    /// 抬起只处理漏送的那一次。返回 false 表示这次查询已由别处处理，或者仍在组字。
    /// </summary>
    internal bool TakeQuerySubmitted()
    {
        if (IsKeyDown(VirtualKey.Enter))
        {
            if (_enterConsumed) return false;
            _enterConsumed = true;
        }
        return !_composing;
    }

    /// <summary>页面在别处已经用掉这次回车（例如来源栏自己提交）时，告诉这里不要再补搜。</summary>
    internal void MarkEnterConsumed() => _enterConsumed = true;

    private void Detach()
    {
        if (_field is not { } field) return;
        field.TextCompositionStarted -= OnCompositionStarted;
        field.TextCompositionEnded -= OnCompositionEnded;
        field.LostFocus -= OnLostFocus;
        field.PreviewKeyDown -= OnKeyDown;
        field.KeyUp -= OnKeyUp;
        _field = null;
    }

    private void Reset()
    {
        _composing = false;
        _backspaceHandled = false;
        _enterConsumed = false;
    }

    private void OnLostFocus(object sender, RoutedEventArgs args) => Reset();

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Back) _backspaceHandled = true;
        if (args.Key == VirtualKey.Enter && (_composing || IsShortcutModifierDown()))
            _enterConsumed = true;
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            OnEnterReleased(sender, args);
            return;
        }
        if (args.Key != VirtualKey.Back) return;
        var handledByEditor = _backspaceHandled;
        _backspaceHandled = false;

        // 微软拼音有时只让退格的抬起到达 WinUI。按下已到达或仍在组字时继续由系统编辑。
        if (handledByEditor || _composing || sender is not TextBox { IsReadOnly: false } field
            || IsShortcutModifierDown()) return;

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

    private void OnEnterReleased(object sender, KeyRoutedEventArgs args)
    {
        var consumed = _enterConsumed;
        _enterConsumed = false;
        // 微软拼音可能漏送普通回车的按下。只有这次回车尚未被组字或查询消费时才补搜。
        if (consumed || _composing || !ReferenceEquals(args.OriginalSource, sender)
            || sender is not TextBox field || IsShortcutModifierDown()) return;
        args.Handled = true;
        _submit?.Invoke(field.Text);
    }

    private void OnCompositionStarted(TextBox sender, TextCompositionStartedEventArgs args)
    {
        _composing = true;
        _compositionStarted?.Invoke();
    }

    private void OnCompositionEnded(TextBox sender, TextCompositionEndedEventArgs args)
    {
        // 回车确认或退格清空组字时，组字结束可以先于按键抬起。
        if (IsKeyDown(VirtualKey.Enter)) _enterConsumed = true;
        if (IsKeyDown(VirtualKey.Back)) _backspaceHandled = true;
        _composing = false;
        _compositionFinished?.Invoke(sender.Text);
    }

    private static bool IsShortcutModifierDown() =>
        IsKeyDown(VirtualKey.Control) || IsKeyDown(VirtualKey.Menu)
        || IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows);

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
}
