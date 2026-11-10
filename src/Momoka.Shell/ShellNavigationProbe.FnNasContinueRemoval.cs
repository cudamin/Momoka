using System.Net;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task FnNasContinueRemovalAsync()
    {
        using var fixture = CreateFixture();
        var picture = await FixtureImageAsync(1280, 720);
        var writes = new List<string>();
        var removed = false;
        var fail = false;
        var movie = new JsonObject { ["guid"] = "resume-movie", ["title"] = "离线续播电影", ["type"] = "Movie", ["duration"] = 120, ["ts"] = 30, ["poster"] = "wide.jpg" };
        var episode = new JsonObject { ["guid"] = "resume-episode", ["parent_guid"] = "season", ["tv_guid"] = "series", ["title"] = "零进度续播记录", ["type"] = "Episode", ["ts"] = 0, ["poster"] = "wide.jpg" };
        fixture.Transport.Reply = async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            if (path.EndsWith("/api/v1/sys/version", StringComparison.Ordinal)) return FnNasEnvelope(new { version = "0.9.8", mediasrvVersion = "0.8.45" });
            if (path.EndsWith("/api/v1/sys/config", StringComparison.Ordinal)) return FnNasEnvelope(new { server_guid = "resume-server", server_name = "离线飞牛" });
            if (path.EndsWith("/api/v1/user/info", StringComparison.Ordinal)) return FnNasEnvelope(new { guid = "user", username = "user", is_admin = 0 });
            if (path.EndsWith("/api/v1/mediadb/list", StringComparison.Ordinal)) return FnNasEnvelope(new[] { new { guid = "library", name = "电影", category = "Movie" } });
            if (path.EndsWith("/api/v1/mediadb/sum", StringComparison.Ordinal)) return FnNasEnvelope(new { library = 1, total = 1 });
            if (path.EndsWith("/api/v1/play/list", StringComparison.Ordinal)) return FnNasEnvelope(removed ? new[] { episode } : new[] { movie, episode });
            if (path.EndsWith("/api/v1/play/record", StringComparison.Ordinal))
            {
                Require(request.Method == HttpMethod.Delete, "续播移除错误地写入进度");
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Require(body == "{\"item_guid\":\"resume-movie\"}", "续播移除发送了父剧/版本或额外字段");
                if (fail) return new(HttpStatusCode.Forbidden);
                writes.Add(body);
                removed = true;
                return Json(new { code = 0, data = (object?)null });
            }
            if (path.EndsWith("/api/v1/item/list", StringComparison.Ordinal)) return FnNasEnvelope(new { list = new[] { movie }, total = 1 });
            if (path.EndsWith("/api/v1/tag/genres", StringComparison.Ordinal)) return FnNasEnvelope(Array.Empty<object>());
            if (path.Contains("/api/v1/sys/img/", StringComparison.Ordinal)) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) };
            throw new InvalidOperationException("未登记的匿名续播路径：" + path);
        };
        await fixture.SelectAsync(host: "fn.invalid", kind: MediaServerKind.FnNas);
        var libraries = await SourceLibraries.LoadAsync(fixture.Session, CancellationToken.None);
        fixture.Settings.Ui.ShowHomeBanner = false;
        fixture.Settings.Ui.HomeRows = HomeLayout.Save(HomeLayout.Plan(null, libraries.Items));
        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            ResizeProbeClient(1280, 900);
            frame.Navigate(typeof(HomePage), new HomeRequest(fixture.Services, libraries.Items, null));
            var home = (HomePage)frame.Content;
            await UntilAsync(() => home.IsReady && home.ViewModel.Shelves.Any(row => row.HomeKey == HomeLayout.Resume && row.Cards.Count == 2));
            var resume = home.ViewModel.Shelves.Single(row => row.HomeKey == HomeLayout.Resume);
            var latest = home.ViewModel.Shelves.Single(row => row.HomeKey == HomeLayout.LibraryKey(libraries.Items[0]));
            var card = resume.Cards.Single(row => row.Item.Id == "resume-movie");
            Require(ItemCommands.Build(fixture.Session, fixture.Actions, home, latest.Cards.Single())
                .Items.OfType<MenuFlyoutItem>().All(row => !Equals(row.Tag, ItemCommand.RemoveContinueWatching)), "普通货架出现续播移除");
            Require(ItemCommands.Build(fixture.Session, fixture.Actions, home, resume.Cards.Single(row => row.Item.Id == "resume-episode"))
                .Items.OfType<MenuFlyoutItem>().Any(row => Equals(row.Tag, ItemCommand.RemoveContinueWatching)), "零进度服务器记录没有移除入口");
            var changed = (Action)Call(home, "CaptureCardReload")!;
            await Confirm(false, "cancel");
            Require(writes.Count == 0 && resume.Cards.Count == 2, "取消仍移除续播");
            fail = true;
            await Confirm(true, "failed");
            Require(writes.Count == 0 && resume.Cards.Count == 2, "失败仍移除续播卡");
            fail = false;
            ResizeProbeClient(760, 900);
            await Confirm(true, "confirmed");
            await UntilAsync(() => home.ViewModel.Shelves.Single(row => row.HomeKey == HomeLayout.Resume).Cards.Count == 1);
            Require(writes.Count == 1, "确认重复提交续播删除");
            Require(home.ViewModel.Shelves.Single(row => row.HomeKey == HomeLayout.Resume).Cards.Single().Item.Id == "resume-episode", "另一条续播记录被误删");
            Require(home.ViewModel.Shelves.Single(row => row.HomeKey == latest.HomeKey).Cards.Single().Item.Id == "resume-movie", "续播移除误删最新货架");
            Require(((RemovedMedia)fixture.Services.GetService(typeof(RemovedMedia))!).Count == 0 && fixture.Actions.Plays == 0, "续播被记成已删媒体或误起播");
            await LayoutAsync(home);
            await SaveProbeScreenAsync("fnnas-continue-removed-760-screen.png");
            home.Release();

            async Task Confirm(bool accept, string state)
            {
                var menu = ItemCommands.Build(fixture.Session, fixture.Actions, home, card, changed: changed);
                var entry = menu.Items.OfType<MenuFlyoutItem>().Single(row => Equals(row.Tag, ItemCommand.RemoveContinueWatching));
                menu.ShowAt(home);
                await LayoutAsync(home);
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(entry).GetPattern(PatternInterface.Invoke)).Invoke();
                ContentDialog? dialog = null;
                await UntilAsync(() => (dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(home.XamlRoot)
                    .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>()
                    .FirstOrDefault(value => Equals(value.Title, "从继续观看移除"))) is not null);
                await LayoutAsync(dialog!);
                Require(dialog!.DefaultButton == ContentDialogButton.Close && dialog.PrimaryButtonText == "移除", "续播确认默认不是取消");
                await SaveProbeScreenAsync($"fnnas-continue-{state}-screen.png");
                if (accept) ClickDialogPrimary(dialog);
                else ((IInvokeProvider)new ButtonAutomationPeer((Button)DialogDismiss.TemplatePart(dialog, "CloseButton")!).GetPattern(PatternInterface.Invoke)).Invoke();
                await UntilAsync(() => !VisualTreeHelper.GetOpenPopupsForXamlRoot(home.XamlRoot)
                    .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>().Any());
                await Task.Delay(200);
            }
        }
        finally
        {
            if (frame.Content is HomePage page) page.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }
}
