using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;

namespace Momoka.Shell.Views;

internal static class ThemeHost
{
    internal static Theme Current { get; } = new();
    internal sealed class Theme { public bool IsDark => true; }
}

public readonly record struct PickedArtwork(byte[] Bytes, string FileName);

// The outer dialog is a read/file boundary. This fixture never performs a media write.
public sealed class CoverDialog
{
    public bool CanPreviewRemoteArtwork { get; init; }
    internal Func<string, RemoteImageSearch, CancellationToken, Task<RemoteImageResult>> Find { get; init; } =
        (_, _, _) => Task.FromResult(new RemoteImageResult());
    internal Func<string, CancellationToken, Task<byte[]?>> Fetch { get; init; } = (_, _) => Task.FromResult<byte[]?>([1]);
    internal Func<Task<PickedArtwork?>> Pick { get; init; } = () => Task.FromResult<PickedArtwork?>(null);

    internal Task<RemoteImageResult> FindAsync(string type, RemoteImageSearch search, CancellationToken token) => Find(type, search, token);
    internal Task<byte[]?> FetchRemoteAsync(string address, CancellationToken token) => Fetch(address, token);
    internal Task<PickedArtwork?> PickArtworkAsync() => Pick();
}

public sealed partial class CoverPickerDialog
{
    internal ComboBox SourcePicker = new();
    internal ToggleSwitch AllLanguagesToggle = new();
    internal GridView Grid = new();
    internal TextBlock Notice = new() { Visibility = Visibility.Collapsed };
    internal TextBlock PreviewNotice = new() { Visibility = Visibility.Collapsed };
    internal ProgressBar SearchProgress = new() { Visibility = Visibility.Collapsed };
    internal Button RetryArtworkSearch = new() { Visibility = Visibility.Collapsed };
    internal Button RetryArtworkPreviews = new() { Visibility = Visibility.Collapsed };
    internal Button UploadArtworkButton = new();

    private void InitializeComponent()
    {
        SourcePicker.SelectionChanged += OnSourceChanged;
        AllLanguagesToggle.Toggled += OnLanguagesChanged;
        Grid.SelectionChanged += OnPicked;
        RetryArtworkSearch.Click = () => OnRetrySearch(RetryArtworkSearch, new());
        RetryArtworkPreviews.Click = () => OnRetry(RetryArtworkPreviews, new());
        UploadArtworkButton.Click = () => OnUpload(UploadArtworkButton, new());
        // XAML wires Toggled before applying IsOn=True; exercise that initial owner-null call.
        AllLanguagesToggle.IsOn = true;
    }

    internal Task SearchSettledAsync() => Task.WhenAll(_searchLoads.ToArray());
    internal Task PreviewsSettledAsync() => _loadTask;
}
