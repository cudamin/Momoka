using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

public sealed partial class MusicLibraryPage
{
    private ItemsRepeater? _artworkRepeater;

    private void OnWallLoaded(object sender, RoutedEventArgs args)
    {
        if (_released) return;
        var repeater = ArtworkTree(MusicWall).OfType<ItemsRepeater>().FirstOrDefault();
        if (ReferenceEquals(repeater, _artworkRepeater)) return;
        DetachWallArtwork();
        _artworkRepeater = repeater;
        if (repeater is null) return;
        repeater.ElementPrepared += OnArtworkPrepared;
        repeater.ElementClearing += OnArtworkClearing;
        // 模板首次Loaded前可能已经prepared；同属主池元素仍在树中，按实际索引区分，不能全claim。
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(repeater); index++)
        {
            var element = VisualTreeHelper.GetChild(repeater, index);
            SetArtworkRealized(element, element is UIElement item && repeater.GetElementIndex(item) >= 0);
        }
    }

    private void OnWallUnloaded(object sender, RoutedEventArgs args)
    {
        if (MusicWall.XamlRoot is null) DetachWallArtwork();
    }

    private static void OnArtworkPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args) =>
        SetArtworkRealized(args.Element, true);

    private static void OnArtworkClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args) =>
        SetArtworkRealized(args.Element, false);

    private void OnTrackContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args) =>
        SetArtworkRealized(args.ItemContainer, !_released && !args.InRecycleQueue);

    private void DetachWallArtwork()
    {
        if (_artworkRepeater is not { } repeater) return;
        repeater.ElementPrepared -= OnArtworkPrepared;
        repeater.ElementClearing -= OnArtworkClearing;
        SetArtworkRealized(repeater, false);
        _artworkRepeater = null;
    }

    private static void SetArtworkRealized(DependencyObject root, bool realized)
    {
        // 只管理含此行为的Image；不清x:Bind的Item值，也不触碰外层选择/焦点或其他图片。
        foreach (var image in ArtworkTree(root).OfType<Image>())
            CardArtwork.SetRealized(image, realized);
    }

    private static IEnumerable<DependencyObject> ArtworkTree(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in ArtworkTree(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
