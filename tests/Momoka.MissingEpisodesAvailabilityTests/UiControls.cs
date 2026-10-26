using Microsoft.UI.Xaml;
using Momoka.Tests;

namespace Microsoft.UI.Xaml.Controls;

public enum InfoBarSeverity { Informational, Warning, Error }
public enum ContentDialogResult { None }
public class Button : FrameworkElement;
public sealed class Frame
{
    public object? Content { get; set; } = new object();
}
public sealed class TextBlock : FrameworkElement
{
    public string Text { get; set; } = "";
}
public sealed class ListView : FrameworkElement
{
    public object? ItemsSource { get; set; }
}
public sealed class ToggleSwitch : FrameworkElement
{
    private bool _isOn;
    public event Action<object, RoutedEventArgs>? Toggled;
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            Toggled?.Invoke(this, new RoutedEventArgs());
        }
    }
}
public class ContentDialog : FrameworkElement
{
    private readonly TaskCompletionSource<ContentDialogResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ElementTheme RequestedTheme { get; set; }
    public bool IsOpen { get; private set; }
    public int HideCount { get; private set; }
    public Task<ContentDialogResult> ShowAsync()
    {
        IsOpen = true;
        DialogPlan.Current.Shown(this);
        return _completion.Task;
    }
    internal void DeliverClosed() => _completion.TrySetResult(ContentDialogResult.None);
    public void Hide()
    {
        HideCount++;
        IsOpen = false;
        if (!DialogPlan.Current.DeferCloseCompletion) DeliverClosed();
    }
}
