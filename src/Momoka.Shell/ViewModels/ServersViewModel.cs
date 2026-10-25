using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Services;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// The servers page: saved servers and their accounts, the aggregate-mode switch, and a scan of the local
/// network.
/// <para>
/// Every button on the page is a command here. That is the part worth having — the page used to carry
/// nine <c>Click</c> handlers, three of which began with <c>if (_busy) return;</c>, so pressing 「测试」
/// while a scan was running looked exactly like pressing a button that did not work. A command that
/// cannot run says so by being greyed out, and the guard and the greying are the same line of code.
/// </para>
/// </summary>
public sealed partial class ServersViewModel : PageViewModel
{
    private const string Category = "服务器";

    private ISettingsService? _settings;
    private EmbySession? _session;
    private IShellActions? _actions;

    /// <summary>True while <see cref="Refresh"/> seeds the switch, so filling it does not read as a user flip.</summary>
    private bool _seedingAggregate;

    /// <summary>The saved servers, rebuilt whenever any of them changes. See <see cref="ServerRow"/>.</summary>
    public ObservableCollection<ServerRow> Servers { get; } = [];

    /// <summary>What the last network scan answered. Cleared at the start of the next one.</summary>
    public ObservableCollection<EmbyDiscoveredServer> Discovered { get; } = [];

    [ObservableProperty]
    public partial string? Subheading { get; set; }

    /// <summary>
    /// 聚合模式：开着时多台服务器、多个账号同时在线；关着时只保留一个在线来源。拨一下立刻写进设置并收口，
    /// 不要求重启。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AggregateNote))]
    [NotifyPropertyChangedFor(nameof(NoteVisibility))]
    public partial bool AggregateMode { get; set; }

    /// <summary>当前这一档对用户意味着什么；开关旁边那一行小字。</summary>
    public string AggregateNote => AggregateMode
        ? "多台服务器、多个账号可以同时在线；主页、搜索、收藏与版本选择覆盖全部在线账号。"
        : "同一时间只保留一台服务器、一个账号在线；登录另一台或另一个账号时替换当前来源，保存的凭据不删除。";

    /// <summary>
    /// 说明只在关着的时候占地方：开着的那一档本身就是结论，再摆一句解释只是把一行工具栏撑成两行
    /// （用户 2026-10-25：「开启…时候下方的说明要隐藏」）。
    /// </summary>
    public Visibility NoteVisibility => Show(!AggregateMode);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool ShowEmpty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscoveredVisibility))]
    public partial bool ShowDiscovered { get; set; }

    public Visibility EmptyVisibility => Show(ShowEmpty);

    public Visibility DiscoveredVisibility => Show(ShowDiscovered);

    /// <summary>
    /// Whether the page is free to start something. One flag for the network scan and the connection test
    /// together: they both write the same notice bar and the same progress bar, so two at once would have
    /// the second one's result overwrite the first one's.
    /// </summary>
    public bool CanRun => !Busy;

    private bool CanTest(ServerRow? row) => CanRun && row is not null;

    /// <summary>
    /// Whether <see cref="Attach"/> has run. All three collaborators arrive in that one call as
    /// non-nullable parameters, so this single question answers for every one of them — including for the
    /// network scan, which happens to touch none of them and must still do nothing on a page nobody
    /// attached. Past this gate the fields are dereferenced with <c>!</c>.
    /// </summary>
    private bool Attached => _settings is not null;

    /// <summary>
    /// The live settings object, read through the service rather than copied: it is one instance for the
    /// process and the settings page edits it in place.
    /// </summary>
    private AppSettings Settings => _settings!.Settings;

    /// <inheritdoc />
    protected override void BusyChanged()
    {
        OnPropertyChanged(nameof(CanRun));
        DiscoverCommand.NotifyCanExecuteChanged();
        TestCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Handed the shell and the three capabilities this page works through, once per navigation to it.
    /// <para>
    /// All three are kept rather than read once: this page is the only one that writes servers and accounts
    /// back, and every button on it is a fresh read of the same three.
    /// </para>
    /// </summary>
    internal void Attach(IShellActions actions, ISettingsService settings, EmbySession session)
    {
        _actions = actions;
        _settings = settings;
        _session = session;
    }

    /// <inheritdoc />
    public override Task ReloadAsync()
    {
        Refresh();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Rebuilds the rows from the settings file. Synchronous on purpose: this is a read of an object
    /// graph already in memory, and a progress bar for it would flash rather than inform.
    /// </summary>
    [RelayCommand]
    internal void Refresh()
    {
        if (!Attached) return;

        var current = _session!.Server;
        var account = _session.Account;
        var sources = _session.Sources;

        _seedingAggregate = true;
        try { AggregateMode = Settings.AggregateMode; }
        finally { _seedingAggregate = false; }

        Servers.Clear();
        foreach (var server in Settings.Servers)
            Servers.Add(new ServerRow(this, server, ReferenceEquals(server, current), account, sources, AggregateMode));

        ShowEmpty = Servers.Count == 0;
        Subheading = Servers.Count == 0
            ? "管理服务器与登录用户"
            : sources.Count == 0
                ? $"已保存 {Servers.Count} 台服务器"
                : $"已保存 {Servers.Count} 台服务器 · {sources.Count} 个账号在线";
        IsReady = true;
    }

    /// <summary>
    /// 拨了聚合模式。关掉：先写设置，再让会话按新档位收口，最后重读一遍行上的在线状态；如果收掉了别的
    /// 在线来源，说一句它们只是下线、保存的账号和密码都还在，免得看起来像被删了。
    /// <para>
    /// 打开：把已保存的账号都接回来。这一条以前是空的 —— 开关拨过去以后什么也不发生，用户还得回这一页
    /// 一台台点「登录账号」，于是「打开聚合模式」看着像没生效（2026-10-25 判为缺陷）。恢复要发网络请求，
    /// 所以是异步的，进度条就挂在页面顶上。
    /// </para>
    /// </summary>
    partial void OnAggregateModeChanged(bool value)
    {
        if (_seedingAggregate || !Attached) return;

        Settings.AggregateMode = value;
        _settings!.Save();

        if (!value)
        {
            var removed = _session!.ApplyAggregateMode();
            Refresh();

            if (removed > 0)
                Notify("已关闭聚合模式", $"其余 {removed} 个在线账号已下线；服务器、账号和保存的凭据都还在。",
                    InfoBarSeverity.Informational);
            return;
        }

        _ = RestoreSavedAccountsAsync();
    }

    /// <summary>
    /// 打开聚合模式之后把能自动登录的账号全接回来。一个都接不上也算成功 —— 这一页本来就可以什么都没有，
    /// 提示说清楚「之后登录的账号会并存」就够了。
    /// </summary>
    private async Task RestoreSavedAccountsAsync()
    {
        if (_session is null) return;

        var token = BeginLoad();
        try
        {
            var online = await _session.RestoreAllAsync(token).ConfigureAwait(true);
            if (!IsCurrent(token)) return;

            Refresh();
            var restorable = _session.RestorableAccounts;
            if (restorable > online)
                Notify("已开启聚合模式",
                    $"{online} 个账号在线；{restorable - online} 个没能自动登录，可以在列表里手动登录。",
                    InfoBarSeverity.Warning);
            else if (online > 1)
                Notify("已开启聚合模式", $"{online} 个账号同时在线。", InfoBarSeverity.Success);
            else
                Notify("已开启聚合模式", "之后登录的其他账号会与当前账号同时在线，不会再替换掉它。",
                    InfoBarSeverity.Informational);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Log.Warn(Category, "打开聚合模式后自动登录账号失败", error);
            if (IsCurrent(token)) Report("自动登录账号失败", error);
        }
        finally
        {
            EndLoad(token);
        }
    }

    /// <summary>
    /// 工具栏那颗「新增账号」：有服务器就加到当前这一台上，一台都没有才退回填地址那一步 —— 那一步里也有
    /// 「已保存的服务器」菜单，所以两台服务器之间换着加账号不用先回列表。
    /// </summary>
    [RelayCommand]
    private void NewAccount()
    {
        if (!Attached) return;

        var server = _session?.Server ?? Settings.Servers.FirstOrDefault();
        if (server is null) _actions?.OpenSignIn();
        else _actions?.OpenSignIn(server, newAccount: true);
    }

    [RelayCommand]
    private void AddAccount(ServerRow? row)
    {
        if (row is not null) _actions?.OpenSignIn(row.Profile, newAccount: true);
    }

    /// <summary>
    /// 聚合模式下的「下线」：把这个账号从在线来源里摘掉，账号本身、保存的令牌和密码都留着，点「登录」就回来。
    /// 和「退出登录」不是一回事 —— 后者会清掉令牌并关掉自动连接。
    /// </summary>
    [RelayCommand]
    private void TakeOffline(AccountRow? row)
    {
        if (!Attached || row is null) return;
        if (!_session!.TakeOffline(row.Server, row.Account)) return;

        Refresh();
        Notify("已下线", $"{row.Username} 已下线；账号和保存的凭据都还在，点「登录」就能回来。",
            InfoBarSeverity.Informational);
    }

    /// <summary>需求 1：find servers on this network without anyone typing an address.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task DiscoverAsync()
    {
        if (!Attached) return;

        var token = BeginLoad();
        Discovered.Clear();
        ShowDiscovered = false;

        try
        {
            var found = await new EmbyServerDiscovery()
                .DiscoverAsync(cancellationToken: token)
                .ConfigureAwait(true);

            if (!IsCurrent(token)) return;

            foreach (var server in found) Discovered.Add(server);
            ShowDiscovered = Discovered.Count > 0;

            Notify(
                Discovered.Count > 0 ? "发现局域网服务器" : "未发现服务器",
                Discovered.Count > 0
                    ? $"找到 {Discovered.Count} 台 Emby 服务器，请选择要添加的服务器。"
                    : "当前网络没有响应 Emby 发现请求。",
                Discovered.Count > 0 ? InfoBarSeverity.Informational : InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Log.Warn(Category, "搜索局域网服务器失败", error);
            if (IsCurrent(token)) Report("搜索局域网服务器失败", error);
        }
        finally
        {
            EndLoad(token);
        }
    }

    /// <summary>
    /// Saves a discovered server and goes to sign in. Called from the page rather than bound as a
    /// command: the item in that list is a Core record with no way back to this view model, and a
    /// <c>ListView</c> whose rows are pickable is the right control for choosing one of several.
    /// </summary>
    internal void OpenDiscovered(EmbyDiscoveredServer discovered)
    {
        if (!Attached) return;

        try
        {
            var profile = _settings!.ResolveServer(discovered.Url);
            if (profile.HasPlaceholderName && !string.IsNullOrWhiteSpace(discovered.Name))
                profile.Name = discovered.Name;

            _settings.Save();
            ShowDiscovered = false;
            _actions!.OpenSignIn(profile);
        }
        catch (Exception error)
        {
            Log.Warn(Category, "保存发现的服务器失败", error);
            Report("保存发现的服务器失败", error);
        }
    }

    /// <summary>
    /// Asks the server who it is, without signing in. Doubles as the way a placeholder name gets
    /// replaced with the real one, which is the only reason a successful test rebuilds the rows.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync(ServerRow? row)
    {
        if (!Attached || row is null) return;

        if (!EmbyServerAddress.TryNormalize(row.Profile.Url, out var apiBase, out var complaint))
        {
            Notify("连接失败", complaint, InfoBarSeverity.Error);
            return;
        }

        var token = BeginLoad();

        try
        {
            var info = await _session!.Gateway
                .DetectAsync(row.Profile.Url, token)
                .ConfigureAwait(true);

            if (!IsCurrent(token)) return;

            row.Profile.ServerKind = info.ServerKind;
            row.Profile.ServerVersion = info.Version;
            row.Profile.ServerId = info.Id;
            row.Profile.Url = EmbyServerAddress.ToDisplayString(info.ApiBase!, info.ServerKind);
            _settings!.Save();

            if (info.ServerName is { Length: > 0 } name && row.Profile.HasPlaceholderName)
            {
                row.Profile.Name = name;
                _settings!.Save();
                Refresh();
            }

            var version = string.IsNullOrWhiteSpace(info.Version) ? "" : $" · 版本 {info.Version}";
            Notify("连接成功", $"{info.ServerName ?? apiBase.Host}{version}", InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Log.Warn(Category, $"测试服务器连接失败：{row.Profile.Url}", error);
            if (IsCurrent(token)) Report("连接失败", error);
        }
        finally
        {
            EndLoad(token);
        }
    }

    /// <summary>
    /// 卡片头上那颗「登录」：认的就是头上写着的那个账号，写的是谁就登谁。一个账号都没有时直接去登录页，
    /// 省得用户先想「这里为什么没反应」。
    /// </summary>
    [RelayCommand]
    private void LoginPrimary(ServerRow? row)
    {
        if (!Attached || row is null) return;

        if (row.PrimaryAccount is not { } account)
        {
            _actions?.OpenSignIn(row.Profile, newAccount: true);
            return;
        }

        LoginAccount(account);
    }

    [RelayCommand]
    private void LoginAccount(AccountRow? row)
    {
        if (_actions is null || row is null) return;
        var source = _session?.Sources.FirstOrDefault(source => source.ServerProfileId == row.Server.Id
            && source.AccountProfileId == row.Account.Id);
        if (source is not null)
        {
            _session!.SelectSource(source);
            Refresh();
            return;
        }
        _ = _actions.SwitchProfileAsync(row.Server, row.Account);
    }

    [RelayCommand]
    private void SignOutAccount(AccountRow? row)
    {
        if (!Attached || row is null) return;
        _session!.SignOut(row.Server, row.Account);
        Refresh();
    }

    [RelayCommand]
    private async Task DeleteServerAsync(ServerRow? row)
    {
        if (!Attached || row is null) return;

        var confirmed = await ConfirmAsync(
            "删除服务器",
            $"确定删除“{row.Name}”及其保存的 {row.Accounts.Count} 个账号吗？",
            "删除").ConfigureAwait(true);
        if (!confirmed || !Attached) return;

        _session!.SignOutServer(row.Profile);

        Settings.Servers.Remove(row.Profile);
        if (Settings.LastServerId == row.Profile.Id)
        {
            Settings.LastServerId = null;
            Settings.LastAccountId = null;
        }

        _settings!.Save();
        Refresh();
    }

    [RelayCommand]
    private async Task DeleteAccountAsync(AccountRow? row)
    {
        if (!Attached || row is null) return;

        var confirmed = await ConfirmAsync("删除账号", $"确定删除账号“{row.Username}”吗？", "删除")
            .ConfigureAwait(true);
        if (!confirmed || !Attached) return;

        _session!.SignOut(row.Server, row.Account);

        row.Server.Accounts.Remove(row.Account);
        if (Settings.LastAccountId == row.Account.Id) Settings.LastAccountId = null;

        _settings!.Save();
        Refresh();
    }
}
