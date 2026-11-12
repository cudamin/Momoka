using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>Actual scope/client/HTTP operations against anonymous, strict in-memory responses.</summary>
internal static class ServerPlaylistTests
{
    private const string UserId = "11111111111111111111111111111111";
    private const string ListId = "22222222222222222222222222222222";
    private const string FirstId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SecondId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string EntryA = "cccccccccccccccccccccccccccccccc";
    private const string EntryB = "dddddddddddddddddddddddddddddddd";
    private const string EntryC = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";

    internal static void Register()
    {
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            Test($"服务器播放列表[{kind}]空清单可创建且显式视频种类", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                var accepted = 0;
                var receipt = await fixture.Client.CreatePlaylistAsync(" 自己的清单 ", [], default, () => accepted++);
                Assert.Equal(ListId, receipt.Id);
                Assert.Equal(1, accepted);
                var request = fixture.Http.Requests.Single();
                Assert.Equal("POST", request.Method);
                if (kind == MediaServerKind.Jellyfin)
                {
                    Assert.Equal("", request.Query);
                    using var json = JsonDocument.Parse(request.Body);
                    Assert.Equal("自己的清单", json.RootElement.GetProperty("Name").GetString());
                    Assert.Equal(0, json.RootElement.GetProperty("Ids").GetArrayLength());
                    Assert.Equal(0, json.RootElement.GetProperty("Users").GetArrayLength());
                    Assert.False(json.RootElement.GetProperty("IsPublic").GetBoolean());
                    Assert.Equal("Video", json.RootElement.GetProperty("MediaType").GetString());
                    Assert.Equal(UserId, json.RootElement.GetProperty("UserId").GetString());
                }
                else
                {
                    Assert.Contains("MediaType=Video", request.Query);
                    Assert.Contains("UserId=" + UserId, request.Query);
                    Assert.DoesNotContain("Ids=", request.Query);
                    Assert.Equal("", request.Body);
                }
                Assert.Equal(0, fixture.Http.LoginRequests);
            }));

            Test($"服务器播放列表[{kind}]创建一次带全部成员及原顺序", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                await fixture.Client.CreatePlaylistAsync("有片清单", [FirstId, SecondId, FirstId], default);
                var request = fixture.Http.Requests.Single();
                if (kind == MediaServerKind.Jellyfin)
                {
                    using var json = JsonDocument.Parse(request.Body);
                    Assert.True(json.RootElement.GetProperty("Ids").EnumerateArray().Select(x => x.GetString())
                        .SequenceEqual([FirstId, SecondId, FirstId]));
                }
                else Assert.Contains("Ids=" + FirstId + "%2C" + SecondId + "%2C" + FirstId, request.Query);
            }));

            Test($"服务器播放列表[{kind}]原始成员保留同媒体的两条entry身份", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                var result = await fixture.Source.ExecuteAsync((client, token) => client.GetPlaylistItemsAsync(ListId, 0, 200, token), default);
                Assert.Equal(3, result.Items.Count);
                Assert.Equal(FirstId, result.Items[0].Id);
                Assert.Equal(FirstId, result.Items[1].Id);
                Assert.Equal(EntryA, result.Items[0].PlaylistItemId);
                Assert.Equal(EntryB, result.Items[1].PlaylistItemId);
                Assert.True(result.Items.All(item => ReferenceEquals(item.Source!.UserStateOwner, fixture.Source.UserStateOwner)));
                Assert.Equal(0, fixture.Http.Writes);
            }));

            Test($"服务器播放列表[{kind}]移除传EntryIds且不删除媒体", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                var confirmed = 0;
                await fixture.Client.RemoveFromPlaylistAsync(ListId, [EntryB], default, () => confirmed++);
                var write = fixture.Http.Requests.Single(x => x.Method != "GET");
                Assert.Equal(kind == MediaServerKind.Jellyfin ? "DELETE" : "POST", write.Method);
                Assert.Equal("/proxy/" + (kind == MediaServerKind.Emby ? "emby/" : "") + "Playlists/" + ListId
                    + (kind == MediaServerKind.Emby ? "/Items/Delete" : "/Items"), write.Path);
                Assert.Equal("?EntryIds=" + EntryB, write.Query);
                Assert.DoesNotContain(FirstId, write.Path + write.Query);
                Assert.Equal(1, confirmed);
                Assert.Equal(1, fixture.Http.Writes);
            }));

            Test($"服务器播放列表[{kind}]排序使用entry和零基目标而非mediaId", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                var confirmed = 0;
                await fixture.Client.MovePlaylistItemAsync(ListId, EntryB, 0, default, () => confirmed++);
                var write = fixture.Http.Requests.Single(x => x.Method != "GET");
                Assert.Equal("POST", write.Method);
                Assert.True(write.Path.EndsWith("/Playlists/" + ListId + "/Items/" + EntryB + "/Move/0", StringComparison.Ordinal));
                Assert.DoesNotContain(FirstId, write.Path);
                Assert.Equal("", write.Query);
                Assert.Equal(1, confirmed);
                Assert.Equal(kind == MediaServerKind.Jellyfin ? 4 : 2, fixture.Http.PermissionReads);
            }));

            foreach (var grant in new bool?[] { false, null })
                Test($"服务器播放列表[{kind}]未确认编辑授权[{grant}]不借管理员或合集权限", () => Run(async () =>
                {
                    using var fixture = new Fixture(kind, admin: true);
                    fixture.Http.CanEdit = grant;
                    Assert.False(await fixture.Client.GetPlaylistEditPermissionAsync(ListId, default));
                    await ThrowsAsync<UnauthorizedAccessException>(() => fixture.Client.AddToPlaylistAsync(ListId, [FirstId], false, default));
                    await ThrowsAsync<UnauthorizedAccessException>(() => fixture.Client.RemoveFromPlaylistAsync(ListId, [EntryA], default));
                    await ThrowsAsync<UnauthorizedAccessException>(() => fixture.Client.MovePlaylistItemAsync(ListId, EntryA, 1, default));
                    Assert.Equal(0, fixture.Http.Writes);
                }));

            Test($"服务器播放列表[{kind}]权限403或404只读仍保留目录", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
                {
                    fixture.Http.PermissionStatus = status;
                    Assert.False(await fixture.Client.GetPlaylistEditPermissionAsync(ListId, default));
                    await ThrowsAsync<UnauthorizedAccessException>(() => fixture.Client.AddToPlaylistAsync(ListId, [FirstId], false, default));
                }
                Assert.Equal(0, fixture.Http.Writes);
            }));

            Test($"服务器播放列表[{kind}]列表不是Playlist或不同身份拒绝写", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                fixture.Http.PermissionIdentity = "ffffffffffffffffffffffffffffffff";
                Assert.False(await fixture.Client.GetPlaylistEditPermissionAsync(ListId, default));
                await ThrowsAsync<UnauthorizedAccessException>(() => fixture.Client.AddToPlaylistAsync(ListId, [FirstId], false, default));
                Assert.Equal(0, fixture.Http.Writes);
            }));

            Test($"服务器播放列表[{kind}]追加按各自协议且每笔重读权限", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                await fixture.Client.AddToPlaylistAsync(ListId, [FirstId, SecondId, FirstId], false, default);
                var write = fixture.Http.Requests.Single(x => x.Method != "GET");
                Assert.Equal("POST", write.Method);
                if (kind == MediaServerKind.Jellyfin)
                {
                    Assert.Contains("Ids=" + FirstId + "%2C" + SecondId + "%2C" + FirstId, write.Query);
                    Assert.Contains("UserId=" + UserId, write.Query);
                    Assert.Equal("", write.Body);
                    Assert.DoesNotContain("SkipDuplicates", write.Query);
                }
                else
                {
                    using var json = JsonDocument.Parse(write.Body);
                    Assert.Equal(FirstId + "," + SecondId + "," + FirstId, json.RootElement.GetProperty("Ids").GetString());
                    Assert.False(json.RootElement.GetProperty("SkipDuplicates").GetBoolean());
                    Assert.Equal(UserId, json.RootElement.GetProperty("UserId").GetString());
                    Assert.Equal("", write.Query);
                }
                fixture.Http.CanEdit = false;
                await ThrowsAsync<UnauthorizedAccessException>(() => fixture.Client.AddToPlaylistAsync(ListId, [SecondId], false, default));
                Assert.Equal(1, fixture.Http.Writes);
            }));

            foreach (var body in new[] { "", "broken-json", "{\"Id\":null}" })
                Test($"服务器播放列表[{kind}]创建响应缺Id保留确认[{body.Length}]", () => Run(async () =>
                {
                    using var fixture = new Fixture(kind);
                    fixture.Http.WriteBody = body;
                    var confirmed = 0;
                    var receipt = await fixture.Client.CreatePlaylistAsync("创建", [], default, () => confirmed++);
                    Assert.True(string.IsNullOrEmpty(receipt.Id));
                    Assert.Equal(1, confirmed);
                    Assert.Equal(1, fixture.Http.Writes);
                }));

            Test($"服务器播放列表[{kind}]2xx后附属JSON断线仍已接受且只写一次", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                fixture.Http.Receipt = () => new BrokenContent();
                var accepted = false;
                var receipt = await fixture.Client.CreatePlaylistAsync("创建", [], default, () => accepted = true);
                Assert.True(accepted);
                Assert.True(string.IsNullOrEmpty(receipt.Id));
                Assert.Equal(1, fixture.Http.Writes);
            }));

            Test($"服务器播放列表[{kind}]2xx回调取消或抛错不抹除成功", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                using var cancel = new CancellationTokenSource();
                var receipt = await fixture.Client.CreatePlaylistAsync("创建", [], cancel.Token, () =>
                {
                    cancel.Cancel();
                    throw new IOException("inert display failed");
                });
                Assert.Equal(1, fixture.Http.Writes);
                Assert.NotNull(receipt);
            }));

            foreach (var failure in new[] { "io", "timeout", "500", "401-after-success" })
                Test($"服务器播放列表[{kind}]已发送未知结果[{failure}]不触发认证重写", () => Run(async () =>
                {
                    using var fixture = new Fixture(kind);
                    fixture.Http.WriteFailure = failure;
                    await ThrowsAsync<PlaylistWriteUncertainException>(() => fixture.Source.ExecuteAsync(
                        (client, token) => client.CreatePlaylistAsync("创建", [], token), default));
                    Assert.Equal(1, fixture.Http.Writes);
                    Assert.Equal(0, fixture.Http.LoginRequests);
                }));

            foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
                Test($"服务器播放列表[{kind}]明确拒绝[{(int)status}]保留HTTP状态", () => Run(async () =>
                {
                    using var fixture = new Fixture(kind);
                    fixture.Http.WriteStatus = status;
                    var error = await CaptureAsync<EmbyApiException>(() => fixture.Client.CreatePlaylistAsync("创建", [], default));
                    Assert.Equal(status, error.StatusCode);
                    Assert.Equal(1, fixture.Http.Writes);
                }));

            Test($"服务器播放列表[{kind}]调用前取消零请求", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                using var cancel = new CancellationTokenSource();
                cancel.Cancel();
                await ThrowsAsync<OperationCanceledException>(() => fixture.Client.CreatePlaylistAsync("创建", [], cancel.Token));
                await ThrowsAsync<OperationCanceledException>(() => fixture.Client.AddToPlaylistAsync(ListId, [FirstId], false, cancel.Token));
                Assert.Equal(0, fixture.Http.Requests.Count);
            }));

            Test($"服务器播放列表[{kind}]读取授权途中撤销由发送前守卫拒绝", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                var revision = fixture.Source.PermissionRevision;
                fixture.Http.AfterPermission = () => fixture.Session.ReplaceClient(fixture.Source,
                    fixture.Source.CurrentConnection with { IsAdministrator = true });
                await ThrowsAsync<OperationCanceledException>(() => fixture.Source.ExecuteAsync((client, token) =>
                    client.AddToPlaylistAsync(ListId, [FirstId], false, token, beforeWrite: () =>
                    {
                        fixture.Source.ThrowIfUnavailable();
                        if (revision != fixture.Source.PermissionRevision) throw new OperationCanceledException();
                    }), default));
                Assert.Equal(0, fixture.Http.Writes);
            }));

            Test($"服务器播放列表[{kind}]旧源退出不可借同编号的新源", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                using var other = new Fixture(kind);
                fixture.Source.IdentityState.Lifetime.Cancel();
                await ThrowsAsync<OperationCanceledException>(() => fixture.Source.ExecuteAsync(
                    (client, token) => client.RemoveFromPlaylistAsync(ListId, [EntryA], token), default));
                Assert.Equal(0, fixture.Http.Writes);
                Assert.Equal(0, other.Http.Writes);
            }));

            Test($"服务器播放列表[{kind}]成功头后来源退出仍可保留成功账本", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                var confirmed = false;
                fixture.Http.AfterWrite = () => fixture.Source.IdentityState.Lifetime.Cancel();
                await ThrowsAsync<OperationCanceledException>(() => fixture.Source.ExecuteAsync((client, token) =>
                    client.CreatePlaylistAsync("创建", [], token, () => confirmed = true), default));
                Assert.True(confirmed);
                Assert.Equal(1, fixture.Http.Writes);
            }));

            Test($"服务器播放列表[{kind}]排序负数、超界和不存在entry拒绝HTTP写", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                await ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Client.MovePlaylistItemAsync(ListId, EntryA, -1, default));
                await ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Client.MovePlaylistItemAsync(ListId, EntryA, 3, default));
                await ThrowsAsync<InvalidOperationException>(() => fixture.Client.MovePlaylistItemAsync(ListId, "missing", 0, default));
                Assert.Equal(0, fixture.Http.Writes);
            }));

            Test($"服务器播放列表[{kind}]成员与清单标识注入在请求前拒绝", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                foreach (var id in new[] { "", ".", "..", "x/y", "x\\y", "x?y", "x#y", "x,y", "x\0y" })
                {
                    await ThrowsAsync<ArgumentException>(() => fixture.Client.RemoveFromPlaylistAsync(id, [EntryA], default));
                    await ThrowsAsync<ArgumentException>(() => fixture.Client.RemoveFromPlaylistAsync(ListId, [id], default));
                    await ThrowsAsync<ArgumentException>(() => fixture.Client.MovePlaylistItemAsync(ListId, id, 0, default));
                    await ThrowsAsync<ArgumentException>(() => fixture.Client.AddToPlaylistAsync(ListId, [id], false, default));
                }
                await ThrowsAsync<ArgumentException>(() => fixture.Client.RemoveFromPlaylistAsync(ListId, [], default));
                await ThrowsAsync<ArgumentException>(() => fixture.Client.CreatePlaylistAsync(" ", [], default));
                Assert.Equal(0, fixture.Http.Requests.Count);
            }));
        }

        foreach (var version in new[] { "10.10.7", "12.2.0" })
            Test($"Jellyfin服务器清单目录[{version}]短原始页继续读取且各列表独立授权", () => Run(async () =>
            {
                using var fixture = new Fixture(MediaServerKind.Jellyfin, version: version);
                fixture.Http.DirectoryPages = [[ListId], [SecondId], []];
                fixture.Http.DeniedLists.Add(SecondId);
                var items = await fixture.Source.ExecuteAsync((client, token) => client.GetServerPlaylistsAsync(token), default);
                Assert.Equal(2, items.Count);
                Assert.True(items[0].CanEditItems == true);
                Assert.False(items[1].CanEditItems == true);
                Assert.Equal(2, fixture.Http.DirectoryReads);
                Assert.Equal(4, fixture.Http.PermissionReads);
                var directory = fixture.Http.Requests.Where(x => x.Query.Contains("IncludeItemTypes=Playlist", StringComparison.Ordinal)).ToArray();
                Assert.Contains("StartIndex=1", directory[1].Query);
                if (version == "12.2.0")
                {
                    Assert.Equal("/proxy/Items", directory[0].Path);
                    Assert.Contains("UserId=" + UserId, directory[0].Query);
                }
                else Assert.Equal("/proxy/Users/" + UserId + "/Items", directory[0].Path);
                Assert.True(fixture.Http.Requests.All(x => x.JellyfinHeader && !x.EmbyHeader));
                Assert.Equal(0, fixture.Http.Writes);
            }));

        Test("Emby服务器清单目录保留只读和未知但选择器仅可编辑", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Emby);
            fixture.Http.DirectoryPages = [[ListId], [SecondId], []];
            fixture.Http.DeniedLists.Add(SecondId);
            var all = await fixture.Client.GetServerPlaylistsAsync(default);
            Assert.Equal(2, all.Count);
            Assert.Equal(false, all[1].CanEditItems);
            fixture.Http.DirectoryReads = 0;
            var editable = await fixture.Client.GetEditableVideoPlaylistsAsync(default);
            Assert.Equal(1, editable.Count);
            Assert.Equal(ListId, editable[0].Id);
            Assert.Equal(0, fixture.Http.PermissionReads);
            Assert.True(fixture.Http.Requests.All(x => x.EmbyHeader && !x.JellyfinHeader));
        }));

        Test("Jellyfin跳过重复是本次读取的客户端选择不发Emby开关", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Jellyfin);
            await fixture.Client.AddToPlaylistAsync(ListId, [FirstId, SecondId, "ffffffffffffffffffffffffffffffff", "ffffffffffffffffffffffffffffffff"], true, default);
            var write = fixture.Http.Requests.Single(x => x.Method != "GET");
            Assert.Contains("Ids=ffffffffffffffffffffffffffffffff", write.Query);
            Assert.DoesNotContain(FirstId, write.Query);
            Assert.DoesNotContain(SecondId, write.Query);
            Assert.DoesNotContain("SkipDuplicates", write.Query + write.Body);
            Assert.Equal(4, fixture.Http.PermissionReads);
        }));

        Test("Jellyfin已全在清单中不写也不伪造服务器确认", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Jellyfin);
            var accepted = 0;
            var guarded = 0;
            await fixture.Client.AddToPlaylistAsync(ListId, [FirstId], true, default, () => accepted++, () => guarded++);
            Assert.Equal(0, fixture.Http.Writes);
            Assert.Equal(0, accepted);
            Assert.Equal(1, guarded);
        }));

        Test("Jellyfin目录后页失败不交半份清单", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Jellyfin);
            fixture.Http.DirectoryPages = [[ListId], [SecondId]];
            fixture.Http.DirectoryFailAt = 1;
            await ThrowsAsync<EmbyApiException>(() => fixture.Client.GetServerPlaylistsAsync(default));
            Assert.Equal(2, fixture.Http.DirectoryReads);
            Assert.Equal(0, fixture.Http.Writes);
        }));

        foreach (var corruption in new[] { "empty", "repeated", "changed-total" })
            Test($"Jellyfin成员分页[{corruption}]不得借半份清单去重或排序", () => Run(async () =>
            {
                using var fixture = new Fixture(MediaServerKind.Jellyfin);
                fixture.Http.MemberCorruption = corruption;
                await ThrowsAsync<InvalidOperationException>(() => fixture.Client.AddToPlaylistAsync(ListId, [SecondId], true, default));
                fixture.Http.MemberReads = 0;
                await ThrowsAsync<InvalidOperationException>(() => fixture.Client.MovePlaylistItemAsync(ListId, EntryA, 1, default));
                Assert.Equal(0, fixture.Http.Writes);
            }));

        Test("Jellyfin成员后页失败不发送去重追加", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Jellyfin);
            fixture.Http.MemberCorruption = "failure";
            await ThrowsAsync<EmbyApiException>(() => fixture.Client.AddToPlaylistAsync(ListId, [SecondId], true, default));
            Assert.Equal(2, fixture.Http.MemberReads);
            Assert.Equal(0, fixture.Http.Writes);
        }));

        Test("Jellyfin授权读取返回401后重新核同用户授权且拒绝新denied", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Jellyfin);
            fixture.Http.BeforeUnauthorizedPermission = () =>
            {
                fixture.Session.ReplaceClient(fixture.Source, fixture.Source.CurrentConnection with { AccessToken = "new-offline-placeholder" });
                fixture.Http.CanEdit = false;
            };
            await ThrowsAsync<UnauthorizedAccessException>(() => fixture.Source.ExecuteAsync((client, token) =>
                client.AddToPlaylistAsync(ListId, [FirstId], false, token), default));
            Assert.Equal(3, fixture.Http.PermissionReads);
            Assert.Equal(0, fixture.Http.Writes);
            Assert.Equal(0, fixture.Http.LoginRequests);
        }));

        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
            foreach (var mediaType in new string?[] { "Audio", "Unknown", null })
                Test($"影视播放列表[{kind}]目标种类[{mediaType}]不能借CanEdit写视频", () => Run(async () =>
                {
                    using var fixture = new Fixture(kind, admin: true);
                    fixture.Http.ListMediaType = mediaType;
                    Assert.False(await fixture.Client.GetPlaylistEditPermissionAsync(ListId, default));
                    Assert.Equal(0, (await fixture.Client.GetEditableVideoPlaylistsAsync(default)).Count);
                    await ThrowsAsync<UnauthorizedAccessException>(() => fixture.Client.AddToPlaylistAsync(ListId, [FirstId], false, default));
                    await ThrowsAsync<UnauthorizedAccessException>(() => fixture.Client.RemoveFromPlaylistAsync(ListId, [EntryA], default));
                    await ThrowsAsync<UnauthorizedAccessException>(() => fixture.Client.MovePlaylistItemAsync(ListId, EntryA, 0, default));
                    Assert.Equal(3, (await fixture.Client.GetPlaylistItemsAsync(ListId, 0, 200, default)).Items.Count);
                    Assert.Equal(0, fixture.Http.Writes);
                }));

        Test("Jellyfin批量新建使用一次单用户视频DTO", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Jellyfin);
            var entries = new[] { new ItemBatchEntry(fixture.Source.Bind(new() { Id = FirstId, Type = EmbyItemType.Movie })),
                new ItemBatchEntry(fixture.Source.Bind(new() { Id = SecondId, Type = EmbyItemType.Episode })) };
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, entries, ItemCommand.AddToPlaylist,
                () => new(IsAdministrator: false), null, default, options: new(NewPlaylistName: "批量"));
            Assert.Equal(2, result.Succeeded);
            Assert.Equal(1, fixture.Http.Writes);
            using var json = JsonDocument.Parse(fixture.Http.Requests.Single(x => x.Method != "GET").Body);
            Assert.Equal(2, json.RootElement.GetProperty("Ids").GetArrayLength());
            Assert.False(json.RootElement.GetProperty("IsPublic").GetBoolean());
        }));

        Test("Jellyfin批量首笔确认后后续撤销正常分成功失败", () => Run(async () =>
        {
            using var fixture = new Fixture(MediaServerKind.Jellyfin);
            var entries = new[] { new ItemBatchEntry(fixture.Source.Bind(new() { Id = FirstId, Type = EmbyItemType.Movie })),
                new ItemBatchEntry(fixture.Source.Bind(new() { Id = SecondId, Type = EmbyItemType.Episode })) };
            var result = await new ItemBatchService().ExecuteAsync(fixture.Source, entries, ItemCommand.AddToPlaylist,
                () => default, null, default, completed: _ => fixture.Http.CanEdit = false,
                options: new(PlaylistId: ListId, SkipPlaylistDuplicates: false));
            Assert.Equal(1, result.Succeeded);
            Assert.Equal(1, result.Failed);
            Assert.Equal(1, fixture.Http.Writes);
        }));

        foreach (var type in new[] { EmbyItemType.Audio, EmbyItemType.Series, EmbyItemType.Season, EmbyItemType.Folder,
            EmbyItemType.BoxSet, EmbyItemType.Playlist, EmbyItemType.Person })
            Test($"影视单项服务器清单不扩到容器与音乐[{type}]", () =>
            {
                var item = new EmbyItem { Id = FirstId, Type = type };
                Assert.False(ServerPlaylistItems.CanAddVideo(item));
                Assert.False(ItemMenu.For(item, jellyfin: true).Any(x => x.Command == ItemCommand.AddToPlaylist));
                Assert.False(ItemMenu.Allows(ItemCommand.AddToPlaylist, default, MediaServerKind.Jellyfin, item));
            });

        foreach (var kind in new[] { MediaServerKind.Plex, MediaServerKind.FnNas, MediaServerKind.FnMusic })
            Test($"服务器清单[{kind}]不借Emby接口", () => Run(async () =>
            {
                using var fixture = new Fixture(kind);
                await ThrowsAsync<NotSupportedException>(() => fixture.Client.GetServerPlaylistsAsync(default));
                await ThrowsAsync<NotSupportedException>(() => fixture.Client.CreatePlaylistAsync("创建", [], default));
                await ThrowsAsync<NotSupportedException>(() => fixture.Client.RemoveFromPlaylistAsync(ListId, [EntryA], default));
                Assert.Equal(0, fixture.Http.Requests.Count);
            }));
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception => await CaptureAsync<T>(action);

    private static async Task<T> CaptureAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T error) { return error; }
        throw new AssertionException("Expected " + typeof(T).Name);
    }

    private static void Run(Func<Task> action) => action().GetAwaiter().GetResult();

    private sealed class Fixture : IDisposable
    {
        internal Transport Http { get; }
        internal EmbySession Session { get; }
        internal EmbySessionScope Source { get; }
        internal EmbyClient Client => Source.Client;

        internal Fixture(MediaServerKind kind, bool admin = false, string version = "10.10.7")
        {
            Http = new();
            var secret = new DenySecrets();
            Session = new(new AppSettings(), new SettingsStore(new AppPaths(Path.Combine(Path.GetTempPath(), "playlist-forbidden|no-io")), secret),
                new CredentialVault(secret), DeviceIdentity.Create("playlist-fixture", "0"), Http);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var transport = (EmbyHttp)typeof(EmbySession).GetField("_http", flags)!.GetValue(Session)!;
            var connection = new EmbyConnection(new Uri("https://playlists.invalid/proxy/" + (kind == MediaServerKind.Emby ? "emby/" : "")),
                "offline-placeholder", UserId, "anonymous", "anonymous", DeviceIdentity.Create("playlist-fixture", "0"))
            { ServerKind = kind, ServerVersion = version, IsAdministrator = admin, CanManageCollections = true };
            Source = new(Session, new EmbySessionScope.Identity(new(transport, connection),
                new ServerProfile { Id = "server", Name = "anonymous", Url = "https://playlists.invalid/proxy/", ServerKind = kind },
                new AccountProfile { Id = "account", UserId = UserId, Username = "anonymous" }));
            var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", flags)!.GetValue(Session)!;
            sources.Add(Source.Key, Source);
            typeof(EmbySession).GetField("_current", flags)!.SetValue(Session, Source);
        }

        public void Dispose() => Session.Dispose();
    }

    private sealed class DenySecrets : ISecretProtector
    {
        public string Protect(string value) => throw new InvalidOperationException("No secret IO");
        public string Unprotect(string value) => throw new InvalidOperationException("No secret IO");
    }

    private sealed record Request(string Method, string Path, string Query, string Body, bool EmbyHeader, bool JellyfinHeader);

    private sealed class Transport : HttpMessageHandler
    {
        internal List<Request> Requests { get; } = [];
        internal bool? CanEdit { get; set; } = true;
        internal string? ListMediaType { get; set; } = "Video";
        internal string? PermissionIdentity { get; set; }
        internal HttpStatusCode PermissionStatus { get; set; } = HttpStatusCode.OK;
        internal HttpStatusCode WriteStatus { get; set; } = HttpStatusCode.NoContent;
        internal string? WriteFailure { get; set; }
        internal string WriteBody { get; set; } = "{\"Id\":\"" + ListId + "\"}";
        internal Func<HttpContent>? Receipt { get; set; }
        internal Action? AfterPermission { get; set; }
        internal Action? AfterWrite { get; set; }
        internal Action? BeforeUnauthorizedPermission { get; set; }
        internal string[][] DirectoryPages { get; set; } = [[ListId]];
        internal HashSet<string> DeniedLists { get; } = [];
        internal int DirectoryReads { get; set; }
        internal int? DirectoryFailAt { get; set; }
        internal string? MemberCorruption { get; set; }
        internal int MemberReads { get; set; }
        internal int PermissionReads { get; private set; }
        internal int Writes { get; private set; }
        internal int LoginRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("playlists.invalid", request.RequestUri!.Host);
            var path = request.RequestUri.AbsolutePath;
            var query = request.RequestUri.Query;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(request.Method.Method, path, query, body,
                request.Headers.Contains("X-Emby-Token"), request.Headers.Authorization?.Scheme == "MediaBrowser"));
            Assert.DoesNotContain("offline-placeholder", query + path);
            if (path.Contains("Authenticate", StringComparison.Ordinal))
            {
                LoginRequests++;
                throw new AssertionException("No real authentication permitted");
            }
            if (request.Method != HttpMethod.Get)
            {
                Assert.Contains("/Playlists", path);
                Writes++;
                if (WriteFailure == "io") throw new IOException("anonymous disconnect");
                if (WriteFailure == "timeout") throw new HttpRequestException("anonymous timeout");
                if (WriteFailure == "401-after-success") throw new EmbyApiException("unknown server outcome");
                if (WriteFailure == "500") return Json("", HttpStatusCode.InternalServerError);
                if (path.EndsWith("/Playlists", StringComparison.Ordinal) && WriteStatus == HttpStatusCode.NoContent)
                    return new(HttpStatusCode.OK) { Content = Receipt?.Invoke() ?? new ReceiptContent(WriteBody, () => AfterWrite?.Invoke()) };
                return new(WriteStatus) { Content = Receipt?.Invoke() ?? new ReceiptContent("", () => AfterWrite?.Invoke()) };
            }
            if (query.Contains("IncludeItemTypes=Playlist", StringComparison.Ordinal))
            {
                var pageIndex = DirectoryReads++;
                if (pageIndex == DirectoryFailAt) return Json("", HttpStatusCode.InternalServerError);
                var page = DirectoryPages[Math.Min(pageIndex, DirectoryPages.Length - 1)];
                var lists = page.Select(id => new
                {
                    Id = id,
                    Name = "清单",
                    Type = "Playlist",
                    CanEditItems = DeniedLists.Contains(id) ? false : CanEdit,
                    MediaType = ListMediaType
                }).ToArray();
                return Json(JsonSerializer.Serialize(new { Items = lists, TotalRecordCount = DirectoryPages.Sum(x => x.Length) }));
            }
            if (path.Contains("/Users/" + UserId, StringComparison.Ordinal) && path.Contains("/Playlists/", StringComparison.Ordinal)
                || path.EndsWith("/Items/" + ListId, StringComparison.Ordinal)
                || path.EndsWith("/Items/" + SecondId, StringComparison.Ordinal))
            {
                PermissionReads++;
                if (BeforeUnauthorizedPermission is { } before)
                {
                    BeforeUnauthorizedPermission = null;
                    before();
                    return Json("", HttpStatusCode.Unauthorized);
                }
                var userPermission = path.Contains("/Playlists/", StringComparison.Ordinal);
                var identity = PermissionIdentity ?? (userPermission ? UserId : path.Split('/')[^1]);
                var listDenied = DeniedLists.Any(x => path.Contains("/Playlists/" + x + "/", StringComparison.Ordinal));
                var json = userPermission
                    ? JsonSerializer.Serialize(new { UserId = identity, CanEdit = listDenied ? false : CanEdit })
                    : JsonSerializer.Serialize(new
                    {
                        Id = identity,
                        Type = "Playlist",
                        CanEditItems = CanEdit,
                        MediaType = ListMediaType
                    });
                AfterPermission?.Invoke();
                return Json(json, PermissionStatus);
            }
            if (path.EndsWith("/Playlists/" + ListId + "/Items", StringComparison.Ordinal)
                || query.Contains("ParentId=" + ListId, StringComparison.Ordinal))
            {
                var memberPage = MemberReads++;
                if (MemberCorruption is not null)
                {
                    if (memberPage > 0 && MemberCorruption == "failure") return Json("", HttpStatusCode.InternalServerError);
                    var entries = memberPage > 0 && MemberCorruption == "empty"
                        ? Array.Empty<object>()
                        : new object[] { new { Id = FirstId, Type = "Movie", PlaylistItemId = EntryA } };
                    return Json(JsonSerializer.Serialize(new
                    {
                        Items = entries,
                        TotalRecordCount = memberPage > 0 && MemberCorruption == "changed-total" ? 3 : 2
                    }));
                }
                return Json(JsonSerializer.Serialize(new
                {
                    Items = new[]
                    {
                        new { Id = FirstId, Type = "Movie", PlaylistItemId = EntryA },
                        new { Id = FirstId, Type = "Movie", PlaylistItemId = EntryB },
                        new { Id = SecondId, Type = "Episode", PlaylistItemId = EntryC }
                    },
                    TotalRecordCount = 3
                }));
            }
            return Json("", HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class ReceiptContent(string body, Action afterDispose) : StringContent(body)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) afterDispose();
        }
    }

    private sealed class BrokenContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new IOException("anonymous receipt disconnected"));
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
