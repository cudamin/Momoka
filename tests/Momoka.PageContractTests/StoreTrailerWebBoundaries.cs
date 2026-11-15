// WebView event/command boundaries only. Production StoreYouTubePreview owns parsing, origin and lifecycle guards.
namespace Microsoft.UI.Xaml.Controls
{
    internal sealed class WebView2
    {
        internal bool IsHitTestVisible { get; set; }
        internal bool IsTabStop { get; set; }
        internal Microsoft.Web.WebView2.Core.CoreWebView2 CoreWebView2 { get; } = new();
        internal Microsoft.Web.WebView2.Core.CoreWebView2 TestCore => CoreWebView2;
        internal bool Closed { get; private set; }
        internal Task EnsureCoreWebView2Async(Microsoft.Web.WebView2.Core.CoreWebView2Environment _) => Task.CompletedTask;
        internal void Close() => Closed = true;
    }
}

namespace Microsoft.Web.WebView2.Core
{
    internal enum CoreWebView2HostResourceAccessKind { DenyCors }
    internal sealed class CoreWebView2EnvironmentOptions
    {
        internal string AdditionalBrowserArguments { get; set; } = "";
    }
    internal sealed class CoreWebView2Environment
    {
        internal static Task<CoreWebView2Environment> CreateWithOptionsAsync(string _, string folder, CoreWebView2EnvironmentOptions options)
        {
            Directory.CreateDirectory(folder);
            return Task.FromResult(new CoreWebView2Environment());
        }
    }
    internal sealed class CoreWebView2Settings
    {
        internal bool AreDefaultContextMenusEnabled { get; set; }
        internal bool AreDevToolsEnabled { get; set; }
        internal bool IsStatusBarEnabled { get; set; }
    }
    internal sealed class CoreWebView2NewWindowRequestedEventArgs : EventArgs { internal bool Handled { get; set; } }
    internal sealed class CoreWebView2DownloadStartingEventArgs : EventArgs { internal bool Cancel { get; set; } }
    internal sealed class CoreWebView2NavigationStartingEventArgs(string uri) : EventArgs
    {
        internal string Uri { get; } = uri;
        internal bool Cancel { get; set; }
    }
    internal sealed class CoreWebView2NavigationCompletedEventArgs : EventArgs { internal bool IsSuccess { get; set; } }
    internal sealed class CoreWebView2WebMessageReceivedEventArgs(string source, string message) : EventArgs
    {
        internal string Source { get; } = source;
        internal string TryGetWebMessageAsString() => message;
    }
    internal sealed class CoreWebView2
    {
        internal CoreWebView2Settings Settings { get; } = new();
        internal bool IsMuted { get; set; }
        internal List<string> Commands { get; } = [];
        internal event EventHandler<CoreWebView2NewWindowRequestedEventArgs>? NewWindowRequested;
        internal event EventHandler<CoreWebView2DownloadStartingEventArgs>? DownloadStarting;
        internal event EventHandler<CoreWebView2NavigationStartingEventArgs>? NavigationStarting;
        internal event EventHandler<CoreWebView2NavigationCompletedEventArgs>? NavigationCompleted;
        internal event EventHandler<CoreWebView2WebMessageReceivedEventArgs>? WebMessageReceived;
        internal void SetVirtualHostNameToFolderMapping(string _, string folder, CoreWebView2HostResourceAccessKind access) { }
        internal void PostWebMessageAsString(string command) => Commands.Add(command);
        internal void Navigate(string uri)
        {
            var starting = new CoreWebView2NavigationStartingEventArgs(uri);
            NavigationStarting?.Invoke(this, starting);
            if (starting.Cancel) return;
            NavigationCompleted?.Invoke(this, new() { IsSuccess = true });
            RaiseMessage("ready", uri);
        }
        internal void RaiseMessage(string message, string source = "https://momoka/index.html") =>
            WebMessageReceived?.Invoke(this, new(source, message));
        internal bool TryOpenWindow()
        {
            var args = new CoreWebView2NewWindowRequestedEventArgs();
            NewWindowRequested?.Invoke(this, args);
            return !args.Handled;
        }
        internal bool TryDownload()
        {
            var args = new CoreWebView2DownloadStartingEventArgs();
            DownloadStarting?.Invoke(this, args);
            return !args.Cancel;
        }
    }
}
