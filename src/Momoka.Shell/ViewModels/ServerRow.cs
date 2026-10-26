using System.Collections.ObjectModel;
using Momoka.Configuration;
using Momoka.Emby;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 保存的服务器分组，不是可见卡片：每个账号由自己的 AccountRow 画成一张独立卡片。
/// 没有账号时保留服务器的登录入口，避免最后一个账号删除后无法重新登录。
/// <para>
/// 行是快照。编辑账号或切换来源会改变行数和连接状态，由 ServersViewModel.Refresh 重建；
/// 绑定的集合本身保持不变。
/// </para>
/// </summary>
public sealed class ServerRow
{
    internal ServerRow(ServersViewModel owner, ServerProfile profile, bool current, AccountProfile? currentAccount,
        IReadOnlyList<EmbySessionScope> sources, bool aggregate)
    {
        Owner = owner;
        Profile = profile;
        var online = sources
            .Where(source => source.ServerProfileId == profile.Id)
            .Select(source => source.AccountProfileId)
            .ToHashSet(StringComparer.Ordinal);

        Accounts = new ObservableCollection<AccountRow>(profile.Accounts.Select(account =>
            new AccountRow(this, account, current && ReferenceEquals(account, currentAccount),
                online.Contains(account.Id), aggregate)));
    }

    /// <summary>模板通过行访问页面的命令，每张卡片以自己的账号作为命令参数。</summary>
    public ServersViewModel Owner { get; }

    public ServerProfile Profile { get; }

    public string Name => string.IsNullOrWhiteSpace(Profile.Name) ? ServerProfile.DefaultName : Profile.Name;

    public string Url => Profile.Url;

    public string KindLabel => ServerAccess.KindName(Profile.ServerKind);

    public ObservableCollection<AccountRow> Accounts { get; }

    public Visibility EmptyVisibility => Accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>一个保存的账号对应一张卡片；保留所属服务器，让每个动作明确作用于这个账号。</summary>
public sealed class AccountRow
{
    internal AccountRow(ServerRow serverGroup, AccountProfile account, bool current, bool connected, bool aggregate)
    {
        ServerGroup = serverGroup;
        Account = account;
        IsCurrent = current;
        IsConnected = connected;
        Aggregate = aggregate;
    }

    public ServerRow ServerGroup { get; }

    public ServersViewModel Owner => ServerGroup.Owner;

    public ServerProfile Server => ServerGroup.Profile;

    public AccountProfile Account { get; }

    public string ServerName => ServerGroup.Name;

    public string Url => ServerGroup.Url;

    public string KindLabel => ServerGroup.KindLabel;

    public string Username => string.IsNullOrWhiteSpace(Account.Username) ? "未命名账号" : Account.Username;

    /// <summary>凭据的优先级与登录页一致；在线和当前状态只属于这个账号，不借用同服其他账号的状态。</summary>
    public string Status => IsConnected ? "已连接"
        : Account.HasSavedToken ? "已保存登录令牌"
        : Account.HasSavedPassword ? "已保存密码"
        : "需要登录";

    public bool IsCurrent { get; }

    public bool IsConnected { get; }

    public bool Aggregate { get; }

    public string LoginLabel => IsConnected ? "设为默认管理账号" : "登录账号";

    public string DeleteLabel => "删除账号";

    public Visibility LoginVisibility => IsCurrent ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OnlineVisibility => IsConnected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SignOutVisibility => IsConnected ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>下线保留凭据，只在聚合模式下对在线账号出现。</summary>
    public Visibility OfflineVisibility => Aggregate && IsConnected ? Visibility.Visible : Visibility.Collapsed;
}
