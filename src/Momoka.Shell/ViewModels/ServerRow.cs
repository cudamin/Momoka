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

    /// <summary>在线状态排序的分组键：有账号在线的一组在前，没有的一组在后。</summary>
    public int StatusRank => Accounts.Any(account => account.IsConnected) ? 0 : 1;

    public ObservableCollection<AccountRow> Accounts { get; }

    public Visibility EmptyVisibility => Accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>按当前排序方式重排账号卡；Manual 保留保存顺序，不移动集合。</summary>
    internal void SortAccounts(ServerSortMode mode, bool descending)
    {
        if (mode == ServerSortMode.Manual) return;

        var ordered = ServerListOrder.Apply(Accounts, mode, descending,
            account => account.Username, account => account.StatusRank);
        for (var index = 0; index < ordered.Count; index++)
        {
            var from = Accounts.IndexOf(ordered[index]);
            if (from >= 0 && from != index) Accounts.Move(from, index);
        }
    }

    private List<AccountRow>? _accountDragOrder;

    /// <summary>拖拽换位会 Remove + Add；中间少一条的集合不写盘，完成后再提交一次最终顺序。</summary>
    internal void BeginAccountReorder() => _accountDragOrder ??= [.. Accounts];

    internal void EndAccountReorder()
    {
        var before = _accountDragOrder;
        _accountDragOrder = null;
        if (before is not null && !before.SequenceEqual(Accounts)) Owner.CommitManualOrder();
    }

    /// <summary>把屏上账号卡顺序写回这个服务器保存的账号列表；顺序没变就不动设置。</summary>
    internal void CommitAccountOrder()
    {
        if (Profile.Accounts.SequenceEqual(Accounts.Select(account => account.Account))) return;

        var ordered = Accounts.Select(account => account.Account).ToList();
        Profile.Accounts.Clear();
        Profile.Accounts.AddRange(ordered);
    }
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

    /// <summary>账号卡那颗主按钮：在线的动作是「设为默认」，离线的动作是「登录」。</summary>
    public string LoginShortLabel => IsConnected ? "设为默认" : "登录";

    /// <summary>完整提示与自动化名称；按钮上只用得下短标签。</summary>
    public string LoginLabel => IsConnected ? "设为默认管理账号" : "登录账号";

    public string LoginGlyph => IsConnected ? "\uE73E" : "\uE77B";

    /// <summary>状态排序的分组键：已连接 0、保存令牌 1、保存密码 2、需要登录 3。</summary>
    public int StatusRank => IsConnected ? 0 : Account.HasSavedToken ? 1 : Account.HasSavedPassword ? 2 : 3;

    public string DeleteLabel => "删除账号";

    public Visibility LoginVisibility => IsCurrent ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public Visibility OnlineVisibility => IsConnected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SignOutVisibility => IsConnected ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>下线保留凭据，只在聚合模式下对在线账号出现。</summary>
    public Visibility OfflineVisibility => Aggregate && IsConnected ? Visibility.Visible : Visibility.Collapsed;
}
