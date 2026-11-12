using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Services;

namespace Momoka.Shell.ViewModels;

/// <summary>Server lists retain one captured account, ordered member identities and independent write receipts.</summary>
public sealed partial class ServerListsViewModel : PageViewModel
{
    private EmbySession? _session;
    private ServerListsService? _service;
    private Action<Action>? _dispatch;
    private EmbySessionScope? _scope;
    private bool _active;
    private long _permissionRevision;
    private long _visit;
    private bool _needsRefresh;
    public ObservableCollection<ServerListAccount> Accounts { get; } = [];
    public ObservableCollection<ServerListLibrary> Libraries { get; } = [];
    public ObservableCollection<ServerList> Lists { get; } = [];
    public ObservableCollection<ServerListMember> Members { get; } = [];
    public ServerListKind Kind { get; private set; }
    public ServerListLibrary? Library { get; private set; }
    public ServerList? OpenedList { get; private set; }
    public EmbySessionScope? Scope => _scope;
    [ObservableProperty] public partial ServerListMember? SelectedMember { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "服务器清单";
    public string AccountName => _scope?.DisplayName ?? "";
    public string CountText => OpenedList is null ? $"{Lists.Count} 个清单" : $"{Members.Count} 项";
    public bool Available => _active && _scope?.IsAvailable == true;
    public bool IsMembers => OpenedList is not null;
    public bool Editable => Available && !_needsRefresh && _scope!.PermissionRevision == _permissionRevision && OpenedList?.CanEdit == true;
    public bool CanCreate => Available && !_needsRefresh && _scope!.PermissionRevision == _permissionRevision
        && _service?.CanCreate(_scope!, Kind, Library?.Item) == true;
    public bool CanSelect => _active && !Busy;
    public Visibility CollectionsVisibility => Show(Available && _scope!.Connection.IsPlex && !IsMembers);
    public Visibility LibraryVisibility => Show(Available && Kind == ServerListKind.Collection && !IsMembers);
    public Visibility DirectoryVisibility => Show(!IsMembers);
    public Visibility MembersVisibility => Show(IsMembers);
    public Visibility CreateVisibility => Show(CanCreate && !IsMembers);
    public Visibility AddVisibility => Show(Editable);
    public Visibility RemoveVisibility => Show(Editable && SelectedMember is { EntryId.Length: > 0 });
    public Visibility UpVisibility => Show(Editable && OpenedList?.CanOrder == true && Members.All(member => member.EntryId.Length > 0)
        && Members.IndexOf(SelectedMember!) > 0);
    public Visibility DownVisibility => Show(Editable && OpenedList?.CanOrder == true && Members.All(member => member.EntryId.Length > 0) && SelectedMember is not null
        && Members.IndexOf(SelectedMember) is var index && index >= 0 && index < Members.Count - 1);
    public Visibility OpenVisibility => Show(Available && SelectedMember is not null);
    public Visibility PlayVisibility => Show(Available && SelectedMember?.Item.IsPlayable == true);
    public Visibility EmptyVisibility => Show(IsReady && !Busy && (IsMembers ? Members.Count == 0 : Lists.Count == 0));
    public string EmptyText => IsMembers ? "此清单还没有媒体。" : Kind == ServerListKind.Collection && Library is null
        ? "选择一个影视库以查看合集。" : "此账号没有普通清单。";

    internal void Attach(EmbySession session, ServerListsService service, Action<Action> dispatch,
        EmbySessionScope? source = null)
    {
        Cancel();
        _session = session;
        _service = service;
        _dispatch = dispatch;
        _active = true;
        ++_visit;
        session.SourcesChanged += OnSourcesChanged;
        session.PermissionsChanged += OnPermissionsChanged;
        RefreshAccounts();
        var chosen = source is null ? Accounts.FirstOrDefault()?.Scope
            : Accounts.FirstOrDefault(account => SameIdentity(account.Scope, source))?.Scope;
        SetScope(chosen);
    }

    private static bool SameIdentity(EmbySessionScope left, EmbySessionScope right) =>
        left.Key == right.Key && ReferenceEquals(left.UserStateOwner, right.UserStateOwner);

    private void RefreshAccounts()
    {
        if (_session is null) return;
        var sources = _session.Sources.Where(ServerListsService.Supports).ToArray();
        foreach (var account in Accounts.Where(account => !sources.Any(scope => SameIdentity(scope, account.Scope))).ToArray())
            Accounts.Remove(account);
        foreach (var scope in sources)
            if (!Accounts.Any(account => SameIdentity(account.Scope, scope))) Accounts.Add(new(scope));
    }

    private void SetScope(EmbySessionScope? scope)
    {
        base.Cancel();
        _scope = scope;
        _needsRefresh = false;
        Kind = ServerListKind.Playlist;
        Library = null;
        OpenedList = null;
        Libraries.Clear();
        Lists.Clear();
        Members.Clear();
        SelectedMember = null;
        Title = "服务器清单";
        IsReady = false;
        Changed();
    }

    internal async Task SelectAccountAsync(ServerListAccount? account)
    {
        if (!_active || account is null || !Accounts.Contains(account)) return;
        SetScope(account.Scope);
        await ReloadAsync();
    }

    internal async Task SelectKindAsync(ServerListKind kind)
    {
        if (!Available || IsMembers || kind == Kind || kind == ServerListKind.Collection && !_scope!.Connection.IsPlex) return;
        base.Cancel();
        Kind = kind;
        Lists.Clear();
        Changed();
        await ReloadAsync();
    }

    internal async Task SelectLibraryAsync(ServerListLibrary? library)
    {
        if (!Available || IsMembers || library is null || !Libraries.Contains(library) || Library == library) return;
        base.Cancel();
        Library = library;
        SelectedMember = null;
        await ReloadAsync();
    }

    public override async Task ReloadAsync()
    {
        if (!Available || Busy || _service is null) return;
        var scope = _scope!;
        var token = BeginLoad();
        var revision = scope.PermissionRevision;
        try
        {
            await ReadAsync(scope, token);
            if (Current(scope, token) && scope.PermissionRevision == revision)
            {
                _permissionRevision = revision;
                _needsRefresh = false;
                if (_service.PermissionNotice(scope) is { } notice) Notify("账号权限未确认", notice, InfoBarSeverity.Warning);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (Current(scope, token)) Report("读取服务器清单失败", error); }
        finally { EndLoad(token); Changed(); }
    }

    private void RequireReadOwner(EmbySessionScope scope, CancellationToken token, long revision)
    {
        if (!Current(scope, token) || scope.PermissionRevision != revision)
            throw new OperationCanceledException("清单读取所属页面或账号权限已改变");
    }

    private async Task ReadAsync(EmbySessionScope scope, CancellationToken token)
    {
        var service = _service!;
        var revision = scope.PermissionRevision;
        if (OpenedList is { } opened)
        {
            var fresh = await service.ReadListAsync(scope, opened, token);
            var members = await service.ReadMembersAsync(scope, fresh, token);
            RequireReadOwner(scope, token, revision);
            OpenedList = fresh;
            Members.Clear();
            RequireReadOwner(scope, token, revision);
            foreach (var member in members)
            {
                Members.Add(member);
                RequireReadOwner(scope, token, revision);
            }
            SelectedMember = null;
            RequireReadOwner(scope, token, revision);
            Title = fresh.Name;
            RequireReadOwner(scope, token, revision);
        }
        else
        {
            if (scope.Connection.IsPlex && Libraries.Count == 0)
            {
                var libraries = await service.ReadLibrariesAsync(scope, token);
                RequireReadOwner(scope, token, revision);
                foreach (var library in libraries)
                {
                    Libraries.Add(new(library));
                    RequireReadOwner(scope, token, revision);
                }
                Library ??= Libraries.FirstOrDefault();
            }
            var lists = await service.ReadDirectoryAsync(scope, Kind, Library?.Item, token);
            RequireReadOwner(scope, token, revision);
            Lists.Clear();
            RequireReadOwner(scope, token, revision);
            foreach (var list in lists)
            {
                Lists.Add(list);
                RequireReadOwner(scope, token, revision);
            }
        }
        Changed();
    }

    internal async Task OpenAsync(ServerList list)
    {
        if (!Available || Busy || !Lists.Contains(list)) return;
        OpenedList = list;
        Title = list.Name;
        Members.Clear();
        SelectedMember = null;
        Changed();
        await ReloadAsync();
    }

    internal async Task OpenItemAsync(EmbyItem item)
    {
        if (!Available || Busy || _service is null) return;
        if (item.Source is null || !SameIdentity(item.Source, _scope!)) return;
        OpenedList = new(item, item.Type == EmbyItemType.BoxSet ? ServerListKind.Collection : ServerListKind.Playlist, false, false);
        Title = item.Name;
        Changed();
        await ReloadAsync();
    }

    internal async Task BackAsync()
    {
        if (!Available || Busy) return;
        OpenedList = null;
        Members.Clear();
        SelectedMember = null;
        Title = "服务器清单";
        Changed();
        await ReloadAsync();
    }

    internal Task CreateAsync(string name, IReadOnlyList<EmbyItem> items) => MutateAsync("创建清单失败",
        (scope, token) => _service!.CreateAsync(scope, Kind, name, Library?.Item, items, token), () => { });

    internal Task AddAsync(IReadOnlyList<EmbyItem> items)
    {
        if (!Editable || OpenedList is not { } list) return Task.CompletedTask;
        return MutateAsync("添加媒体失败", (scope, token) => _service!.AddAsync(scope, list, items, token), () => { });
    }

    internal Task RemoveAsync()
    {
        if (!Editable || OpenedList is not { } list || SelectedMember is not { } member || !Members.Contains(member)) return Task.CompletedTask;
        return MutateAsync("移出清单失败", (scope, token) => _service!.RemoveAsync(scope, list, member, token), () =>
        { Members.Remove(member); SelectedMember = null; });
    }

    internal Task MoveAsync(int direction)
    {
        if (!Editable || OpenedList is not { CanOrder: true } list || SelectedMember is not { } member) return Task.CompletedTask;
        var from = Members.IndexOf(member);
        var to = from + direction;
        if (direction is not (-1 or 1) || from < 0 || to < 0 || to >= Members.Count) return Task.CompletedTask;
        var snapshot = Members.ToArray();
        return MutateAsync("调整顺序失败", (scope, token) => _service!.MoveAsync(scope, list, snapshot, member, to, token), () => { }, (member.EntryId, to));
    }

    private async Task MutateAsync(string failure, Func<EmbySessionScope, CancellationToken, Task> write, Action apply,
        (string EntryId, int Index)? requestedMove = null)
    {
        if (!Available || Busy || _service is null || IsMembers && !Editable || !IsMembers && !CanCreate) return;
        var scope = _scope!;
        var token = BeginLoad();
        var list = OpenedList;
        var confirmed = false;
        try
        {
            await write(scope, token);
            confirmed = true;
            if (!Current(scope, token)) return;
            apply();
            Changed();
            try
            {
                var revision = scope.PermissionRevision;
                await ReadAsync(scope, token);
                if (Current(scope, token) && scope.PermissionRevision == revision)
                {
                    var matches = requestedMove is not { } requested || requested.Index < Members.Count && Members[requested.Index].EntryId == requested.EntryId;
                    Notify(matches ? "清单已保存" : "服务器已接收排序", matches ? null : "服务器返回的顺序与目标不同，请刷新核对。",
                        matches ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
                }
                else if (Current(scope, token)) Notify("清单已保存", "账号权限已更新，请刷新核对最新清单。", InfoBarSeverity.Warning);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                if (Current(scope, token))
                {
                    _needsRefresh = true;
                    if (list is not null) OpenedList = list with { CanEdit = false, CanOrder = false };
                    Notify("已保存，但刷新失败", Momoka.Diagnostics.Failure.Describe(error)
                        + "。点击刷新重新读取即可。", InfoBarSeverity.Warning);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (Current(scope, token) && error is (Momoka.Plex.PlexWriteUncertainException or PlaylistWriteUncertainException))
            {
                _needsRefresh = true;
                if (list is not null) OpenedList = list with { CanEdit = false, CanOrder = false };
            }
            if (Current(scope, token)) Report(confirmed ? "已保存，但刷新失败" : failure, error);
        }
        finally { EndLoad(token); Changed(); }
    }

    private bool Current(EmbySessionScope scope, CancellationToken token) => _active && IsCurrent(token)
        && _scope is not null && SameIdentity(_scope, scope) && scope.IsAvailable;

    partial void OnSelectedMemberChanged(ServerListMember? value) => Changed();
    protected override void BusyChanged() => Changed();
    private void Changed()
    {
        foreach (var property in new[] { nameof(AccountName), nameof(CountText), nameof(Available), nameof(IsMembers), nameof(Editable), nameof(CanSelect),
            nameof(CollectionsVisibility), nameof(LibraryVisibility), nameof(DirectoryVisibility), nameof(MembersVisibility),
            nameof(CreateVisibility), nameof(AddVisibility), nameof(RemoveVisibility), nameof(UpVisibility), nameof(DownVisibility),
            nameof(OpenVisibility), nameof(PlayVisibility), nameof(EmptyVisibility), nameof(EmptyText), nameof(Library), nameof(OpenedList) })
            OnPropertyChanged(property);
    }

    private void OnSourcesChanged(object? sender, EventArgs args)
    {
        var visit = _visit;
        _dispatch?.Invoke(() =>
        {
            if (!_active || visit != _visit) return;
            RefreshAccounts();
            if (_scope?.IsAvailable == false) { base.Cancel(); Changed(); Notify("清单所属账号已退出", "重新登录该账号后再打开清单。", InfoBarSeverity.Warning); }
        });
    }
    private void OnPermissionsChanged(object? sender, EmbySessionScope scope)
    {
        var visit = _visit;
        var revision = scope.PermissionRevision;
        _dispatch?.Invoke(() =>
        {
            if (!_active || visit != _visit || _scope is null || !SameIdentity(_scope, scope) || _permissionRevision >= revision) return;
            _needsRefresh = true;
            Changed();
            Notify("账号权限已更新", "点击刷新重新核对清单的编辑权限。", InfoBarSeverity.Informational);
        });
    }

    public override void Cancel()
    {
        _active = false;
        ++_visit;
        if (_session is not null) { _session.SourcesChanged -= OnSourcesChanged; _session.PermissionsChanged -= OnPermissionsChanged; }
        base.Cancel();
    }
    public override void Dispose() { Cancel(); base.Dispose(); }
}
