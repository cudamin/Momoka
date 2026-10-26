using System.Text.Json;
using WebControl = Microsoft.UI.Xaml.Controls.WebView2;

namespace Microsoft.Web.WebView2.Core;

// Property shapes verified against this tree's restored WebView2 1.0.3719.77 metadata.
public sealed class CoreWebView2NavigationStartingEventArgs(ulong id, string uri, bool redirected = false)
{
    public ulong NavigationId { get; } = id;
    public string Uri { get; } = uri;
    public bool IsRedirected { get; } = redirected;
}

public sealed class CoreWebView2NavigationCompletedEventArgs(ulong id, bool success, CoreWebView2WebErrorStatus error)
{
    public ulong NavigationId { get; } = id;
    public bool IsSuccess { get; } = success;
    public CoreWebView2WebErrorStatus WebErrorStatus { get; } = error;
}

public enum CoreWebView2WebErrorStatus { Unknown, OperationCanceled, Timeout, HostNameNotResolved }

public sealed class CoreWebView2NewWindowRequestedEventArgs
{
    public bool Handled { get; set; }
    public string Uri { get; init; } = "http://r019.invalid/help";
}

public sealed class CoreWebView2
{
    private Action<CoreWebView2, CoreWebView2NewWindowRequestedEventArgs>? _newWindow;
    private WebControl? _owner;
    internal ulong NextId { get; set; } = 1;
    internal bool AutoStart { get; set; } = true;
    internal Exception? NavigateFailure { get; set; }
    internal bool HoldInline { get; set; }
    internal string InlineResult { get; set; } = "true";
    internal Exception? InlineFailure { get; set; }
    internal List<NavigationCall> Navigations { get; } = [];
    internal List<ScriptCall> Scripts { get; } = [];
    internal int NewWindowSubscribers => _newWindow?.GetInvocationList().Length ?? 0;
    internal int CloseCount { get; private set; }
    public event Action<CoreWebView2, CoreWebView2NewWindowRequestedEventArgs> NewWindowRequested
    {
        add => _newWindow += value;
        remove => _newWindow -= value;
    }

    internal void Bind(WebControl owner) => _owner = owner;

    public void Navigate(string uri)
    {
        var address = new Uri(uri, UriKind.Absolute);
        if (!address.Host.EndsWith(".invalid", StringComparison.Ordinal))
            throw new InvalidOperationException("R-019 only accepts in-memory anonymous .invalid fixture URLs");
        if (NavigateFailure is { } failure) throw failure;
        var call = new NavigationCall(NextId++, uri);
        Navigations.Add(call);
        if (AutoStart) Start(call);
    }

    internal void NavigateFromScript(string uri)
    {
        Action<string> endpoint = Navigate; // Fake endpoint, not WinUI initialization.
        endpoint(uri);
    }

    internal void Start(NavigationCall call, bool redirected = false) =>
        _owner!.EmitStarting(new(call.Id, call.Uri, redirected));
    internal void Complete(NavigationCall call, bool success = true,
        CoreWebView2WebErrorStatus error = CoreWebView2WebErrorStatus.Unknown) =>
        _owner!.EmitCompleted(new(call.Id, success, error));
    internal Action CaptureCompletion(NavigationCall call, bool success, CoreWebView2WebErrorStatus error) =>
        _owner!.CaptureCompletion(new(call.Id, success, error));
    internal void EmitUnknownCompletion(ulong id, bool success, CoreWebView2WebErrorStatus error) =>
        _owner!.EmitCompleted(new(id, success, error));

    public Task<string> ExecuteScriptAsync(string script) => Script(script);

    internal Task<string> Script(string script)
    {
        // This boundary never interprets JavaScript or opens a browser/storage. Only classify the
        // existing production route/sample call so tests can pause its response and UI continuation.
        var inline = script.Contains("Emby.Page.show", StringComparison.Ordinal);
        if (!inline && !script.Contains("var data = JSON.parse(localStorage.getItem(", StringComparison.Ordinal))
            throw new InvalidOperationException("Only the real page's route/sample boundary is allowed");
        var call = new ScriptCall(inline);
        Scripts.Add(call);
        if (inline && !HoldInline)
        {
            if (InlineFailure is { } failure) call.Fail(failure);
            else call.ReturnRaw(InlineResult);
        }
        return call.Task;
    }

    internal void Close()
    {
        CloseCount++;
        foreach (var script in Scripts) script.Cancel();
    }

    internal void EmitNewWindow() => _newWindow?.Invoke(this, new());
}

internal sealed record NavigationCall(ulong Id, string Uri);

internal sealed class ScriptCall(bool inline)
{
    private readonly TaskCompletionSource<string> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Inline { get; } = inline;
    internal Task<string> Task => _result.Task;
    internal void ReturnRaw(string value) => _result.TrySetResult(value);
    internal void ReturnSample(string value) => ReturnRaw(JsonSerializer.Serialize(value));
    internal void Fail(Exception error) => _result.TrySetException(error);
    internal void Cancel() => _result.TrySetCanceled();
}
