using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>Real Frame history, ItemsView layout and screen evidence; all HTTP remains anonymous.</summary>
    private async Task PerformanceDeepLibraryReturnAsync()
    {
        var evidence = new List<object>();
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.FnNas, MediaServerKind.Plex })
        {
            using var fixture = CreateFixture();
            var reads = new List<(int Start, int Limit)>();
            var poster = await FixtureImageAsync(170, 255);
            fixture.Transport.Reply = async (message, _) =>
            {
                var path = message.RequestUri!.AbsolutePath;
                Require(message.Method == HttpMethod.Get || kind == MediaServerKind.FnNas
                    && message.Method == HttpMethod.Post && path.EndsWith("/api/v1/item/list", StringComparison.Ordinal),
                    "深返回探针只允许匿名读取，不允许服务端写入");
                if (kind == MediaServerKind.FnNas)
                {
                    if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
                    if (path.EndsWith("/api/v1/sys/version", StringComparison.Ordinal)) return FnNasEnvelope(new { version = "0.9.8", mediasrvVersion = "0.9.8" });
                    if (path.EndsWith("/api/v1/sys/config", StringComparison.Ordinal)) return FnNasEnvelope(new { server_guid = "fn-guid", server_name = "匿名飞牛" });
                    if (path.EndsWith("/api/v1/user/info", StringComparison.Ordinal)) return FnNasEnvelope(new { guid = "user", username = "user", is_admin = "1" });
                    if (path.EndsWith("/api/v1/mediadb/list", StringComparison.Ordinal)) return FnNasEnvelope(Array.Empty<object>());
                    if (path.EndsWith("/api/v1/item/list", StringComparison.Ordinal))
                    {
                        using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync());
                        var limit = body.RootElement.GetProperty("page_size").GetInt32();
                        var start = (body.RootElement.GetProperty("page").GetInt32() - 1) * limit;
                        reads.Add((start, limit));
                        return FnNasEnvelope(new
                        {
                            total = 2000,
                            list = Enumerable.Range(start, Math.Max(0, Math.Min(limit, 2000 - start)))
                                .Select(index => new { guid = "deep-" + index, title = "匿名电影 " + index, type = "Movie" }).ToArray()
                        });
                    }
                    return new(HttpStatusCode.NotFound);
                }
                if (kind == MediaServerKind.Plex)
                {
                    if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
                    if (path.EndsWith("/identity", StringComparison.Ordinal)) return Json(new { MediaContainer = new { machineIdentifier = "machine", version = "1.41.2" } });
                    if (path.EndsWith("/library/sections", StringComparison.Ordinal)) return Json(new { MediaContainer = new { Directory = Array.Empty<object>() } });
                    if (path.EndsWith("/library/sections/library/all", StringComparison.Ordinal))
                    {
                        var start = int.Parse(message.Headers.GetValues("X-Plex-Container-Start").Single(), System.Globalization.CultureInfo.InvariantCulture);
                        var limit = int.Parse(message.Headers.GetValues("X-Plex-Container-Size").Single(), System.Globalization.CultureInfo.InvariantCulture);
                        reads.Add((start, limit));
                        return Json(new { MediaContainer = new
                        {
                            totalSize = 2000,
                            Metadata = Enumerable.Range(start, Math.Max(0, Math.Min(limit, 2000 - start)))
                                .Select(index => new { ratingKey = "deep-" + index, title = "匿名电影 " + index, type = "movie" }).ToArray()
                        } });
                    }
                    return Json(new { MediaContainer = new { friendlyName = "匿名 Plex", version = "1.41.2", machineIdentifier = "machine" } });
                }
                if (path.EndsWith("/Items", StringComparison.Ordinal))
                {
                    var start = int.Parse(Query(message, "StartIndex") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
                    var limit = int.Parse(Query(message, "Limit") ?? "20", System.Globalization.CultureInfo.InvariantCulture);
                    reads.Add((start, limit));
                    return Json(new ItemsResult
                    {
                        TotalRecordCount = 2000,
                        Items = Enumerable.Range(start, Math.Max(0, Math.Min(limit, 2000 - start)))
                            .Select(index => new EmbyItem { Id = "deep-" + index, Name = "匿名电影 " + index, Type = EmbyItemType.Movie }).ToList()
                    });
                }
                return kind == MediaServerKind.Jellyfin ? JellyfinResponse(message, poster) : FakeTransport.Standard(message);
            };
            await fixture.SelectAsync(host: "deep.invalid", kind: kind);
            fixture.Settings.Ui.PageSize = 100;
            var frame = new Frame();
            _root.Children.Add(frame);
            try
            {
                var request = new LibraryRequest
                {
                    Services = fixture.Services, Title = "匿名深返回库", Source = fixture.Session.Capture(),
                    ParentId = "library", CollectionType = "movies"
                };
                ResizeProbeClient(1280, 800);
                Require(frame.Navigate(typeof(LibraryPage), request), "深返回首个 Frame 导航失败");
                var page = (LibraryPage)frame.Content;
                await UntilAsync(() => page.ViewModel.IsReady && page.ViewModel.Cards.Count > 0);
                await LayoutAsync(page);
                await page.ViewModel.LoadUntilAsync(1500);
                await LayoutAsync(page);
                var wall = Get<ItemsView>(page, "CardWall");
                var anchorKey = page.ViewModel.Cards[page.ViewModel.LocalIndexOf(1500)].Item.IdentityKey;
                wall.StartBringItemIntoView(page.ViewModel.LocalIndexOf(1500), new BringIntoViewOptions
                { AnimationDesired = false, VerticalAlignmentRatio = 0 });
                // A recycled element may still have a nonzero size far outside the viewport. Wait for
                // the requested row to actually reach the top before making the partial-row offset.
                await UntilAsync(() => IsRealized(page, anchorKey, identity: true)
                    && Math.Abs(AnchorY(page, anchorKey)) < 3);
                await ScrollProbeAsync(wall.ScrollView!, wall.ScrollView!.VerticalOffset + 17);
                await LayoutAsync(page);
                await UntilAsync(() => IsRealized(page, anchorKey, identity: true)
                    && Math.Abs(AnchorY(page, anchorKey) + 17) < 3);
                var beforeY = AnchorY(page, anchorKey);
                await SaveProbeScreenAsync($"performance-deep-before-{kind}-1280-screen.png");
                page.Release();
                Require(request.AnchorIdentityKey == anchorKey && request.AnchorIndex == 1500
                    && Math.Abs(request.AnchorY - beforeY) < 3,
                    $"页面 Release 未捕获第1500项与局部偏移：index={request.AnchorIndex}, y={request.AnchorY}, expectedY={beforeY}");
                var savedKey = request.AnchorIdentityKey!;
                var savedY = request.AnchorY;
                Require(frame.Navigate(typeof(LibraryPage), LibraryRequest.Search(fixture.Services)), "深返回中间页面导航失败");
                var search = (LibraryPage)frame.Content;
                await LayoutAsync(search);
                search.Release();
                Require(frame.CanGoBack, "真实 Frame 没有返回记录");
                var beforeRead = reads.Count;
                ResizeProbeClient(760, 800);
                frame.GoBack();
                var returned = (LibraryPage)frame.Content;
                await UntilAsync(() => returned.ViewModel.IsReady && returned.ViewModel.RestoredFromCache);
                await LayoutAsync(returned);
                await UntilAsync(() => IsRealized(returned, savedKey, identity: true)
                    && Math.Abs(AnchorY(returned, savedKey) - savedY) < 3);
                Require(returned.ViewModel.WindowStart > 0 && returned.ViewModel.Cards.Count <= 400,
                    "真实深返回未恢复有限非零窗口");
                Require(reads.Skip(beforeRead).All(read => read.Start > 1000), "返回核对串读了前缀");
                var cache = fixture.Services.GetRequiredService<BrowseSnapshotStore>();
                Require(cache.TryGetLibrary(cache.LibraryKeys.Single(), [SnapshotSource.Of(fixture.Session.Capture())], out var snapshot)
                    && snapshot.Items.Count <= 400, "真实返回快照超过 400 项");
                await SaveProbeScreenAsync($"performance-deep-return-{kind}-760-screen.png");

                beforeRead = reads.Count;
                var previousStart = returned.ViewModel.WindowStart;
                await returned.ViewModel.LoadPreviousAsync();
                await LayoutAsync(returned);
                await UntilAsync(() => IsRealized(returned, savedKey, identity: true)
                    && Math.Abs(AnchorY(returned, savedKey) - savedY) < 3);
                Require(returned.ViewModel.WindowStart < previousStart && reads[beforeRead].Start < previousStart,
                    "真实上翻没有向前读取");
                ResizeProbeClient(1280, 800);
                await LayoutAsync(returned);
                await UntilAsync(() => IsRealized(returned, savedKey, identity: true));
                await SaveProbeScreenAsync($"performance-deep-return-{kind}-1280-screen.png");
                var end = returned.ViewModel.WindowEnd;
                beforeRead = reads.Count;
                returned.ViewModel.LoadMore();
                await UntilAsync(() => !returned.ViewModel.Busy && returned.ViewModel.WindowEnd > end);
                // FnNas maps arbitrary ranges to one or two whole server pages before Skip.
                Require(kind == MediaServerKind.FnNas
                        ? reads[beforeRead].Start <= end && end < reads[beforeRead].Start + reads[beforeRead].Limit
                        : reads[beforeRead].Start == end,
                    "真实下翻起点未使用绝对窗口尾部");
                evidence.Add(new { backend = kind.ToString(), savedIndex = request.AnchorIndex, savedY, beforeY, windowStart = returned.ViewModel.WindowStart,
                    windowEnd = returned.ViewModel.WindowEnd, snapshotItems = snapshot.Items.Count,
                    requests = reads.Select(read => new { start = read.Start, limit = read.Limit }).ToArray(), realFrame = true });
                Require(fixture.Actions.Plays == 0, "深返回探针触发了播放动作");

                // Deterministic owner cancellation: old queued restoration clears only its own pending state.
                var query = returned.ViewModel.QueryToken;
                Call(returned, "BeforeWindowChange");
                Call(returned, "RestorePendingAnchor");
                var oldVersion = Get<int>(returned, "_anchorVersion");
                returned.ViewModel.Cancel();
                returned.ViewModel.Resume(false);
                Call(returned, "AnchorContinuationCurrent", oldVersion, Get<int>(returned, "_navigation"), query);
                Require(!Get<bool>(returned, "_restoringAnchor") && Get<object?>(returned, "_pendingAnchor") is null,
                    "旧查询恢复残留标志挡住新分页");
                returned.Release();
                frame.Content = null;
                frame.BackStack.Clear();
            }
            finally
            {
                if (frame.Content is IShellContent content) content.Release();
                frame.Content = null;
                frame.BackStack.Clear();
                _root.Children.Remove(frame);
            }
        }
        await WritePerformanceEvidenceAsync("performance-deep-return.json", new
        { backends = evidence, realNetwork = false, playback = false, serverWrites = false });

        static bool IsRealized(LibraryPage page, string key, bool identity = false)
        {
            var card = page.ViewModel.Cards.FirstOrDefault(card => identity ? card.Item.IdentityKey == key : card.Item.Id == key);
            var scroll = Get<ItemsView>(page, "CardWall").ScrollView;
            return card is not null && scroll is not null && Descendants(Get<ItemsView>(page, "CardWall")).OfType<PosterCard>()
                .Any(poster => ReferenceEquals(poster.Card, card) && poster.ActualHeight > 0
                    && poster.TransformToVisual(scroll).TransformPoint(new Point(0, 0)).Y < scroll.ViewportHeight
                    && poster.TransformToVisual(scroll).TransformPoint(new Point(0, 0)).Y + poster.ActualHeight > 0);
        }
        static double AnchorY(LibraryPage page, string identity)
        {
            var wall = Get<ItemsView>(page, "CardWall");
            var card = Descendants(wall).OfType<PosterCard>().First(poster => poster.Card?.Item.IdentityKey == identity);
            return card.TransformToVisual(wall.ScrollView).TransformPoint(new Point(0, 0)).Y;
        }
    }
}
