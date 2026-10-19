using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Momoka.Configuration;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Services;
using Momoka.Shell;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// The servers page: saved servers and their accounts, a scan of the local network, and what the poster
/// cache has taken up on disk.
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
    private EmbyImageStore? _images;
    private IShellActions? _actions;

    /// <summary>The saved servers, rebuilt whenever any of them changes. See <see cref="ServerRow"/>.</summary>
    public ObservableCollection<ServerRow> Servers { get; } = [];

    /// <summary>What the last network scan answered. Cleared at the start of the next one.</summary>
    public ObservableCollection<EmbyDiscoveredServer> Discovered { get; } = [];

    [ObservableProperty]
    public partial string? Subheading { get; set; }

    /// <summary>
    /// 连接模式的两个单选档。只写不读回用户输入（RadioButton 的 Command 走 <see cref="SetMode"/>），
    /// 显示值由 <see cref="Refresh"/> 从设置文件算出来 —— 一处决定，拨完开关和打开页面看到的是同一份答案。
    /// </summary>
    [ObservableProperty]
    public partial bool ExclusiveMode { get; set; }

    [ObservableProperty]
    public partial bool AggregateMode { get; set; }

    [ObservableProperty]
    public partial string ModeTitle { get; set; } = "";

    [ObservableProperty]
    public partial string ModeDescription { get; set; } = "";

    /// <summary>这一页顶上的「当前界面」：Emby 界面、Jellyfin 界面或通用界面。</summary>
    [ObservableProperty]
    public partial string InterfaceTitle { get; set; } = "";

    [ObservableProperty]
    public partial string InterfaceDescription { get; set; } = "";

    /// <summary>独占模式一个账号、聚合模式全部来源的那一句在线摘要。</summary>
    [ObservableProperty]
    public partial string OnlineSummary { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool ShowEmpty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscoveredVisibility))]
    public partial bool ShowDiscovered { get; set; }

    /// <summary>The cache line: 「1.1 GB · 3,481 个文件 · 上限 400 MB」 — the budget being whatever the
    /// 界面 card's 图片缓存上限 row currently says, not a fixed 400.</summary>
    [ObservableProperty]
    public partial string? CacheSummary { get; set; }

    public Visibility EmptyVisibility => Show(ShowEmpty);

    public Visibility DiscoveredVisibility => Show(ShowDiscovered);

    /// <summary>
    /// Whether the page is free to start something. One flag for the network scan, the connection test
    /// and the cache wipe together: they all write the same notice bar and the same progress bar, so two
    /// at once would have the second one's result overwrite the first one's.
    /// </summary>
    public bool CanRun => !Busy;

    private bool CanTest(ServerRow? row) => CanRun && row is not null;

    /// <summary>
    /// Whether <see cref="Attach"/> has run. All four collaborators arrive in that one call as
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
        ClearCacheCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Handed the shell and the three capabilities this page works through, once per navigation to it.
    /// <para>
    /// All three are kept rather than read once: this page is the only one that writes servers and accounts
    /// back, and every button on it is a fresh read of the same three.
    /// </para>
    /// </summary>
    internal void Attach(IShellActions actions, ISettingsService settings, EmbySession session, EmbyImageStore images)
    {
        _actions = actions;
        _settings = settings;
        _session = session;
        _images = images;
    }

    /// <inheritdoc />
    public override Task ReloadAsync()
    {
        Refresh();
        return MeasureCacheAsync();
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
        var mode = Settings.ServerAccess.Mode;
        var exclusive = mode == ServerConnectionMode.Exclusive;

        var serverIds = sources.Select(source => source.ServerProfileId).Distinct(StringComparer.Ordinal).ToArray();
        var flavor = ServerAccess.InterfaceFor(serverIds.Length, sources.Select(source => source.Connection.ServerKind));
        var decidesInterface = serverIds.Length == 1 ? serverIds[0] : null;

        ExclusiveMode = exclusive;
        AggregateMode = !exclusive;
        ModeTitle = ServerAccess.ModeName(mode);
        ModeDescription = exclusive
            ? "同一时间只登录一台服务器、一个账号。切换到别的服务器或账号时，当前连接退出；保存的账号和密码保留。"
            : "可同时登录多台服务器、多个账号，主页、搜索与收藏合并显示。同时连接多台服务器时使用通用界面。";
        InterfaceTitle = ServerAccess.InterfaceName(flavor);
        InterfaceDescription = DescribeInterface(flavor, serverIds.Length, sources.Count);
        OnlineSummary = DescribeOnline(current, account, serverIds.Length, sources.Count, exclusive);

        Servers.Clear();
        foreach (var server in Settings.Servers)
            Servers.Add(new ServerRow(this, server, ReferenceEquals(server, current), account, sources, mode,
                decidesInterface is not null && string.Equals(decidesInterface, server.Id, StringComparison.Ordinal)));

        ShowEmpty = Servers.Count == 0;
        Subheading = Servers.Count == 0
            ? "管理服务器与登录用户"
            : exclusive
                ? $"已保存 {Servers.Count} 台服务器 · {ServerAccess.ModeName(mode)}，同一时间一个账号在线"
                : $"已保存 {Servers.Count} 台服务器 · {sources.Count} 个账号同时在线，主页与搜索自动聚合";
    }

    private static string DescribeInterface(ServerInterfaceFlavor flavor, int serverCount, int accountCount)
    {
        if (accountCount == 0)
            return "登录后按当前服务器切换界面；同时连接多台服务器时回到通用界面。";

        if (flavor == ServerInterfaceFlavor.Generic)
            return serverCount > 1
                ? $"同时连着 {serverCount} 台服务器，服务端特有的按钮与管理入口已收起，只保留各端共有的功能。"
                : "Plex 先用通用界面：服务端管理入口由服务器自己的能力决定。";

        if (flavor == ServerInterfaceFlavor.FnNas)
            return "由当前唯一的飞牛影视服务器决定，设置左栏照它的「帐号／影视服务器」目录显示。";

        return $"由当前唯一的服务器决定，服务端管理与网页控制台照 {ServerAccess.InterfaceName(flavor)} 的网页端显示。";
    }

    private static string DescribeOnline(ServerProfile? server, AccountProfile? account, int serverCount, int accountCount,
        bool exclusive)
    {
        if (accountCount == 0) return "当前未登录";
        if (exclusive)
            return $"在线：{server?.Name ?? "服务器"} · {account?.Username ?? "账号"}（独占模式）";
        return $"{serverCount} 台服务器 · {accountCount} 个账号在线";
    }

    /// <summary>
    /// 拨连接模式。独占模式立刻把其它在线来源收掉（凭据保留），并让主窗口与设置左栏当场跟上。
    /// </summary>
    private void SetMode(ServerConnectionMode mode)
    {
        if (!Attached || Settings.ServerAccess.Mode == mode) return;

        Settings.ServerAccess.Mode = mode;
        _settings!.Save();
        var closed = _session!.ApplyAccessMode();
        ShellPrefs.ApplyServerAccess();
        Refresh();

        var title = mode == ServerConnectionMode.Exclusive ? "已切换到独占模式" : "已切换到聚合模式";
        var message = mode switch
        {
            ServerConnectionMode.Exclusive when closed > 0 =>
                $"已退出 {closed} 个其它在线账号；保存的账号和密码仍然保留，需要时可以再登录。",
            ServerConnectionMode.Exclusive => "以后同一时间只保留一台服务器、一个账号在线。",
            _ => "现在可以同时登录多台服务器和多个账号。"
        };
        Notify(title, message, InfoBarSeverity.Success);
    }

    [RelayCommand]
    private void UseExclusive() => SetMode(ServerConnectionMode.Exclusive);

    [RelayCommand]
    private void UseAggregate() => SetMode(ServerConnectionMode.Aggregate);

    [RelayCommand]
    private void AddServer() => _actions?.OpenSignIn();

    [RelayCommand]
    private void AddAccount(ServerRow? row)
    {
        if (row is not null) _actions?.OpenSignIn(row.Profile, newAccount: true);
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

    /// <summary>Signs in to a server, reusing whichever account was last used on it.</summary>
    [RelayCommand]
    private void LoginServer(ServerRow? row)
    {
        if (!Attached || row is null) return;

        var account = row.Profile.FindAccount(Settings.LastAccountId)
            ?? row.Profile.Accounts.FirstOrDefault();
        _ = _actions!.SwitchProfileAsync(row.Profile, account);
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

    /// <summary>
    /// Counts the poster cache. Not gated on <see cref="CanRun"/> and does not raise the progress bar:
    /// it is a caption, it runs on arrival, and it has no business disabling the buttons around it.
    /// <para>
    /// This is also where <see cref="PageViewModel.IsReady"/> is set, rather than in
    /// <see cref="Refresh"/>: the flag means 「everything on this page has its final content」, and the
    /// startup self-check reads the page the moment it goes true. Setting it as soon as the rows exist
    /// would have the report quote 「正在统计…」 as the cache size.
    /// </para>
    /// </summary>
    internal async Task MeasureCacheAsync()
    {
        if (!Attached) return;

        CacheSummary = "正在统计…";
        var usage = await _images!.MeasureAsync().ConfigureAwait(true);
        CacheSummary = Describe(usage, _images.MaxBytes);
        IsReady = true;
    }

    /// <summary>
    /// 需求 12：throw the cached artwork away. Wired here rather than on the settings page because this
    /// is where the local traces of a server are — the accounts, the saved tokens, and the pictures.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ClearCacheAsync()
    {
        if (!Attached) return;

        var confirmed = await ConfirmAsync(
            "清除图片缓存",
            $"将删除已下载的海报与缩略图（{CacheSummary}）。下次浏览会重新下载，不影响服务器和账号。",
            "清除").ConfigureAwait(true);
        if (!confirmed || !Attached) return;

        var token = BeginLoad();
        CacheSummary = "正在清除…";

        try
        {
            var removed = await _images!.ClearAsync().ConfigureAwait(true);
            if (!IsCurrent(token)) return;

            // The decoded ones too, or the covers already on screen keep showing from memory and the
            // 「清除了」 notice is about files the user cannot see.
            PosterCache.Clear();

            Notify("已清除图片缓存", $"删除了 {removed:N0} 个文件。", InfoBarSeverity.Success);
        }
        catch (Exception error)
        {
            Log.Warn(Category, "清除图片缓存失败", error);
            if (IsCurrent(token)) Report("清除图片缓存失败", error);
        }
        finally
        {
            EndLoad(token);
            await MeasureCacheAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 「上限」 comes along because a size on its own says nothing: 1.1 GB is either a problem or exactly
    /// what was asked for, and the number that decides which is the budget beside it.
    /// </summary>
    private static string Describe(ImageCacheUsage usage, long max)
    {
        var limit = $"上限 {TimeFormat.FileSize(max)}";
        return usage.Files == 0
            ? $"暂无缓存 · {limit}"
            : $"{TimeFormat.FileSize(usage.Bytes)} · {usage.Files:N0} 个文件 · {limit}";
    }
}
