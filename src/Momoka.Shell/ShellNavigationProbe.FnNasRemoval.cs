using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.FnNas;
using Momoka.Shell.Services;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task FnNasRemovalAsync()
    {
        using var fixture = CreateFixture();
        var writes = new List<string>();
        var removed = false;
        var denyRead = false;
        var picture = await FixtureImageAsync(400, 600);
        fixture.Transport.Reply = async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            if (path.EndsWith("/sys/version", StringComparison.Ordinal)) return FnNasEnvelope(new { version = "0.9.8", mediasrvVersion = "0.8.45" });
            if (path.EndsWith("/sys/config", StringComparison.Ordinal)) return FnNasEnvelope(new { server_guid = "removal-server", server_name = "离线飞牛" });
            if (path.EndsWith("/user/info", StringComparison.Ordinal)) return FnNasEnvelope(new { guid = "user", username = "user", is_admin = 1 });
            if (path.EndsWith("/mediadb/list", StringComparison.Ordinal) || path.EndsWith("/mdb/list", StringComparison.Ordinal))
                return FnNasEnvelope(new[] { new { guid = "library", name = "飞牛电影", category = "Movie", dir_list = new[] { "/media/电影" } } });
            if (path.EndsWith("/mediadb/sum", StringComparison.Ordinal)) return FnNasEnvelope(new { total = 1 });
            if (path.EndsWith("/play/list", StringComparison.Ordinal)) return FnNasEnvelope(Array.Empty<object>());
            if (path.Contains("/sys/img/", StringComparison.Ordinal)) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(picture) };
            if (path.EndsWith("/media/itemfile/movie", StringComparison.Ordinal)) return FnNasEnvelope(new[]
            {
                new { media_guid = "file-a", file_path = "/media/电影/a.mkv", file_size = 1_048_576, resolution = "1080p" },
                new { media_guid = "file-b", file_path = "/media/电影/b.mkv", file_size = 2_097_152, resolution = "4K" }
            });
            if (path.EndsWith("/item/movie", StringComparison.Ordinal) && request.Method == HttpMethod.Delete)
            {
                var body = await request.Content!.ReadAsStringAsync(token);
                Require(JsonNode.Parse(body)!["delete_file"]!.GetValue<int>() == 0, "移出错误地删除物理文件");
                writes.Add(body);
                removed = true;
                return Json(new { code = 0, data = (object?)null });
            }
            if (path.EndsWith("/scrap/removeFromBlackByPath", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadAsStringAsync(token);
                Require(body == "{\"mdb_guid\":\"library\",\"path\":\"/media/电影/a.mkv\"}", "恢复路径/媒体库不正确");
                writes.Add(body);
                removed = false;
                denyRead = true;
                return Json(new { code = 0, data = (object?)null });
            }
            if (path.EndsWith("/fileViewAdmin/list", StringComparison.Ordinal))
            {
                if (denyRead) return new(HttpStatusCode.ServiceUnavailable);
                var child = request.RequestUri.Query.Contains("fv_guid=folder", StringComparison.Ordinal);
                return FnNasEnvelope(new JsonObject
                {
                    ["mdb_name"] = "飞牛电影",
                    ["dir"] = child ? "/media/电影" : "",
                    ["jump_list"] = new JsonArray(),
                    ["list"] = child ? new JsonArray(new JsonObject { ["guid"] = "movie", ["media_guid"] = "file-a", ["file_name"] = "a.mkv", ["path"] = "/media/电影/a.mkv", ["type"] = "Movie", ["blacked"] = removed ? 1 : 0 })
                        : new JsonArray(new JsonObject { ["guid"] = "folder", ["file_name"] = "电影", ["path"] = "/media/电影", ["type"] = "Directory", ["blacked"] = 0 })
                });
            }
            throw new InvalidOperationException("未登记的匿名移出路径：" + path);
        };
        await fixture.SelectAsync(host: "fn-removal.invalid", kind: MediaServerKind.FnNas);
        var service = fixture.Services.GetRequiredService<FnNasLibraryRemovalService>();
        fixture.Actions.FnNasRemove = (scope, item, versions, mediaGuid, token) => service.RemoveAsync(scope, item, versions, mediaGuid, token);
        fixture.Actions.MenuPermissions = _ => new(IsAdministrator: true, CanDeleteContent: true);
        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            ResizeProbeClient(1280, 900);
            frame.Navigate(typeof(FnNasLibrariesPage), new FnNasLibrariesRequest(fixture.Services, fixture.Session.Capture().Key));
            var page = (FnNasLibrariesPage)frame.Content;
            await UntilAsync(() => page.ViewModel.CanUse && page.ViewModel.Libraries.Count == 1);
            await LayoutAsync(page);
            Require(Descendants(page).OfType<Button>().Any(button => button.Name == "FnNasLibraryRemovedFiles"), "飞牛媒体库缺恢复入口");
            await SaveProbeScreenAsync("fnnas-removal-library-1280-screen.png");
            var item = fixture.Session.Capture().Bind(new EmbyItem
            {
                Id = "movie",
                Name = "匿名电影",
                Type = EmbyItemType.Movie,
                FnNasLibraryGuid = "library",
                MediaSources = [new() { Id = "file-a", Path = "/media/电影/a.mkv" }, new() { Id = "file-b", Path = "/media/电影/b.mkv" }]
            });
            var card = new CardItem(item, fixture.Images, 160);
            var changed = 0;
            var removedCallbacks = 0;
            await Remove(false, false);
            Require(writes.Count == 0 && item.MediaSources.Count == 2, "取消移出仍写入/撤源");
            await Remove(true, true);
            Require(writes.Count == 1 && changed == 1 && removedCallbacks == 0 && item.MediaSources.Single().Id == "file-b", "单版移出没有只撤选中源");

            var row = page.ViewModel.Libraries.Single();
            var button = Descendants(page).OfType<Button>().Single(value => value.Name == "FnNasLibraryRemovedFiles");
            button.Tag = row;
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
            FnNasRemovedFilesDialog? recovery = null;
            await UntilAsync(() => (recovery = OpenDialogs().OfType<FnNasRemovedFilesDialog>().SingleOrDefault()) is not null && recovery.ViewModel.CanUse);
            var directory = recovery!.ViewModel.Entries.Single();
            await recovery.ViewModel.OpenAsync(directory);
            Require(recovery.ViewModel.Entries.Single().IsRemoved, "恢复目录没有移出文件");
            recovery.ViewModel.Selected = recovery.ViewModel.Entries.Single();
            await LayoutAsync(recovery);
            await SaveProbeScreenAsync("fnnas-removal-recovery-1280-screen.png");
            ResizeProbeClient(760, 900);
            await LayoutAsync(recovery);
            await SaveProbeScreenAsync("fnnas-removal-recovery-760-screen.png");
            ClickDialogPrimary(recovery);
            await UntilAsync(() => !recovery.ViewModel.Busy && recovery.ViewModel.NoticeOpen);
            Require(writes.Count == 2 && !recovery.ViewModel.CanRestore && recovery.ViewModel.Entries.Count == 0,
                "恢复确认后刷新失败重发/仍允许旧恢复");
            Require(recovery.ViewModel.NoticeTitle?.Contains("已提交恢复", StringComparison.Ordinal) == true, "刷新失败覆盖恢复成功");
            await SaveProbeScreenAsync("fnnas-removal-saved-refresh-failed-760-screen.png");
            denyRead = false;
            await recovery.ViewModel.ReloadAsync();
            Require(writes.Count == 2 && fixture.Actions.Plays == 0, "刷新重发恢复或误播放");
            recovery.Hide();
            await UntilAsync(() => !OpenDialogs().Any());
            page.Release();

            async Task Remove(bool accept, bool version)
            {
                var operation = (Task)typeof(ItemCommands).GetMethod(version ? "FnNasVersionsAsync" : "FnNasRemoveFromLibraryAsync",
                    BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                    [fixture.Session.Capture(), fixture.Actions, page, card, (Action)(() => changed++), (Action<CardItem>)(_ => removedCallbacks++)])!;
                if (version)
                {
                    await UntilAsync(() => OpenDialogs().OfType<FnNasVersionsDialog>().Any());
                    var versions = OpenDialogs().OfType<FnNasVersionsDialog>().Single();
                    Get<ListView>(versions, "Files").SelectedIndex = 0;
                    await LayoutAsync(versions);
                    Require(versions.SecondaryButtonText == "移出所选版本", "版本表缺移出入口");
                    var secondary = (Button)DialogDismiss.TemplatePart(versions, "SecondaryButton")!;
                    ((IInvokeProvider)new ButtonAutomationPeer(secondary).GetPattern(PatternInterface.Invoke)).Invoke();
                }
                await UntilAsync(() => OpenDialogs().Any(dialog => dialog.Title?.ToString()?.StartsWith("移出", StringComparison.Ordinal) == true));
                var confirm = OpenDialogs().Single(dialog => dialog.Title?.ToString()?.StartsWith("移出", StringComparison.Ordinal) == true);
                Require(confirm.DefaultButton == ContentDialogButton.Close && confirm.PrimaryButtonText == "移出", "移出确认默认未取消");
                await SaveProbeScreenAsync(version ? "fnnas-removal-version-confirm-screen.png" : "fnnas-removal-cancel-screen.png");
                if (accept) ClickDialogPrimary(confirm);
                else confirm.Hide();
                await operation;
                await UntilAsync(() => !OpenDialogs().Any());
            }
            IEnumerable<ContentDialog> OpenDialogs() => VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>();
        }
        finally
        {
            foreach (var dialog in VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot)
                         .SelectMany(popup => Descendants(popup.Child)).OfType<ContentDialog>()) dialog.Hide();
            if (frame.Content is FnNasLibrariesPage page) page.Release();
            _root.Children.Remove(frame);
            ResizeInspect(1420, 980);
        }
    }
}
