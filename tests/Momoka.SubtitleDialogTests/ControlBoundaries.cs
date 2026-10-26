using Microsoft.UI.Xaml;

namespace Microsoft.UI.Xaml.Controls;

public enum InfoBarSeverity { Informational, Success, Warning, Error }
public sealed class ContentDialogClosingEventArgs : EventArgs { public bool Cancel { get; set; } }

public class ContentDialog : FrameworkElement
{
    private readonly TaskCompletionSource _closed = new();
    internal static Action<ContentDialog>? Shown { get; set; }
    public ElementTheme RequestedTheme { get; set; }
    public object? Title { get; set; }
    public event Action<ContentDialog, ContentDialogClosingEventArgs>? Closing;

    public Task ShowAsync()
    {
        Shown?.Invoke(this);
        return _closed.Task;
    }

    internal bool FixtureClose()
    {
        if (_closed.Task.IsCompleted) return true;
        var args = new ContentDialogClosingEventArgs();
        Closing?.Invoke(this, args);
        if (args.Cancel) return false;
        _closed.SetResult();
        return true;
    }
}

public sealed class Button : FrameworkElement;
public sealed class TextBlock : FrameworkElement { public string Text { get; set; } = ""; }
public sealed class ItemsControl : FrameworkElement { public object? ItemsSource { get; set; } }
public sealed class ComboBoxItem { public object? Content { get; set; } public object? Tag { get; set; } }
public sealed class ComboBox : FrameworkElement
{
    public List<object> Items { get; } = [];
    public int SelectedIndex { get; set; }
    public object? SelectedItem => Items.ElementAtOrDefault(SelectedIndex);
}
public sealed class ProgressRing : FrameworkElement { public bool IsActive { get; set; } }
public sealed class ToggleSwitch : FrameworkElement { public bool IsOn { get; set; } }

/// <summary>Attached-property boundary: the dialog only sets the tooltip text.</summary>
public static class ToolTipService
{
    public static void SetToolTip(object target, object? value) { }
}
