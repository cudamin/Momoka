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

    internal void Clear()
    {
        _loads.Cancel();
        _scope = null;
        HasItems = false;
    }

    internal async Task RefreshAsync()
    {
        if (_scope is not { IsCurrent: true } scope) return;
        var token = _loads.Begin();
        try
        {
            var result = await scope.ExecuteAsync(
                (client, ct) => client.GetItemsAsync(ItemQuery.Favorites(0, 1), ct), token).ConfigureAwait(true);
            if (_loads.IsCurrent(token) && scope.IsCurrent)
                HasItems = result.Items.Count > 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (_loads.IsCurrent(token) && scope.IsCurrent)
                Log.Warn("收藏", "读取收藏入口失败", error);
        }
    }

    public void Dispose()
    {
        Clear();
        _loads.Dispose();
    }
}
