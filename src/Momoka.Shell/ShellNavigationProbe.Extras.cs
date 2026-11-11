using System.Net;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.ViewModels;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>真实详情与菜单，HTTP和播放只到匿名内存边界，不启动内核或真实媒体请求。</summary>
    private async Task DetailExtrasAsync()
    {
        var receipts = new List<object>();
        var poster = await FixtureImageAsync(400, 600);
        var backdrop = await FixtureImageAsync(1600, 900);
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            using var fixture = CreateFixture();
            fixture.Settings.Ui.EnableImmersive = true;
            var movie = new EmbyItem
            {
                Id = "movie", Name = "花絮与特典 · 离线测试电影", Type = EmbyItemType.Movie, MediaType = "Video",
                Overview = "查看幕后制作、访谈与删减片段，每段内容使用自己的媒体，从头开始播放。",
                SpecialFeatureCount = kind == MediaServerKind.Jellyfin ? 2 : 0,
                UserData = new() { PlaybackPositionTicks = 450_000_000 },
                ImageTags = new() { ["Primary"] = "fixture" }, BackdropImageTags = ["fixture"],
                MediaSources = [new() { Id = "movie-file", Container = "mkv", RunTimeTicks = 900_000_000 }]
            };
            var first = Extra("extra-first", "幕后制作", "BehindTheScenes");
            var second = Extra("extra-second", "删减片段", "DeletedScene");
            var extras = new List<EmbyItem>
            {
                first, second, first, movie,
                new() { Id = "audio", Type = EmbyItemType.Audio },
                new() { Id = "virtual", Type = EmbyItemType.Video, LocationType = "Virtual" },
                new() { Id = "offline", Type = EmbyItemType.Video, LocationType = "Offline" },
                new() { Id = "denied", Type = EmbyItemType.Video, PlayAccess = "None" },
                new() { Id = "foreign", Type = EmbyItemType.Video, ServerId = "other-server" },
                new() { Id = "bad/path", Type = EmbyItemType.Video }
            };
            var paths = new List<string>();
            var fail = false;
            TaskCompletionSource<HttpResponseMessage>? held = null;
            fixture.Actions.CapturePlay = (_, _) => { };
            fixture.Transport.Reply = (request, _) =>
            {
                Require(request.Method == HttpMethod.Get, "花絮探针拒绝服务器写入");
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/SpecialFeatures", StringComparison.Ordinal))
                {
                    Require(path.EndsWith(kind == MediaServerKind.Emby
                        ? "/Users/user/Items/movie/SpecialFeatures" : "/Items/movie/SpecialFeatures", StringComparison.Ordinal),
                        "花絮请求没有使用所属服务器的正式路由");
                    Require(kind == MediaServerKind.Emby ? Query(request, "Fields") == EmbyFields.Files
                        : Query(request, "UserId") == "user", "花絮请求参数不正确");
                    paths.Add(request.RequestUri.PathAndQuery);
                    return held?.Task ?? Task.FromResult(fail
                        ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json(extras));
                }
                if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                    return Task.FromResult(Json(new
                    {
                        ProductName = kind.ToString(), ServerName = "匿名附加内容",
                        Version = kind == MediaServerKind.Emby ? "4.10.1.0" : "10.10.7", Id = "extras-server"
                    }));
                if (path.EndsWith("/Users/Me", StringComparison.Ordinal) || path.EndsWith("/Users/user", StringComparison.Ordinal))
                    return Task.FromResult(Json(new { Id = "user", Policy = new { IsAdministrator = false } }));
                if (path.Contains("/Images/", StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new ByteArrayContent(path.Contains("/Primary", StringComparison.Ordinal) ? poster : backdrop) });
                if (path.EndsWith("/Items/movie", StringComparison.Ordinal)) return Task.FromResult(Json(movie));
                if (path.EndsWith("/MediaSegments/movie", StringComparison.Ordinal))
                    return Task.FromResult(Json(new { Items = Array.Empty<object>() }));
                if (path.EndsWith("/Similar", StringComparison.Ordinal)) return Task.FromResult(Items(0, 0, "similar"));
                return Task.FromResult(FakeTransport.Standard(request));
            };
            await fixture.SelectAsync(kind: kind);
            var host = new Grid { Margin = new Thickness(0, 68, 0, 0) };
            var frame = new Frame();
            host.Children.Add(frame);
            _root.Children.Add(host);
            DetailPage? page = null;
            try
            {
                frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, movie));
                page = (DetailPage)frame.Content;
                page.AttachWindow(_window!);
                await UntilAsync(() => page.IsReady && page.ViewModel.StillImage is not null);
                foreach (var width in new[] { 1280, 760 })
                {
                    await ResizeResponsiveAsync(page, width, 900);
                    await LayoutAsync(page);
                    var button = Get<Button>(page, width < 820 ? "CompactExtrasButton" : "ColumnExtrasButton");
                    Require(button.Visibility == Visibility.Visible && button.ActualWidth > 0 && button.ActualHeight > 0,
                        $"{kind}/{width}：花絮入口没有实际布局");
                    await SaveProbeScreenAsync($"detail-extras-{kind}-{width}-screen.png");
                    var plays = fixture.Actions.Plays;
                    new ButtonAutomationPeer(button).Invoke();
                    await UntilAsync(() => Get<MenuFlyout?>(page, "_extraMenu") is { IsOpen: true } menu
                        && menu.Items.OfType<MenuFlyoutItem>().Count() == 2);
                    var menu = Get<MenuFlyout>(page, "_extraMenu");
                    var options = menu.Items.OfType<MenuFlyoutItem>().ToArray();
                    Require(options[0].Text == "幕后制作 · 幕后花絮" && options[1].Text == "删减片段 · 删减片段",
                        "过滤后未保留两个可播放媒体及其分类");
                    Require(options.All(row => !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(row))),
                        "花絮选项缺少自动化句柄");
                    await SettleAsync();
                    await SaveProbeScreenAsync($"detail-extras-menu-{kind}-{width}-screen.png");
                    new MenuFlyoutItemAutomationPeer(options[0]).Invoke();
                    await UntilAsync(() => fixture.Actions.Plays == plays + 1);
                    Require(fixture.Actions.LastPlayback is { } call && call.Item.Id == "extra-first"
                        && call.Parent is null && call.Episodes is null && call.Choice is { StartTicks: 0, ExactStart: true, Source: null }
                        && call.Item.Source is { IsAvailable: true } source
                        && source.Key == fixture.Session.Capture().Key
                        && page.ViewModel.PlayTarget?.Id == "movie" && movie.ResumeTicks == 450_000_000,
                        "真实菜单没有把独立媒体与零起点交到假播放边界");
                    menu.Hide();
                    receipts.Add(new { Kind = kind.ToString(), Width = width, Options = options.Select(row => row.Text).ToArray(),
                        StartTicks = 0, ExactStart = true, ParentPassed = false, QueuePassed = false, RealNetwork = 0, RealPlayback = 0 });
                }

                var narrow = Get<Button>(page, "CompactExtrasButton");
                fail = true;
                await page.ShowExtrasAsync(narrow);
                var failed = Get<MenuFlyout>(page, "_extraMenu");
                var retry = failed.Items.OfType<MenuFlyoutItem>().Single(row => AutomationProperties.GetAutomationId(row) == "ExtraRetry");
                Require(page.ViewModel.ExtraVisibility == Visibility.Visible
                    && failed.Items.OfType<MenuFlyoutItem>().Any(row => row.Text.StartsWith("读取花絮与特典失败：", StringComparison.Ordinal)),
                    "读取错误被当成空态或失去重试入口");
                await SaveProbeScreenAsync($"detail-extras-failed-{kind}-screen.png");
                fail = false;
                new MenuFlyoutItemAutomationPeer(retry).Invoke();
                await UntilAsync(() => Get<MenuFlyout?>(page, "_extraMenu") is { IsOpen: true } restored
                    && !ReferenceEquals(restored, failed) && restored.Items.OfType<MenuFlyoutItem>().Count() == 2);
                Get<MenuFlyout>(page, "_extraMenu").Hide();

                extras.Clear();
                await page.ShowExtrasAsync(narrow);
                await LayoutAsync(page);
                Require(page.ViewModel.ExtraVisibility == Visibility.Collapsed && Get<MenuFlyout?>(page, "_extraMenu") is null,
                    "真实空结果没有隐藏按钮并关闭菜单");
                var reads = paths.Count;
                Require(await page.ViewModel.GetExtrasAsync() is null && paths.Count == reads, "空态重复发送无效读取");
                await SaveProbeScreenAsync($"detail-extras-empty-{kind}-screen.png");
                extras.Add(first);
                movie.MediaType = kind == MediaServerKind.Emby ? "Audio" : "Video";
                movie.SpecialFeatureCount = 0;
                await page.ViewModel.ReloadAsync();
                Require(page.ViewModel.ExtraVisibility == Visibility.Collapsed
                    && await page.ViewModel.GetExtrasAsync() is null && paths.Count == reads, "失去前提后仍读取花絮");
                movie.MediaType = "Video";
                movie.SpecialFeatureCount = kind == MediaServerKind.Jellyfin ? 2 : 0;
                await page.ViewModel.ReloadAsync();
                await LayoutAsync(page);
                Require(narrow.Visibility == Visibility.Visible, "前提与内容恢复后入口没有恢复");
                await page.ShowExtrasAsync(narrow);
                var stale = Get<DetailViewModel.ExtraSelection>(page.ViewModel, "_extraSelection");
                var oldOption = stale.Options.Single();
                var capturedMenu = Get<MenuFlyout>(page, "_extraMenu");
                var beforeCancel = fixture.Actions.Plays;
                capturedMenu.Hide();
                Require(fixture.Actions.Plays == beforeCancel, "关闭菜单执行了播放");
                await page.ViewModel.ReloadAsync();
                await page.ViewModel.OpenExtraAsync(stale, oldOption);
                Require(fixture.Actions.Plays == beforeCancel, "重新读取后旧菜单仍能播放");
                held = Pending<HttpResponseMessage>();
                reads = paths.Count;
                var pending = page.ShowExtrasAsync(narrow);
                await UntilAsync(() => paths.Count > reads);
                var loading = Get<MenuFlyout>(page, "_extraMenu");
                Require(loading.Items.OfType<MenuFlyoutItem>().Single().Text == "正在读取花絮与特典…", "在飞读取缺少进度");
                page.Release();
                held.SetResult(Json(extras));
                await pending;
                Require(!loading.IsOpen && page.ViewModel.ExtraVisibility == Visibility.Collapsed
                    && fixture.Actions.Plays == beforeCancel, "离页后迟到响应恢复菜单或请求播放");
                var rejected = false;
                try { Require(page.ViewModel.ExtraVisibility == Visibility.Visible, "负向对照：离页入口不可显示"); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "花絮探针断言没有拒绝反例");
                receipts.Add(new { Kind = kind.ToString(), Paths = paths, EmptyHidden = true, Restored = true,
                    RetryRecovered = true, StaleRejected = true, LateRejected = true, PlaybackCalls = fixture.Actions.Plays,
                    RealNetwork = 0, RealPlayback = 0 });
            }
            finally
            {
                held?.TrySetResult(Json(extras));
                page?.Release();
                frame.Content = null;
                _root.Children.Remove(host);
            }
        }
        await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "detail-extras-receipt.json"),
            JsonSerializer.Serialize(receipts, new JsonSerializerOptions { WriteIndented = true }));

        static EmbyItem Extra(string id, string name, string category) => new()
        {
            Id = id, Name = name, Type = EmbyItemType.Video, MediaType = "Video", ExtraType = category,
            UserData = new() { PlaybackPositionTicks = 300_000_000 },
            MediaSources = [new() { Id = id + "-file", Container = "mkv", RunTimeTicks = 600_000_000 }]
        };
    }
}
