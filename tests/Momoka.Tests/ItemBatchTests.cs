using System.Net;
using System.Reflection;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class ItemBatchTests
{
    internal static void Register()
    {
        Test("媒体批量：业务服务可独立调用", () =>
            Assert.NotNull(typeof(EmbyItem).Assembly.GetType("Momoka.Emby.ItemBatchService")));
        Test("媒体批量：Emby删除保留逐项正式DELETE与取消边界", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            using var cancel = new CancellationTokenSource();
            fixture.Http.Answer("Items/101", "").Fail("Items/102", HttpStatusCode.Forbidden)
                .When("Items/102", cancel.Cancel);
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.Delete,
                () => new(CanDeleteContent: true), null, cancel.Token);
            Assert.Equal(1, result.Succeeded);
            Assert.Equal(1, result.Failed);
            Assert.Equal(1, result.Canceled);
            Assert.Equal("DELETE", fixture.Http.Only("Items/101").Method);
            Assert.Equal(0, fixture.Http.Count("Items/103"));
        }));
        Test("媒体批量：Emby新增命令可用于播放子菜单与服务器写入", () =>
        {
            foreach (var name in new[] { "PlayFromBeginning", "Shuffle", "AddToPlaylist", "ResetMetadata", "ScanLibraryFiles" })
                Assert.True(Enum.TryParse<ItemCommand>(name, out _), name);
        });
        Test("媒体批量：新增协议与账号能力必须可独立调用", () =>
        {
            foreach (var name in new[] { "GetEditablePlaylistsAsync", "CreatePlaylistAsync", "AddToPlaylistAsync", "ScanLibraryFilesAsync" })
                Assert.NotNull(typeof(EmbyClient).GetMethod(name), name);
        });
        Test("媒体批量：后台媒体转换命令与协议已退役", () =>
        {
            Assert.False(Enum.TryParse<ItemCommand>("Convert", out _));
            Assert.True(ItemBatch.Commands.All(command => command.ToString() != "Convert"));
            foreach (var name in new[] { "GetConversionOptionsAsync", "CreateConversionJobAsync" })
                Assert.True(typeof(EmbyClient).GetMethod(name) is null, name);
            Assert.True(typeof(EmbyItem).Assembly.GetType("Momoka.Emby.EmbyConversionSelection") is null);
        });
        Test("媒体批量：Emby删除权限与安全类型拒绝零写", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.Delete,
                () => new(CanDeleteContent: false), null, default);
            Assert.Equal(3, result.Failed);
            foreach (var type in new[] { EmbyItemType.CollectionFolder, EmbyItemType.Person, EmbyItemType.Genre, EmbyItemType.Studio })
            {
                fixture.Entries[0].Item.Type = type;
                Assert.False(ItemBatch.Allowed(ItemCommand.Delete, fixture.Source, fixture.Entries[0], new(CanDeleteContent: true)));
            }
            fixture.Entries[0].Item.Type = EmbyItemType.Movie;
            fixture.Entries[0].Item.CanDelete = false;
            Assert.False(ItemBatch.Allowed(ItemCommand.Delete, fixture.Source, fixture.Entries[0], new(CanDeleteContent: true)));
            Assert.Equal(0, fixture.Http.Total);
        }));
        Test("媒体批量：Emby移除识别保留独立Reset协议", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            fixture.Entries[1].Item.Type = EmbyItemType.Episode;
            fixture.Entries[2].Item.Type = EmbyItemType.Series;
            fixture.Http.Answer("Items/Metadata/Reset", "");
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.ResetMetadata,
                () => new(IsAdministrator: true), null, default);
            Assert.Equal(2, result.Succeeded);
            Assert.Equal(1, result.Failed);
            foreach (var id in new[] { "101", "103" })
            {
                var sent = fixture.Http.Only("ItemIds=" + id);
                Assert.Equal("POST", sent.Method);
                using var json = System.Text.Json.JsonDocument.Parse(sent.Body);
                Assert.Equal(id, json.RootElement.GetProperty("ItemIds").GetString());
            }
            Assert.Equal(0, fixture.Http.Count("/Refresh"));
            Assert.Equal(0, fixture.Http.Count("/Items/101/"));
        }));
        Test("媒体批量：Emby文件扫描只允许实际文件夹且不扫全库", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            fixture.Entries[0].Item.Type = EmbyItemType.Series;
            fixture.Entries[0].Item.IsFolder = true;
            fixture.Entries[1].Item.Type = EmbyItemType.BoxSet;
            fixture.Entries[1].Item.IsFolder = true;
            fixture.Http.Answer("Items/101/Refresh", "");
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.ScanLibraryFiles,
                () => new(IsAdministrator: true), null, default);
            Assert.Equal(1, result.Succeeded);
            Assert.Equal(2, result.Failed);
            var sent = fixture.Http.Only("Items/101/Refresh");
            Assert.Equal("POST", sent.Method);
            Assert.Contains("Recursive=true", sent.Url);
            Assert.Contains("MetadataRefreshMode=Default", sent.Url);
            Assert.Contains("ImageRefreshMode=Default", sent.Url);
            Assert.Contains("ReplaceAllMetadata=false", sent.Url);
            Assert.Contains("ReplaceAllImages=false", sent.Url);
            Assert.Equal(0, fixture.Http.Count("Library/Refresh"));
        }));
        Test("媒体批量：Emby播放枚举仅接受可播放或可展开视频类型", () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            foreach (var command in new[] { ItemCommand.Play, ItemCommand.PlayFromBeginning, ItemCommand.Shuffle })
            {
                foreach (var type in new[] { EmbyItemType.Movie, EmbyItemType.Episode, EmbyItemType.Series, EmbyItemType.Season,
                    EmbyItemType.Folder, EmbyItemType.BoxSet, EmbyItemType.Playlist, EmbyItemType.CollectionFolder })
                {
                    fixture.Entries[0].Item.Type = type;
                    Assert.True(ItemBatch.Allowed(command, fixture.Source, fixture.Entries[0], default), type);
                }
                foreach (var type in new[] { EmbyItemType.Person, EmbyItemType.Genre, EmbyItemType.Studio,
                    EmbyItemType.Audio, EmbyItemType.MusicAlbum, EmbyItemType.MusicArtist, "Unknown" })
                {
                    fixture.Entries[0].Item.Type = type;
                    Assert.False(ItemBatch.Allowed(command, fixture.Source, fixture.Entries[0], default), type);
                }
            }
            Assert.Equal(0, fixture.Http.Total);
        });
        foreach (var kind in new[] { MediaServerKind.Jellyfin, MediaServerKind.Plex, MediaServerKind.FnNas })
        {
            Test($"媒体批量：{kind}不借用新增Emby批量命令", () =>
            {
                using var fixture = new Fixture(kind);
                foreach (var command in new[] { ItemCommand.Play, ItemCommand.PlayFromBeginning, ItemCommand.Shuffle,
                    ItemCommand.Delete, ItemCommand.AddToPlaylist, ItemCommand.ResetMetadata,
                    ItemCommand.RefreshMetadata, ItemCommand.ScanLibraryFiles })
                {
                    if (kind == MediaServerKind.Jellyfin && command == ItemCommand.AddToPlaylist) continue;
                    Assert.False(ItemBatch.Allowed(command, fixture.Source, fixture.Entries[0], new(IsAdministrator: true)));
                }
                Assert.Equal(0, fixture.Http.Total);
            });
        }
        Test("媒体批量：Emby新建播放列表一次带全体成员", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            fixture.Http.Answer("Playlists", "{\"Id\":\"new-list\",\"ItemAddedCount\":3}");
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.AddToPlaylist,
                () => default, null, default, options: new(NewPlaylistName: " 新列表 "));
            Assert.Equal(3, result.Succeeded);
            Assert.True(result.Results.All(value => value.PlaylistId == "new-list"));
            var sent = fixture.Http.Only("Playlists");
            Assert.Equal("POST", sent.Method);
            Assert.Contains("Ids=101%2C102%2C103", sent.Url);
            Assert.Contains("Name=%E6%96%B0%E5%88%97%E8%A1%A8", sent.Url);
            Assert.Contains("UserId=user", sent.Url);
            Assert.Equal("", sent.Body);
        }));
        Test("媒体批量：Emby聚合写成功但响应无效仍保留确认且不重发", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            fixture.Http.Answer("Playlists", "not-json");
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.AddToPlaylist,
                () => default, null, default, options: new(NewPlaylistName: "列表"));
            Assert.Equal(3, result.Succeeded);
            Assert.True(result.Results.All(value => value.RefreshError is not null));
            Assert.Equal(1, fixture.Http.Count("Playlists"));
        }));
        Test("媒体批量：Emby新建播放列表保留聚合成功回执上下文", () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby, new BatchBackgroundTransport());
            using var context = new BatchContext();
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var callbackThread = Environment.CurrentManagedThreadId;
                var published = 0;
                var task = new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.AddToPlaylist,
                    () => default, null, default, _ =>
                    {
                        Assert.Equal(callbackThread, Environment.CurrentManagedThreadId);
                        published++;
                    }, options: new(NewPlaylistName: "列表"));
                context.DrainUntil(task);
                Assert.Equal(3, task.GetAwaiter().GetResult().Succeeded);
                Assert.Equal(3, published);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        });
        Test("媒体批量：Emby播放列表追加携带用户和去重选择", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            fixture.Http.Answer("Users/user/Items/list", "{\"Id\":\"list\",\"Type\":\"Playlist\",\"MediaType\":\"Video\",\"CanEditItems\":true}")
                .Answer("Playlists/list/Items", "{\"ItemAddedCount\":1}");
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.AddToPlaylist,
                () => default, null, default, options: new(PlaylistId: "list", SkipPlaylistDuplicates: false));
            Assert.Equal(3, result.Succeeded);
            Assert.Equal(3, fixture.Http.Count("Playlists/list/Items"));
            foreach (var sent in fixture.Http.SentTo("Playlists/list/Items"))
            {
                using var json = System.Text.Json.JsonDocument.Parse(sent.Body);
                Assert.Equal("user", json.RootElement.GetProperty("UserId").GetString());
                Assert.False(json.RootElement.GetProperty("SkipDuplicates").GetBoolean());
            }
            Assert.True(result.Results.All(value => value.PlaylistId == "list"));
        }));
        Test("媒体批量：Emby聚合提交在回调失败或取消后仍保留服务器确认", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            using var cancel = new CancellationTokenSource();
            fixture.Http.Answer("Playlists", "{\"Id\":\"new-list\"}");
            var called = 0;
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.AddToPlaylist,
                () => default, null, cancel.Token, _ =>
                {
                    called++;
                    cancel.Cancel();
                    throw new IOException("display failed");
                }, options: new(NewPlaylistName: "列表"));
            Assert.Equal(3, result.Succeeded);
            Assert.Equal(3, called);
            Assert.True(result.Results.All(value => value.RefreshError == "display failed"));
            Assert.Equal(1, fixture.Http.Total);
        }));
        Test("媒体批量：Emby聚合新建拒绝异账号条目并维持原回执顺序", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            using var another = new Fixture(MediaServerKind.Emby);
            fixture.Entries[1] = another.Entries[1];
            fixture.Http.Answer("Playlists", "{\"Id\":\"new-list\"}");
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.AddToPlaylist,
                () => default, null, default, options: new(NewPlaylistName: "列表"));
            Assert.Equal(2, result.Succeeded);
            Assert.Equal(ItemBatchStatus.Failed, result.Results[1].Status);
            Assert.Equal(fixture.Entries[1], result.Results[1].Entry);
            Assert.Contains("Ids=101%2C103", fixture.Http.Only("Playlists").Url);
            Assert.Equal(0, another.Http.Total);
        }));
        Test("媒体批量：Emby扫描与重置危险标识在HTTP前拒绝", () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            foreach (var invalid in new[] { "", ".", "..", "a/b", "a\\b", "a?b", "a#b", "a,b" })
            {
                Assert.Throws<ArgumentException>(() => fixture.Source.Client.ScanLibraryFilesAsync(invalid, default));
                Assert.Throws<ArgumentException>(() => fixture.Source.Client.ResetMetadataAsync(invalid, default));
                Assert.Throws<ArgumentException>(() => fixture.Source.Client.DeleteItemAsync(invalid, default));
                Assert.Throws<ArgumentException>(() => fixture.Source.Client.AddToPlaylistAsync(invalid, ["101"], true, default));
            }
            Assert.Equal(0, fixture.Http.Total);
        });
        Test("媒体批量：Emby刷新选项逐项生效且撤销后停止写入", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            fixture.Http.Answer("/Refresh", "");
            var allowed = true;
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.RefreshMetadata,
                () => new(IsAdministrator: allowed), null, default, receipt =>
                {
                    if (receipt.Status == ItemBatchStatus.Succeeded) allowed = false;
                }, options: new(RefreshMode: "all", ReplaceImages: true, ReplaceThumbnails: true));
            Assert.Equal(1, result.Succeeded);
            Assert.Equal(2, result.Failed);
            var sent = fixture.Http.Only("/Refresh");
            Assert.Contains("ReplaceAllMetadata=true", sent.Url);
            Assert.Contains("ReplaceAllImages=true", sent.Url);
            Assert.Contains("ReplaceThumbnailImages=true", sent.Url);
            Assert.Contains("MetadataRefreshMode=FullRefresh", sent.Url);
        }));
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Test($"媒体批量：{kind}部分失败不回滚成功且取消剩余零写", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                using var cancel = new CancellationTokenSource();
                fixture.Http.Answer("PlayedItems/101", "{\"Played\":true}")
                    .Fail("PlayedItems/102", HttpStatusCode.Forbidden)
                    .When("PlayedItems/102", cancel.Cancel);
                var result = await fixture.Execute(ItemCommand.MarkPlayed, cancel.Token);
                Assert.Equal(1, result.Succeeded);
                Assert.Equal(1, result.Failed);
                Assert.Equal(1, result.Canceled);
                Assert.Equal("POST", fixture.Http.Only("PlayedItems/101").Method);
                Assert.Equal(0, fixture.Http.Count("PlayedItems/103"));
            }));
            Test($"媒体批量：{kind}取消前没有请求", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                var result = await fixture.Execute(ItemCommand.Favorite, new CancellationToken(true));
                Assert.Equal(3, result.Canceled);
                Assert.Equal(0, fixture.Http.Total);
            }));
            Test($"媒体批量：{kind}逐项权限与账号隔离", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                using var another = new Fixture(kind);
                fixture.Http.Answer("Collections/set/Items", "");
                var denied = fixture.Entries[1].Item;
                denied.CanEditItems = false;
                fixture.Entries[2] = another.Entries[0];
                var result = await fixture.Execute(ItemCommand.AddToCollection);
                Assert.Equal(kind == MediaServerKind.Emby ? 1 : 2, result.Succeeded);
                Assert.Equal(1, fixture.Http.SentTo("Collections/set/Items").Count(request => request.Url.Contains("101")));
                Assert.Equal(0, another.Http.Total);
            }));
        }
        Test("媒体批量：权限撤销后后续合集写入停止", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Jellyfin);
            fixture.Http.Answer("Collections/set/Items", "");
            var allowed = true;
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.AddToCollection,
                () => new(IsAdministrator: false, CanManageCollections: allowed), "set", default,
                receipt => { if (receipt.Status == ItemBatchStatus.Succeeded) allowed = false; });
            Assert.Equal(1, result.Succeeded);
            Assert.Equal(2, result.Failed);
            Assert.Equal(1, fixture.Http.Total);
        }));
        Test("媒体批量：Plex补读失败保留成功且不重写", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Plex);
            fixture.Http.Answer("/:/scrobble", "").Fail("/library/metadata/", HttpStatusCode.InternalServerError);
            var result = await fixture.Execute(ItemCommand.MarkPlayed);
            Assert.Equal(3, result.Succeeded);
            Assert.True(result.Results.All(receipt => receipt.RefreshError is not null));
            Assert.Equal(3, fixture.Http.Count("/:/scrobble"));
            Assert.True(fixture.Http.SentTo("/:/scrobble").All(request => request.Method == "GET"));
            Assert.Equal(0, fixture.Http.Count("/PlayedItems/"));
            Assert.False(ItemBatch.Allowed(ItemCommand.Favorite, fixture.Source, fixture.Entries[0], default));
            fixture.Entries[0].Item.Type = EmbyItemType.BoxSet;
            Assert.False(ItemBatch.Allowed(ItemCommand.MarkPlayed, fixture.Source, fixture.Entries[0], default));
        }));
        Test("媒体批量：Plex开始补读前已发布写入确认", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Plex);
            var confirmed = 0;
            fixture.Http.Answer("/:/scrobble", "").Fail("/library/metadata/", HttpStatusCode.InternalServerError)
                .When("/library/metadata/", () => Assert.True(confirmed > 0));
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.MarkPlayed,
                () => default, null, default, receipt => { if (receipt.Status == ItemBatchStatus.Succeeded) confirmed++; });
            Assert.Equal(3, confirmed);
            Assert.Equal(3, result.Succeeded);
        }));
        Test("媒体批量：刷新回调异常不重做已确认写入", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            fixture.Http.Answer("FavoriteItems/", "");
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.Favorite,
                () => default, null, default, _ => throw new IOException("display failed"));
            Assert.Equal(3, result.Succeeded);
            Assert.Equal(3, fixture.Http.Total);
            Assert.True(result.Results.All(receipt => receipt.RefreshError == "display failed"));
        }));
        Test("媒体批量：合集关系已失效拒绝且不删除媒体", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Jellyfin);
            using var lifetime = new CancellationTokenSource();
            for (var i = 0; i < fixture.Entries.Length; i++) fixture.Entries[i] = fixture.Entries[i] with
            { Membership = new(fixture.Source, "set", fixture.Entries[i].Item.Id, lifetime.Token) };
            fixture.Http.Answer("Collections/set/Items", "");
            lifetime.Cancel();
            var result = await fixture.Execute(ItemCommand.RemoveFromCollection);
            Assert.Equal(3, result.Failed);
            Assert.Equal(0, fixture.Http.Total);
        }));
        Test("媒体批量：成功响应释放时退出账号仍保留已确认成功", () => Run(async () =>
        {
            var handler = new ReceiptDisposeHandler();
            using var fixture = new Fixture(MediaServerKind.Emby, handler);
            handler.AfterReceipt = fixture.Source.IdentityState.Lifetime.Cancel;
            var confirmations = 0;
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, fixture.Entries, ItemCommand.MarkPlayed,
                () => default, null, default, receipt => { if (receipt.Status == ItemBatchStatus.Succeeded) confirmations++; });
            Assert.Equal(1, result.Succeeded);
            Assert.Equal(2, result.Canceled);
            Assert.Equal(1, confirmations);
            Assert.Equal(1, handler.Writes);
        }));
    }

    private static void Run(Func<Task> run) => run().GetAwaiter().GetResult();

    private sealed class Fixture : IDisposable
    {
        public StubTransport Http { get; } = new();
        public EmbySession Session { get; }
        public EmbySessionScope Source { get; }
        public ItemBatchEntry[] Entries { get; }
        public Fixture(MediaServerKind kind, HttpMessageHandler? handler = null)
        {
            var secret = new DenySecrets();
            var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "batch-forbidden|no-io"));
            Session = new(new AppSettings(), new SettingsStore(paths, secret), new CredentialVault(secret), DeviceIdentity.Create("batch", "0"), handler ?? Http);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var transport = (EmbyHttp)typeof(EmbySession).GetField("_http", flags)!.GetValue(Session)!;
            var connection = new EmbyConnection(new Uri(kind == MediaServerKind.Emby ? "https://batch.invalid/emby/" : "https://batch.invalid/"),
                "placeholder", "user", "anonymous", "anonymous", DeviceIdentity.Create("batch", "0"))
            { ServerKind = kind, IsAdministrator = false, CanManageCollections = true };
            Source = new(Session, new EmbySessionScope.Identity(new(transport, connection),
                new ServerProfile { Id = "server", Name = "anonymous", Url = "https://batch.invalid/", ServerKind = kind },
                new AccountProfile { Id = "account", UserId = "user", Username = "anonymous" }));
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", flags)!.GetValue(Session)!;
            sources.Add(Source.Key, Source);
            typeof(EmbySession).GetField("_current", flags)!.SetValue(Session, Source);
            Entries = Enumerable.Range(101, 3).Select(id => new ItemBatchEntry(Source.Bind(new EmbyItem
            { Id = id.ToString(), Name = "影片 " + id, Type = EmbyItemType.Movie }))).ToArray();
        }
        public Task<ItemBatchResult> Execute(ItemCommand command, CancellationToken cancellationToken = default) =>
            new ItemBatchService().ExecuteAsync(Source, Entries, command,
                () => new(IsAdministrator: false, CanManageCollections: true), "set", cancellationToken);
        public void Dispose() => Session.Dispose();
    }

    private sealed class DenySecrets : ISecretProtector
    {
        public string Protect(string value) => throw new InvalidOperationException("No secret IO");
        public string Unprotect(string value) => throw new InvalidOperationException("No secret IO");
    }

    private sealed class ReceiptDisposeHandler : HttpMessageHandler
    {
        public Action? AfterReceipt { get; set; }
        public int Writes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.True(request.RequestUri!.AbsolutePath.EndsWith("PlayedItems/101", StringComparison.Ordinal));
            Writes++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ReceiptContent(() => AfterReceipt?.Invoke()) });
        }
    }

    private sealed class BatchContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Work, object? State)> _queue = new();
        public override void Post(SendOrPostCallback work, object? state) => _queue.Add((work, state));
        public void DrainUntil(Task task)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
                if (_queue.TryTake(out var next, 50)) next.Work(next.State);
            Assert.True(task.IsCompleted, "聚合回执必须在调用线程完成");
        }
        public void Dispose() => _queue.Dispose();
    }

    private sealed class BatchBackgroundTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.Run(() =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.True(request.RequestUri!.AbsolutePath.EndsWith("/Playlists", StringComparison.Ordinal));
                var body = "{\"Id\":\"new-list\"}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            }, cancellationToken);
    }
    private sealed class ReceiptContent(Action afterReceipt) : StringContent("{\"Played\":true}")
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) afterReceipt();
        }
    }
}
