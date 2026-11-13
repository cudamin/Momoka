using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Momoka.Emby;

namespace Momoka.Shell.ViewModels;

/// <summary>
/// 影视作品里的一组：一种署名、它的总数和先摆出来的那几行。
/// <para>
/// 行集合是稳定对象（<see cref="Rows"/> 只增不换），和页面其它列表同一条约定；<see cref="Total"/>
/// 是服务器答的总数，标题上的「(156)」和「查看全部」的露面都由它决定，不由已经摆出来的行数决定。
/// </para>
/// </summary>
public sealed class PersonCreditGroup
{
    public string Type { get; init; } = "";

    /// <summary>「演员 (156)」。</summary>
    public string Heading { get; init; } = "";

    public int Total { get; init; }

    public ObservableCollection<PersonCreditRow> Rows { get; } = [];

    public Visibility MoreVisibility => Total > Rows.Count ? Visibility.Visible : Visibility.Collapsed;
}
