using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Services;
using Momoka.Shell.ViewModels;

namespace Momoka.ServerListsOwnershipTests;

/// <summary>Actual Core/service/VM code, with only XAML values and anonymous network endpoints substituted.</summary>
internal static class Program
{
    private static async Task<int> Main()
    {
        var cases = new (string Name, Func<Task> Run)[]
        {
            ("旧未知写迟到不能污染已打开的另一账号", LateUnknownWrite),
            ("旧刷新失败迟到不能锁住重入后的另一账号", LateRefreshFailure),
            ("旧读取失败迟到不能覆盖新账号成员或通知", LateReadFailure),
            ("Members.Reset同步离页后旧读取不再Add", ReentrantMemberCancel),
            ("Lists.Reset同步离页后旧目录不再Add", ReentrantDirectoryCancel),
            ("Members.Reset同步切账号后旧内容不回填", ReentrantAccountSwitch),
            ("Plex成员声明仍有剩余却空页拒绝半份结果", PlexEmptyMembers),
            ("Plex成员后页总数改变拒绝混合结果", PlexChangedMembers),
            ("Plex成员重复页拒绝循环或重复内容", PlexRepeatedMembers),
            ("Plex目录声明空缺页拒绝半份列表", PlexEmptyDirectory),
            ("Plex目录后页总数改变拒绝混合列表", PlexChangedDirectory),
            ("Plex目录重复页拒绝循环或重复列表", PlexRepeatedDirectory),
            ("Plex无总数成员按原始满页继续读取", PlexUnknownMemberTotal),
            ("旧账号退出后服务不借其他账号的生命周期", OriginalScopeLifetime)
        };
        var failures = 0;
        foreach (var (name, run) in cases)
        {
            try { await run().WaitAsync(TimeSpan.FromSeconds(15)); Console.WriteLine("通过 " + name); }
            catch (Exception error) { failures++; Console.WriteLine("失败 " + name + "\n" + error); }
        }
        Console.WriteLine($"共 {cases.Length} 项，通过 {cases.Length - failures}，失败 {failures}，跳过 0");
        return failures == 0 ? 0 : 1;
    }

    private static async Task LateUnknownWrite()
    {
        using var f = new Fixture();
        await f.OpenA();
        f.Vm.SelectedMember = f.Vm.Members[0];
        using var gate = new Gate();
        f.Transport.WriteA = async () => { await gate.Enter(); throw new IOException("anonymous delivery unknown"); };
        var old = f.Vm.RemoveAsync();
        await gate.Entered.Task;
        await f.OpenB();
        var fresh = f.Vm.OpenedList;
        gate.Release();
        await old;
        Check(ReferenceEquals(fresh, f.Vm.OpenedList) && f.Vm.Editable && f.Vm.Members.All(item => item.Item.Name.StartsWith("b-", StringComparison.Ordinal)),
            "旧未知写改变新账号清单/编辑状态");
        Check(f.Transport.Writes == 1, "未知写重复发送");
    }

    private static async Task LateRefreshFailure()
    {
        using var f = new Fixture();
        await f.OpenA();
        f.Vm.SelectedMember = f.Vm.Members[0];
        using var gate = new Gate();
        f.Transport.ReadA = async () => { if (f.Transport.Writes > 0) { await gate.Enter(); throw new IOException("anonymous late refresh"); } };
        var old = f.Vm.RemoveAsync();
        await gate.Entered.Task;
        await f.OpenB();
        var fresh = f.Vm.OpenedList;
        gate.Release();
        await old;
        Check(ReferenceEquals(fresh, f.Vm.OpenedList) && f.Vm.Editable && f.Vm.Members.Count == 3 && !f.Vm.NoticeOpen,
            "旧刷新失败污染新账号");
        Check(f.Transport.Writes == 1, "确认写刷新失败重发");
    }

    private static async Task LateReadFailure()
    {
        using var f = new Fixture();
        await f.Vm.ReloadAsync();
        using var gate = new Gate();
        f.Transport.ReadA = async () => { await gate.Enter(); throw new IOException("anonymous late read"); };
        var old = f.Vm.OpenAsync(f.Vm.Lists[0]);
        await gate.Entered.Task;
        await f.OpenB();
        gate.Release();
        await old;
        Check(f.Vm.Scope!.Key == f.B.Key && f.Vm.Members.All(item => item.Item.Name.StartsWith("b-", StringComparison.Ordinal))
            && !f.Vm.NoticeOpen, "旧读取失败覆盖新账号");
    }

    private static async Task ReentrantMemberCancel()
    {
        using var f = new Fixture();
        await f.OpenA();
        var values = f.Vm.Members;
        var resets = 0;
        values.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Reset && ++resets == 1) f.Vm.Cancel();
        };
        await f.Vm.ReloadAsync();
        Check(f.Vm.Members.Count == 0 && ReferenceEquals(values, f.Vm.Members), "Reset取消后旧foreach继续填成员");
    }

    private static async Task ReentrantDirectoryCancel()
    {
        using var f = new Fixture();
        await f.Vm.ReloadAsync();
        var values = f.Vm.Lists;
        values.CollectionChanged += (_, args) => { if (args.Action == NotifyCollectionChangedAction.Reset) f.Vm.Cancel(); };
        await f.Vm.ReloadAsync();
        Check(f.Vm.Lists.Count == 0 && ReferenceEquals(values, f.Vm.Lists), "Reset取消后旧foreach继续填目录");
    }

    private static async Task ReentrantAccountSwitch()
    {
        using var f = new Fixture();
        await f.OpenA();
        var switched = false;
        Task? switchTask = null;
        f.Vm.Members.CollectionChanged += (_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Reset || switched) return;
            switched = true;
            switchTask = f.OpenB();
        };
        await f.Vm.ReloadAsync();
        if (switchTask is not null) await switchTask;
        Check(f.Vm.Scope!.Key == f.B.Key && f.Vm.Members.Count == 3
            && f.Vm.Members.All(item => item.Item.Name.StartsWith("b-", StringComparison.Ordinal)), "Reset切账号后旧成员回填到B");
    }

    private static Task PlexEmptyMembers() => PlexMemberFailure("empty");
    private static Task PlexChangedMembers() => PlexMemberFailure("changed");
    private static Task PlexRepeatedMembers() => PlexMemberFailure("repeated");
    private static async Task PlexMemberFailure(string mode)
    {
        using var f = new Fixture(MediaServerKind.Plex);
        f.Transport.MemberMode = mode;
        var list = new ServerList(f.A.Bind(new() { Id = "200", Type = EmbyItemType.Playlist, Name = "Plex" }),
            ServerListKind.Playlist, true, true);
        await ThrowsAsync<InvalidDataException>(() => f.Service.ReadMembersAsync(f.A, list, default));
        Check(f.Transport.MemberRequests >= 2 && f.Transport.Writes == 0, "成员坏分页没有沿真实只读路径");
    }

    private static Task PlexEmptyDirectory() => PlexDirectoryFailure("empty");
    private static Task PlexChangedDirectory() => PlexDirectoryFailure("changed");
    private static Task PlexRepeatedDirectory() => PlexDirectoryFailure("repeated");
    private static async Task PlexDirectoryFailure(string mode)
    {
        using var f = new Fixture(MediaServerKind.Plex);
        f.Transport.DirectoryMode = mode;
        await ThrowsAsync<InvalidDataException>(() => f.Service.ReadDirectoryAsync(f.A, ServerListKind.Playlist, null, default));
        Check(f.Transport.DirectoryRequests >= 2 && f.Transport.Writes == 0, "目录坏分页没有沿真实只读路径");
    }

    private static async Task PlexUnknownMemberTotal()
    {
        using var f = new Fixture(MediaServerKind.Plex);
        f.Transport.MemberMode = "unknown";
        var list = new ServerList(f.A.Bind(new() { Id = "200", Type = EmbyItemType.Playlist, Name = "Plex" }),
            ServerListKind.Playlist, true, true);
        var members = await f.Service.ReadMembersAsync(f.A, list, default);
        Check(members.Count == 101 && f.Transport.MemberRequests == 2, "无total误把满页当完整或重复成员丢失");
    }

    private static async Task OriginalScopeLifetime()
    {
        using var f = new Fixture();
        await f.OpenA();
        var list = f.Vm.OpenedList!;
        f.Session.SignOut(f.ServerA, f.AccountA);
        await ThrowsAsync<OperationCanceledException>(() => f.Service.ReadMembersAsync(f.A, list, default));
        await ThrowsAsync<OperationCanceledException>(() => f.Service.RemoveAsync(f.A, list,
            new(f.A.Bind(new() { Id = "same", Type = EmbyItemType.Movie }), "entry-a"), default));
        Check(f.B.IsAvailable && f.Transport.Writes == 0, "原source撤销影响无关source或借新source执行");
    }

    private static async Task ThrowsAsync<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
    private static void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
}

internal sealed class Gate : IDisposable
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal async Task Enter() { Entered.TrySetResult(); await _release.Task; }
    internal void Release() => _release.TrySetResult();
    public void Dispose() => Release();
}

internal sealed class Fixture : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal AnonymousTransport Transport { get; } = new();
    internal EmbySession Session { get; }
    internal ServerListsService Service { get; } = new();
    internal ServerListsViewModel Vm { get; } = new();
    internal EmbySessionScope A { get; }
    internal EmbySessionScope B { get; }
    internal ServerProfile ServerA { get; }
    internal AccountProfile AccountA { get; }

    internal Fixture(MediaServerKind kind = MediaServerKind.Emby)
    {
        var settings = new AppSettings();
        var device = DeviceIdentity.Create("list-ownership", "test");
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Private, null,
            [settings, new SettingsStore(new AppPaths(Path.Combine(Path.GetTempPath(), "ownership-denied|no-io")), NoSecrets.Instance),
                new CredentialVault(NoSecrets.Instance), device, Transport], CultureInfo.InvariantCulture)!;
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(Session)!;
        var sources = (Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Private)!.GetValue(Session)!;
        ServerA = new() { Id = "server-a", Name = "anonymous A" };
        AccountA = new() { Id = "account-a", UserId = "1", Username = "anonymous A" };
        A = Add(ServerA, AccountA, "a");
        B = Add(new() { Id = "server-b", Name = "anonymous B" }, new() { Id = "account-b", UserId = "1", Username = "anonymous B" }, "b");
        typeof(EmbySession).GetField("_current", Private)!.SetValue(Session, A);
        Vm.Attach(Session, Service, action => action(), A);

        EmbySessionScope Add(ServerProfile server, AccountProfile account, string side)
        {
            var connection = new EmbyConnection(new Uri("https://ownership.invalid/" + side + "/"), "offline-placeholder", "1", "anonymous", "anonymous", device)
            { ServerKind = kind, IsAdministrator = true };
            var identity = Activator.CreateInstance(typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!,
                [new EmbyClient(http, connection), server, account])!;
            var source = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Private, null, [Session, identity], CultureInfo.InvariantCulture)!;
            sources.Add(source.Key, source);
            return source;
        }
    }

    internal async Task OpenA() { await Vm.ReloadAsync(); await Vm.OpenAsync(Vm.Lists[0]); }
    internal async Task OpenB() { await Vm.SelectAccountAsync(Vm.Accounts.Single(account => account.Scope.Key == B.Key)); await Vm.OpenAsync(Vm.Lists[0]); }
    public void Dispose() { Vm.Dispose(); Session.Dispose(); }

    private sealed class NoSecrets : ISecretProtector
    {
        internal static NoSecrets Instance { get; } = new();
        public string Protect(string value) => throw new InvalidOperationException("No credential IO");
        public string Unprotect(string value) => throw new InvalidOperationException("No credential IO");
    }
}

internal sealed class AnonymousTransport : HttpMessageHandler
{
    internal Func<Task>? WriteA { get; set; }
    internal Func<Task>? ReadA { get; set; }
    internal string? MemberMode { get; set; }
    internal string? DirectoryMode { get; set; }
    internal int MemberRequests { get; private set; }
    internal int DirectoryRequests { get; private set; }
    internal int Writes { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var uri = request.RequestUri!;
        if (uri.Host != "ownership.invalid") throw new InvalidOperationException("Only anonymous in-memory server reads permitted");
        var path = uri.AbsolutePath;
        var side = path.StartsWith("/b/", StringComparison.Ordinal) ? "b" : "a";
        if (request.Method != HttpMethod.Get)
        {
            Writes++;
            if (side == "a" && WriteA is not null) await WriteA();
            return new(HttpStatusCode.NoContent);
        }
        if (side == "a" && ReadA is not null) await ReadA();
        if (path.EndsWith("media/providers", StringComparison.Ordinal)) return Json(new
        {
            MediaContainer = new
            {
                MediaProvider = new[] { new { identifier = "com.plexapp.plugins.library", Feature = new[]
            { new { type = "playlist", key = "/playlists", requires = "" }, new { type = "collection", key = "/library/collections", requires = "" },
                new { type = "manage", key = "", requires = "" } } } }
            }
        });
        if (path.EndsWith("playlists/all", StringComparison.Ordinal))
        {
            var index = DirectoryRequests++;
            var total = DirectoryMode == "changed" && index > 0 ? 3 : 2;
            var rows = DirectoryMode == "empty" && index > 0 ? Array.Empty<object>() : new object[]
            { new { ratingKey = DirectoryMode == "repeated" || index == 0 ? "200" : "201", title = "Plex list", type = "playlist", playlistType = "video", smart = 0, readOnly = 0 } };
            return Json(new { MediaContainer = new { Metadata = rows, totalSize = total } });
        }
        if (path.EndsWith("playlists/200/items", StringComparison.Ordinal))
        {
            var index = MemberRequests++;
            var rows = MemberMode == "empty" && index > 0 ? Array.Empty<object>() : MemberMode == "unknown" && index == 0
                ? Enumerable.Range(1, 100).Select(number => PlexItem(number)).ToArray()
                : new object[] { PlexItem(MemberMode == "repeated" || index == 0 ? 1 : MemberMode == "unknown" ? 101 : 2) };
            if (MemberMode == "unknown") return Json(new { MediaContainer = new { Metadata = rows } });
            return Json(new { MediaContainer = new { Metadata = rows, totalSize = MemberMode == "changed" && index > 0 ? 3 : 2 } });
        }
        if (path.EndsWith("Items/playlist", StringComparison.Ordinal)) return Json(List(side));
        if (path.EndsWith("/Items", StringComparison.Ordinal) && uri.Query.Contains("ParentId=playlist", StringComparison.Ordinal))
            return Json(new { Items = new[] { Item(side, "same", "entry-a"), Item(side, "same", "entry-b"), Item(side, "other", "entry-c") }, TotalRecordCount = 3 });
        if (path.EndsWith("/Items", StringComparison.Ordinal)) return Json(new { Items = new[] { List(side) }, TotalRecordCount = 1 });
        return new(HttpStatusCode.NotFound);
    }

    private static object PlexItem(int number) => new
    {
        ratingKey = number.ToString(CultureInfo.InvariantCulture),
        title = "Plex movie",
        type = "movie",
        playlistItemID = (number + 1000).ToString(CultureInfo.InvariantCulture)
    };
    private static object List(string side) => new { Id = "playlist", Name = side + "-playlist", Type = "Playlist", MediaType = "Video", CanEditItems = true };
    private static object Item(string side, string id, string entry) => new { Id = id, Name = side + "-" + entry, Type = "Movie", MediaType = "Video", PlaylistItemId = entry };
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
}
