using System.Net;
using System.Text.Json.Nodes;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    /// <summary>
    /// 2026-11-01 用户报「飞牛混合内容媒体库显示内容不全」：官方自定义库页按 All 问
    /// 电影、节目、文件夹、视频；文件夹下钻改发 parent_guid 且不带类型。筛选行也照官方
    /// 按库根／文件夹裁剪（见 FnNasFilterCatalog）。
    /// </summary>
    private async Task FnNasMixedLibraryAsync()
    {
        using var fixture = CreateFixture();
        var poster = await FixtureImageAsync(400, 600);
        var bodies = new List<JsonObject>();
        var uris = new List<string>();
        fixture.Transport.Reply = async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            uris.Add(request.RequestUri.PathAndQuery);
            if (path.EndsWith("/System/Info/Public", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (path.EndsWith("/api/v1/sys/version", StringComparison.Ordinal))
                return FnNasEnvelope(new { version = "0.9.8", mediasrvVersion = "0.9.8" });
            if (path.EndsWith("/api/v1/sys/config", StringComparison.Ordinal))
                return FnNasEnvelope(new { server_guid = "fn-guid", server_name = "飞牛影视" });
            if (path.EndsWith("/api/v1/user/info", StringComparison.Ordinal))
                return FnNasEnvelope(new { guid = "user", username = "user", is_admin = "1" });
            if (path.EndsWith("/api/v1/mediadb/list", StringComparison.Ordinal))
                return FnNasEnvelope(new[] { new { guid = "fn-mix", name = "混合影片", category = "Mix" } });
            if (path.EndsWith("/api/v1/mediadb/sum", StringComparison.Ordinal))
                return FnNasEnvelope(new JsonObject { ["fn-mix"] = 4 });
            if (path.EndsWith("/api/v1/play/list", StringComparison.Ordinal))
                return FnNasEnvelope(Array.Empty<object>());
            if (path.EndsWith("/api/v1/item/list", StringComparison.Ordinal))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                bodies.Add(body);
                // 和真实服务器一样按 tags.type 过滤：库根问 All 时四种都回来，文件夹页不带类型。
                var types = body["tags"]?["type"]?.AsArray()
                    .Select(node => node!.GetValue<string>()).ToArray() ?? [];
                var all = new[]
                {
                    MixedItem("fn-movie", "电影", "Movie"),
                    MixedItem("fn-tv", "节目", "TV"),
                    MixedItem("fn-folder", "文件夹", "Directory"),
                    MixedItem("fn-video", "视频", "Video")
                };
                var list = types.Length == 0 ? all : all.Where(item => types.Contains(item["type"]!.GetValue<string>())).ToArray();
                return FnNasEnvelope(new { list, total = list.Length });
            }
            if (path.EndsWith("/api/v1/tag/list", StringComparison.Ordinal))
                return FnNasEnvelope(new JsonObject());
            if (path.EndsWith("/api/v1/tag/genres", StringComparison.Ordinal)
                || path.EndsWith("/api/v1/tag/iso3166", StringComparison.Ordinal))
                return FnNasEnvelope(Array.Empty<object>());
            if (path.Contains("/api/v1/sys/img/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
        await fixture.SelectAsync(host: "fn.invalid", kind: MediaServerKind.FnNas);

        var libraries = await SourceLibraries.LoadAsync(fixture.Session, CancellationToken.None);
        var library = libraries.Items.Single();
        Require(library.Name == "混合影片" && library.CollectionType == "mixed",
            $"飞牛 Mix 分类没有映射成混合库：{library.Name} / {library.CollectionType}");

        var frame = new Frame();
        _root.Children.Add(frame);
        try
        {
            ResizeProbeClient(1280, 900);
            frame.Navigate(typeof(LibraryPage), new LibraryRequest
            {
                Services = fixture.Services,
                Title = library.Name,
                ParentId = library.Id,
                CollectionType = library.CollectionType
            });
            var page = (LibraryPage)frame.Content;
            await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);

            Require(page.ViewModel.Cards.Count == 4,
                $"混合库只显示了 {page.ViewModel.Cards.Count} 项，官方 All 应回电影、节目、文件夹、视频四项");
            var rootQuery = bodies.FirstOrDefault(body => body["ancestor_guid"]?.GetValue<string>() == "fn-mix")
                ?? throw new InvalidOperationException("混合库没有发 ancestor_guid 查询");
            var rootTypes = rootQuery["tags"]?["type"]?.AsArray()
                .Select(node => node!.GetValue<string>()).ToArray() ?? [];
            Require(string.Join(',', rootTypes) == "Movie,TV,Directory,Video",
                $"混合库的 tags.type 不是官方 All：{string.Join(',', rootTypes)}");
            Require(!rootQuery.ContainsKey("parent_guid"), "库根不该发 parent_guid");

            var invoke = new ButtonAutomationPeer(Get<Button>(page, "FilterButton"));
            ((IInvokeProvider)invoke.GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => page.ViewModel.FnNasFilterOpen && !page.ViewModel.FnNasFilterLoading
                && page.ViewModel.FnNasFilterRows.Count > 0);
            Require(string.Join(',', page.ViewModel.FnNasFilterRows.Select(row => row.Label))
                == "影视分类,类型,分辨率,视频动态范围,音频规格,国家/地区,发行年份,匹配状态,是否已观看",
                "混合库筛选行没有照官网页端画全");
            Require(uris.Any(uri => uri.StartsWith("/media/v/api/v1/tag/list?ancestor_guid=fn-mix&is_favorite=0", StringComparison.Ordinal)),
                "混合库筛选没有按 ancestor_guid 问 tag/list：" + string.Join(" | ", uris.Where(uri => uri.Contains("/tag/list", StringComparison.Ordinal))));
            await LayoutAsync(page);
            await SaveProbeScreenAsync("library-fnnas-mixed-screen.png");

            (frame.Content as IShellContent)?.Release();
            frame.Content = null;
            frame.Navigate(typeof(LibraryPage), new LibraryRequest
            {
                Services = fixture.Services,
                Title = "文件夹",
                ParentId = "fn-folder",
                ParentType = EmbyItemType.Folder
            });
            var folder = (LibraryPage?)frame.Content
                ?? throw new InvalidOperationException("飞牛文件夹页没有创建");
            await UntilAsync(() => folder.ViewModel.IsReady && !folder.ViewModel.Busy);

            var folderQuery = bodies.LastOrDefault(body => body["parent_guid"]?.GetValue<string>() == "fn-folder")
                ?? throw new InvalidOperationException("文件夹页没有发 parent_guid 查询");
            Require(!folderQuery.ContainsKey("ancestor_guid"), "文件夹页不该再发 ancestor_guid");
            Require(folderQuery["tags"]!.AsObject().Count == 0, "文件夹页没有默认类型，tags 应为空");

            var folderInvoke = new ButtonAutomationPeer(Get<Button>(folder, "FilterButton"));
            ((IInvokeProvider)folderInvoke.GetPattern(PatternInterface.Invoke)).Invoke();
            await UntilAsync(() => folder.ViewModel.FnNasFilterOpen && !folder.ViewModel.FnNasFilterLoading
                && folder.ViewModel.FnNasFilterRows.Count > 0);
            Require(string.Join(',', folder.ViewModel.FnNasFilterRows.Select(row => row.Label))
                == "分辨率,视频动态范围,音频规格,是否已观看",
                "文件夹页筛选行不是官方的四行");
            Require(uris.Any(uri => uri.StartsWith("/media/v/api/v1/tag/list?parent_guid=fn-folder&is_favorite=0", StringComparison.Ordinal)),
                "文件夹页筛选没有按 parent_guid 问 tag/list：" + string.Join(" | ", uris.Where(uri => uri.Contains("/tag/list", StringComparison.Ordinal))));
            await LayoutAsync(folder);
            await LayoutAsync(folder);
            await SaveProbeScreenAsync("library-fnnas-folder-filter-screen.png");

            Require(fixture.Actions.Plays == 0, "飞牛混合库检查意外请求播放");
        }
        finally
        {
            (frame.Content as IShellContent)?.Release();
            frame.Content = null;
            _root.Children.Remove(frame);
        }
    }

    private static JsonObject MixedItem(string id, string title, string type) => new()
    {
        ["guid"] = id,
        ["title"] = title,
        ["type"] = type,
        ["poster"] = "/poster/" + id + ".webp",
        ["poster_width"] = 1920,
        ["poster_height"] = 2560
    };
}
