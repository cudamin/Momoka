using System.Net;
using Momoka.Emby;
using Momoka.FnNas;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class FnNasProtocolTests
{
    internal static void RegisterLibraryRemoval()
    {
        Test("飞牛恢复隐藏：只忘掉匹配主体，其他删除保持", () =>
        {
            var first = new EmbyItem { Id = "first", Type = EmbyItemType.Movie };
            var second = new EmbyItem { Id = "second", Type = EmbyItemType.Movie };
            var hidden = new RemovedMedia();
            var ticket = new object();
            using (RemovedMedia.TemporaryRemoval(ticket)) hidden.Remember(first);
            hidden.Remember(second);
            hidden.Forget(ticket);
            Assert.False(hidden.Hides(first));
            Assert.True(hidden.Hides(second));
            Assert.Equal(1, hidden.Count);
        });
        Test("飞牛恢复隐藏：同ID后续物理删除独立，移出票只忘自己", () =>
        {
            var item = new EmbyItem { Id = "same", Type = EmbyItemType.Movie };
            var hidden = new RemovedMedia();
            var ticket = new object();
            using (RemovedMedia.TemporaryRemoval(ticket)) hidden.Remember(item);
            hidden.Remember(item);
            hidden.Forget(ticket);
            Assert.True(hidden.Hides(item));
            Assert.Equal(1, hidden.Count);
        });
        Test("飞牛恢复隐藏：摘卡异步续链继承移出票，调用返回后普通Remember独立", () =>
        {
            var hidden = new RemovedMedia();
            var temporary = new EmbyItem { Id = "temporary", Type = EmbyItemType.Movie };
            var physical = new EmbyItem { Id = "physical", Type = EmbyItemType.Movie };
            var ticket = new object();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task callback;
            using (RemovedMedia.TemporaryRemoval(ticket)) callback = RememberLater();
            hidden.Remember(physical);
            entered.SetResult();
            callback.GetAwaiter().GetResult();
            hidden.Forget(ticket);
            Assert.False(hidden.Hides(temporary));
            Assert.True(hidden.Hides(physical));
            async Task RememberLater()
            {
                await entered.Task.ConfigureAwait(false);
                hidden.Remember(temporary);
            }
        });
        Test("飞牛恢复隐藏：恢复先于异步摘卡完成时退役票不再复活", () =>
        {
            var hidden = new RemovedMedia();
            var item = new EmbyItem { Id = "late", Type = EmbyItemType.Movie };
            var ticket = new object();
            hidden.Forget(ticket);
            using (RemovedMedia.TemporaryRemoval(ticket)) hidden.Remember(item);
            Assert.False(hidden.Hides(item));
            Assert.Equal(0, hidden.Count);
        });
        Test("飞牛移出菜单：管理员成对入口可见，普通未知权限与其他后端隐藏", () =>
        {
            var item = new EmbyItem { Id = "movie", Name = "电影", Type = EmbyItemType.Movie, FnNasLibraryGuid = "library" };
            var admin = new ItemMenuPermissions(IsAdministrator: true, CanDeleteContent: true);
            Assert.True(ItemMenu.For(item, fnNas: true, permissions: admin).Any(entry => entry.Command == ItemCommand.RemoveFromLibrary));
            foreach (var permissions in new[] { new ItemMenuPermissions(IsAdministrator: false), new ItemMenuPermissions(), admin with { CanDeleteContent = false } })
            {
                Assert.False(ItemMenu.For(item, fnNas: true, permissions: permissions).Any(entry => entry.Command == ItemCommand.RemoveFromLibrary));
                Assert.False(ItemMenu.Allows(ItemCommand.RemoveFromLibrary, permissions, MediaServerKind.FnNas, item));
            }
            foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin, MediaServerKind.Plex })
                Assert.False(ItemMenu.Allows(ItemCommand.RemoveFromLibrary, admin, kind, item));
            foreach (var type in new[] { EmbyItemType.CollectionFolder, EmbyItemType.Person, EmbyItemType.Series, EmbyItemType.Season })
                Assert.False(ItemMenu.For(new EmbyItem { Id = "other", Type = type }, fnNas: true, permissions: admin)
                    .Any(entry => entry.Command == ItemCommand.RemoveFromLibrary));
        });
        Test("飞牛移出：整个条目与指定版本只发送保留文件删除正文", () =>
        {
            using var transport = new FixtureTransport(request =>
            {
                CheckSignature(request);
                return request.Uri.AbsolutePath.EndsWith("/user/info", StringComparison.Ordinal)
                    ? Reply("{\"is_admin\":1}") : Reply("null");
            });
            using var http = new FnNasHttp(transport, disposeHandler: false);
            var client = new FnNasClient(http);
            client.RemoveItemFromLibraryAsync(Connection, "movie", []).GetAwaiter().GetResult();
            client.RemoveItemFromLibraryAsync(Connection, "movie", ["file-a"]).GetAwaiter().GetResult();
            Assert.Equal(4, transport.Requests.Count);
            Assert.Equal(HttpMethod.Delete, transport.Requests[1].Method);
            Assert.Equal("/v/api/v1/item/movie", transport.Requests[1].Uri.AbsolutePath);
            Assert.Equal("{\"delete_file\":0,\"media_guids\":[]}", transport.Requests[1].Body);
            Assert.Equal("{\"delete_file\":0,\"media_guids\":[\"file-a\"]}", transport.Requests[3].Body);
        });
        Test("飞牛恢复目录：根与子目录沿官方fileViewAdmin读取且正常文件不成为恢复项", () =>
        {
            using var transport = new FixtureTransport(request =>
            {
                CheckSignature(request);
                if (request.Uri.AbsolutePath.EndsWith("/user/info", StringComparison.Ordinal)) return Reply("{\"is_admin\":1}");
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/v/api/v1/fileViewAdmin/list", request.Uri.AbsolutePath);
                return Reply("""
                    {"mdb_name":"电影","dir":"/影视/电影","jump_list":[],"list":[
                      {"guid":"folder","type":"Directory","path":"/影视/电影","file_name":"电影","blacked":0},
                      {"guid":"movie","media_guid":"file-a","type":"Movie","path":"/影视/电影/a.mkv","file_name":"a.mkv","blacked":1},
                      {"guid":"other","media_guid":"file-b","type":"Movie","path":"/影视/电影/b.mkv","file_name":"b.mkv","blacked":0}
                    ]}
                    """);
            });
            using var http = new FnNasHttp(transport, disposeHandler: false);
            var client = new FnNasClient(http);
            var root = client.GetRemovedFileDirectoryAsync(Connection, "library").GetAwaiter().GetResult();
            var child = client.GetRemovedFileDirectoryAsync(Connection, "library", "folder").GetAwaiter().GetResult();
            Assert.Equal("?mdb_guid=library", transport.Requests[1].Uri.Query);
            Assert.Equal("?mdb_guid=library&fv_guid=folder", transport.Requests[3].Uri.Query);
            Assert.Equal("电影", root.LibraryName);
            Assert.Equal("/影视/电影", child.Path);
            Assert.Equal(2, root.Entries.Count);
            Assert.True(root.Entries[0].CanBrowse);
            Assert.False(root.Entries[0].CanRestore);
            Assert.Equal("file-a", root.Entries[1].MediaGuid);
            Assert.True(root.Entries[1].CanRestore);
        });
        Test("飞牛恢复：只传媒体库与原路径，确认后不追加刷新或扫描", () =>
        {
            using var transport = new FixtureTransport(request =>
            {
                CheckSignature(request);
                return request.Uri.AbsolutePath.EndsWith("/user/info", StringComparison.Ordinal)
                    ? Reply("{\"is_admin\":1}") : Reply("null");
            });
            using var http = new FnNasHttp(transport, disposeHandler: false);
            new FnNasClient(http).RestoreRemovedFileAsync(Connection, "library", "/影视/A + B.mkv").GetAwaiter().GetResult();
            Assert.Equal(2, transport.Requests.Count);
            Assert.Equal(HttpMethod.Post, transport.Requests[1].Method);
            Assert.Equal("/v/api/v1/scrap/removeFromBlackByPath", transport.Requests[1].Uri.AbsolutePath);
            Assert.Equal("{\"mdb_guid\":\"library\",\"path\":\"/影视/A + B.mkv\"}", transport.Requests[1].Body);
        });
        Test("飞牛恢复：普通账号与预取消没有管理读取或写入", () =>
        {
            using var transport = new FixtureTransport(_ => Reply("{\"is_admin\":0}"));
            using var http = new FnNasHttp(transport, disposeHandler: false);
            var client = new FnNasClient(http);
            Assert.Equal(-5, Assert.Catch<FnNasApiException>(() => client.RestoreRemovedFileAsync(Connection, "library", "/a.mkv").GetAwaiter().GetResult()).Code);
            Assert.Equal(-5, Assert.Catch<FnNasApiException>(() => client.GetRemovedFileDirectoryAsync(Connection, "library").GetAwaiter().GetResult()).Code);
            Assert.Equal(-5, Assert.Catch<FnNasApiException>(() => client.RemoveItemFromLibraryAsync(Connection, "movie", []).GetAwaiter().GetResult()).Code);
            Assert.Equal(3, transport.Requests.Count);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => client.RestoreRemovedFileAsync(Connection, "library", "/a.mkv", cancellation.Token).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => client.RemoveItemFromLibraryAsync(Connection, "movie", [], cancellation.Token).GetAwaiter().GetResult());
            Assert.Equal(3, transport.Requests.Count);
        });
        Test("飞牛移出恢复：非法ID和空路径发出前拒绝，不把未知列表当空", () =>
        {
            using var transport = new FixtureTransport(request => request.Uri.AbsolutePath.EndsWith("/user/info", StringComparison.Ordinal)
                ? Reply("{\"is_admin\":1}") : Reply("{\"list\":{}}"));
            using var http = new FnNasHttp(transport, disposeHandler: false);
            var client = new FnNasClient(http);
            Assert.Throws<ArgumentException>(() => client.RemoveItemFromLibraryAsync(Connection, "movie", [""]).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => client.GetRemovedFileDirectoryAsync(Connection, "library", "../folder").GetAwaiter().GetResult());
            foreach (var path in new[] { "", " ", "/a\n.mkv", "/a/../b.mkv" })
                Assert.Throws<ArgumentException>(() => client.RestoreRemovedFileAsync(Connection, "library", path).GetAwaiter().GetResult());
            Assert.Equal(0, transport.Requests.Count);
            Assert.Throws<FnNasApiException>(() => client.GetRemovedFileDirectoryAsync(Connection, "library").GetAwaiter().GetResult());
        });
        Test("飞牛恢复：业务权限错误照实失败，不读取或重发恢复", () =>
        {
            using var transport = new FixtureTransport(request => request.Uri.AbsolutePath.EndsWith("/user/info", StringComparison.Ordinal)
                ? Reply("{\"is_admin\":1}") : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"code\":-16,\"msg\":\"fixture-secret-token\"}") });
            using var http = new FnNasHttp(transport, disposeHandler: false);
            var error = Assert.Catch<FnNasApiException>(() => new FnNasClient(http).RestoreRemovedFileAsync(Connection, "library", "/a.mkv").GetAwaiter().GetResult());
            Assert.Equal(-16, error.Code);
            Assert.DoesNotContain(Secret, error.Message);
            Assert.Equal(2, transport.Requests.Count);
        });
    }
}
