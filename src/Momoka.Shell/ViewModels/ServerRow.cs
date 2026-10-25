using System.Collections.ObjectModel;
using Momoka.Configuration;
using Momoka.Emby;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 一台保存的服务器，画成一张卡片。
/// <para>
/// 卡片头上那一行是「服务器名 · 类型 · 账号」，右边只剩登录、下线、删除三颗 —— 用户 2026-10-25 的原话：
/// 「服务器名 账号 emby放到一行里，右边只保留删除和登录……不要分两个卡片」。以前是外层一张服务器卡、里面再
/// 套一张账号卡，两圈边框、两套图标，同一件事要说两遍；现在账号就写在服务器名旁边，多出来的账号排在下面
/// 一行行（没有第二圈边框），一眼能数清这台服务器上有谁。
/// </para>
/// <para>
/// 快照，自己不通知：这一页的每一次编辑 —— 连接测试改出来的名字、删掉的账号、切走的来源 —— 都会改变有哪些行、
/// 哪一行是当前的，所以 <see cref="ServersViewModel.Refresh"/> 重建整个列表，而不是想办法补丁式地改它。
/// 不会变的行不需要说自己变了。
/// </para>
/// </summary>
public sealed class ServerRow
{
    internal ServerRow(ServersViewModel owner, ServerProfile profile, bool current, AccountProfile? currentAccount,
        IReadOnlyList<EmbySessionScope> sources, bool aggregate)
    {
        Owner = owner;
        Profile = profile;
        IsCurrent = current;
        Aggregate = aggregate;
        var online = sources
            .Where(source => source.ServerProfileId == profile.Id)
            .Select(source => source.AccountProfileId)
            .ToHashSet(StringComparer.Ordinal);
        IsOnline = online.Count > 0;

        var rows = profile.Accounts
            .Select(account => new AccountRow(owner, profile, account,
                current && ReferenceEquals(account, currentAccount), online.Contains(account.Id), aggregate))
            .ToList();

        // 头上那一个是「最该被点的那个」：当前账号优先，其次在线账号，最后才是列表里第一个。
        // 离线账号排在前面时，点「登录」应该进的是那台服务器上还活着的那一个 —— 这是 PreferredAccount 的
        // 同一套取舍，只是这里要具体到一个 AccountRow 才画得出来。
        PrimaryAccount = rows.FirstOrDefault(row => row.IsCurrent)
            ?? rows.FirstOrDefault(row => row.IsConnected)
            ?? rows.FirstOrDefault();
        Accounts = new ObservableCollection<AccountRow>(rows);
        Others = new ObservableCollection<AccountRow>(
            rows.Where(row => !ReferenceEquals(row, PrimaryAccount)));
    }

    /// <summary>
    /// The row's way back to the commands.
    /// <para>
    /// A <c>DataTemplate</c>'s <c>x:Bind</c> is rooted at the item, not the page, so a button inside one
    /// can only reach a command through the item it is drawing. One command on the view model taking the
    /// row as its parameter beats a copy of every command on every row — and it beats what this page did
    /// before, which was <c>Tag="{Binding}"</c> plus a <c>Click</c> handler that pattern-matched the
    /// tag back out again and did nothing at all if the match failed.
    /// </para>
    /// </summary>
    public ServersViewModel Owner { get; }

    public ServerProfile Profile { get; }

    /// <summary>Whether this is the server the session's default management account is on right now.</summary>
    public bool IsCurrent { get; }

    /// <summary>这台服务器此刻有没有账号在线 —— 卡片头上那颗「在线」标记。</summary>
    public bool IsOnline { get; }

    /// <summary>聚合模式开着的时候才有「下线」这颗按钮：独占模式下下线等于退出，那是另一件事。</summary>
    public bool Aggregate { get; }

    public string Name => string.IsNullOrWhiteSpace(Profile.Name) ? ServerProfile.DefaultName : Profile.Name;

    public string Url => Profile.Url;

    public string KindLabel => ServerAccess.KindName(Profile.ServerKind);

    /// <summary>卡片头上那个账号：名字、状态各一行都取它。</summary>
    public AccountRow? PrimaryAccount { get; }

    /// <summary>这台服务器上的全部账号，头上那一个也在里面 —— 数「这一页有多少账号」时用它。</summary>
    public ObservableCollection<AccountRow> Accounts { get; }

    /// <summary>这台服务器上除头上那一个以外的账号，排在下面，没有第二圈卡片。</summary>
    public ObservableCollection<AccountRow> Others { get; }

    public string AccountName => PrimaryAccount?.Username ?? "无账号";

    public string AccountStatus => PrimaryAccount?.Status ?? "还没有保存的账号";

    /// <summary>
    /// 自检用来数「这一页真的把账号画出来了几个」：头上那个账号是直接写进卡片首行的，不走
    /// <see cref="AccountRow"/> 的模板，所以视觉树里没有它自己的 <c>ContentPresenter</c>。给它一个只在真有时
    /// 才出现的标识，<see cref="Views.ServersPage.Realised"/> 才数得对 —— 否则一台一账号的服务器会被报成
    /// 「绑了 1 个、画了 0 个」。
    /// </summary>
    public string PrimaryAccountMarker => PrimaryAccount is null ? "" : "ServerPrimaryAccount";

    public string LoginLabel => IsOnline ? "切换到此服务器" : "登录服务器";

    public string AddAccountLabel => "新增账号";

    public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OnlineVisibility => IsOnline ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OfflineVisibility =>
        Aggregate && PrimaryAccount?.IsConnected == true ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>右键菜单里「退出登录」那一项的可见性：只有在线账号才有登录状态可退。</summary>
    public Visibility PrimarySignOutVisibility =>
        PrimaryAccount?.SignOutVisibility ?? Visibility.Collapsed;

    public Visibility OthersVisibility => Others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// One saved account. Carries its owning server as well as its owner, so signing in needs neither a walk
/// up the visual tree nor a second lookup by id.
/// </summary>
public sealed class AccountRow
{
    internal AccountRow(ServersViewModel owner, ServerProfile server, AccountProfile account, bool current,
        bool connected, bool aggregate)
    {
        Owner = owner;
        Server = server;
        Account = account;
        IsCurrent = current;
        IsConnected = connected;
        Aggregate = aggregate;
    }

    /// <inheritdoc cref="ServerRow.Owner" />
    public ServersViewModel Owner { get; }

    public ServerProfile Server { get; }

    public AccountProfile Account { get; }

    public string Username => string.IsNullOrWhiteSpace(Account.Username) ? "未命名账号" : Account.Username;

    /// <summary>
    /// What signing in as this account would take. Ordered by how little it asks of the user, which is
    /// also the order the sign-in page tries them in.
    /// </summary>
    public string Status => IsCurrent
        ? "已连接 · 默认管理账号"
        : IsConnected ? "已连接"
        : Account.HasSavedToken ? "已保存登录令牌"
        : Account.HasSavedPassword ? "已保存密码"
        : "需要登录";

    public bool IsCurrent { get; }

    public bool IsConnected { get; }

    public bool Aggregate { get; }

    public string LoginLabel => IsConnected ? "设为默认管理账号" : "登录账号";

    public string DeleteLabel => "删除账号";

    public Visibility LoginVisibility => IsCurrent ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SignOutVisibility => IsConnected ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>下线只在聚合模式下出现，而且只对在线账号出现 —— 离线的账号没有「下线」可言。</summary>
    public Visibility OfflineVisibility =>
        Aggregate && IsConnected ? Visibility.Visible : Visibility.Collapsed;
}
