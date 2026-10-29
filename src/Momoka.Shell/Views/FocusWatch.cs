using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Momoka.Shell.Views;

/// <summary>
/// 「指针/焦点 整圈变强调色」里焦点那一半。胶片格的其余状态都是卡片自己的数据（看到一半、还剩几集、
/// 全看完），只有这两个是「用户此刻在哪」，而这两个当中只有焦点不在卡片身上。
/// <para>
/// 键盘焦点永远落不到 <see cref="PosterCard"/> 或 <see cref="EpisodeRow"/> 上：能接焦点的是外面那个容器
/// —— 媒体库里是 <c>ItemContainer</c>，主页和详情页里是包着卡片的 <c>Button</c>。页面各写一遍等于同一件
/// 事写三遍，所以先越过卡片自己的 <see cref="UserControl"/>，再找外面的单项容器。
/// </para>
/// <para>
/// 只接 <see cref="ButtonBase"/> 或 <see cref="ItemContainer"/>；中间的布局元素可跳过，遇到其他控件就
/// 停止，不能把 <c>ItemsView</c> 或 <c>ScrollView</c> 当单项宿主，否则别的卡片拿到焦点也会一起亮。
/// </para>
/// <para>
/// <c>GotFocus</c>/<c>LostFocus</c> 是冒泡事件，所以焦点落到容器内部（悬浮层那三个按钮）也算数，这正是
/// 要的：那时指针本来就在这张卡上。
/// </para>
/// </summary>
/// <param name="inside">卡片里的任意一个元素，从它开始往上找容器。</param>
/// <param name="paint">
/// 焦点来了传 true、走了传 false。调用方把它和指针那一半或起来 —— 两个来源各管自己的旗子，谁都不会
/// 把对方的状态擦掉。
/// </param>
internal sealed class FocusWatch(FrameworkElement inside, Action<bool> paint)
{
    private Control? _host;

    /// <summary>
    /// 挂上。<c>Loaded</c> 里调用，不能更早：容器是在卡片进树之后才在它上面的，构造函数里往上找什么都
    /// 找不到。重复调用无害。
    /// </summary>
    public void Attach()
    {
        if (_host is not null) return;

        _host = Host(inside);
        if (_host is null) return;

        _host.GotFocus += OnGot;
        _host.LostFocus += OnLost;

        // 回收回来的容器可能本来就带着焦点：它自己没有 Loaded/Unloaded，只有卡片有。所以挂上的时候
        // 先照实读一次，而不是假定「新来的都没焦点」。
        paint(_host.FocusState != FocusState.Unfocused);
    }

    /// <summary>
    /// 摘掉。<c>Unloaded</c> 里调用 —— 容器活得比卡片长，不摘就是往一个还在用的容器上挂着一个指向已经
    /// 回收掉的卡片的处理器。顺手把框线灭掉，理由和 <see cref="HoverWatch.Leave"/> 一样：容器换了个卡片
    /// 回来，不能带着上一张的状态。
    /// </summary>
    public void Detach()
    {
        if (_host is null) return;

        _host.GotFocus -= OnGot;
        _host.LostFocus -= OnLost;
        _host = null;

        paint(false);
    }

    /// <summary>自检：监听对象仍是这张卡当前的外层单项容器。</summary>
    public bool Probe() => _host is not null && ReferenceEquals(_host, Host(inside));

    private void OnGot(object sender, RoutedEventArgs e) => paint(true);

    private void OnLost(object sender, RoutedEventArgs e) => paint(false);

    private static Control? Host(DependencyObject start)
    {
        // Root 的父节点就是卡片自身；UserControl 也继承 Control，不能把它误当外层焦点宿主。
        DependencyObject? card = start;
        while (card is not null && card is not (PosterCard or EpisodeRow))
            card = VisualTreeHelper.GetParent(card);
        if (card is null) return null;

        for (var node = VisualTreeHelper.GetParent(card); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase or ItemContainer) return (Control)node;
            if (node is Control) return null;
        }

        return null;
    }
}
