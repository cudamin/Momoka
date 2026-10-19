using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

public sealed partial class FnNasUsersViewModel : FnNasAdminViewModel
{
    private JsonObject? _original;
    private JsonObject _defaultPermission = new();
    private readonly Dictionary<string, string> _libraryNames = new(StringComparer.Ordinal);
    public ObservableCollection<FnNasUserRow> Users { get; } = [];
    public ObservableCollection<FnNasChoice> Libraries { get; } = [];
    [ObservableProperty] public partial bool Editing { get; set; }
    [ObservableProperty] public partial bool EditingDefault { get; set; }
    [ObservableProperty] public partial string UserName { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string ConfirmPassword { get; set; } = "";
    [ObservableProperty] public partial int Permission { get; set; }
    public bool IsNew => _original is null && !EditingDefault;
    public bool IsAdministrator => _original is not null && FnNasJson.Flag(_original, "is_admin");
    public bool CanSave => CanUse && Editing;
    public bool CanDelete => CanSave && !EditingDefault && _original is not null && !IsAdministrator;
    public bool CanUnlock => CanDelete && FnNasJson.Flag(_original, "is_banned");
    public string EditorTitle => EditingDefault ? "新用户默认权限" : IsNew ? "新增用户" : "编辑用户";
    public string CountText => $"{Users.Count} 个用户";
    public Visibility ListVisibility => Show(!Editing);
    public Visibility EditorVisibility => Show(Editing);
    public Visibility IdentityVisibility => Show(!EditingDefault);
    public Visibility PermissionVisibility => Show(EditingDefault || !IsAdministrator);
    public Visibility LibrariesVisibility => Show((EditingDefault || !IsAdministrator) && Permission == 1);
    public Visibility DeleteVisibility => Show(!EditingDefault && _original is not null && !IsAdministrator);
    public Visibility UnlockVisibility => Show(!EditingDefault && _original is not null && !IsAdministrator && FnNasJson.Flag(_original, "is_banned"));
    public string PasswordNote => IsNew ? "为新用户设置登录密码。" : "留空可保留当前密码。";

    protected override void ResetEditor()
    {
        Editing = false;
        _original = null;
        Password = ConfirmPassword = "";
        Users.Clear();
        Libraries.Clear();
        AnnounceEditor();
    }

    public override Task ReloadAsync() => RunAsync("读取用户失败", async (scope, token) =>
    {
        var libraries = await scope.ExecuteAsync((client, ct) => client.FnNas.GetAdminLibrariesAsync(client.FnNasConnection, ct), token);
        var defaults = await scope.ExecuteAsync((client, ct) => client.FnNas.GetDefaultPermissionAsync(client.FnNasConnection, ct), token);
        if (!Current(token)) return;
        _libraryNames.Clear();
        foreach (var library in libraries) _libraryNames[FnNasJson.Text(library, "guid")] = FnNasJson.Text(library, "name");
        _defaultPermission = FnNasJson.Copy(defaults);
        await ReadUsersAsync(scope, token);
    });

    private async Task ReadUsersAsync(EmbySessionScope scope, CancellationToken token)
    {
        var users = await scope.ExecuteAsync((client, ct) => client.FnNas.GetUsersAsync(client.FnNasConnection, ct), token);
        if (!Current(token)) return;
        Users.Clear();
        foreach (var user in users) Users.Add(new FnNasUserRow(user, _libraryNames));
        OnPropertyChanged(nameof(CountText));
    }

    [RelayCommand(CanExecute = nameof(CanUse))]
    private void NewUser() => Open(null);

    internal void Open(FnNasUserRow? row)
    {
        if (!CanUse) return;
        EditingDefault = false;
        _original = row is null ? null : FnNasJson.Copy(row.Document);
        UserName = row?.Name ?? "";
        Password = ConfirmPassword = "";
        PopulatePermission(_original ?? _defaultPermission);
        Editing = true;
        AnnounceEditor();
    }

    [RelayCommand(CanExecute = nameof(CanUse))]
    private void Defaults()
    {
        if (!CanUse) return;
        EditingDefault = true;
        _original = null;
        Password = ConfirmPassword = "";
        PopulatePermission(_defaultPermission);
        Editing = true;
        AnnounceEditor();
    }

    private void PopulatePermission(JsonObject source)
    {
        Permission = FnNasJson.Number(source, "media_permission");
        var selected = FnNasUserRow.PermissionIds(source).ToHashSet(StringComparer.Ordinal);
        Libraries.Clear();
        foreach (var (id, name) in _libraryNames) Libraries.Add(new FnNasChoice(id, name) { Selected = selected.Contains(id) });
        foreach (var id in selected.Where(id => !_libraryNames.ContainsKey(id))) Libraries.Add(new FnNasChoice(id, "不可用媒体库") { Selected = true });
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Back()
    {
        if (!CanSave) return;
        Editing = false;
        Password = ConfirmPassword = "";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => !CanSave ? Task.CompletedTask : RunAsync("操作或刷新失败，请刷新列表核对后重试", async (scope, token) =>
    {
        var draft = FnNasJson.Copy(EditingDefault ? _defaultPermission : _original ?? new JsonObject());
        if (EditingDefault || !IsAdministrator)
        {
            draft["media_permission"] = Permission;
            draft["mediadb_list"] = FnNasJson.Array(Permission == 1 ? Libraries.Where(row => row.Selected).Select(row => row.Id) : []);
        }
        if (EditingDefault)
        {
            await scope.ExecuteAsync((client, ct) => client.FnNas.UpdateDefaultPermissionAsync(client.FnNasConnection, draft, ct), token);
            if (!Current(token)) return;
            _defaultPermission = draft;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(UserName) || UserName.Trim().Length > 32) throw new ArgumentException("用户名必须为 1～32 个字符。");
            if (IsNew && Password.Length == 0) throw new ArgumentException("请为新用户设置密码。");
            if (Password.Length > 0 && Password.Length is < 6 or > 127) throw new ArgumentException("密码长度须为 6～127 个字符。");
            if (Password != ConfirmPassword) throw new ArgumentException("两次输入的密码不一致。");
            draft["username"] = UserName.Trim();
            draft.Remove("password");
            if (Password.Length > 0) draft["password"] = Password;
            if (_original is { } original)
                await scope.ExecuteAsync((client, ct) => client.FnNas.UpdateUserAsync(client.FnNasConnection, FnNasJson.Text(original, "guid"), draft, ct), token);
            else
            {
                draft["is_admin"] = 0;
                await scope.ExecuteAsync((client, ct) => client.FnNas.CreateUserAsync(client.FnNasConnection, draft, ct), token);
            }
            if (!Current(token)) return;
        }
        // 创建响应只保证成功，不保证返回 GUID。先关闭草稿，避免后续列表刷新失败造成重复创建。
        Editing = false;
        Password = ConfirmPassword = "";
        Notify("已保存", "用户设置已更新。", InfoBarSeverity.Success);
        await ReadUsersAsync(scope, token);
    });

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => !CanDelete ? Task.CompletedTask : RunAsync("删除用户失败", async (scope, token) =>
    {
        if (_original is not { } user) return;
        var name = FnNasJson.Text(user, "username");
        if (!await ConfirmAsync("删除用户", $"确定删除「{name}」吗？此操作无法撤销。", "删除") || !Current(token)) return;
        await scope.ExecuteAsync((client, ct) => client.FnNas.DeleteUserAsync(client.FnNasConnection, FnNasJson.Text(user, "guid"), ct), token);
        if (!Current(token)) return;
        Editing = false;
        Password = ConfirmPassword = "";
        await ReadUsersAsync(scope, token);
        if (Current(token)) Notify("已删除", $"用户「{name}」已删除。", InfoBarSeverity.Success);
    });

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private Task UnlockAsync() => !CanUnlock ? Task.CompletedTask : RunAsync("解锁用户失败", async (scope, token) =>
    {
        if (_original is not { } user) return;
        await scope.ExecuteAsync((client, ct) => client.FnNas.UnlockUserAsync(client.FnNasConnection, FnNasJson.Text(user, "guid"), ct), token);
        if (!Current(token)) return;
        Editing = false;
        Password = ConfirmPassword = "";
        await ReadUsersAsync(scope, token);
        if (Current(token)) Notify("已解锁", "用户现在可以重新登录。", InfoBarSeverity.Success);
    });

    partial void OnEditingChanged(bool value) => AnnounceEditor();
    partial void OnEditingDefaultChanged(bool value) => AnnounceEditor();
    partial void OnPermissionChanged(int value) => OnPropertyChanged(nameof(LibrariesVisibility));
    private void AnnounceEditor()
    {
        foreach (var name in new[] { nameof(IsNew), nameof(IsAdministrator), nameof(EditorTitle), nameof(ListVisibility), nameof(EditorVisibility), nameof(IdentityVisibility), nameof(PermissionVisibility), nameof(LibrariesVisibility), nameof(DeleteVisibility), nameof(UnlockVisibility), nameof(PasswordNote) }) OnPropertyChanged(name);
        BusyChanged();
    }
    protected override void BusyChanged()
    {
        base.BusyChanged();
        foreach (var name in new[] { nameof(CanSave), nameof(CanDelete), nameof(CanUnlock) }) OnPropertyChanged(name);
        NewUserCommand.NotifyCanExecuteChanged();
        DefaultsCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        UnlockCommand.NotifyCanExecuteChanged();
    }
    public override void Cancel()
    {
        base.Cancel();
        Password = ConfirmPassword = "";
    }
}
