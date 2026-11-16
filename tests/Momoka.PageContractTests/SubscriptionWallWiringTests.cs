using Microsoft.UI.Xaml;
using Momoka.Emby;
using Momoka.MoviePilot;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests
{
    /// <summary>
    /// 订阅墙在列表页的接线契约：分类页立墙收横排、汇总库保留横排收墙，墙随附加/离开收放轮询信号，
    /// 分类页页面空态收起。编译链接真实页面 code-behind，墙视图与横排视图是边界替身（只记录接线与启停信号），
    /// 不证明真实 WinUI 布局、DispatcherTimer 启停或渲染——那些由运行期 UI 探针覆盖。
    /// </summary>
    internal static class SubscriptionWallWiringTests
    {
        internal static void Register()
        {
            foreach (var key in new[] { HomeLayout.MoviePilotMovies, HomeLayout.MoviePilotSeries })
            {
                Test("订阅墙接线：分类页立墙收横排并启动轮询 " + key, () =>
                {
                    var page = new MoviePilotSubscriptionsPage();
                    page.Attach(Request(key));
                    Assert.True(page.ContractWallCategory);
                    Assert.Equal(Visibility.Visible, page.ContractWallVisibility);
                    Assert.Equal(Visibility.Collapsed, page.ContractRowVisibility);
                    Assert.Equal(1, page.ContractWallAttaches);
                    Assert.True(page.ContractWallActive, "分类页附加后墙应处于轮询激活态");
                    Assert.Equal(Visibility.Collapsed, page.ContractEmptyVisibility);
                });
                Test("订阅墙接线：真实 OnNavigatedTo 也立墙 " + key, () =>
                {
                    var page = new MoviePilotSubscriptionsPage();
                    page.ContractNavigateTo(Request(key));
                    Assert.Equal(Visibility.Visible, page.ContractWallVisibility);
                    Assert.True(page.ContractWallActive);
                });
                Test("订阅墙接线：Release 停轮询并松开墙 " + key, () =>
                {
                    var page = new MoviePilotSubscriptionsPage();
                    page.Attach(Request(key));
                    page.Release();
                    Assert.False(page.ContractWallActive, "释放后墙必须停轮询");
                    Assert.True(page.ContractWallDetaches >= 1);
                    page.Release();
                    Assert.False(page.ContractWallActive);
                });
                Test("订阅墙接线：OnNavigatedFrom 停轮询 " + key, () =>
                {
                    var page = new MoviePilotSubscriptionsPage();
                    page.ContractNavigateTo(Request(key));
                    page.ContractNavigateFrom();
                    Assert.False(page.ContractWallActive);
                });
            }
            Test("订阅墙接线：汇总库保留横排、收墙、不轮询", () =>
            {
                var page = new MoviePilotSubscriptionsPage();
                page.Attach(Request(MoviePilotSubscriptionsRequest.LibraryKey));
                Assert.False(page.ContractWallCategory);
                Assert.Equal(Visibility.Collapsed, page.ContractWallVisibility);
                Assert.Equal(Visibility.Visible, page.ContractRowVisibility);
                Assert.Equal(0, page.ContractWallAttaches);
                Assert.False(page.ContractWallActive);
                Assert.Equal(Visibility.Visible, page.ContractEmptyVisibility);
            });
            Test("订阅墙接线：分类页与汇总库往返时轮询跟着收放", () =>
            {
                var page = new MoviePilotSubscriptionsPage();
                page.Attach(Request(HomeLayout.MoviePilotMovies));
                Assert.True(page.ContractWallActive);
                page.Attach(Request(MoviePilotSubscriptionsRequest.LibraryKey));
                Assert.False(page.ContractWallActive, "切到汇总库应停墙轮询");
                Assert.Equal(Visibility.Collapsed, page.ContractWallVisibility);
                page.Attach(Request(HomeLayout.MoviePilotSeries));
                Assert.True(page.ContractWallActive, "切回分类页应重新立墙轮询");
                Assert.Equal(Visibility.Visible, page.ContractWallVisibility);
                page.Release();
                Assert.False(page.ContractWallActive);
            });
            // 契约测试用的是边界替身，加载不了真实 XAML：EgSpace* 是 Double 刻度，
            // 接到 Padding / Margin / BorderThickness（都是 Thickness）上会在 InitializeComponent
            // 抛 XamlParseException，订阅页当场打不开。Spacing 那几个是 Double，可以照常引。
            Test("订阅墙 XAML：Thickness 属性不引用 Double 刻度键", () =>
            {
                var root = FindRoot();
                foreach (var file in new[]
                {
                    "MoviePilotSubscriptionsPage.xaml",
                    "MoviePilotSubscriptionWallView.xaml",
                    "MoviePilotSubscriptionWallCard.xaml"
                })
                {
                    var text = File.ReadAllText(Path.Combine(root, "src", "Momoka.Shell", "Views", file));
                    foreach (var property in new[] { "Padding", "Margin", "BorderThickness" })
                        foreach (var key in new[] { "EgSpaceXS", "EgSpaceS", "EgSpaceM", "EgSpaceL", "EgSpaceXL", "EgSpace2XL" })
                            Assert.False(text.Contains(property + "=\"{StaticResource " + key + "}\""),
                                file + " 的 " + property + " 引了 Double 刻度键 " + key + "，运行时会在 InitializeComponent 抛 XamlParseException");
                }
            });
            // 2026-11-12 用户令：删掉那排药丸，复刻网页端订阅页左上角那三个图标。
            // 网页端 subscribe.vue 的 appendButtons 就是筛选／拖拽排序／批量三颗；排序项并进筛选面板。
            Test("订阅墙工具栏：网页端三个图标键，不再有药丸", () =>
            {
                var text = File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionWallView.xaml"));
                foreach (var name in new[] { "WallFilterButton", "WallSortModeButton", "WallBatchButton" })
                    Assert.True(text.Contains("x:Name=\"" + name + "\""), "工具栏缺少 " + name);
                foreach (var pill in new[] { "Content=\"拖拽排序\"", "Content=\"批量操作\"", "Content=\"维护\"", "Content=\"刷新订阅\"" })
                    Assert.False(text.Contains(pill), "工具栏还留着药丸键 " + pill);
                // 筛选面板照网页端那颗键的 VMenu：名称搜索 + 状态列表 + 排序列表。
                Assert.True(text.Contains("PlaceholderText=\"名称\""), "筛选面板缺少名称搜索");
                Assert.True(text.Contains("Text=\"状态\"") && text.Contains("Text=\"排序\""), "筛选面板缺少状态／排序两个小标题");
                Assert.True(text.Contains("x:Name=\"WallFilterOptions\"") && text.Contains("x:Name=\"WallSortOptions\""),
                    "筛选面板缺少状态／排序两个列表");
                Assert.False(text.Contains("WallSortFlyout"), "排序浮层应当并进筛选面板，不再单独一颗键");
            });
            // 2026-11-12 用户令：日期该到片名上方，不要和更多按钮一行（网页端 SubscribeCard 桌面档的 year 行）。
            Test("订阅墙卡片：年份一行排在片名上方", () =>
            {
                var text = File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionWallCard.xaml"));
                var year = text.IndexOf("x:Name=\"CardYear\"", StringComparison.Ordinal);
                var title = text.IndexOf("x:Name=\"CardTitle\"", StringComparison.Ordinal);
                Assert.True(year > 0, "卡片缺少年份那一行");
                Assert.True(title > year, "年份那一行必须排在片名之前");
                Assert.True(text.Contains("Card.WallYear") && text.Contains("Card.WallTitle"), "年份／片名要读墙上的那一对读数");
            });
            // 2026-11-16 用户令「订阅媒体库怎么不会跟媒体库一样自动调整封面大小」：卡片的宽度从前是写死的
            // 340（格子 352），窗口拖宽只多出空档、拖窄把卡片裁掉一截。现在格子宽度按这一页的可用宽度摊
            // （MoviePilotWallMetrics，与媒体库同一条算术），卡片横向撑满格子，高度仍是网页端那张卡的 150。
            Test("订阅墙卡片：宽度跟着格子走，不再钉死 340", () =>
            {
                var root = Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views");
                var card = File.ReadAllText(Path.Combine(root, "MoviePilotSubscriptionWallCard.xaml"));
                // 只认 Root 那一个开始标签里的属性：注释里留着旧数字当来由，不能拿整份文件找字符串。
                var start = card.IndexOf("<Grid x:Name=\"Root\"", StringComparison.Ordinal);
                Assert.True(start > 0, "找不到卡片根格");
                var tag = card.Substring(start, card.IndexOf('>', start) - start + 1);
                Assert.False(tag.Contains("Width=\""), "卡片根格又写死了宽度，窗口变化它不会跟着变：" + tag);
                Assert.True(tag.Contains("HorizontalAlignment=\"Stretch\""), "卡片要横向撑满自己的格子：" + tag);
                Assert.True(tag.Contains("Height=\"150\""), "卡片高度仍是网页端 SubscribeCard 的 150：" + tag);
                var wall = File.ReadAllText(Path.Combine(root, "MoviePilotSubscriptionWallView.xaml"));
                Assert.True(wall.Contains("SizeChanged=\"OnWallGridSizeChanged\"")
                    && wall.Contains("ContainerContentChanging=\"OnWallContainerChanging\""),
                    "网格没有接上「量到可用宽度就重算格宽」的入口");
                var code = File.ReadAllText(Path.Combine(root, "MoviePilotSubscriptionWallView.xaml.cs"));
                Assert.Contains("MoviePilotWallMetrics.Fit(available)", code);
                Assert.Contains("panel.ItemWidth = itemWidth;", code);
            });
            // 墙控件在本专项里是边界替身；这里守真实源码接线，实际弹出几何由下方原生探针量。
            Test("订阅墙菜单：目标是更多按钮，弹窗业务属主仍是页面", () =>
            {
                var root = Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views");
                var card = File.ReadAllText(Path.Combine(root, "MoviePilotSubscriptionWallCard.xaml.cs"));
                var wall = File.ReadAllText(Path.Combine(root, "MoviePilotSubscriptionWallView.xaml.cs"));
                Assert.Contains("internal FrameworkElement MenuAnchor => CardMore;", card);
                Assert.Contains("MoviePilotSubscriptionMenu.Create(this, ViewModel, card, () => Open(card), _service).ShowAt(control.MenuAnchor);", wall);
                Assert.False(wall.Contains(".ShowAt(control);"), "不能退回整张宽卡定位");
                Assert.Contains("MenuRequested?.Invoke(this, EventArgs.Empty)", card);
            });
            Test("订阅墙菜单：电影和电视剧都运行真实点击及几何探针", () =>
            {
                var root = Path.Combine(FindRoot(), "src", "Momoka.Shell");
                var sidebar = File.ReadAllText(Path.Combine(root, "ShellNavigationProbe.Sidebar.cs"));
                var probe = File.ReadAllText(Path.Combine(root, "ShellNavigationProbe.SubscriptionMenu.cs"));
                Assert.Contains("await CheckSubscriptionWallMenuAnchorAsync(wall, \"movie\");", sidebar);
                Assert.Contains("await CheckSubscriptionWallMenuAnchorAsync(Get<MoviePilotSubscriptionWallView>(seriesPage, \"Wall\"), \"series\");", sidebar);
                Assert.Contains("InvokeSubscriptionButton(button);", probe);
                Assert.Contains("Bounds(button, _root)", probe);
                Assert.Contains("Bounds(presenter!, _root)", probe);
                Assert.Contains("horizontalGap <= 12 && verticalGap <= 12 && (horizontalGap == 0 || verticalGap == 0)", probe);
                Assert.Contains("mp-wall-menu-{kind}-screen.png", probe);
                Assert.Contains("popup.IsOpen = false", probe);
            });
            // 2026-11-14 布局环修复后，窄行的集封面按行宽三分之一收图（上限 144、保持 16:9）；固定 144×81
            // 只在宽行成立。探针必须量展开统计列表里那一行；页面上已收起的通用集列表封面同名，不能抓错。
            Test("订阅季集封面：窄行按行宽三分之一收图，探针只量展开统计行", () =>
            {
                var root = Path.Combine(FindRoot(), "src", "Momoka.Shell");
                var view = File.ReadAllText(Path.Combine(root, "Views", "MoviePilotMediaSeasonsView.xaml.cs"));
                Assert.Contains("var width = Math.Min(144, Math.Max(0, rowWidth / 3));", view);
                Assert.Contains("var height = width * 9 / 16;", view);
                Assert.Contains("var actual = Math.Min(144, Math.Max(0, grid.ActualWidth / 3));", view);
                var detail = File.ReadAllText(Path.Combine(root, "ShellNavigationProbe.Subscriptions.cs"));
                Assert.Contains("repeater.Name == \"EpisodeStatistics\"", detail);
                Assert.Contains("Math.Min(144, pictureRow.ActualWidth / 3)", detail);
                var recovery = File.ReadAllText(Path.Combine(root, "ShellNavigationProbe.SubscriptionRecovery.cs"));
                Assert.Contains("Math.Min(144, row.ActualWidth / 3)", recovery);
            });
        }

        private static string FindRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
            throw new AssertionException("找不到本次编译所属的工作树");
        }

        private static MoviePilotSubscriptionsRequest Request(string key) => new(null!, key,
            _ => throw new AssertionException("只读接线契约不打开订阅或运行网络"));
    }
}
