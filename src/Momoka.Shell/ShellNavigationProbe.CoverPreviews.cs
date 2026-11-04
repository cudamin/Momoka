using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Shell.Views;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task CoverPreviewLayoutAsync()
    {
        var dimensions = new Dictionary<string, (int Width, int Height)>(StringComparer.Ordinal)
        {
            ["Primary"] = (400, 600),
            ["Logo"] = (600, 160),
            ["Thumb"] = (640, 360),
            ["Banner"] = (1000, 200),
            ["Disc"] = (400, 400),
            ["Art"] = (640, 360),
            ["Backdrop"] = (520, 340)
        };
        var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var pair in dimensions)
            bytes[pair.Key] = await CoverMarkedImageAsync(pair.Value.Width, pair.Value.Height, pair.Key == "Logo");
        var series = new EmbyItem
        {
            Id = "cover-preview-series",
            Name = "离线图片完整预览",
            Type = EmbyItemType.Series,
            ImageTags = new()
            {
                ["Primary"] = "poster",
                ["Logo"] = "logo",
                ["Thumb"] = "thumb",
                ["Banner"] = "banner",
                ["Disc"] = "disc",
                ["Art"] = "art"
            },
            BackdropImageTags = ["backdrop"]
        };
        foreach (var width in new[] { 1280, 760 })
        {
            ResizeProbeClient(width, 800);
            var requests = new List<(string Type, int Width)>();
            var dialog = Cover(series, () => Task.FromResult<EmbyItem?>(series), fetch: (_, type, _, requestedWidth, _) =>
            { requests.Add((type, requestedWidth)); return Task.FromResult<byte[]?>(bytes[type]); });
            var shown = dialog.ShowAsync().AsTask();
            try
            {
                await LayoutAsync(dialog);
                await Get<Task>(dialog, "_initialLoad");
                await LayoutAsync(dialog);
                foreach (var slot in dialog.Groups.Concat(dialog.Backdrops))
                {
                    var tile = Descendants(dialog).OfType<Button>().Single(button => ReferenceEquals(button.Tag, slot)
                        && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "ArtworkTile");
                    await CoverWholeImageAsync(tile, slot, $"cover-preview-series-{slot.ImageType}-{width}.png");
                    await CoverSlotControlsAsync(dialog, slot);
                    Require(requests.Any(request => request.Type == slot.ImageType
                        && request.Width >= (int)Math.Ceiling(slot.PreviewWidth * dialog.XamlRoot.RasterizationScale)),
                        $"{slot.ImageType} {width} 服务器请求分辨率低于实际缩放下的预览框");
                    Require(slot.Picture is { } picture && picture.DecodePixelWidth == slot.PreviewWidth,
                        $"{slot.ImageType} {width} 位图解码没有按实际逻辑预览宽度");
                }
                var scroll = Get<ScrollViewer>(dialog, "ArtworkScroll");
                await ScrollProbeAsync(scroll, 0);
                await LayoutAsync(dialog);
                Require(Math.Abs(scroll.VerticalOffset) < 1, "封面顶部截图必须回到图片滚动容器的零偏移");
                var firstSlot = dialog.Groups.First();
                var firstTile = Descendants(dialog).OfType<Button>().Single(button => ReferenceEquals(button.Tag, firstSlot)
                    && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "ArtworkTile");
                CoverViewportBounds(firstTile, scroll, $"{width} 顶部海报");
                await SaveProbeScreenAsync($"cover-preview-series-all-{width}.png");
                await ScrollProbeAsync(scroll, scroll.ScrollableHeight);
                await LayoutAsync(dialog);
                var backdropSlot = dialog.Backdrops.First();
                var backdropTile = Descendants(dialog).OfType<Button>().Single(button => ReferenceEquals(button.Tag, backdropSlot)
                    && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "ArtworkTile");
                CoverViewportBounds(backdropTile, scroll, $"{width} 背景图");
                await SaveProbeScreenAsync($"cover-preview-series-backdrops-{width}.png");
            }
            finally { dialog.Hide(); await shown; }
        }
        ResizeProbeClient(1280, 800);
        var episode = new EmbyItem
        {
            Id = "cover-preview-episode",
            Name = "单集横向剧照",
            Type = EmbyItemType.Episode,
            ImageTags = new() { ["Primary"] = "still" },
            ParentLogoItemId = "parent",
            ParentLogoImageTag = "parent-logo",
            ParentBackdropItemId = "parent",
            ParentBackdropImageTags = ["parent-backdrop"]
        };
        var still = await CoverMarkedImageAsync(640, 360);
        var episodeRequests = new List<(string ItemId, string Type)>();
        var episodeDialog = Cover(episode, () => Task.FromResult<EmbyItem?>(episode),
            fetch: (id, type, _, _, _) =>
            { episodeRequests.Add((id, type)); return Task.FromResult<byte[]?>(still); },
            remote: (_, _) => Task.FromResult<byte[]?>(still));
        var episodeShown = episodeDialog.ShowAsync().AsTask();
        try
        {
            await LayoutAsync(episodeDialog);
            await Get<Task>(episodeDialog, "_initialLoad");
            await LayoutAsync(episodeDialog);
            var slot = episodeDialog.Groups.First(row => row.ImageType == "Primary");
            var tile = Descendants(episodeDialog).OfType<Button>().Single(button => ReferenceEquals(button.Tag, slot)
                && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "ArtworkTile");
            Require(Math.Abs(tile.ActualWidth / tile.ActualHeight - 16.0 / 9.0) < 0.03,
                $"单集海报框必须为横向16:9，实际{tile.ActualWidth:0.0}×{tile.ActualHeight:0.0}");
            await CoverWholeImageAsync(tile, slot, "cover-preview-episode-primary.png");
            Require(episodeDialog.Groups.Count(row => row.Has) == 1 && episodeDialog.Backdrops.Count == 0,
                "单集图片编辑器不能借用父级徽标或背景图作为可修改图片");
            Require(episodeRequests.All(request => request.ItemId == episode.Id && request.Type == "Primary"),
                "单集图片编辑器不能请求父级图片");
            Require(episodeDialog.Groups.Where(row => row.ImageType != "Primary").All(row => !row.CanDelete),
                "没有本集图片的图种不能启用删除按钮");
            foreach (var episodeSlot in episodeDialog.Groups)
                await CoverSlotControlsAsync(episodeDialog, episodeSlot);
            await ScrollProbeAsync(Get<ScrollViewer>(episodeDialog, "ArtworkScroll"), 0);
            await LayoutAsync(episodeDialog);
            await SaveProbeScreenAsync("cover-preview-episode.png");
            var episodePicker = new CoverPickerDialog(episode, slot.Kind, new RemoteImageResult
            { Images = [new() { Url = "https://fixture.invalid/episode-primary", ProviderName = "横向单集候选" }] }, episodeDialog);
            var panel = (Task<ContentDialogResult>)Call(episodeDialog, "ShowPanelAsync", episodePicker)!;
            try
            {
                var choice = Get<List<CoverChoice>>(episodePicker, "_choices").Single();
                await UntilAsync(() => choice.Picture is not null && !choice.IsLoading);
                await LayoutAsync(episodeDialog);
                var preview = Descendants(episodeDialog).OfType<Border>()
                    .Single(border => border.Name == "CoverCandidatePreview");
                Require(preview.ActualWidth > 100 && preview.ActualHeight > 40
                    && Math.Abs(preview.ActualWidth / preview.ActualHeight - 16.0 / 9.0) < 0.03,
                    $"单集候选海报框必须为横向16:9，实际{preview.ActualWidth:0.0}×{preview.ActualHeight:0.0}");
                Require(Descendants(preview).OfType<Image>().Single().Stretch == Stretch.Uniform,
                    "单集候选图不能裁切16:9画面");
                var candidateGrid = Get<GridView>(episodePicker, "Grid");
                var candidateScroll = Descendants(candidateGrid).OfType<ScrollViewer>().Single();
                CoverViewportBounds(preview, candidateScroll, "单集候选16:9预览");
                var caption = Descendants(episodeDialog).OfType<TextBlock>().Single(text => text.Text == choice.Caption);
                CoverViewportBounds(caption, candidateScroll, "单集候选说明");
                await CoverMarkedEdgesAsync(preview, "单集候选16:9预览");
                await SaveReturnFrameAsync(preview, "cover-preview-episode-candidate-marked.png");
                candidateGrid.SelectedItem = choice;
                await LayoutAsync(episodeDialog);
                Require(episodeDialog.IsPrimaryButtonEnabled && episodeDialog.PrimaryButtonText == "使用这张",
                    "单集横图候选必须能选择且确认按钮可用");
                var useButton = Descendants(episodeDialog).OfType<Button>().Single(button => Equals(button.Content, "使用这张"));
                CoverViewportBounds(useButton, episodeDialog, "单集候选确认按钮");
                await SaveProbeScreenAsync("cover-preview-episode-candidate.png");
            }
            finally
            {
                Get<TaskCompletionSource<ContentDialogResult>>(episodeDialog, "_panelCompletion").TrySetResult(ContentDialogResult.None);
                await panel;
            }
        }
        finally { episodeDialog.Hide(); await episodeShown; ResizeProbeClient(1280, 800); }
    }
    // Four opaque edge bands around a transparent or opaque middle reveal any fit-to-fill clipping.
    private static async Task<byte[]> CoverMarkedImageAsync(int width, int height, bool transparent = false)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                var edge = x < width / 6 ? 1 : x >= width * 5 / 6 ? 2
                    : y < height / 6 ? 3 : y >= height * 5 / 6 ? 4 : 0;
                pixels[offset] = edge == 3 ? (byte)255 : (byte)0;
                pixels[offset + 1] = edge is 2 or 4 ? (byte)255 : (byte)0;
                pixels[offset + 2] = edge is 1 or 4 ? (byte)255 : (byte)0;
                pixels[offset + 3] = edge != 0 || !transparent ? (byte)255 : (byte)0;
            }
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private async Task CoverWholeImageAsync(Button tile, ArtworkSlot slot, string screenshot)
    {
        var image = Descendants(tile).OfType<Image>().Single();
        Require(image.Stretch == Stretch.Uniform && image.Source is not null,
            $"{slot.ImageType} 预览必须完整等比显示实际位图");
        Require(tile.ActualWidth > 100 && tile.ActualHeight > 20, $"{slot.ImageType} 预览没有可见面积");
        ScrollViewer? scroll = null;
        for (var ancestor = VisualTreeHelper.GetParent(tile); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is ScrollViewer candidate) { scroll = candidate; break; }
        if (scroll is not null)
        {
            var point = tile.TransformToVisual(scroll).TransformPoint(default);
            await ScrollProbeAsync(scroll, scroll.VerticalOffset + point.Y - 24);
            await LayoutAsync(tile);
            CoverViewportBounds(tile, scroll, slot.ImageType);
        }
        await CoverMarkedEdgesAsync(tile, slot.ImageType);
        await SaveReturnFrameAsync(tile, screenshot);
    }

    private static async Task CoverMarkedEdgesAsync(FrameworkElement element, string label)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var buffer = await bitmap.GetPixelsAsync();
        var pixels = buffer.ToArray();
        var edges = new int[4];
        for (var at = 0; at + 3 < pixels.Length; at += 4)
        {
            var blue = pixels[at]; var green = pixels[at + 1]; var red = pixels[at + 2];
            if (red > 180 && green < 80 && blue < 80) edges[0]++;
            if (green > 180 && red < 80 && blue < 80) edges[1]++;
            if (blue > 180 && red < 80 && green < 80) edges[2]++;
            if (red > 180 && green > 180 && blue < 80) edges[3]++;
        }
        Require(edges.All(count => count >= 9), $"{label} 实际渲染丢失图片边缘：{string.Join(",", edges)}");
    }

    private async Task CoverSlotControlsAsync(CoverDialog dialog, ArtworkSlot slot)
    {
        var tile = Descendants(dialog).OfType<Button>().Single(button => ReferenceEquals(button.Tag, slot)
            && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "ArtworkTile");
        var stack = (StackPanel)VisualTreeHelper.GetParent(tile);
        var scroll = Get<ScrollViewer>(dialog, "ArtworkScroll");
        var point = stack.TransformToVisual(scroll).TransformPoint(default);
        await ScrollProbeAsync(scroll, scroll.VerticalOffset + point.Y - 12);
        await LayoutAsync(dialog);
        var buttons = Descendants(stack).OfType<Button>().Where(button => button.Visibility == Visibility.Visible).ToArray();
        Require(buttons.Length >= (slot.Kind.Many ? 3 : 4), $"{slot.Caption} 必须保留完整预览和操作按钮");
        foreach (var button in buttons)
        {
            var action = Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button);
            CoverViewportBounds(button, stack, $"{slot.Caption} {action} 在图片项内");
            CoverViewportBounds(button, scroll, $"{slot.Caption} {action} 在滚动视口内");
        }
        var caption = Descendants(stack).OfType<TextBlock>().Single(text => text.Text == slot.Caption);
        CoverViewportBounds(caption, stack, $"{slot.Caption} 标题在图片项内");
        CoverViewportBounds(caption, scroll, $"{slot.Caption} 标题在滚动视口内");
    }

    private static void CoverViewportBounds(FrameworkElement element, FrameworkElement viewport, string label)
    {
        var point = element.TransformToVisual(viewport).TransformPoint(default);
        Require(element.Visibility == Visibility.Visible && element.ActualWidth > 20 && element.ActualHeight > 10
            && point.X >= -1 && point.Y >= -1
            && point.X + element.ActualWidth <= viewport.ActualWidth + 1
            && point.Y + element.ActualHeight <= viewport.ActualHeight + 1,
            $"{label} 必须完整位于实际视口：({point.X:0.0},{point.Y:0.0}) "
            + $"{element.ActualWidth:0.0}×{element.ActualHeight:0.0} / {viewport.ActualWidth:0.0}×{viewport.ActualHeight:0.0}");
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null && !ReferenceEquals(parent, viewport);
            parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollViewer) break;
            if (parent is not FrameworkElement ancestor || ancestor.ActualWidth <= 0 || ancestor.ActualHeight <= 0) continue;
            var local = element.TransformToVisual(ancestor).TransformPoint(default);
            Require(local.X >= -1 && local.Y >= -1 && local.X + element.ActualWidth <= ancestor.ActualWidth + 1
                && local.Y + element.ActualHeight <= ancestor.ActualHeight + 1,
                $"{label} 被中间容器 {ancestor.GetType().Name} 裁切：({local.X:0.0},{local.Y:0.0}) "
                + $"{element.ActualWidth:0.0}×{element.ActualHeight:0.0} / {ancestor.ActualWidth:0.0}×{ancestor.ActualHeight:0.0}");
        }
    }

    private async Task CoverCurrentFailureAsync()
    {
        ResizeProbeClient(1280, 800);
        var image = await CoverMarkedImageAsync(400, 600);
        var canLoad = false;
        var item = new EmbyItem
        {
            Id = "current-preview-failure",
            Name = "离线当前图片失败恢复",
            ImageTags = new() { ["Primary"] = "unchanged-tag" }
        };
        var requests = 0;
        var reloads = 0;
        var dialog = Cover(item, () => { reloads++; return Task.FromResult<EmbyItem?>(item); }, fetch: (_, _, _, _, _) =>
        { requests++; return Task.FromResult<byte[]?>(canLoad ? image : [1, 2, 3]); });
        var shown = dialog.ShowAsync().AsTask();
        try
        {
            await LayoutAsync(dialog);
            await Get<Task>(dialog, "_initialLoad");
            await LayoutAsync(dialog);
            var slot = dialog.Groups.First(row => row.ImageType == "Primary");
            Require(slot.Has && slot.Picture is null && slot.PreviewFailed && !slot.IsLoading,
                "当前图片加载失败不能冒充没有图片，也不能一直显示加载中");
            var tile = Descendants(dialog).OfType<Button>().Single(button => ReferenceEquals(button.Tag, slot)
                && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "ArtworkTile");
            Require(Descendants(tile).OfType<TextBlock>().Any(text => text.Text == slot.PreviewStatus
                && text.Visibility == Visibility.Visible && text.ActualWidth > 0),
                "当前图片的预览加载失败文字必须实际显示");
            var artworkScroll = Get<ScrollViewer>(dialog, "ArtworkScroll");
            await ScrollProbeAsync(artworkScroll, 0);
            await LayoutAsync(dialog);
            CoverViewportBounds(tile, artworkScroll, "当前图片失败");
            await SaveProbeScreenAsync("cover-current-preview-failed.png");
            var retry = Descendants(dialog).OfType<Button>().Single(button => ReferenceEquals(button.Tag, slot)
                && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "RetryCurrentArtworkPreview");
            await CoverSlotControlsAsync(dialog, slot);
            CoverViewportBounds(retry, artworkScroll, "当前图片单图重试按钮");
            Require(retry.IsEnabled, "当前图片单图重试按钮必须启用");
            canLoad = true;
            Call(dialog, "OnRetryPreview", retry, new RoutedEventArgs());
            await UntilAsync(() => !Get<bool>(dialog, "_busy") && slot.Picture is not null);
            Require(requests == 2 && reloads == 0 && !slot.PreviewFailed && !dialog.Touched && !dialog.NeedsRefresh,
                "单图预览重试只能读取图片，不能重载条目或写入服务器");
            await LayoutAsync(dialog);
            await SaveProbeScreenAsync("cover-current-preview-retried.png");
            slot.Retag("fresh-tag");
            item.ImageTags["Primary"] = "fresh-tag";
            canLoad = false;
            Call(dialog, "OnRefreshArtwork", Get<Button>(dialog, "RefreshArtworkButton"), new RoutedEventArgs());
            await UntilAsync(() => !Get<bool>(dialog, "_busy") && slot.PreviewFailed && slot.Picture is null);
            canLoad = true;
            Call(dialog, "OnRefreshArtwork", Get<Button>(dialog, "RefreshArtworkButton"), new RoutedEventArgs());
            await UntilAsync(() => !Get<bool>(dialog, "_busy") && slot.Picture is not null);
            Require(requests == 4 && reloads == 2 && !slot.PreviewFailed && slot.StatusVisibility == Visibility.Collapsed,
                "刷新必须在标签未变时重新读取失败图片并清除失败状态");
            Require(!dialog.Touched && !dialog.NeedsRefresh, "只读预览刷新不能被记成服务器图片修改");
            await CoverWholeImageAsync(tile, slot, "cover-current-preview-recovered.png");
            await LayoutAsync(dialog);
            await SaveProbeScreenAsync("cover-current-preview-recovered-screen.png");
        }
        finally { dialog.Hide(); await shown; }
    }

    private async Task CoverCandidateLimitAsync()
    {
        ResizeProbeClient(1280, 800);
        var image = await CoverMarkedImageAsync(120, 180);
        var release = Pending<bool>();
        var active = 0;
        var maximum = 0;
        var calls = new List<string>();
        var item = new EmbyItem { Id = "candidate-list", Name = "离线完整候选列表" };
        var dialog = new CoverDialog(item, (_, _) => Task.FromResult(new RemoteImageResult()),
            (_, _, _, _, _) => Task.FromResult<byte[]?>(null), (_, _, _, _) => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask, (_, _, _) => Task.CompletedTask,
            _ => Task.FromResult<EmbyItem?>(item), async (address, token) =>
            {
                calls.Add(address);
                active++;
                maximum = Math.Max(maximum, active);
                try { await release.Task.WaitAsync(token); return image; }
                finally { active--; }
            })
        { XamlRoot = _root.XamlRoot };
        var shown = dialog.ShowAsync().AsTask();
        try
        {
            await LayoutAsync(dialog);
            var images = Enumerable.Range(0, 60).Select(index => new RemoteImageInfo
            { Url = $"https://fixture.invalid/image/{index}", ProviderName = $"候选 {index + 1}" }).ToList();
            images.Insert(4, new RemoteImageInfo { Url = "", ProviderName = "无地址" });
            images.Insert(18, new RemoteImageInfo { Url = " ", ProviderName = "空白地址" });
            var picker = new CoverPickerDialog(item, ItemArtwork.SingleKinds.First(), new RemoteImageResult
            { Images = images, TotalRecordCount = 60 }, dialog);
            var panel = (Task<ContentDialogResult>)Call(dialog, "ShowPanelAsync", picker)!;
            try
            {
                var choices = Get<List<CoverChoice>>(picker, "_choices");
                Require(choices.Count == 60 && choices.All(choice => !string.IsNullOrWhiteSpace(choice.Info.Url)),
                    "完整候选列表不能截成24张，空URL必须过滤");
                await UntilAsync(() => calls.Count == 4);
                await SettleAsync();
                Require(active == 4 && maximum == 4 && calls.Count == 4,
                    "候选预览并发必须为4，未释放前不能启动第五个读取");
                release.TrySetResult(true);
                await UntilAsync(() => choices.All(choice => choice.Picture is not null && !choice.IsLoading));
                Require(calls.Count == 60 && maximum == 4 && active == 0,
                    "全部60张候选必须完成一次读取，最大并发不能超过4");
                var grid = Get<GridView>(picker, "Grid");
                grid.SelectedItem = choices[^1];
                Require(picker.Picked?.Url == "https://fixture.invalid/image/59",
                    "第60张候选必须可以被选择");
                await LayoutAsync(dialog);
                var tailScroll = Descendants(grid).OfType<ScrollViewer>().Single();
                await ScrollProbeAsync(tailScroll, tailScroll.ScrollableHeight);
                await LayoutAsync(dialog);
                await SaveProbeScreenAsync("cover-candidates-sixty-tail.png");
            }
            finally
            {
                release.TrySetResult(true);
                if (!panel.IsCompleted)
                    Get<TaskCompletionSource<ContentDialogResult>>(dialog, "_panelCompletion").TrySetResult(ContentDialogResult.None);
                await panel;
            }
        }
        finally { release.TrySetResult(true); dialog.Hide(); await shown; }
    }
    private async Task CoverRemotePreviewAsync()
    {
        var image = await CoverMarkedImageAsync(400, 600);
        var slow = Pending<byte[]?>();
        var cancelled = Pending<byte[]?>();
        var calls = new List<string>();
        var tokens = new List<CancellationToken>();
        var allowRetry = false;
        var item = new EmbyItem { Id = "remote-preview", Name = "离线候选图" };
        var dialog = new CoverDialog(item, (_, _) => Task.FromResult(new RemoteImageResult()),
            (_, _, _, _, _) => Task.FromResult<byte[]?>(null), (_, _, _, _) => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask, (_, _, _) => Task.CompletedTask,
            _ => Task.FromResult<EmbyItem?>(item), (address, token) =>
            {
                calls.Add(address); tokens.Add(token);
                return address switch
                {
                    "https://fixture.invalid/slow" => slow.Task,
                    "https://fixture.invalid/cancelled" => cancelled.Task,
                    "https://fixture.invalid/bad-thumbnail" => Task.FromResult<byte[]?>([1, 2, 3]),
                    "https://fixture.invalid/retry" when !allowRetry => Task.FromResult<byte[]?>([1, 2, 3]),
                    _ => Task.FromResult<byte[]?>(image)
                };
            })
        { XamlRoot = _root.XamlRoot };
        var shown = dialog.ShowAsync().AsTask();
        try
        {
            await LayoutAsync(dialog);
            var found = new RemoteImageResult
            {
                Providers = ["离线源"],
                Images =
            [
                new() { Url = "https://fixture.invalid/slow", ProviderName = "慢首张" },
                new() { Url = "https://fixture.invalid/fast", ProviderName = "快第二张" },
                new() { Url = "https://fixture.invalid/fallback", ThumbnailUrl = "https://fixture.invalid/bad-thumbnail", ProviderName = "缩略失败回原图" },
                new() { Url = "https://fixture.invalid/retry", ProviderName = "失败后重试" }
            ]
            };
            var picker = new CoverPickerDialog(item, ItemArtwork.SingleKinds.First(), found, dialog);
            var panel = (Task<ContentDialogResult>)Call(dialog, "ShowPanelAsync", picker)!;
            try
            {
                var choices = Get<List<CoverChoice>>(picker, "_choices");
                await UntilAsync(() => choices[1].Picture is not null && choices[2].Picture is not null);
                Require(choices[0].Picture is null && !slow.Task.IsCompleted,
                    "慢首张不应阻塞其他候选图显示");
                Require(calls.Contains("https://fixture.invalid/bad-thumbnail") && calls.Contains("https://fixture.invalid/fallback"),
                    "无效缩略图必须回退真实原图地址");
                await UntilAsync(() => choices[3].PreviewFailed && !choices[3].IsLoading);
                Require(choices[3].PreviewFailed && !string.IsNullOrWhiteSpace(choices[3].PreviewStatus),
                    "失败候选必须留下可见失败状态");
                await LayoutAsync(dialog);
                var grid = Get<GridView>(picker, "Grid");
                grid.SelectedItem = choices[1];
                await LayoutAsync(dialog);
                Require(dialog.PrimaryButtonText == "使用这张" && dialog.IsPrimaryButtonEnabled,
                    "候选选择后必须出现可用的明确确认按钮");
                Require(Descendants(dialog).OfType<Button>().Any(button => Equals(button.Content, "使用这张")
                    && button.Visibility == Visibility.Visible && button.ActualWidth > 20 && button.IsEnabled),
                    "候选确认按钮没有进入实际布局");
                var status = Descendants(dialog).OfType<TextBlock>().FirstOrDefault(text => text.Text == choices[3].PreviewStatus);
                Require(status is { Visibility: Visibility.Visible } && status.ActualWidth > 0,
                    "失败状态文字没有显示到实际候选卡片");
                var retryButton = Descendants(dialog).OfType<Button>().Single(button => ReferenceEquals(button.Tag, choices[3])
                    && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) == "RetryArtworkPreview");
                var gridScroll = Descendants(grid).OfType<ScrollViewer>().Single();
                var retryPoint = retryButton.TransformToVisual(gridScroll).TransformPoint(default);
                await ScrollProbeAsync(gridScroll, gridScroll.VerticalOffset + retryPoint.Y + retryButton.ActualHeight
                    - gridScroll.ActualHeight + 20);
                await LayoutAsync(dialog);
                Require(retryButton.IsEnabled, "失败候选必须立即启用单图重试按钮");
                CoverViewportBounds(retryButton, gridScroll, "候选单图重试按钮");
                CoverViewportBounds(retryButton, dialog, "候选单图重试按钮在弹窗内");
                await SaveReturnFrameAsync((FrameworkElement)VisualTreeHelper.GetParent(retryButton), "cover-candidate-retry-card.png");
                await LayoutAsync(dialog);
                await SaveProbeScreenAsync("cover-candidates-failed-and-confirm.png");
                allowRetry = true;
                Call(picker, "OnRetry", retryButton, new RoutedEventArgs());
                await UntilAsync(() => choices[3].Picture is not null && !choices[3].PreviewFailed);
                Require(!slow.Task.IsCompleted && choices[0].Picture is null,
                    "单图重试必须在另一个候选仍挂起时恢复");
                Require(calls.Count(address => address == "https://fixture.invalid/slow") == 1,
                    "单图重试不能重复请求另一个未完成候选");
                Require(calls.Count(address => address == "https://fixture.invalid/retry") == 2,
                    "重试必须重新加载失败候选一次");
                Require(calls.Count(address => address == "https://fixture.invalid/fast") == 1,
                    "重试不能重复下载已经成功的候选");
                slow.TrySetResult(image);
                await UntilAsync(() => choices.All(choice => !choice.IsLoading));
                Get<TaskCompletionSource<ContentDialogResult>>(dialog, "_panelCompletion").TrySetResult(ContentDialogResult.None);
                Require(await panel == ContentDialogResult.None, "取消候选不能自动选择或上传");
                Require(tokens.Any(token => token.IsCancellationRequested), "关闭候选面板必须取消预览读取令牌");
            }
            finally
            {
                slow.TrySetResult(image);
                if (!panel.IsCompleted)
                    Get<TaskCompletionSource<ContentDialogResult>>(dialog, "_panelCompletion").TrySetResult(ContentDialogResult.None);
                await panel;
            }
            var cancelPicker = new CoverPickerDialog(item, ItemArtwork.SingleKinds.First(), new RemoteImageResult
            {
                Images = [new() { Url = "https://fixture.invalid/cancelled", ProviderName = "取消晚到" }]
            }, dialog);
            var cancelPanel = (Task<ContentDialogResult>)Call(dialog, "ShowPanelAsync", cancelPicker)!;
            try
            {
                var cancelChoice = Get<List<CoverChoice>>(cancelPicker, "_choices").Single();
                await UntilAsync(() => calls.Contains("https://fixture.invalid/cancelled"));
                Get<TaskCompletionSource<ContentDialogResult>>(dialog, "_panelCompletion").TrySetResult(ContentDialogResult.None);
                Require(await cancelPanel == ContentDialogResult.None, "关闭候选必须保留取消结果");
                Require(tokens[^1].IsCancellationRequested, "未完成预览请求没有收到取消令牌");
                cancelled.TrySetResult(image);
                await SettleAsync();
                Require(cancelChoice.Picture is null, "取消后晚到的位图不能回填已关闭候选");
            }
            finally
            {
                cancelled.TrySetResult(image);
                if (!cancelPanel.IsCompleted)
                    Get<TaskCompletionSource<ContentDialogResult>>(dialog, "_panelCompletion").TrySetResult(ContentDialogResult.None);
                await cancelPanel;
            }
        }
        finally { dialog.Hide(); await shown; }
    }
}
