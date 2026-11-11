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
    private sealed class ReceiptContent(Action afterReceipt) : StringContent("{\"Played\":true}")
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) afterReceipt();
        }
    }
}
