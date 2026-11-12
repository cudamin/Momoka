using System.Diagnostics;
using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Music;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>匿名元数据直接追加到实际页面集合，单独测容器/图片；网络分页由音乐分页探针核验。</summary>
    private async Task MusicArtworkAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var picture = await FixtureImageAsync(400, 400);
        var requests = 0;
        fixture.Transport.Reply = (message, _) =>
        {
            Require(message.Method == HttpMethod.Get, "音乐图片探针拒绝写入");
            if (message.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal))
            {
                requests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) });
            }
            return Task.FromResult(Json(new ItemsResult { Items = [], TotalRecordCount = 0 }));
        };
        var scope = fixture.Session.Capture();
        var frame = new Frame();
        _root.Children.Add(frame);
        var samples = new List<object>();
        try
        {
            ResizeProbeClient(1100, 760);
            Require(frame.Navigate(typeof(MusicLibraryPage), new LibraryRequest
            {
                Services = fixture.Services,
                ParentId = "music-artwork",
                Title = "匿名音乐图片",
                CollectionType = "music",
                Source = scope
            }), "音乐图片页未导航");
            var page = (MusicLibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady);
            foreach (var section in new[] { MusicSection.Tracks, MusicSection.Albums, MusicSection.Artists, MusicSection.Playlists })
            {
                await page.ViewModel.SelectAsync(section);
                foreach (var tab in Get<WrapRow>(page, "SectionTabs").Children.OfType<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>())
                    tab.IsChecked = tab.Tag is string value && int.TryParse(value, out var index) && index == (int)section;
                foreach (var total in new[] { 100, 1_000, 10_000 })
                {
                    foreach (var card in page.ViewModel.Rows) card.AbandonPoster();
                    page.ViewModel.Rows.Clear();
                    page.ViewModel.EmptyVisibility = Visibility.Collapsed;
                    var wall = MusicCatalog.UsesCoverWall(section);
                    var type = section switch
                    {
                        MusicSection.Albums => EmbyItemType.MusicAlbum,
                        MusicSection.Artists => EmbyItemType.MusicArtist,
                        MusicSection.Playlists => EmbyItemType.Playlist,
                        _ => EmbyItemType.Audio
                    };
                    var width = wall ? MusicCatalog.WallTileWidth : MusicCatalog.TrackArtWidth;
                    var prefix = $"art-{section}-{total}-";
                    page.ViewModel.Title = "匿名音乐容器量测";
                    page.ViewModel.CountLabel = MusicCatalog.CountLabel(section, total);
                    var readsBefore = requests;
                    var build = Stopwatch.StartNew();
                    for (var index = 0; index < total; index++)
                        page.ViewModel.Rows.Add(new CardItem(scope.Bind(new EmbyItem
                        {
                            Id = prefix + index,
                            Name = "匿名音乐 " + index,
                            Type = type,
                            ImageTags = new() { [EmbyImageStore.Primary] = "fixture" }
                        }), fixture.Images, width, aspect: 1, indicators: false));
                    var metadataMs = build.Elapsed.TotalMilliseconds;
                    var first = Stopwatch.StartNew();
                    await LayoutAsync(page);
                    var list = Get<ListView>(page, "MusicRows");
                    var wallView = Get<ItemsView>(page, "MusicWall");
                    var outer = Get<Grid>(page, "WallScroll");
                    var scroll = wallView.ScrollView;
                    if (wall)
                    {
                        Require(scroll is not null, "音乐墙没有原生ScrollView");
                        MusicArtworkProbeRules.Viewport(outer.ActualHeight, scroll!.ViewportHeight);
                    }
                    await UntilAsync(() => Descendants(wall ? wallView : list).OfType<Image>()
                        .Any(image => CardArtwork.GetItem(image)?.Item.Id.StartsWith(prefix, StringComparison.Ordinal) == true && image.Source is not null));
                    var firstImageMs = first.Elapsed.TotalMilliseconds;
                    var initial = Read();
                    await WritePerformanceEvidenceAsync("music-artwork-current-state.json", new
                    {
                        section = section.ToString(),
                        total,
                        stage = "initial",
                        initial,
                        hostHeight = outer.ActualHeight,
                        innerViewportHeight = scroll?.ViewportHeight
                    });
                    Check(initial);
                    Require(initial.Images > 0 && requests > readsBefore, "音乐模板有标签但没有图请求或实际Image.Source");
                    Require(initial.DecodeWidths.All(value => value == width), "音乐解码宽度与44/170 DIP不一致");
                    var firstImage = Descendants(wall ? wallView : list).OfType<Image>().First(image =>
                        CardArtwork.GetItem(image)?.Item.Id.StartsWith(prefix, StringComparison.Ordinal) == true && image.Source is not null);
                    var firstCard = CardArtwork.GetItem(firstImage)!;
                    var negativeRejected = false;
                    CardArtwork.SetItem(firstImage, null);
                    try
                    {
                        Require(CardArtwork.GetItem(firstImage) is not null && firstImage.Source is not null,
                            "已移除图片需求的反例不能通过实际图片检查");
                    }
                    catch (InvalidOperationException) { negativeRejected = true; }
                    finally { CardArtwork.SetItem(firstImage, firstCard); }
                    Require(negativeRejected && firstImage.Source is not null, "图片需求反例未拒绝或原容器未恢复");
                    var intervals = new List<double>();
                    var clock = Stopwatch.StartNew();
                    var last = 0d;
                    void Rendering(object? sender, object args)
                    {
                        var now = clock.Elapsed.TotalMilliseconds;
                        if (last > 0) intervals.Add(now - last);
                        last = now;
                    }
                    CompositionTarget.Rendering += Rendering;
                    try
                    {
                        if (wall)
                        {
                            scroll!.ScrollTo(0, scroll.ScrollableHeight,
                                new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
                            await LayoutAsync(page);
                        }
                        else
                        {
                            list.ScrollIntoView(page.ViewModel.Rows[^1]);
                            await LayoutAsync(page);
                        }
                        await WaitForDiagnosticsFrameAsync();
                        await CheckItemAsync(total - 1);
                        var tail = Read();
                        await WritePerformanceEvidenceAsync("music-artwork-current-state.json", new
                        {
                            section = section.ToString(),
                            total,
                            stage = "tail",
                            tail,
                            hostHeight = outer.ActualHeight,
                            innerViewportHeight = scroll?.ViewportHeight
                        });
                        Check(tail);
                        if (wall) scroll!.ScrollTo(0, 0,
                            new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
                        else list.ScrollIntoView(page.ViewModel.Rows[0]);
                        await LayoutAsync(page);
                        await WaitForDiagnosticsFrameAsync();
                        await CheckItemAsync(0);
                        var restored = Read();
                        await WritePerformanceEvidenceAsync("music-artwork-current-state.json", new
                        {
                            section = section.ToString(),
                            total,
                            stage = "restored",
                            restored,
                            hostHeight = outer.ActualHeight,
                            innerViewportHeight = scroll?.ViewportHeight
                        });
                        Check(restored);
                        var ordered = intervals.Order().ToArray();
                        using var process = Process.GetCurrentProcess();
                        process.Refresh();
                        samples.Add(new
                        {
                            section = section.ToString(),
                            total,
                            metadataMs,
                            firstImageMs,
                            initial,
                            tail,
                            restored,
                            imageGets = requests - readsBefore,
                            frameSamples = ordered.Length,
                            frameP95Ms = ordered.Length == 0 ? 0 : ordered[Math.Min(ordered.Length - 1, (int)Math.Ceiling(ordered.Length * .95) - 1)],
                            frameMaxMs = ordered.Length == 0 ? 0 : ordered[^1],
                            workingSetBytes = process.WorkingSet64,
                            privateBytes = process.PrivateMemorySize64,
                            removedDemandRejected = negativeRejected,
                            viewportWidth = wall ? scroll?.ViewportWidth : list.ActualWidth,
                            viewportHeight = wall ? outer.ActualHeight : list.ActualHeight,
                            innerViewportHeight = wall ? wallView.ScrollView?.ViewportHeight : null,
                            rasterizationScale = _root.XamlRoot.RasterizationScale,
                            decodedTextureBytesMeasured = false,
                            directMetadataFixture = true
                        });
                        await WritePerformanceEvidenceAsync("music-artwork-performance.json", samples);
                    }
                    finally { CompositionTarget.Rendering -= Rendering; }
                    if (total == 100 && section is MusicSection.Tracks or MusicSection.Albums)
                        await SaveProbeScreenAsync($"music-artwork-{section}-1100-screen.png");

                    MusicArtworkReading Read()
                    {
                        var controls = Descendants(wall ? wallView : list).ToArray();
                        var repeater = wall ? controls.OfType<ItemsRepeater>().FirstOrDefault() : null;
                        var realized = wall ? repeater is null ? 0 : Enumerable.Range(0, total).Count(index => repeater.TryGetElement(index) is not null)
                            : Enumerable.Range(0, total).Count(index => list.ContainerFromIndex(index) is not null);
                        var loaded = controls.OfType<Image>().Where(image =>
                            CardArtwork.GetItem(image)?.Item.Id.StartsWith(prefix, StringComparison.Ordinal) == true && image.Source is not null).ToArray();
                        return new(realized, controls.Count(element => wall ? element is ItemContainer : element is ListViewItem),
                            loaded.Length, page.ViewModel.Rows.Count(card => card.RowPoster is not null),
                            page.ViewModel.Rows.Count(card => Get<bool>(card, "_wanted")),
                            page.ViewModel.Rows.Count(card => Get<WeakReference<object>?>(card, "_holder") is { } holder
                                && holder.TryGetTarget(out var target) && target is Image { XamlRoot: not null }),
                            loaded.Select(image => image.Source).OfType<Microsoft.UI.Xaml.Media.Imaging.BitmapImage>()
                                .Select(bitmap => bitmap.DecodePixelWidth).Distinct().ToArray());
                    }

                    void Check(MusicArtworkReading reading) => MusicArtworkProbeRules.Demands(total, reading.Realized, reading.Images,
                        reading.HeldPosters, reading.WantedRows, reading.LiveHolders);

                    async Task CheckItemAsync(int index)
                    {
                        if (!wall) return;
                        var repeater = Descendants(wallView).OfType<ItemsRepeater>().Single();
                        await UntilAsync(() => repeater.TryGetElement(index) is not null);
                        var element = (FrameworkElement)repeater.TryGetElement(index)!;
                        var card = Descendants(element).OfType<Image>().Select(CardArtwork.GetItem).FirstOrDefault(item => item is not null);
                        var focused = element is Control control && control.Focus(FocusState.Keyboard);
                        var point = element.TransformToVisual(scroll).TransformPoint(new());
                        MusicArtworkProbeRules.Item(prefix + index, card?.Item.Id ?? "", focused, point.Y, element.ActualHeight, scroll!.ViewportHeight);
                    }
                }
            }
            await WritePerformanceEvidenceAsync("music-artwork-performance.json", samples);
            Require(fixture.Actions.Plays == 0, "音乐图片探针触发了播放");
            page.Release();
            Require(page.ViewModel.Rows.All(card => card.RowPoster is null), "音乐页离页后仍保留位图");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            frame.Content = null;
            frame.BackStack.Clear();
            _root.Children.Remove(frame);
            ResizeProbeClient(1420, 980);
        }
    }

    private sealed record MusicArtworkReading(int Realized, int TreeContainers, int Images, int HeldPosters, int WantedRows, int LiveHolders, int[] DecodeWidths);
}
