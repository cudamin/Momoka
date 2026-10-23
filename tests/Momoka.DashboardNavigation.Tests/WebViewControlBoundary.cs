using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Microsoft.UI.Xaml.Controls;

public sealed class WebView2
{
    private readonly Func<string, Task<string>> _script;
    private Action<WebView2, CoreWebView2NavigationStartingEventArgs>? _starting;
    private Action<WebView2, CoreWebView2NavigationCompletedEventArgs>? _completed;
    public WebView2(CoreWebView2 core)
    {
        CoreWebView2 = core;
        _script = core.Script; // In-memory endpoint, not an uninitialized real control.
        core.Bind(this);
    }
    public CoreWebView2? CoreWebView2 { get; }
    public Visibility Visibility { get; set; } = Visibility.Visible;
    internal int StartingSubscribers => _starting?.GetInvocationList().Length ?? 0;
    internal int CompletedSubscribers => _completed?.GetInvocationList().Length ?? 0;
    public event Action<WebView2, CoreWebView2NavigationStartingEventArgs> NavigationStarting
    {
        add => _starting += value;
        remove => _starting -= value;
    }
    public event Action<WebView2, CoreWebView2NavigationCompletedEventArgs> NavigationCompleted
    {
        add => _completed += value;
        remove => _completed -= value;
    }
    public Task<string> ExecuteScriptAsync(string script) => _script(script);
    public void Close() => CoreWebView2!.Close();
    internal void EmitStarting(CoreWebView2NavigationStartingEventArgs args) => _starting?.Invoke(this, args);
    internal void EmitCompleted(CoreWebView2NavigationCompletedEventArgs args) => _completed?.Invoke(this, args);
    internal Action CaptureCompletion(CoreWebView2NavigationCompletedEventArgs args)
    {
        var subscribers = _completed;
        return () => subscribers?.Invoke(this, args);
    }
}
