using CommunityToolkit.Mvvm.ComponentModel;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>标题栏收藏入口的可见性；每次读取只取一项，旧账号的结果不能回填。</summary>
public sealed partial class FavoritesViewModel : ObservableObject, IDisposable
{
    private readonly LoadGeneration _loads = new();
    private EmbySession? _session;
    private EmbySessionScope? _scope;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visibility))]
    public partial bool HasItems { get; set; }

    public Visibility Visibility => HasItems ? Visibility.Visible : Visibility.Collapsed;

    internal void Attach(EmbySessionScope scope)
    {
        Clear();
        _scope = scope;
    }

    internal void Attach(EmbySession session)
    {
        Clear();
        _session = session;
    }

    internal void Clear()
    {
        _loads.Cancel();
        _scope = null;
        _session = null;
        HasItems = false;
    }

    internal async Task RefreshAsync()
    {
        var sources = _session is { } session ? FavoriteAccounts.Applicable(session.Sources)
            : _scope is { IsAvailable: true } scope ? FavoriteAccounts.Applicable([scope]) : [];
        if (sources.Count == 0) return;
        var token = _loads.Begin();
        try
        {
            var results = await Task.WhenAll(sources.Select(source => HasFavoritesAsync(source, token))).ConfigureAwait(true);
            if (_loads.IsCurrent(token)) HasItems = results.Any(found => found);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (_loads.IsCurrent(token))
                Log.Warn("收藏", "读取收藏入口失败", error);
        }
    }

    private static async Task<bool> HasFavoritesAsync(EmbySessionScope source, CancellationToken token)
    {
        try
        {
            var result = await source.ExecuteAsync(
                (client, ct) => client.GetItemsAsync(ItemQuery.Favorites(0, 1), ct), token).ConfigureAwait(false);
            return source.IsAvailable && result.Items.Count > 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            Log.Warn("收藏", $"读取 {source.DisplayName} 的收藏入口失败", error);
            return false;
        }
    }

    public void Dispose()
    {
        Clear();
        _loads.Dispose();
    }
}
