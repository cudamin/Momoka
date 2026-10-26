using System.Collections.Concurrent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.AniBt;
using Momoka.Emby;

namespace Momoka.Shell.Views;

internal sealed partial class ShellPage
{
    private const string Category = "shell";
    private readonly IServiceProvider? _services;
    private readonly EmbySession? _session;
    internal FrameworkElement Root { get; } = new() { XamlRoot = new XamlRoot() };
    internal Frame ContentFrame { get; } = new();
    internal OfflineDispatcher DispatcherQueue { get; } = new();
    internal List<string> MoviePilotQueries { get; } = [];
    internal List<AniBtSearchQuery> AniBtQueries { get; } = [];
    internal List<string> Notices { get; } = [];
    internal bool MissingShowing => _missingEpisodesShowing;

    internal ShellPage(IServiceProvider? services, EmbySession? session)
    {
        _services = services;
        _session = session;
    }

    internal Task OpenMissing(EmbyItem item, IReadOnlyList<EmbyItem> missing) => ShowMissingEpisodesAsync(item, missing);
    private void Notify(string message, InfoBarSeverity severity) => Notices.Add(message);
    // Captures only the handoff. Never constructs a resource window or runs search/submit.
    private void SearchMoviePilotVersions(string query) => MoviePilotQueries.Add(query);
    private void SearchAniBtVersions(AniBtSearchQuery query) => AniBtQueries.Add(query);
}

internal sealed class OfflineDispatcher
{
    private readonly ConcurrentQueue<Action> _queue = new();
    internal bool AcceptsWork { get; set; } = true;
    internal int Count => _queue.Count;
    internal bool TryEnqueue(Action action)
    {
        if (!AcceptsWork) return false;
        _queue.Enqueue(action);
        return true;
    }
    internal void Drain()
    {
        while (_queue.TryDequeue(out var action)) action();
    }
}
