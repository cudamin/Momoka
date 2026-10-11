using Momoka.AniBt;
using Momoka.Emby;
using Momoka.Diagnostics;
using Momoka.MoviePilot;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

public sealed partial class ShellPage
{
    private bool _aniBtScrapeShowing;

    void IShellActions.SearchAniBtVersions(EmbyItem item) => SearchAniBtVersions(AniBtSearchQuery.From(item));

    private void SearchAniBtVersions(AniBtSearchQuery query)
    {
        if (_services is null || query.Title.Length == 0) return;
        var moviePilot = _services.GetRequiredService<MoviePilotService>();
        if (!moviePilot.Available) return;
        _moviePilotWindow ??= MoviePilotWindow.TryCreate(_window);
        if (_moviePilotWindow is null)
        {
            Notify("打不开资源搜索窗口，请稍后再试", InfoBarSeverity.Error);
            return;
        }
        _moviePilotWindow.Show(_services.GetRequiredService<AniBtService>(), moviePilot, query,
            _services.GetRequiredService<Platform.ISystemLauncher>());
    }

    void IShellActions.ShowAniBtScrape(EmbySessionScope scope, EmbyItem item) => _ = ShowAniBtScrapeAsync(scope, item);

    private async Task ShowAniBtScrapeAsync(EmbySessionScope scope, EmbyItem item)
    {
        if (_aniBtScrapeShowing || !scope.IsCurrent || _services is null || XamlRoot is null) return;
        var moviePilot = _services.GetRequiredService<MoviePilotService>();
        if (!moviePilot.Available) return;
        _aniBtScrapeShowing = true;
        try
        {
            var dialog = new AniBtScrapeDialog(_services.GetRequiredService<AniBtService>(), scope, item) { XamlRoot = XamlRoot };
            await MoviePilotDialog.ShowAsync(dialog, moviePilot);
        }
        catch (Exception error) { Notify(Failure.Describe(error), InfoBarSeverity.Warning); }
        finally { _aniBtScrapeShowing = false; }
    }
}
