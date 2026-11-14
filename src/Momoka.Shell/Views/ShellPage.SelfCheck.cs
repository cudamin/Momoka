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
    /// <param name="Ok">可见按键都摆对了，排成一行，整排也在标题栏里。</param>
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
    /// 自检：标题栏左上那一排此刻的排法与显隐。
    /// <para>
    /// 2026-11-12 晚的规矩（用户令「主页不显示返回按钮，进入媒体页面或者媒体库页面的时候显示，放左上角，
    /// 折叠侧边栏按钮移到它右边」「标题栏左上角写 Momoka」，前进键同日退役）：非侧边栏模式是
    /// 返回（非主页）· 主页 · 收藏（有收藏才在）· 设置 · 搜索（窄窗才在）；侧边栏模式是
    /// 返回（非主页）· 折叠（搜索入口 2026-11-13 起不在栏里，只剩标题栏正中那颗框）。这一关判五件事：
    /// 返回的显隐与目的地对得上、该在的都在、可见的那几颗按这个次序排成一行、折叠键紧贴在返回右边、
    /// 整排落在标题栏那 40 像素里。
    /// </para>
    /// <para>
    /// 「折叠键在返回右边」在主页上看不见（返回收着），所以量之前先把它摆出来一拍，量完在 finally 里交回
    /// <see cref="SyncChrome"/> —— 和下面那几档箭头状态是同一套做法：摆的是状态，量的是真几何。
    /// </para>
    /// </summary>
    internal TitleActionProbe ProbeTitleActions()
    {
        var saved = _trail.ToArray();

        try
        {
            Windows.Foundation.Point At(FrameworkElement element) =>
                element.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, 0));

            // 返回的判据本身：主页收起，其余都在。问的是那条纯函数，不必真去导航一趟。
            var backRule = BackVisibilityFor("home") == Visibility.Collapsed
                && BackVisibilityFor("library:1") == Visibility.Visible
                && BackVisibilityFor(null) == Visibility.Visible;

            // 收藏是唯一会自己收放的一颗：判据与 SyncTitleActions 同一句 —— 有收藏且不在侧边栏模式。
            var favoritesShown = !SidebarShown && _session?.IsSignedIn == true && Favorites.HasItems;
            var backExpected = ContentHost.Visibility == Visibility.Visible
                ? BackVisibilityFor(_current)
                : Visibility.Collapsed;
            var slots = BackButton.Visibility == backExpected
                && NavToggleButton.Visibility == (SidebarShown ? Visibility.Visible : Visibility.Collapsed)
                && HomeButton.Visibility == (SidebarShown ? Visibility.Collapsed : Visibility.Visible)
                && FavoritesButton.Visibility == (favoritesShown ? Visibility.Visible : Visibility.Collapsed)
                && SettingsButton.Visibility == (SidebarShown ? Visibility.Collapsed : Visibility.Visible);

            // 返回与折叠的先后：摆出来量一拍（主页上返回本来收着）。
            // **量之前一定要排一拍**：刚露面的返回键在这一次布局之前 ActualWidth 还是 0，而下面那条
            // 「各格都有尺寸」当场就会红 —— 这一句是 2026-11-12 晚收藏页那一跑红出来的。
            if (BackButton.Visibility == Visibility.Collapsed) BackButton.Visibility = Visibility.Visible;
            UpdateLayout();

            // 声明次序就是视觉次序：返回 → 折叠 → 主页 → 收藏 → 设置/搜索（应用名独立于按钮行）。
            FrameworkElement[] order =
                [BackButton, NavToggleButton, HomeButton, FavoritesButton, SettingsButton, SearchButton];
            var keys = order.Where(key => key.Visibility == Visibility.Visible).ToArray();
            var places = keys.Select(At).ToArray();

            var lined = true;
            var widest = 0.0;
            var rowCentre = places[0].Y + keys[0].ActualHeight / 2;
            for (var index = 1; index < places.Length; index++)
            {
                var gap = places[index].X - (places[index - 1].X + keys[index - 1].ActualWidth);
                widest = Math.Max(widest, gap);
                // 按键之间挨着排（0–8）—— 这一排全是 40 见方的托盘、间距 2，应用名不占按钮行里的格子。
                const double allowed = 8;
                // 竖着比中心：按键 32 高、标题那行字只有二十来像素，比上沿会把「居中对齐」判成「错开一行」。
                var centre = places[index].Y + keys[index].ActualHeight / 2;
                lined &= gap >= 0 && gap <= allowed && Math.Abs(centre - rowCentre) < 2;
            }

            // 折叠键紧贴返回右边（用户令「折叠侧边栏按钮移到它右边」）：两者都可见时它们必须是相邻的两格。
            var toggleAfterBack = !SidebarShown
                || (BackButton.Visibility == Visibility.Visible
                    && Math.Abs(At(NavToggleButton).X - (At(BackButton).X + BackButton.ActualWidth)) <= 8
                    && Math.Abs(At(NavToggleButton).Y - At(BackButton).Y) < 1);

            // 2026-11-14 返回与折叠按下方导航图标盒统一为 20，其他标题栏入口沿用 28。
            var glyph = keys.OfType<Button>().Select(button => button.Content as Viewbox)
                .FirstOrDefault(viewbox => viewbox is not null);
            var enlarged = glyph is { ActualWidth: >= 20, ActualHeight: >= 20 }
                && BackButton.Content is Viewbox { Width: 20, Height: 20 }
                && NavToggleButton.Content is Viewbox { Width: 20, Height: 20 };

            var sized = TitleActions.ActualWidth > 0 && TitleActions.ActualHeight > 0
                && keys.All(key => key.ActualWidth > 0 && key.ActualHeight > 0);

            // 在标题栏里：整排从顶部下移 4 像素起（2026-11-13 起 40 高的托盘落在 48 高的条子里），
            // 托盘底边离标题栏下沿 4 像素 —— 既没有越出去，也不是居中。
            var group = At(TitleActions);
            var bar = At(AppTitleBar);
            var lowered = Math.Abs(group.Y - (bar.Y + 4)) <= 1;
            var inBar = lowered
                && group.Y + TitleActions.ActualHeight <= bar.Y + AppTitleBar.Height
                && group.X > bar.X
                && group.X + TitleActions.ActualWidth <= bar.X + AppTitleBar.ActualWidth;

            // 两支箭头的状态：走不动时只是变暗、面包屑按层数收放。前进键退役之后只剩返回这一支。
            ApplyChrome(false, 1);
            UpdateLayout();
            var idle = !BackButton.IsEnabled && TrailBar.Visibility == Visibility.Collapsed;

            ApplyChrome(true, 1);
            UpdateLayout();
            var shallow = BackButton.IsEnabled && TrailBar.Visibility == Visibility.Collapsed;

            _trail.Add(new Crumb("自检", string.Empty));
            _trail.Add(new Crumb("自检 · 下一层", string.Empty));
            ApplyChrome(true, _trail.Count);
            UpdateLayout();
            var deep = BackButton.IsEnabled && TrailBar.Visibility == Visibility.Visible;

            var ok = backRule && slots && sized && enlarged && inBar && lined && toggleAfterBack
                && idle && shallow && deep;

            return new TitleActionProbe(ok,
                $"{keys.Length} 格：{string.Join(" · ", keys.Select(key => key.Name))}，"
                    + $"整排从 ({group.X:0},{group.Y:0}) 起，占 {TitleActions.ActualWidth:0}×{TitleActions.ActualHeight:0}，最大间距 {widest:0}；"
                    + $"标题栏 {AppTitleBar.ActualWidth:0}×{AppTitleBar.Height:0}，{(inBar ? "整排在里面" : "整排没落在标题栏里")}；"
                    + $"{(lined ? "依次排成一行" : "没排成一行")}；"
                    + $"{(toggleAfterBack ? "折叠键贴在返回右边" : "折叠键没贴在返回右边")}；"
                    + $"{(sized ? "各格都有尺寸" : "有格子没尺寸")}、{(enlarged ? "导航图标是 20 的统一框" : "导航图标尺寸不一致")}、"
                    + $"该在的{(slots ? "都在" : "有缺")}、返回判据{(backRule ? "对" : "错")}、"
                    + $"哪儿都走不动时{(idle ? "返回按不动、面包屑收起" : "不对")}、"
                    + $"退得回去时{(shallow ? "返回亮、面包屑收着" : "不对")}、"
                    + $"退过一步时{(deep ? "返回亮且面包屑可见" : "不对")}；"
                    + $"收藏 {(FavoritesButton.Visibility == Visibility.Visible ? "在" : "收")}"
                    + $"（{(favoritesShown ? "有收藏" : "没有收藏")}）、"
                    + $"设置 {(SettingsButton.Visibility == Visibility.Visible ? "在" : "收")}、"
                    + $"搜索框 {(TitleSearchBox.Visibility == Visibility.Visible ? $"在（{TitleSearchBox.ActualWidth:0} 宽）" : "收")}、"
                    + $"放大镜 {(SearchButton.Visibility == Visibility.Visible ? "在" : "收")}、"
                    + $"返回 {(BackButton.Visibility == Visibility.Visible ? "在" : "收")}/{BackButton.IsEnabled}、"
                    + $"折叠 {(NavToggleButton.Visibility == Visibility.Visible ? "在" : "收")}",
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
    /// <item>整条外壳正好 48 高（<c>ChromeHeight</c>）—— 页面顶上让开的那一段就是这个数，少让就是页头被切半行，
    /// 多让就是图上一条底色，而第 1 行没删干净的样子正是这里多出来的那 48。内容区自己的上内边距也得是同一
    /// 个数：标题栏加高时只改一边，页面就会被压掉或露出一条底。</item>
    /// <item>账号那颗按钮贴着右上角（留在系统那三颗窗口按钮左边），而且和左边那一排同在标题栏这 48 像素里
    /// —— 它挪进来是第 1 行删掉的后果，落位错了就压不进挖给它的洞，一按就是拖窗口。</item>
    /// <item>账号那一格的样子跟着模式走：侧边栏模式只留一颗 40×40 的人像（和栏里那些按钮同一块方格，
    /// 中心 y=24 与标题栏正中那颗搜索框对齐），其余模式是一行「名字 · 服务器」。两行那一档 2026-11-13 随
    /// 「人像图标移到最小化左边」一起退役 —— 名字和服务器名只在标题栏那一格里露字。</item>
    /// </list>
    /// 那颗「主页」按钮和左边那一排的落位排布由 <see cref="ProbeTitleActions"/> 一起量。
    /// </summary>
    internal (bool Ok, string Detail) ProbeChrome()
    {
        var shown = Chrome.Visibility == Visibility.Visible && AppTitleBar.Visibility == Visibility.Visible;

        var barAt = At(AppTitleBar);
        var chrome = barAt.Y + AppTitleBar.Height;
        var stacked = Math.Abs(chrome - HomePage.ChromeHeight) <= 0.5;
        // 页面顶上让开的那一段必须和外壳同高：2026-11-01 标题栏 32→40、2026-11-13 40→48 时，忘掉这一份就会
        // 让页面从旧的高度起画、被外壳压掉一截。2026-11-12 晚起这一份从内边距搬到了外边距（画布要让开标题栏
        // 那一层材质，见 ShellPage.xaml 的 ContentHost），量的是同一个数。
        var padded = Math.Abs(ContentHost.Margin.Top - HomePage.ChromeHeight) <= 0.5;

        var accountAt = At(AccountButton);
        var right = accountAt.X + AccountButton.ActualWidth;
        var accountShown = AccountButton.Visibility == Visibility.Visible;

        // 账号那一颗 2026-11-13 起只有一种落位：标题栏右上角、系统那三颗窗口按钮左边（用户令「人像图标移到
        // 最小化左边」）。要求它真的在屏上、没有压出窗口、没越进那三颗按钮的地盘，并且和左边那一排同在
        // 标题栏这一行里 —— 落位错了就压不进挖给它的洞，一按就是拖窗口。
        var corner = accountShown
            && AccountButton.ActualWidth > 0
            && AccountButton.ActualHeight > 0
            && right <= ActualWidth + 0.5
            && ActualWidth - right <= 200
            && accountAt.Y >= barAt.Y - 0.5
            && accountAt.Y + AccountButton.ActualHeight <= chrome + 0.5;

        // 那一格的样子：侧边栏模式只留一颗 40×40 的人像（和栏里那些按钮同一块方格、同一条中线）；
        // 其余模式是一行「名字 · 服务器」，名字在服务器名左边、中间那颗「·」露着。
        // 一行那一档比的是**竖直中心**不是上沿：名字是 13 号 SemiBold（实高约 16.5）、服务器名走 12 号
        // 等宽（约 14），两格都 VerticalAlignment=Center，上沿天生差 (16.5−14)/2≈1.3 像素 —— 比上沿会把
        // 这个字体高度差当成「没排在一行」，而屏上两者的基线其实对得只差 0.05 像素。容差取 1.5。
        var iconOnly = SidebarShown
            && AccountGlyph.Visibility == Visibility.Visible
            && AccountText.Visibility == Visibility.Collapsed
            && Math.Abs(AccountButton.ActualWidth - 40) <= 0.5
            && Math.Abs(AccountButton.ActualHeight - 40) <= 0.5;
        var userAt = At(AccountUser);
        var serverAt = At(AccountServer);
        var textMode = !SidebarShown
            && AccountText.Visibility == Visibility.Visible
            && AccountDot.Visibility == Visibility.Visible
            && userAt.X < serverAt.X
            && Math.Abs(userAt.Y + AccountUser.ActualHeight / 2 - (serverAt.Y + AccountServer.ActualHeight / 2)) <= 1.5;
        var arrangement = iconOnly || textMode;
        var arrangementText = iconOnly
            ? "只留一颗 40×40 的人像"
            : textMode ? "一行：名字 · 服务器" : "那一格的样子不对（该人像或一行文字）";

        var ok = shown && stacked && padded && corner && arrangement;

        return (ok,
            $"{(shown ? "外壳在屏上" : "外壳收着了")}；"
                + $"标题栏从 y={barAt.Y:0} 起、高 {AppTitleBar.Height:0}，外壳共 {chrome:0} 高"
                + $"（该 {HomePage.ChromeHeight:0}{(stacked ? "" : "，对不上")}）"
                + $"，内容区上外边距 {ContentHost.Margin.Top:0}{(padded ? "" : " 对不上")}；"
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

    /// <summary>
    /// 标题栏正中那颗搜索框此刻落在窗口里的矩形，给「标题栏按键」那一关和窗口挖的洞对一遍。null 是「收着」
    /// （窄窗、播放中、没登录）—— 那一档窗口也不该为它留洞。
    /// </summary>
    internal (double X, double Y, double Width, double Height)? ProbeSearchHole() => SearchRect();
}
