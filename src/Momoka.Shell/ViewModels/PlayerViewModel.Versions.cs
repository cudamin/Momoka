using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Playback;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.ViewModels;

public sealed partial class PlayerViewModel
{
    private CancellationTokenSource? _versionLoad;

    private void OnVersionSourcesChanged(object? sender, EventArgs e) => OnUi(() =>
    {
        VersionControlsVisible = Versions.Count > 1;
        NoteVersionCount();
        if (_nowPlaying is { } item && item.Source?.IsAvailable != false)
            _ = LoadVersionsAsync(item, _generation, ++_versionDiscovery);
    });

    private async Task LoadVersionsAsync(EmbyItem item, int generation, int discovery)
    {
        _versionLoad?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _versionLoad = cancellation;
        try
        {
            var result = await MediaVersionDiscovery.FindAsync(_session, item,
                Settings.Playback.MediaSourceOrder, cancellation.Token).ConfigureAwait(true);
            if (generation != _generation || discovery != _versionDiscovery || !ReferenceEquals(_nowPlaying, item)) return;
            _versions = result.Versions;
            VersionControlsVisible = Versions.Count > 1;
            NoteVersionCount();
            if (result.FailedSources.Count > 0)
                Noticed?.Invoke($"部分来源的版本读取失败：{string.Join("、", result.FailedSources)}", InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Log.Warn(Category, "读取其他来源版本失败", error);
            if (generation == _generation && discovery == _versionDiscovery)
                Noticed?.Invoke("读取其他来源版本失败，仍可选择已加载的版本", InfoBarSeverity.Warning);
        }
        finally
        {
            if (ReferenceEquals(_versionLoad, cancellation)) _versionLoad = null;
        }
    }
}
