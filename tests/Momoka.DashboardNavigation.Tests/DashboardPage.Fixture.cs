using Momoka.Emby;
using Momoka.Shell.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Momoka.Shell.Views;

public sealed partial class DashboardPage
{
    // Value and owner boundaries only. Attach/Start/login/credential seeding are deliberately not run.
    private readonly WebView2 Web;
    private readonly VisualValue NoticePanel = new() { Visibility = Visibility.Collapsed };
    private readonly VisualValue Notice = new();
    private readonly VisualValue Readout = new();

    internal DashboardPage(CoreWebView2 core, EmbySessionScope scope, string? sourceKey)
    {
        Web = new(core);
        _scope = scope;
        _sourceKey = sourceKey;
        _launcher = new ForbiddenLauncher();
        _request = new object();
        _loadContent = true;
        _immersive = false;
        _core = true;
        _seeded = false;
        FixtureWire(core); // The exact production StartAsync subscription statements.
    }

    internal Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; } = new();
    internal WebView2 FixtureWeb => Web;
    internal string FixtureNotice => Notice.Text;
    internal Visibility FixtureNoticeVisibility => NoticePanel.Visibility;
    internal string FixtureReadout => Readout.Text;
    internal int FixtureGeneration => _generation;
    internal void FixtureRefresh() => OnRefresh(Web, new());
    internal Task FixtureSample() => SampleAsync(_generation);
    internal Task FixtureWatchdog(int generation) => GiveUpAsync(generation);
    internal void FixtureShowOldNotice()
    {
        _error = "old fixture error"; // Arrange a preexisting overlay using the real Show method.
        Show("old fixture notice");
    }
    internal bool FixtureContentEnabled => _loadContent && !_immersive;
}

internal sealed class VisualValue
{
    internal string Text { get; set; } = "";
    internal Visibility Visibility { get; set; } = Visibility.Visible;
}
