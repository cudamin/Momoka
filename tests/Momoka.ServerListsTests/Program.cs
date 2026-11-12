using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Configuration;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Services;
using Momoka.Shell.ViewModels;

namespace Momoka.ServerListsTests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var cases = new (string Name, Func<Task> Run)[]
        {
            ("成员重复媒体保留独立身份，移动只操作选中的成员", DuplicateMove),
            ("写入已确认但刷新失败保留本地结果，不再发送写入", ConfirmedRefreshFailure),
            ("离页拒绝迟到成员结果，集合身份稳定", LateRead),
            ("来源退出后不能移除或借另一账号写入", RevokedScope),
            ("缺失成员标识隐藏移除和排序", MissingEntryIdentity),
            ("不同账号媒体不能加入清单", ForeignItem),
            ("只读清单仍可查看成员且无编辑入口", ReadOnly),
            ("创建空视频清单后重新读目录", EmptyCreate),
            ("读取期间权限更换拒绝陈旧可编辑结果", PermissionDuringRead),
            ("创建已确认但目录刷新失败禁止重复创建", CreateRefreshFailure),
            ("Plex最后权限核对降级后目录和成员保持只读", PlexPermissionDowngrade)
        };
        var failed = 0;
        foreach (var (name, run) in cases)
        {
            try { await run().WaitAsync(TimeSpan.FromSeconds(15)); Console.WriteLine("通过 " + name); }
            catch (Exception error) { failed++; Console.WriteLine("失败 " + name + "\n" + error); }
        }
        Console.WriteLine($"共 {cases.Length} 项，通过 {cases.Length - failed}，失败 {failed}，跳过 0");
        return failed == 0 ? 0 : 1;
    }
    private static async Task DuplicateMove()
    {
        using var f = new Fixture();
        await f.Open();
        Check(f.Vm.Members.Count == 3 && f.Vm.Members[0].Item.Id == f.Vm.Members[1].Item.Id, "重复媒体被去重");
        f.Vm.SelectedMember = f.Vm.Members[1];
        await f.Vm.MoveAsync(-1);
        Check(f.Transport.Writes.Count == 1 && f.Transport.Writes[0].Path.Contains("/entry-b/Move/0", StringComparison.Ordinal), "移动使用了媒体ID或错误位置");
        Check(f.Vm.NoticeSeverity == InfoBarSeverity.Warning && f.Vm.Members[1].EntryId == "entry-b", "服务器未达到目标顺序仍声称完成");
    }
    private static async Task ConfirmedRefreshFailure()
    {
        using var f = new Fixture();
        await f.Open();
        f.Vm.SelectedMember = f.Vm.Members[1];
        f.Transport.FailReadsAfterWrite = true;
        await f.Vm.RemoveAsync();
        Check(f.Transport.Writes.Count == 1 && f.Vm.Members.Count == 2, "确认移除被刷新失败回滚");
        Check(f.Vm.NoticeSeverity == InfoBarSeverity.Warning && f.Vm.NoticeTitle == "已保存，但刷新失败", "成功回执被报成失败");
        f.Vm.SelectedMember = f.Vm.Members[0];
        await f.Vm.RemoveAsync();
        Check(f.Transport.Writes.Count == 1, "刷新失败后重复写入");
    }
    private static async Task LateRead()
    {
        using var f = new Fixture();
        await f.Vm.ReloadAsync();
        var collection = f.Vm.Members;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.BeforeMembers = async () => { entered.TrySetResult(); await release.Task; };
        var read = f.Vm.OpenAsync(f.Vm.Lists[0]);
        await entered.Task;
        f.Vm.Cancel();
        release.TrySetResult();
        await read;
        Check(f.Vm.Members.Count == 0 && ReferenceEquals(collection, f.Vm.Members), "离页结果回填或集合被替换");
    }
    private static async Task RevokedScope()
    {
        using var f = new Fixture();
        await f.Open();
        f.Vm.SelectedMember = f.Vm.Members[0];
        f.Session.SignOut();
        await f.Vm.RemoveAsync();
        Check(!f.Vm.Available && f.Transport.Writes.Count == 0, "退出后仍写入");
    }
    private static async Task MissingEntryIdentity()
    {
        using var f = new Fixture();
        f.Transport.MissingIdentity = true;
        await f.Open();
        f.Vm.SelectedMember = f.Vm.Members[1];
        Check(f.Vm.RemoveVisibility == Visibility.Collapsed && f.Vm.UpVisibility == Visibility.Collapsed && f.Vm.DownVisibility == Visibility.Collapsed,
            "无成员身份仍可改关系");
    }
    private static async Task ForeignItem()
    {
        using var f = new Fixture();
        await f.Open();
        try { await f.Service.AddAsync(f.Scope, f.Vm.OpenedList!, [new EmbyItem { Id = "foreign", Type = EmbyItemType.Movie }], default); }
        catch (InvalidOperationException) { Check(f.Transport.Writes.Count == 0, "跨账号守卫迟于写入"); return; }
        throw new InvalidOperationException("没有拒绝缺失来源的媒体");
    }
    private static async Task ReadOnly()
    {
        using var f = new Fixture();
        f.Transport.Editable = false;
        await f.Open();
        f.Vm.SelectedMember = f.Vm.Members[0];
        Check(f.Vm.Members.Count == 3 && f.Vm.AddVisibility == Visibility.Collapsed && f.Vm.RemoveVisibility == Visibility.Collapsed, "只读清单入口错误");
    }
    private static async Task EmptyCreate()
    {
        using var f = new Fixture();
        await f.Vm.ReloadAsync();
        await f.Vm.CreateAsync("周末", []);
        Check(f.Transport.Writes.Count == 1 && f.Transport.Writes[0].Path == "/emby/Playlists", "创建没有调用持久列表接口");
        Check(f.Vm.NoticeSeverity == InfoBarSeverity.Success, "创建成功未刷新");
    }
    private static async Task PermissionDuringRead()
    {
        using var f = new Fixture();
        await f.Vm.ReloadAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Transport.BeforeMembers = async () => { entered.TrySetResult(); await release.Task; };
        var pending = f.Vm.OpenAsync(f.Vm.Lists[0]);
        await entered.Task;
        f.RevisePermissions();
        release.TrySetResult();
        await pending;
        Check(f.Vm.Members.Count == 0 && !f.Vm.Editable && !f.Vm.CanCreate, "旧权限读结果重新授权了清单");
    }
    private static async Task CreateRefreshFailure()
    {
        using var f = new Fixture();
        await f.Vm.ReloadAsync();
        f.Transport.FailReadsAfterWrite = true;
        await f.Vm.CreateAsync("周末", []);
        Check(f.Transport.Writes.Count == 1 && !f.Vm.CanCreate, "确认创建后刷新失败仍可重复创建");
        await f.Vm.CreateAsync("周末", []);
        Check(f.Transport.Writes.Count == 1, "创建重复执行");
        f.Transport.FailReadsAfterWrite = false;
        await f.Vm.ReloadAsync();
        Check(f.Vm.CanCreate, "独立刷新后创建入口未恢复");
    }
    private static async Task PlexPermissionDowngrade()
    {
        using var f = new Fixture(plex: true);
        f.Transport.FailProfileAt = 3;
        await f.Vm.ReloadAsync();
        Check(f.Vm.Lists.Count == 1 && !f.Vm.Lists[0].CanEdit && !f.Vm.CanCreate, "目录最后权限失败仍授权写入");
        f.Transport.FailProfileAt = int.MaxValue;
        await f.Vm.ReloadAsync();
        Check(f.Vm.Lists[0].CanEdit && f.Vm.CanCreate, "权限恢复后未恢复创建");
        f.Transport.FailProfileAt = f.Transport.ProfileReads + 2;
        await f.Vm.OpenAsync(f.Vm.Lists[0]);
        Check(f.Vm.Members.Count == 3 && !f.Vm.Editable && f.Vm.NoticeTitle == "账号权限未确认", "最后权限读取失败仍显示可编辑成员");
        Check(f.Transport.Writes.Count == 0, "权限核对触发写入");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

internal sealed class Fixture : IDisposable
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public Transport Transport { get; } = new();
    public ServerListsService Service { get; } = new();
    public ServerListsViewModel Vm { get; } = new();
    public EmbySession Session { get; }
    public EmbySessionScope Scope { get; }
    public Fixture(bool plex = false)
    {
        var settings = new AppSettings();
        var device = DeviceIdentity.Create("server-lists-tests", "test");
        var directory = Path.Combine(Path.GetTempPath(), "momoka-server-lists-tests", Guid.NewGuid().ToString("N"));
        Session = (EmbySession)Activator.CreateInstance(typeof(EmbySession), Private, null,
            [settings, new SettingsStore(new AppPaths(directory), NoSecrets.Instance), new CredentialVault(NoSecrets.Instance), device, Transport], CultureInfo.InvariantCulture)!;
        var http = (EmbyHttp)typeof(EmbySession).GetField("_http", Private)!.GetValue(Session)!;
        Transport.Plex = plex;
        var connection = new EmbyConnection(new Uri(plex ? "https://server-lists.invalid/media/" : "https://server-lists.invalid/emby/"),
            "placeholder", plex ? "7" : "user", "fixture", "fixture", device)
        { ServerKind = plex ? MediaServerKind.Plex : MediaServerKind.Emby };
        var identityType = typeof(EmbySessionScope).GetNestedType("Identity", BindingFlags.NonPublic)!;
        var identity = Activator.CreateInstance(identityType, [new EmbyClient(http, connection),
            new ServerProfile { Id = "server", Name = "fixture" }, new AccountProfile { Id = "account", Username = "fixture" }])!;
        Scope = (EmbySessionScope)Activator.CreateInstance(typeof(EmbySessionScope), Private, null, [Session, identity], CultureInfo.InvariantCulture)!;
        ((Dictionary<string, EmbySessionScope>)typeof(EmbySession).GetField("_sources", Private)!.GetValue(Session)!).Add(Scope.Key, Scope);
        typeof(EmbySession).GetField("_current", Private)!.SetValue(Session, Scope);
        Vm.Attach(Session, Service, action => action(), Scope);
    }
    public async Task Open() { await Vm.ReloadAsync(); await Vm.OpenAsync(Vm.Lists[0]); }
    public void RevisePermissions()
    {
        var identity = typeof(EmbySessionScope).GetProperty("IdentityState", Private)!.GetValue(Scope)!;
        identity.GetType().GetProperty("PermissionRevision")!.SetValue(identity, Scope.PermissionRevision + 1);
        var callback = (EventHandler<EmbySessionScope>?)typeof(EmbySession).GetField("PermissionsChanged", Private)!.GetValue(Session);
        callback?.Invoke(Session, Scope);
    }
    public void Dispose() { Vm.Dispose(); Session.Dispose(); }
    private sealed class NoSecrets : ISecretProtector
    {
        public static NoSecrets Instance { get; } = new();
        public string Protect(string value) => throw new InvalidOperationException("不写凭据");
        public string Unprotect(string value) => throw new InvalidOperationException("不读凭据");
    }
}

internal sealed class Transport : HttpMessageHandler
{
    internal sealed record Sent(string Path, string Query);
    public List<Sent> Writes { get; } = [];
    public bool Editable { get; set; } = true;
    public bool MissingIdentity { get; set; }
    public bool FailReadsAfterWrite { get; set; }
    public Func<Task>? BeforeMembers { get; set; }
    public bool Plex { get; set; }
    public int ProfileReads { get; private set; }
    public int FailProfileAt { get; set; } = int.MaxValue;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = request.RequestUri.Query;
        if (Plex)
        {
            if (request.RequestUri.Host == "plex.tv")
            {
                ProfileReads++;
                return ProfileReads >= FailProfileAt ? new(HttpStatusCode.ServiceUnavailable) : Json(new { id = 7, anonymous = false });
            }
            object container;
            if (path == "/media/media/providers") container = new
            {
                MediaProvider = new[] { new { identifier = "com.plexapp.plugins.library",
                Feature = new[] { new { type = "playlist", key = "/playlists", requires = "nonAnonymousAccount" } } } }
            };
            else if (path == "/media/library/sections") container = new { Directory = Array.Empty<object>() };
            else if (path is "/media/playlists/all" or "/media/playlists/80") container = new
            {
                Metadata = new[] { new
                { ratingKey = "80", title = "Plex周末", type = "playlist", playlistType = "video", readOnly = 0, smart = 0 } },
                totalSize = 1
            };
            else if (path == "/media/playlists/80/items") container = new
            {
                Metadata = Enumerable.Range(1, 3).Select(index => new
                { ratingKey = "10", title = "影片", type = "movie", playlistItemID = (200 + index).ToString(CultureInfo.InvariantCulture) }).ToArray(),
                totalSize = 3
            };
            else throw new InvalidOperationException("未知Plex夹具请求：" + path);
            return Json(new { MediaContainer = container });
        }
        if (request.Method != HttpMethod.Get)
        {
            Writes.Add(new(path, query));
            return path == "/emby/Playlists" ? Json(new { Id = "new" }) : new(HttpStatusCode.NoContent);
        }
        if (FailReadsAfterWrite && Writes.Count > 0) return new(HttpStatusCode.ServiceUnavailable);
        if (path.EndsWith("/Items/playlist", StringComparison.Ordinal)) return Json(ListItem());
        if (path.EndsWith("/Items", StringComparison.Ordinal) || path.Contains("/Playlists/playlist/Items", StringComparison.Ordinal))
        {
            if (query.Contains("ParentId=playlist", StringComparison.Ordinal) || path.Contains("/Playlists/playlist/Items", StringComparison.Ordinal))
            {
                if (BeforeMembers is not null) await BeforeMembers();
                return Json(new { Items = new[] { Media("same", "entry-a"), Media("same", "entry-b"), Media("other", "entry-c") }, TotalRecordCount = 3 });
            }
            return Json(new { Items = new[] { ListItem() }, TotalRecordCount = 1 });
        }
        return new(HttpStatusCode.NotFound);
    }
    private object ListItem() => new { Id = "playlist", Name = "周末清单", Type = "Playlist", MediaType = "Video", CanEditItems = Editable };
    private object Media(string id, string entry) => new { Id = id, Name = id, Type = "Movie", MediaType = "Video", PlaylistItemId = MissingIdentity ? null : entry };
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
}
