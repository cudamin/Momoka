using System.Net;
using System.Text.Json;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task MoviePilotMediaCardsAsync()
    {
        var theme = ThemeHost.Current;
        var topMost = _window!.TopMost;
        _window.TopMost = true;
        try
        {
            foreach (var fault in new[] { "", "partial", "unknown" })
            {
                var transport = new MediaCardsProbeServer { Fault = fault };
                using var fixture = CreateFixture(transport);
                await fixture.SelectAsync();
                await ConnectMoviePilotAsync(fixture);
                var service = (MoviePilotService)fixture.Services.GetService(typeof(MoviePilotService))!;
                var subject = new EmbyItem
                {
                    Id = "cards-series",
                    Name = MediaCardsProbeServer.Title,
                    Type = EmbyItemType.Series,
                    ProviderIds = new Dictionary<string, string> { ["Tmdb"] = "1" }
                };
                fixture.Transport.Reply = (request, _) =>
                {
                    var path = request.RequestUri!.AbsolutePath;
                    if (path.EndsWith("/Items/cards-series", StringComparison.Ordinal)) return Task.FromResult(Json(subject));
                    if (path.EndsWith("/Items", StringComparison.Ordinal) && Query(request, "ParentId") == "cards-series")
                        return Task.FromResult(Json(new
                        {
                            Items = Enumerable.Range(1, 3).Select(id => new EmbyItem
                            {
                                Id = "card-episode-" + id,
                                Name = "第 " + id + " 集",
                                Type = EmbyItemType.Episode,
                                SeriesId = subject.Id,
                                SeriesName = subject.Name,
                                IndexNumber = id,
                                ParentIndexNumber = 1,
                                Path = MediaCardsProbeServer.Destination(id)
                            }).ToArray(),
                            TotalRecordCount = 3
                        }));
                    return Task.FromResult(FakeTransport.Standard(request));
                };
                ResizeInspect(1280, 900);
                var dialog = new MoviePilotMediaManagementDialog(service, fixture.Session.Capture(), subject,
                    MoviePilotHistoryAction.DeleteAll)
                { XamlRoot = _root.XamlRoot };
                var showing = dialog.ShowAsync().AsTask();
                try
                {
                    await UntilAsync(() => dialog.IsPrimaryButtonEnabled);
                    Require(dialog.Records.Count == 3 && dialog.Records.All(row => row.Media.Selected), "剧卡片未默认全选");
                    Require(Get<CheckBox>(dialog, "SelectAllRecords").IsChecked == true && transport.Ids.Count == 0, "全选状态错误或打开就删除");
                    var poster = await PosterLoader.DecodeAsync(await FixtureImageAsync(400, 600), 48);
                    foreach (var row in dialog.Records) row.Media.Artwork = poster;
                    await LayoutAsync(dialog);
                    if (fault.Length == 0)
                    {
                        foreach (var palette in Momoka.Theming.UiThemes.All)
                        {
                            ThemeHost.Apply(palette);
                            await LayoutAsync(dialog);
                            AssertCards(dialog);
                            await SaveProbeScreenAsync($"mp-media-cards-{palette.Id}-1280-screen.png");
                        }
                        ThemeHost.Apply(theme);
                        foreach (var width in new[] { 760, 420 })
                        {
                            ResizeInspect(width, 900);
                            await LayoutAsync(dialog);
                            AssertCards(dialog);
                            await SaveProbeScreenAsync($"mp-media-cards-{width}-screen.png");
                        }
                        ResizeInspect(1280, 900);
                        await LayoutAsync(dialog);
                        var check = Descendants(dialog).OfType<CheckBox>().First(control => control.Name == "SelectRecord");
                        ((IToggleProvider)FrameworkElementAutomationPeer.CreatePeerForElement(check).GetPattern(PatternInterface.Toggle)).Toggle();
                        Require(dialog.Records.Count(row => row.Media.Selected) == 2 &&
                            Get<CheckBox>(dialog, "SelectAllRecords").IsChecked is null, "取消个别记录没有更新选择");
                    }
                    var expected = dialog.Records.Where(row => row.Media.Selected).Select(row => row.Id).ToArray();
                    InvokeMediaCardDialog(dialog, "PrimaryButton");
                    await UntilAsync(() => dialog.PrimaryButtonText.Length == 0);
                    await LayoutAsync(dialog);
                    Require(dialog.Records.Select(row => row.Id).SequenceEqual(expected), "结果列表丢失选中记录或包含未选择记录");
                    Require(dialog.Records.All(row => !row.Selectable), "提交后仍可改变确认范围");
                    if (fault.Length == 0)
                    {
                        Require(transport.Ids.SequenceEqual(expected) && dialog.Records.All(row => row.Complete), "未按所选范围删除或成功结果错误");
                        Require(!Get<InfoBar>(dialog, "Notice").IsOpen, "成功结果仍占用大块通知区");
                    }
                    else
                    {
                        Require(transport.Ids.Count == 1 && dialog.Records.Count == 3 && dialog.Records.All(row => row.NeedsReview), "部分结果丢记录或自动续删");
                        Require(dialog.Records.Skip(1).All(row => row.Message.Contains("未取得回执", StringComparison.Ordinal)), "未取得回执的行被当成成功");
                    }
                    await SaveProbeScreenAsync($"mp-media-cards-result-{(fault.Length == 0 ? "success" : fault)}-screen.png");
                    InvokeMediaCardDialog(dialog, "CloseButton");
                    await showing;
                }
                finally { dialog.Hide(); await showing; }
            }
        }
        finally { ThemeHost.Apply(theme); _window.TopMost = topMost; }

        void AssertCards(ContentDialog dialog)
        {
            var list = Get<ListView>(dialog, "RecordList");
            Require(list.ActualWidth > 100 && list.ActualHeight > 40, "卡片清单没有可用空间");
            var card = Descendants(dialog).OfType<Grid>().First(grid => grid.Name == "RecordCard");
            var paths = (FrameworkElement)card.FindName("CardPaths");
            var facts = (FrameworkElement)card.FindName("CardFacts");
            var pathBounds = paths.TransformToVisual(card).TransformBounds(new Windows.Foundation.Rect(0, 0, paths.ActualWidth, paths.ActualHeight));
            var factBounds = facts.TransformToVisual(card).TransformBounds(new Windows.Foundation.Rect(0, 0, facts.ActualWidth, facts.ActualHeight));
            Require(pathBounds.Right <= card.ActualWidth + 1 && factBounds.Right <= card.ActualWidth + 1, "路径或状态越过卡片边界");
            Require(pathBounds.Bottom <= factBounds.Top + 1 || pathBounds.Right <= factBounds.Left + 1, "路径与状态重叠");
            var text = Descendants(card).OfType<TextBlock>().First(value => value.Name == "SourceFullPath");
            Require(text.IsTextSelectionEnabled && text.Text.Contains("S01E", StringComparison.Ordinal), "文件名不可选择或绑定失效");
        }
    }

    private static void InvokeMediaCardDialog(ContentDialog dialog, string name)
    {
        var text = name == "PrimaryButton" ? dialog.PrimaryButtonText : dialog.CloseButtonText;
        var button = Descendants(dialog).OfType<Button>().Single(value => value.Name == name && Equals(value.Content, text) && !new ButtonAutomationPeer(value).IsOffscreen());
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private sealed class MediaCardsProbeServer : HttpMessageHandler
    {
        internal const string Title = "一觉醒来就有了最强装备跟太空船，决定以自家独栋建筑为目标当佣兵自由过活";
        internal string Fault { get; init; } = "";
        internal List<long> Ids { get; } = [];
        internal static string Destination(int id) => $"/Downloads_link/电视剧/{Title} (2026)/Season 01/{Title}.S01E{id:00}.1080p.AAC.mkv";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Require(request.RequestUri!.Host == "moviepilot.invalid", "卡片探针拒绝真实网络");
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("login/access-token", StringComparison.Ordinal)) return Json(new { access_token = "fixture-token" });
            if (path.EndsWith("dashboard/system", StringComparison.Ordinal)) return Json(new { version = "fixture" });
            if (path.EndsWith("user/current", StringComparison.Ordinal)) return Json(new { is_active = true, is_superuser = true });
            if (path.EndsWith("system/global/user", StringComparison.Ordinal)) return Json(new { AI_AGENT_ENABLE = true });
            if (path == "/api/v1/history/transfer" && request.Method == HttpMethod.Get)
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        total = 3,
                        list = Enumerable.Range(1, 3).Select(id => new
                        {
                            id,
                            title = Title,
                            type = "电视剧",
                            year = "2026",
                            status = true,
                            mode = "softlink",
                            seasons = "S01",
                            episodes = $"E{id:00}",
                            date = "2026-10-31 08:50:04",
                            media_source = "themoviedb",
                            media_id = "1",
                            src_storage = "local",
                            dest_storage = "local",
                            src = $"/Downloads/BT/[字幕组] Saikyousoubi S01E{id:00} [WebRip 1080p HEVC-10bit AAC].mkv",
                            dest = Destination(id),
                            src_fileitem = new { storage = "local", type = "file", path = $"/Downloads/BT/[字幕组] Saikyousoubi S01E{id:00} [WebRip 1080p HEVC-10bit AAC].mkv", size = 511830000 },
                            dest_fileitem = new { storage = "local", type = "file", path = Destination(id) }
                        }).ToArray()
                    }
                });
            if (path == "/api/v1/history/transfer" && request.Method == HttpMethod.Delete)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Ids.Add(body.RootElement.GetProperty("id").GetInt64());
                if (Fault == "unknown") return Json(new { success = true, data = new { } });
                return Json(new
                {
                    success = Fault != "partial",
                    data = new
                    {
                        source = new { status = "deleted" },
                        destination = new { status = Fault == "partial" ? "failed" : "deleted" },
                        history = Fault == "partial" ? "retained" : "deleted",
                        message = Fault == "partial" ? "媒体库文件无删除权限" : ""
                    }
                });
            }
            return Json(Array.Empty<object>());
        }
    }
}
