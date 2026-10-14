using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Momoka.Shell.Views;

/// <summary>
/// 自检看外壳的那一面。分出来一份而不是塞进 <c>ShellPage.xaml.cs</c>：这些成员只有自检调用，路由那边一个也
/// 用不到，混在一起读的人分不清哪些是外壳真正的行为。
/// </summary>
public sealed partial class ShellPage
{
    /// <summary>标题栏那一排按键的样子，逻辑像素，量的是整排。</summary>
    /// <param name="Ok">五颗都摆对了，排成一行，整排也在标题栏里。</param>
    internal sealed record TitleActionProbe(bool Ok, string Detail, double X, double Y, double Width, double Height);

    /// <summary>
    /// 自检：主页首屏 —— 顶上一块框起来的轮播卡片（四边留间隔，2026-09-10「弄个框把轮播图框起来」），底下横排
    /// 接在它下沿之后。
    /// <para>
    /// 判据：卡片高度就是 <see cref="HomeCarousel.Height"/> 按它自己的宽算出来的那个数（带宽比页宽窄 48），整块
    /// 放得进第一屏，横排接在卡片下沿之后（<see cref="HomePage.FoldRead"/>）。顺带报一句「页宽就是客户区宽」——
    /// 这句话变假就说明谁又在页面左边塞了一列。
    /// </para>
    /// </summary>
    internal (bool? Ok, string Detail) ProbeHomeFold()
    {
        if (Pages.Content is not HomePage home) return (false, "当前不是主页");
        if (_window is null) return (false, "量不到主窗口的形状");
        if (!_window.BrowseFoldMeasurable)
            return (null, "窗口比 16:9 还扁（或正在放片子／全屏／最大化），首屏边界这一读只报不判");

        // HomeBanner 的 SizeChanged 会在这一轮布局里改高度，再走一轮让货架拿到最终坐标。
        UpdateLayout();
        home.UpdateLayout();
        UpdateLayout();

        var fold = home.FoldRead();
        var width = home.ActualWidth;
        var client = (XamlRoot?.Size.Width ?? 0) - ContentHost.Margin.Left;
        var detail = $"页宽 {width:0}：{fold.Detail}";

        if (fold.Ok is null) return (null, detail);

        var fullWidth = client > 0 && Math.Abs(width - client) <= 2;

        return (fold.Ok == true && fullWidth,
            detail + $"；客户区宽 {client:0}"
                + (fullWidth ? "，页面占满导航后的内容区" : "，和内容区宽度对不上"));
    }

    /// <summary>
    /// 把这一排按键的几种状态各摆一遍，量出来对一遍：设置、搜索两颗任何时候都按得动；两头都走不动时两支箭头
    /// 都在、都按不动（**2026-10-13 晚起不再变淡** —— 停用走的是静止那支墨，见 <c>_titleInk</c>，所以这一关
    /// 问的是 <c>IsEnabled</c>，不是墨的深浅）；能退不能进时只有返回按得动；两样都能走又有两层路径时，两支箭头
    /// 加面包屑那一行。主页那颗的亮暗跟着 <c>_current</c> 走，这里不摆它 —— 排成一行、落在标题栏里这两件事照量。
    /// <para>
    /// 之所以要一个探针而不是直接看现场：后两颗由 <c>Frame.CanGoBack</c>、<c>Frame.CanGoForward</c> 和路径深度
    /// 决定，而前两件是框架自己算的，自检没法让它说某句话。<see cref="ApplyChrome"/> 把三件事变成参数，于是每
    /// 种状态都能摆出来 —— 收尾时把路径原样放回去，再按现场重新决定一次。
    /// </para>
    /// <para>
    /// 量回来的矩形还有第二个用处：<c>ShellSelfCheck</c> 拿它和窗口留的那个洞对一遍。洞开错了地方在屏幕上
    /// 完全看不出来，直到有人去点按键，结果把窗口拖走。账号那颗按钮也在这一条上、也有自己的洞，那半个由
    /// <see cref="ProbeChrome"/> 量 —— 这里只管左边这一排五颗的次序和落位。
    /// </para>
    /// </summary>
    internal TitleActionProbe ProbeTitleActions()
    {
        var saved = _trail.ToArray();

        try
        {
            Windows.Foundation.Point At(FrameworkElement element) =>
                element.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, 0));

            // 顺序就是屏幕上从左到右的顺序。侧边栏模式里前三颗是「导航开关、后退、前进」—— 开关 2026-10-13
            // 从侧边栏内部挪到标题栏左上角，两支箭头因此紧跟在它右边（见 ShellPage.Sidebar.cs）。
            Button[] keys = SidebarShown ? [NavToggleButton, BackButton, ForwardButton] : FavoritesButton.Visibility == Visibility.Visible
                ? [HomeButton, FavoritesButton, SettingsButton, SearchButton, BackButton, ForwardButton]
                : [HomeButton, SettingsButton, SearchButton, BackButton, ForwardButton];

            // 前两颗跟去过哪儿无关，所以三种状态下都得亮着 —— 一颗跟着导航变暗的设置按键，是按不出设置窗口。
            bool AlwaysOn() => SettingsButton.IsEnabled && SearchButton.IsEnabled;

            // 哪儿也走不动：五颗照旧站着，只是后两颗按不动 —— 会消失的按键会挪动旁边那些，而这一排的位置就是
            // 窗口那个洞的位置，挪一下洞就开歪了。面包屑那一行此时整行收起，不留一条空带子。
            ApplyChrome(false, false, 1);
            UpdateLayout();
            var idle = Chrome.Visibility == Visibility.Visible
                && AppTitleBar.Visibility == Visibility.Visible
                && keys.All(key => key.Visibility == Visibility.Visible)
                && AlwaysOn()
                && !BackButton.IsEnabled
                && !ForwardButton.IsEnabled
                && TrailBar.Visibility == Visibility.Collapsed;

            // 退得动、进不动、只有一层：只有返回亮着，面包屑那行收着 —— 一条只有一格的面包屑说的就是页面
            // 标题自己那句话。
            ApplyChrome(true, false, 1);
            UpdateLayout();
            var shallow = AlwaysOn()
                && BackButton.IsEnabled
                && !ForwardButton.IsEnabled
                && TrailBar.Visibility == Visibility.Collapsed;

            // 钻进去一层又退回来过：两支箭头都亮，面包屑那行也在。
            _trail.Add(new Crumb("自检", string.Empty));
            _trail.Add(new Crumb("自检 · 下一层", string.Empty));
            ApplyChrome(true, true, _trail.Count);
            UpdateLayout();
            var deep = AlwaysOn()
                && BackButton.IsEnabled
                && ForwardButton.IsEnabled
                && TrailBar.Visibility == Visibility.Visible;

            // 位置：整排在标题栏那 32 像素里，左端留白在它左边，页面在它下面。
            var group = At(TitleActions);
            var bar = At(AppTitleBar);
            var places = keys.Select(At).ToArray();

            var sized = TitleActions.ActualWidth > 0 && TitleActions.ActualHeight > 0
                && keys.All(key => key.ActualWidth > 0 && key.ActualHeight > 0);

            // 在标题栏里：上下不越出那 32 像素 —— 托盘 26 高，上下各留 3 像素（取等号也算通过，这里只问「没
            // 越出去」）。左边那段留白留着拖窗口，右边离系统那三个按钮还远。
            var inBar = group.Y >= bar.Y
                && group.Y + TitleActions.ActualHeight <= bar.Y + AppTitleBar.Height
                && group.X > bar.X
                && group.X + TitleActions.ActualWidth <= bar.X + AppTitleBar.ActualWidth;

            // 一排：从左到右按顺序排开，彼此挨着，同一高度 —— Claude app 那一行就是这么排的。
            var lined = true;
            var widest = 0.0;
            for (var index = 1; index < places.Length; index++)
            {
                var gap = places[index].X - (places[index - 1].X + keys[index - 1].ActualWidth);
                widest = Math.Max(widest, gap);
                lined &= gap >= 0 && gap <= 8 && Math.Abs(places[index].Y - places[0].Y) < 1;
            }

            var ok = idle && shallow && deep && sized && inBar && lined;

            return new TitleActionProbe(ok,
                $"{keys.Length} 颗按键各 {keys[0].ActualWidth:0}×{keys[0].ActualHeight:0}，整排从 ({group.X:0},{group.Y:0}) 起"
                    + $"，占 {TitleActions.ActualWidth:0}×{TitleActions.ActualHeight:0}，最大间距 {widest:0}；"
                    + $"标题栏 {AppTitleBar.ActualWidth:0}×{AppTitleBar.Height:0}，{(inBar ? "整排在里面" : "整排没落在标题栏里")}；"
                    + $"{(lined ? "依次排成一行" : "没排成一行")}；"
                    + $"哪儿都走不动时{(idle ? "当前模式的按键都在、设置搜索可用、两支箭头按不动、面包屑整行收起" : "不对")}、"
                    + $"只能退时{(shallow ? "只有返回按得动" : "不对")}、"
                    + $"两头都能走时{(deep ? "两支都按得动加面包屑" : "不对")}",
                group.X, group.Y, TitleActions.ActualWidth, TitleActions.ActualHeight);
        }
        finally
        {
            _trail.Clear();
            foreach (var crumb in saved) _trail.Add(crumb);

            // 现场怎样就怎样：探针摆过的状态到这里全部作废。
            SyncChrome();
            UpdateLayout();
        }
    }

    /// <summary>
    /// 自检：外壳顶上那一行（2026-09-09「删掉栏目后原处留空位」之后，第 1 行连着那条 48 像素空带一起删掉了，
    /// 外壳只剩标题栏这一行）。这一关剩下四件事，每一件的坏法在截图里都看不出来：
    /// <list type="number">
    /// <item>那一行在屏上（登录页和播放时整块收起）。</item>
    /// <item>整条外壳正好 32 高（<c>ChromeHeight</c>）—— 页面顶上让开的那一段就是这个数，少让就是页头被切半行，
    /// 多让就是图上一条底色，而第 1 行没删干净的样子正是这里多出来的那 48。</item>
    /// <item>账号那颗按钮贴着右上角（留在系统那三颗窗口按钮左边），而且和左边那一排五颗同在标题栏那 32 像素里
    /// —— 它挪进来是第 1 行删掉的后果，落位错了就压不进挖给它的洞，一按就是拖窗口。</item>
    /// <item>账号那两格字的排法跟着落位走：侧边栏展开那一档两行（用户名在上、服务器名在下），其余各档一行
    /// （「名字 · 服务器」）。</item>
    /// </list>
    /// 那颗「主页」按钮和左边那一排的落位排布由 <see cref="ProbeTitleActions"/> 一起量。
    /// </summary>
    internal (bool Ok, string Detail) ProbeChrome()
    {
        var shown = Chrome.Visibility == Visibility.Visible && AppTitleBar.Visibility == Visibility.Visible;

        var barAt = At(AppTitleBar);
        var chrome = barAt.Y + AppTitleBar.Height;
        var stacked = Math.Abs(chrome - HomePage.ChromeHeight) <= 0.5;

        var accountAt = At(AccountButton);
        var right = accountAt.X + AccountButton.ActualWidth;
        var accountShown = AccountButton.Visibility == Visibility.Visible;

        // 账号那一组 2026-10-13 起有两种落位：侧边栏在屏上时它整组落在窗口左下角（栏的左端、贴着下沿），
        // 不在侧边栏模式时它回到标题栏右上角、留在系统那三颗窗口按钮左边。两种都要求它真的在屏上、
        // 而且没有压出窗口。
        var clusterAt = At(AccountCluster);
        var corner = accountShown
            && AccountButton.ActualWidth > 0
            && AccountButton.ActualHeight > 0
            && (SidebarShown
                ? clusterAt.X <= 1
                    && clusterAt.Y + AccountCluster.ActualHeight >= ActualHeight - 1
                    && clusterAt.X + AccountCluster.ActualWidth <= ActualWidth + 0.5
                : right <= ActualWidth + 0.5
                    && ActualWidth - right <= 200
                    && accountAt.Y >= barAt.Y - 0.5
                    && accountAt.Y + AccountButton.ActualHeight <= chrome + 0.5);

        // 账号那两格字的排法（2026-10-13 用户：「改成用户名在上服务器名在下」）：侧边栏展开那一档是两行 ——
        // 用户名在上、服务器名在下、左沿对齐在同一条竖线上，中间那个「·」收起，按钮真长到了两行那么高；
        // 其余各档是一行 —— 「名字 · 服务器」，名字在服务器名左边。排错了在截图里都只是「一行小字」，
        // 得逐字读才看得出来，所以按两格字的相对位置判，不按截图。
        var twoLines = SidebarShown && Sidebar.IsPaneOpen;
        var textShown = AccountText.Visibility == Visibility.Visible;
        var userAt = At(AccountUser);
        var serverAt = At(AccountServer);
        var arrangement = !textShown
            ? true
            : twoLines
                ? userAt.Y + AccountUser.ActualHeight <= serverAt.Y + 1
                    && Math.Abs(userAt.X - serverAt.X) <= 1
                    && AccountDot.Visibility == Visibility.Collapsed
                    && AccountButton.ActualHeight >= AccountUser.ActualHeight + AccountServer.ActualHeight
                : Math.Abs(userAt.Y - serverAt.Y) <= 1 && userAt.X < serverAt.X;
        var arrangementText = !textShown
            ? "那两格字收着（图标栏）"
            : twoLines
                ? arrangement ? "两行：用户名在上、服务器名在下" : "两格字的排法不对（该两行、用户名在上）"
                : arrangement ? "一行：名字 · 服务器" : "两格字的排法不对（该一行、名字在左）";

        var ok = shown && stacked && corner && arrangement;

        return (ok,
            $"{(shown ? "外壳在屏上" : "外壳收着了")}；"
                + $"标题栏从 y={barAt.Y:0} 起、高 {AppTitleBar.Height:0}，外壳共 {chrome:0} 高"
                + $"（该 {HomePage.ChromeHeight:0}{(stacked ? "" : "，对不上")}）；"
                + $"账号 {(accountShown ? $"{AccountButton.ActualWidth:0}×{AccountButton.ActualHeight:0} 左沿 {accountAt.X:0}／窗口 {ActualWidth:0}×{ActualHeight:0}" : "收着")}"
                + $"，{(corner ? SidebarShown ? "落在左下角、在侧边栏里" : "贴着右上角、在标题栏里" : "不在它该在的位置")}"
                + $"，{arrangementText}");

        Windows.Foundation.Point At(FrameworkElement element) =>
            element.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, 0));
    }

    /// <summary>
    /// 自检：账号那颗按钮此刻落在窗口里的矩形，给「标题栏按键」那一关拿去和窗口为它挖的洞对一遍 ——
    /// 账号名一换宽度就变，那个洞是跟着 <c>SizeChanged</c> 重报的，这里量的是现场那一份。null 是「收着」
    /// （未登录），那一关对 null 只报不判。
    /// </summary>
    internal (double X, double Y, double Width, double Height)? ProbeAccountHole() => AccountRect();
}
