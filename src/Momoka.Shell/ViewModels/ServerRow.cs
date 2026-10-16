using System.Collections.ObjectModel;
using Momoka.Configuration;
using Momoka.Emby;
using Microsoft.UI.Xaml;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// One saved server and the account rows drawn beneath it.
/// <para>
/// A snapshot, with no change notification of its own: every edit on this page — a rename from a
/// successful connection test, a deleted account, a switched profile — changes which rows exist or which
/// one is current, so <see cref="ServersViewModel.Refresh"/> rebuilds the list rather than trying to
/// patch it. Rows that cannot change do not need to say when they have.
/// </para>
/// </summary>
public sealed class ServerRow
{
    internal ServerRow(ServersViewModel owner, ServerProfile profile, bool current, AccountProfile? currentAccount,
        IReadOnlyList<EmbySessionScope> sources)
    {
        Owner = owner;
        Profile = profile;
        IsCurrent = current;
        Accounts = new ObservableCollection<AccountRow>(profile.Accounts.Select(account =>
            new AccountRow(owner, profile, account, current && ReferenceEquals(account, currentAccount),
                sources.Any(source => source.ServerProfileId == profile.Id && source.AccountProfileId == account.Id))));
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

    /// <summary>Whether this is the server the session is signed in to right now.</summary>
    public bool IsCurrent { get; }

    public string Name => string.IsNullOrWhiteSpace(Profile.Name) ? ServerProfile.DefaultName : Profile.Name;

    public string Url => Profile.Url;

    public string AccountSummary => Accounts.Count == 0 ? "无账号" : $"{Accounts.Count(account => account.IsConnected)} / {Accounts.Count} 个账号已连接";

    public ObservableCollection<AccountRow> Accounts { get; }

    public Visibility CurrentVisibility => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyAccountsVisibility => Accounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>
/// One saved account. Carries its owning server as well as its owner, so signing in needs neither a walk
/// up the visual tree nor a second lookup by id.
/// </summary>
public sealed class AccountRow
{
    internal AccountRow(ServersViewModel owner, ServerProfile server, AccountProfile account, bool current, bool connected)
    {
        Owner = owner;
        Server = server;
        Account = account;
        IsCurrent = current;
        IsConnected = connected;
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
        : IsConnected ? "已连接 · 已参与聚合"
        : Account.HasSavedToken ? "已保存登录令牌"
        : Account.HasSavedPassword ? "已保存密码"
        : "需要登录";

    public bool IsCurrent { get; }

    public bool IsConnected { get; }

    public string LoginLabel => IsConnected ? "设为默认管理账号" : "登录账号";

    public Visibility LoginVisibility => IsCurrent ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SignOutVisibility => IsConnected ? Visibility.Visible : Visibility.Collapsed;
}
