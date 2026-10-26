namespace Microsoft.UI.Xaml.Controls;

// Inert event/value endpoints only. No HWND, WinRT, desktop, native or app activation.
public enum InfoBarSeverity { Informational, Success, Warning, Error }
public sealed class ContentDialogOpenedEventArgs : EventArgs;
public sealed class ContentDialogClosedEventArgs : EventArgs;
public sealed class ContentDialogClosingEventArgs : EventArgs
{
    public bool Cancel { get; set; }
}
public sealed class ContentDialogButtonClickEventArgs : EventArgs
{
    public bool Cancel { get; set; }
}
public class ContentDialog
{
    public object? Title { get; set; }
    public Microsoft.UI.Xaml.ElementTheme RequestedTheme { get; set; }
    public string PrimaryButtonText { get; set; } = "";
    public bool IsPrimaryButtonEnabled { get; set; }
    public event Action<ContentDialog, ContentDialogOpenedEventArgs>? Opened;
    public event Action<ContentDialog, ContentDialogClosingEventArgs>? Closing;
    public event Action<ContentDialog, ContentDialogClosedEventArgs>? Closed;

    internal void OpenForFixture() => Opened?.Invoke(this, new());
    internal bool CloseForFixture()
    {
        var args = new ContentDialogClosingEventArgs();
        Closing?.Invoke(this, args);
        if (!args.Cancel) Closed?.Invoke(this, new());
        return !args.Cancel;
    }
}
