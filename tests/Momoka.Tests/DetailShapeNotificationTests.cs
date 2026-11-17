using System.ComponentModel;
using System.Reflection;
using Momoka.Emby;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static partial class DetailActionReadinessTests
{
    private static void RegisterShapeTests(Assembly assembly, string root)
    {
        Test("商店操作块：四类页面宽窄常驻，离开商店恢复已看文字且通知真实绑定", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season, Shape.Episode })
            {
                using var f = new Fixture(assembly, root, shape);
                Await(f.Load());
                f.Ui.EnableImmersive = false;
                var refresh = f.Vm.GetType().GetMethod("RefreshAppearance", Members)!;
                refresh.Invoke(f.Vm, null);
                foreach (var width in new[] { 1240d, 1024d, 760d, 360d })
                {
                    f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, width);
                    Assert.Equal("Visible", f.Get("StoreCoverActionsVisibility")!.ToString());
                    Assert.Equal("Collapsed", f.Get("ColumnWatchedLabelsVisibility")!.ToString());
                    Assert.Equal("Collapsed", f.Get("CompactVisibility")!.ToString());
                    Assert.Equal("Visible", f.Get("ColumnActionsVisibility")!.ToString());
                    if (shape == Shape.Episode)
                        Assert.Equal(width < DetailHero.CompactFloor, (bool)f.Get("IsCompact")!, "单集背景紧凑判定不应被按钮行改变");
                }
                var notices = new List<string>();
                ((INotifyPropertyChanged)f.Vm).PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
                f.Ui.EnableImmersive = true;
                refresh.Invoke(f.Vm, null);
                Assert.Equal("Collapsed", f.Get("StoreCoverActionsVisibility")!.ToString());
                Assert.Equal("Visible", f.Get("ColumnWatchedLabelsVisibility")!.ToString());
                foreach (var name in new[] { "StoreCoverActionsVisibility", "ColumnWatchedLabelsVisibility" })
                    Assert.True(notices.Contains(name), "模式恢复未通知 " + name);
            }
        });
        Test("商店模式：四类详情独立装饰背景，不增加正文占位，宽度与模式变化通知绑定", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season, Shape.Episode })
            {
                using var f = new Fixture(assembly, root, shape);
                Await(f.Load());
                var target = f.Get("PlayTarget");
                var shelf = f.Get("EpisodeShelf");
                var notices = new List<string>();
                ((INotifyPropertyChanged)f.Vm).PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
                f.Ui.EnableImmersive = false;
                f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
                Assert.True((bool)f.Get("StoreMode")!);
                Assert.Equal("Collapsed", f.Get("HeroCardVisibility")!.ToString());
                Assert.Equal(180d, (double)f.Get("StillWidth")!);
                Assert.Equal(270d, (double)f.Get("StillHeight")!);
                Assert.False((bool)f.Get("BackdropShown")!);
                Assert.Equal("Collapsed", f.Get("StoreBackdropVisibility")!.ToString(), "无图时不得留下背景层");
                Assert.Equal("Collapsed", f.Get("StoreCornerArtVisibility")!.ToString(), "没选艺术图／没有艺术图时那一角收着");
                Assert.Equal("Collapsed", f.Get("CornerVisibility")!.ToString());
                Assert.Equal(0d, (double)f.Get("TailMinHeight")!);
                Assert.Equal(0d, (double)f.Get("PaperMinHeight")!);
                foreach (var name in new[] { "StoreMode", "StoreBackdropVisibility", "StoreCornerArtVisibility", "StoreBackdropHeight", "StoreTextWidth", "StoreHeroWidth", "CornerVisibility", "HeroContent" })
                    Assert.True(notices.Contains(name), "模式切换缺少通知：" + name);
                foreach (var width in new[] { 360d, 760d, 1023d, 1024d, 1025d, 1072d, 1200d, 2400d, 760d })
                {
                    notices.Clear();
                    f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, width);
                    var height = (double)f.Get("StoreBackdropHeight")!;
                    Assert.Equal(DetailHero.StoreBackdropHeight(width, (double)f.Get("HeroHeight")!), height);
                    Assert.Equal(DetailHero.StoreBackdropWidth(width), (double)f.Get("StoreBackdropWidth")!);
                    Assert.True((double)f.Get("StillWidth")! > 0 && (double)f.Get("StillHeight")! > 0,
                        "商店窄窗仍把封面尺寸归零：" + shape);
                    Assert.True(notices.Contains("StoreBackdropHeight"));
                    var previewWidth = Math.Min(640, width * 0.46);
                    var leftWidth = Math.Max(0, width - 2 * DetailHero.StorePageInset(width) - (width >= DetailHero.StoreWideFloor ? previewWidth + 28 : 0));
                    var stillWidth = (double)f.Get("StillWidth")!;
                    // 2026-11-16「位置不够时不要裁切，直接压在背景图上」：让位之后放不下内容时，
                    // 片名栏撑到 StoreTextFloor，头图那一行跟着长；超出部分压在右侧剧照上。
                    Assert.Equal(DetailHero.StoreTextColumn(width, leftWidth, stillWidth, 22), (double)f.Get("StoreTextWidth")!);
                    Assert.Equal(DetailHero.StoreHeroColumn(width, leftWidth, stillWidth, 22), (double)f.Get("StoreHeroWidth")!);
                    // 2026-11-17 用户令「高度要和左边的封面一样高，右边边缘不能超过下面的卡片」：艺术图
                    // 高跟封面一致、宽按图自己的比例（夹具没有图，按 16:9 兜底），上沿与封面同线、右沿贴内容右沿。
                    Assert.Equal(stillWidth > 0 ? (double)f.Get("StillHeight")! : 0d, (double)f.Get("StoreArtHeight")!);
                    Assert.Equal(480d, (double)f.Get("StoreArtWidth")!);
                    var storeArtMargin = f.Get("StoreArtMargin")!;
                    var marginType = storeArtMargin.GetType();
                    Assert.Equal(DetailHero.StoreHeroInsetTop(width),
                        (double)marginType.GetProperty("Top")!.GetValue(storeArtMargin)!);
                    Assert.Equal(DetailHero.StorePageInset(width),
                        (double)marginType.GetProperty("Right")!.GetValue(storeArtMargin)!);
                    // 2026-11-17 用户令「面积不够大，要到卡片上方」：纯色底高＝头图 + 尾部那道 2 + 纸面 14 上留白。
                    var tailInset = f.Get("TailInset")!;
                    var sheetPadding = f.Get("SheetPadding")!;
                    Assert.Equal((double)f.Get("HeroHeight")!
                            + (double)tailInset.GetType().GetProperty("Top")!.GetValue(tailInset)!
                            + (double)sheetPadding.GetType().GetProperty("Top")!.GetValue(sheetPadding)!,
                        (double)f.Get("StoreArtWashHeight")!);
                    Assert.True(notices.Contains("StoreContentWidth"));
                    Assert.True(notices.Contains("StoreHeroWidth"));
                }
                notices.Clear();
                f.Vm.GetType().GetProperty("StoreDetailsRoom", Members)!.SetValue(f.Vm, 700d);
                Assert.True(notices.Contains("StoreBackdropHeight"), "详情晚到变高没有通知亚克力区域");
                Assert.True(notices.Contains("StoreArtWashHeight"), "详情晚到变高没有通知纯色底");
                Assert.Equal((double)f.Get("HeroHeight")!, (double)f.Get("StoreBackdropHeight")!);
                // 2026-11-17 用户令「商店模式新增去掉背景图、渐变，右上角改为显示艺术图的功能」：设置那一格
                // 翻了之后，两格显隐都要重算并通知 —— 这一份夹具没有图，两格都收着。
                notices.Clear();
                f.Ui.StoreCornerArt = true;
                f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
                Assert.Equal("Collapsed", f.Get("StoreBackdropVisibility")!.ToString());
                Assert.Equal("Collapsed", f.Get("StoreCornerArtVisibility")!.ToString());
                Assert.True(notices.Contains("StoreBackdropVisibility"), "选艺术图没有通知背景层重算");
                Assert.True(notices.Contains("StoreCornerArtVisibility"), "选艺术图没有通知艺术图那一角重算");
                f.Ui.StoreCornerArt = false;
                f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
                f.Ui.EnableImmersive = true;
                f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
                Assert.False((bool)f.Get("StoreMode")!);
                Assert.True(ReferenceEquals(target, f.Get("PlayTarget")));
                Assert.True(ReferenceEquals(shelf, f.Get("EpisodeShelf")));
            }
        });

        Test("商店封面：按右栏徽标到播放键下沿实测放大，离开商店恢复 260×375 盒", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            Await(f.Load());
            f.Ui.EnableImmersive = false;
            f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
            f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 1240d);

            // 还没量到：仍是原来的 180×270 兜底盒。
            Assert.Equal(180d, (double)f.Get("StillWidth")!);
            Assert.Equal(270d, (double)f.Get("StillHeight")!);

            // 视图量到「徽标上沿到主播放键下沿」307：封面跟着长，没图时按 180:270 同形。
            var room = f.Vm.GetType().GetProperty("StoreCoverRoom", Members)!;
            room.SetValue(f.Vm, 307d);
            Assert.Equal(307d, (double)f.Get("StillHeight")!);
            Assert.Equal(205d, (double)f.Get("StillWidth")!);

            // 不足兜底的高不缩，仍是 180×270。
            room.SetValue(f.Vm, 200d);
            Assert.Equal(270d, (double)f.Get("StillHeight")!);
            Assert.Equal(180d, (double)f.Get("StillWidth")!);

            // 离开商店模式：回到其余详情页的 260×375 上限盒（夹具没有图）。
            f.Ui.EnableImmersive = true;
            f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
            Assert.Equal(260d, (double)f.Get("StillWidth")!);
            Assert.Equal(375d, (double)f.Get("StillHeight")!);
        });
        Test("商店间距：带高回到内容本身，首卡离头部末沿只留 StorePreviewGap", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Episode);
            Await(f.Load());
            f.Ui.EnableImmersive = false;
            f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
            f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 1240d);
            f.Vm.GetType().GetProperty("StackRoom")!.SetValue(f.Vm, 200d);
            f.Vm.GetType().GetProperty("StoreDetailsRoom", Members)!.SetValue(f.Vm, 160d);
            // 没有封面图时商店档按 180×270 的兜底尺寸摆着；带高取两栏里高的那个，不多留「封面下常驻行」的 52。
            var still = (double)f.Get("StillHeight")!;
            Assert.Equal(270d, still);
            Assert.Equal(Math.Max(200d, still) + 160d, (double)f.Get("HeroContent")!);
            var padding = f.Get("SheetPadding")!;
            var top = (double)padding.GetType().GetProperty("Top")!.GetValue(padding)!;
            var left = (double)padding.GetType().GetProperty("Left")!.GetValue(padding)!;
            // 尾部的 TailInset 上留白还有 2，两者合起来是 StorePreviewGap 那 16。
            Assert.Equal(DetailHero.StorePreviewGap - 2, top);
            Assert.Equal(DetailHero.StorePageInset(1240d) - 10, left);
        });
        Test("商店剧情与剩余时间：固定两行提示，保留沉浸展开偏好与续播值", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            Await(f.Load());
            f.Vm.GetType().GetProperty("Expanded")!.SetValue(f.Vm, true);
            f.Vm.GetType().GetProperty("OverviewTrimmed")!.SetValue(f.Vm, true);
            f.Ui.EnableImmersive = false;
            f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
            Assert.Equal(2, (int)f.Get("OverviewMaxLines")!);
            Assert.Equal("CharacterEllipsis", f.Get("OverviewTrimming")!.ToString());
            Assert.Equal("Collapsed", f.Get("ReadMoreVisibility")!.ToString());
            Assert.Equal("Collapsed", f.Get("RemainingTimeVisibility")!.ToString());
            foreach (var width in new[] { 1240d, 760d, 360d, 1240d })
            {
                f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, width);
                Assert.Equal(2, (int)f.Get("OverviewMaxLines")!);
                Assert.Equal("Collapsed", f.Get("ReadMoreVisibility")!.ToString());
            }
            f.Ui.EnableImmersive = true;
            f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
            Assert.Equal(0, (int)f.Get("OverviewMaxLines")!);
            Assert.Equal("None", f.Get("OverviewTrimming")!.ToString());
            Assert.Equal("Visible", f.Get("ReadMoreVisibility")!.ToString());
            Assert.Equal(f.Get("ResumeVisibility")!.ToString(), f.Get("RemainingTimeVisibility")!.ToString());
            ((System.Windows.Input.ICommand)f.Get("ToggleOverviewCommand")!).Execute(null);
            Assert.Equal(4, (int)f.Get("OverviewMaxLines")!);
            f.Vm.GetType().GetProperty("Overview")!.SetValue(f.Vm, "");
            Assert.Equal("Collapsed", f.Get("ReadMoreVisibility")!.ToString(), "空剧情不受旧裁切状态影响");
        });

        Test("P4 详情布局：请求在途跨真实断点，完整主体到达保留最新宽高和集合", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season, Shape.Episode })
            {
                using var f = new Fixture(assembly, root, shape);
                f.Ui.EnableImmersive = true;
                f.Transport.HoldDetail = true;
                var load = f.Load();
                f.Transport.WaitForDetail();
                var shelf = f.Get("EpisodeShelf");
                var notices = new List<string>();
                ((INotifyPropertyChanged)f.Vm).PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
                foreach (var width in new[] { 600d, 760d, 819d, 820d, 821d, 1100d, 1280d, 819d, 821d })
                {
                    notices.Clear();
                    f.Vm.GetType().GetProperty("Viewport")!.SetValue(f.Vm, 560d);
                    f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, width);
                    Assert.Equal(width < DetailHero.CompactFloor, (bool)f.Get("IsCompact")!);
                    Assert.Equal(width, (double)f.Get("HeroLayoutWidth")!);
                    foreach (var name in new[] { "IsCompact", "HeroLayoutWidth", "CompactVisibility", "ColumnActionsVisibility", "PickersVisibility" })
                        Assert.True(notices.Contains(name), "在途宽窄切换未通知：" + name);
                }
                f.Transport.DetailRelease.TrySetResult(true);
                Await(load);
                Assert.Equal(821d, (double)f.Get("PageWidth")!);
                Assert.Equal(560d, (double)f.Get("Viewport")!);
                Assert.False((bool)f.Get("IsCompact")!);
                Assert.True(ReferenceEquals(shelf, f.Get("EpisodeShelf")), "布局变化重建集带");
            }
        });

        Test("详情两种模式：开启宽窗固定沉浸，窄窗仍紧凑，商店保留卡面布局", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season })
            {
                using var f = new Fixture(assembly, root, shape);
                foreach (var item in f.Transport.Catalog.Values) item.BackdropImageTags.Add("fixture");
                Await(f.Load());
                void Set(string name, object value) => f.Vm.GetType().GetProperty(name)!.SetValue(f.Vm, value);
                void Refresh() => f.Vm.GetType().GetMethod("RefreshAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Vm, null);
                f.Ui.EnableImmersive = true;
                Set("PageWidth", 1200d);
                foreach (var height in new[] { 620d, 1120d, 2000d })
                {
                    Set("Viewport", height);
                    Refresh();
                    Assert.True((bool)f.Get("Showcase")!, "宽窗不再存在自动普通模式");
                }
                Set("PageWidth", 760d);
                Assert.True((bool)f.Get("IsCompact")!);
                Assert.False((bool)f.Get("Showcase")!);
                Assert.True((bool)f.Get("BackdropShown")!, "沉浸启用时保留窄窗背景");
                f.Ui.EnableImmersive = false;
                Refresh();
                Assert.False((bool)f.Get("BackdropShown")!);
                Set("PageWidth", 1200d);
                Assert.True((bool)f.Get("HeroCardShown")!);
                Assert.False((bool)f.Get("BackdropShown")!);
                Assert.False((bool)f.Get("Showcase")!);
            }
        });

        Test("沉浸总开关：电影剧季同页关闭成卡面，宽窄保留卡面，恢复原偏好", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season })
            {
                using var f = new Fixture(assembly, root, shape);
                foreach (var item in f.Transport.Catalog.Values) item.BackdropImageTags.Add("fixture");
                Await(f.Load());
                void Set(string name, object value) => f.Vm.GetType().GetProperty(name)!.SetValue(f.Vm, value);
                void Refresh() => f.Vm.GetType().GetMethod("RefreshAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Vm, null);
                var enabled = f.Ui.GetType().GetProperty("EnableImmersive");
                Assert.NotNull(enabled, "缺少沉浸模式总开关");
                Set("PageWidth", 1200d);
                Set("Viewport", 2000d);
                Refresh();
                var target = f.Get("PlayTarget");
                var notices = new List<string>();
                ((INotifyPropertyChanged)f.Vm).PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
                enabled!.SetValue(f.Ui, false);
                Refresh();
                Assert.False((bool)f.Get("BackdropShown")!);
                Assert.False((bool)f.Get("Showcase")!);
                Assert.True((bool)f.Get("HeroCardShown")!);
                Assert.Equal("Top", f.Get("HeroContentAlignment")!.ToString());
                Assert.Equal(0d, (double)f.Get("TailMinHeight")!);
                foreach (var name in new[] { "HeroCardShown", "HeroCardVisibility", "HeroInset", "TailInset", "TailMargin", "HeroLayoutHeight", "SheetPadding", "ColumnVideoVisibility", "ColumnPickersVisibility" })
                    Assert.True(notices.Contains(name), "同页切换没有通知：" + name);
                Assert.True(ReferenceEquals(target, f.Get("PlayTarget")), "外观切换改变播放目标");
                Set("PageWidth", 760d);
                Assert.False((bool)f.Get("BackdropShown")!);
                Assert.True((bool)f.Get("HeroCardShown")!, "商店模式窄窗继续使用卡面");
                Assert.False((bool)f.Get("IsCompact")!);
                Set("PageWidth", 1200d);
                Assert.True((bool)f.Get("HeroCardShown")!);
                enabled.SetValue(f.Ui, true);
                Refresh();
                Assert.True((bool)f.Get("BackdropShown")!);
                Assert.True((bool)f.Get("Showcase")!);
                Assert.False((bool)f.Get("HeroCardShown")!);
            }
        });

        Test("详情封面：四类页面统一集页尺寸并保留原图比例", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season, Shape.Episode })
            {
                using var f = new Fixture(assembly, root, shape);
                Await(f.Load());
                f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 1200d);
                f.Vm.GetType().GetField("_stillPixels", Members)!.SetValue(f.Vm, (400, 600));
                f.Vm.GetType().GetMethod("RefitArtwork", Members)!.Invoke(f.Vm, null);
                Assert.Equal(250d, (double)f.Get("StillWidth")!);
                Assert.Equal(375d, (double)f.Get("StillHeight")!);
            }
        });

        Test("商店模式：跨窄窗断点保留封面、双栏操作并同页恢复紧凑模式", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season })
            {
                using var f = new Fixture(assembly, root, shape);
                Await(f.Load());
                f.Vm.GetType().GetField("_stillPixels", Members)!.SetValue(f.Vm, (400, 600));
                void Refresh() => f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
                var notices = new List<string>();
                ((INotifyPropertyChanged)f.Vm).PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
                foreach (var width in new[] { 600d, 760d, 819d, 820d, 1200d })
                {
                    f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, width);
                    f.Ui.EnableImmersive = false;
                    notices.Clear();
                    Refresh();
                    Assert.False((bool)f.Get("IsCompact")!);
                    Assert.True((bool)f.Get("HeroCardShown")!);
                    Assert.Equal(180d, (double)f.Get("StillWidth")!);
                    Assert.Equal(270d, (double)f.Get("StillHeight")!);
                    Assert.Equal("Collapsed", f.Get("CompactVisibility")!.ToString());
                    Assert.Equal("Visible", f.Get("ColumnActionsVisibility")!.ToString());
                    foreach (var name in new[] { "IsCompact", "StillVisibility", "CompactVisibility", "CornerVisibility", "ColumnActionsVisibility", "WideProgressVisibility", "PickersVisibility" })
                        Assert.True(notices.Contains(name), "模式切换缺少通知：" + name);
                    f.Ui.EnableImmersive = true;
                    Refresh();
                    Assert.Equal(width < DetailHero.CompactFloor, (bool)f.Get("IsCompact")!);
                    Assert.Equal(width < DetailHero.CompactFloor ? 0d : 375d, (double)f.Get("StillHeight")!);
                }
            }
        });

        Test("商店模式：主体更正为单集时重新通知紧凑布局并保留封面", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            f.Ui.EnableImmersive = false;
            f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 760d);
            Await(f.Load());
            f.Vm.GetType().GetField("_stillPixels", Members)!.SetValue(f.Vm, (400, 600));
            f.Vm.GetType().GetMethod("RefitArtwork", Members)!.Invoke(f.Vm, null);
            var notices = new List<string>();
            ((INotifyPropertyChanged)f.Vm).PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
            f.Seed.Type = EmbyItemType.Episode;
            Await(f.Load());
            Assert.True((bool)f.Get("IsCompact")!);
            Assert.Equal(270d, (double)f.Get("StillHeight")!);
            foreach (var name in new[] { "IsCompact", "CompactVisibility", "StillVisibility" })
                Assert.True(notices.Contains(name), "类型更正缺少布局通知：" + name);
        });

        Test("单集沉浸采用剧页布局，开关与宽窄恢复保留本集状态", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Episode);
            foreach (var item in f.Transport.Catalog.Values) item.BackdropImageTags.Add("fixture");
            Await(f.Load());
            void Set(string name, object value) => f.Vm.GetType().GetProperty(name)!.SetValue(f.Vm, value);
            void Refresh() => f.Vm.GetType().GetMethod("RefreshAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Vm, null);
            Set("PageWidth", 1200d);
            Set("Viewport", 900d);
            f.Ui.EnableImmersive = true;
            Refresh();
            var target = f.Get("PlayTarget");
            var subline = f.Get("Subline");
            var shelf = f.Get("EpisodeShelf");
            Assert.True((bool)f.Get("Showcase")!);
            Assert.True((bool)f.Get("BackdropShown")!);
            Assert.False((bool)f.Get("HeroCardShown")!);
            Assert.False((bool)f.Get("ShowShelfLines")!);
            Assert.Equal(260d, (double)f.Get("StillWidth")!);
            Assert.Equal(375d, (double)f.Get("StillHeight")!);
            Assert.Equal("Top", f.Get("CoverAlignment")!.ToString());
            // 单集沉浸沿用剧页布局，片名栏顶对齐，操作通过星号行落在封面下沿。
            Assert.Equal("Top", f.Get("HeroContentAlignment")!.ToString());
            // 这一份夹具一张图都没有，所以角上那一格收着 —— 收的原因是「没有艺术图」，不是「沉浸档不摆它」
            // （2026-11-08 起两种模式都摆，判据在 DetailHero.ShowsCorner，页面上由 ShellNavigationProbe 量）。
            Assert.Equal("Collapsed", f.Get("CornerVisibility")!.ToString());
            var notices = new List<string>();
            ((INotifyPropertyChanged)f.Vm).PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
            f.Ui.EnableImmersive = false;
            Refresh();
            Assert.True((bool)f.Get("HeroCardShown")!);
            Assert.False((bool)f.Get("BackdropShown")!);
            Assert.False((bool)f.Get("Showcase")!);
            Assert.Equal(180d, (double)f.Get("StillWidth")!);
            Assert.Equal(270d, (double)f.Get("StillHeight")!);
            Assert.Equal("Top", f.Get("CoverAlignment")!.ToString());
            foreach (var name in new[] { "CornerVisibility", "CoverAlignment", "HeroContentAlignment", "HeroCardShown" })
                Assert.True(notices.Contains(name), "模式变化缺少通知：" + name);
            Set("PageWidth", 760d);
            Assert.False((bool)f.Get("HeroCardShown")!);
            Assert.False((bool)f.Get("BackdropShown")!, "商店模式使用独立静态背景与亚克力");
            Assert.Equal(180d, (double)f.Get("StillWidth")!);
            f.Ui.EnableImmersive = true;
            Refresh();
            Assert.False((bool)f.Get("Showcase")!);
            foreach (var width in new[] { 820d, 819d, 1200d }) Set("PageWidth", width);
            Assert.True((bool)f.Get("Showcase")!);
            Assert.Equal(375d, (double)f.Get("StillHeight")!);
            Assert.True(ReferenceEquals(target, f.Get("PlayTarget")), "模式切换不得换播放目标");
            Assert.Equal(subline, f.Get("Subline"));
            Assert.True(ReferenceEquals(shelf, f.Get("EpisodeShelf")), "模式切换不得重建集列表");
        });

        Test("无图单集开启沉浸仍保留卡面，背景到达和收起即时更新", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Episode);
            Await(f.Load());
            f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 1200d);
            f.Ui.EnableImmersive = true;
            f.Vm.GetType().GetMethod("RefreshAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Vm, null);
            Assert.False((bool)f.Get("Showcase")!);
            Assert.True((bool)f.Get("HeroCardShown")!);
            var notices = new List<string>();
            ((INotifyPropertyChanged)f.Vm).PropertyChanged += (_, args) => notices.Add(args.PropertyName ?? "");
            foreach (var artwork in new[] { true, false })
            {
                notices.Clear();
                f.Vm.GetType().GetProperty("HeroArt")!.SetValue(f.Vm, artwork);
                Assert.Equal(artwork, (bool)f.Get("Showcase")!);
                Assert.Equal(!artwork, (bool)f.Get("HeroCardShown")!);
                Assert.Equal(375d, (double)f.Get("StillHeight")!);
                foreach (var name in new[] { "ColumnVideoVisibility", "VideoLineVisibility", "ColumnPickersVisibility", "CardTailGapHeight", "HeroInset", "TailInset", "OverviewPanelPadding" })
                    Assert.True(notices.Contains(name), "背景变化缺少布局通知：" + name);
            }
        });

        foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season })
            Test($"详情 {shape} 商店模式关闭背景并使用卡面，切回恢复沉浸", () =>
            {
                using var f = new Fixture(assembly, root, shape);
                Await(f.Load());
                f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 1200d);
                f.Vm.GetType().GetProperty("HeroArt")!.SetValue(f.Vm, true);
                f.Ui.EnableImmersive = false;
                f.Vm.GetType().GetMethod("RefreshAppearance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Vm, null);
                Assert.False((bool)f.Get("BackdropShown")!);
                Assert.False((bool)f.Get("Showcase")!);
                Assert.True((bool)f.Get("HeroCardShown")!);
                f.Ui.EnableImmersive = true;
                f.Vm.GetType().GetProperty("HeroArt")!.SetValue(f.Vm, true);
                Assert.True((bool)f.Get("Showcase")!, "开启后普通宽窗也固定沉浸");
            });
        Test("简洁详情顶部留白：四种页面下移，窄档与左右留白同值，切回沉浸保留原边距", () =>
        {
            foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season, Shape.Episode })
            {
                using var f = new Fixture(assembly, root, shape);
                foreach (var item in f.Transport.Catalog.Values) item.BackdropImageTags.Add("fixture");
                Await(f.Load());
                double Top()
                {
                    var inset = f.Get("HeroInset")!;
                    return (double)inset.GetType().GetProperty("Top")!.GetValue(inset)!;
                }
                void Refresh() => f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
                // 2026-11-17 用户令「窄窗口出现亚克力背景时候左边跟上面的间距要一致」：宽版式仍是
                // StoreHeroTop，窄档（亚克力那一档）等于 StorePageInset。
                foreach (var (width, immersiveTop) in new[] { (1200d, 30d), (360d, 20d) })
                {
                    f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, width);
                    Assert.Equal(immersiveTop, Top());
                    f.Ui.EnableImmersive = false;
                    Refresh();
                    Assert.Equal(DetailHero.StoreHeroInsetTop(width), Top(),
                        shape + " 简洁详情顶部留白不对");
                    if (width < DetailHero.StoreWideFloor)
                        Assert.Equal(DetailHero.StorePageInset(width), Top(),
                            shape + " 窄档顶部留白没有和左右留白同值");
                    else
                        Assert.Equal(DetailHero.StoreHeroTop, Top(), shape + " 宽版式顶部留白被窄档改动");
                    f.Ui.EnableImmersive = true;
                    Refresh();
                    Assert.Equal(immersiveTop, Top(), shape + " 沉浸顶部边距被简洁布局改动");
                }
            }
        });
        Test("单集唯一音轨固定显示并保留视频读数", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Episode);
            var source = f.Transport.Catalog["episode"].MediaSources[0];
            source.MediaStreams = [new() { Type = "Audio", Index = 1, Codec = "aac" }];
            source.Container = "mkv";
            Await(f.Load());
            f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 1200d);
            Assert.Equal("Visible", f.Get("AudioVisibility")!.ToString());
            Assert.Equal("Visible", f.Get("ColumnPickersVisibility")!.ToString());
            Assert.Equal("Visible", f.Get("ColumnVideoVisibility")!.ToString());
        });



        foreach (var shape in new[] { Shape.Movie, Shape.Series, Shape.Season, Shape.Episode })
            Test($"详情 {shape} 零条、唯一与多条音轨固定显示，宽窄文件栏跟播放目标", () =>
            {
                using var f = new Fixture(assembly, root, shape);
                Await(f.Load());
                var source = f.Transport.Catalog[shape == Shape.Movie ? "movie" : "episode"].MediaSources[0];
                foreach (var count in new[] { 0, 1, 2, 0 })
                {
                    source.MediaStreams = Enumerable.Range(1, count).Select(index =>
                        new MediaStream { Type = "Audio", Index = index, Codec = "aac" }).ToList();
                    Await(f.Load());
                    Assert.NotNull(f.Get("PlayTarget"));
                    Assert.Equal(count + 1, ((System.Collections.IList)f.Get("AudioTracks")!).Count);
                    Assert.Equal("Visible", f.Get("AudioVisibility")!.ToString());
                    f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 1200d);
                    Assert.Equal("Visible", f.Get("ColumnPickersVisibility")!.ToString());
                    Assert.Equal("Collapsed", f.Get("PickersVisibility")!.ToString());
                    f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, 760d);
                    Assert.Equal("Collapsed", f.Get("ColumnPickersVisibility")!.ToString());
                    Assert.Equal("Visible", f.Get("PickersVisibility")!.ToString());
                    f.Ui.EnableImmersive = false;
                    f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
                    var compact = (bool)f.Get("IsCompact")!;
                    Assert.Equal(compact ? "Collapsed" : "Visible", f.Get("ColumnPickersVisibility")!.ToString());
                    Assert.Equal(compact ? "Visible" : "Collapsed", f.Get("PickersVisibility")!.ToString());
                    Assert.Equal("Visible", f.Get("AudioVisibility")!.ToString());
                    f.Ui.EnableImmersive = true;
                    f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
                }
                f.Vm.GetType().GetProperty("PlayTarget")!.SetValue(f.Vm, null);
                f.Vm.GetType().GetProperty("VideoLine")!.SetValue(f.Vm, "视频：1080p");
                foreach (var width in new[] { 1200d, 760d })
                {
                    f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, width);
                    Assert.Equal("Collapsed", f.Get("ColumnPickersVisibility")!.ToString());
                    Assert.Equal("Collapsed", f.Get("PickersVisibility")!.ToString(), "无播放目标时文件栏仍有入口");
                    f.Ui.EnableImmersive = false;
                    f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
                    Assert.Equal("Collapsed", f.Get("ColumnPickersVisibility")!.ToString(), "简洁模式残留视频读数不能复活音频入口");
                    f.Ui.EnableImmersive = true;
                    f.Vm.GetType().GetMethod("RefreshAppearance", Members)!.Invoke(f.Vm, null);
                }
            });

        Test("人物详情不因固定音频栏显示文件选择入口", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            f.Seed.Type = EmbyItemType.Person;
            f.Seed.MediaSources.Clear();
            Await(f.Load());
            Assert.Null(f.Get("PlayTarget"));
            foreach (var width in new[] { 1200d, 760d })
            {
                f.Vm.GetType().GetProperty("PageWidth")!.SetValue(f.Vm, width);
                Assert.Equal("Collapsed", f.Get("ColumnPickersVisibility")!.ToString());
                Assert.Equal("Collapsed", f.Get("PickersVisibility")!.ToString());
            }
        });

        foreach (var shape in new[] { Shape.Movie, Shape.Episode, Shape.Series, Shape.Season })
            Test($"P1 {shape}预览与完整主体同类型只通知一次版式", () =>
            {
                using var f = new Fixture(assembly, root, shape);
                f.Transport.HoldDetail = true;
                var notices = Watch(f, "ContinueVisibility");
                var load = f.Load();
                f.Transport.WaitForDetail();
                Assert.Equal(1, notices.Count, "预览必须先确定版式");
                f.Transport.DetailRelease.TrySetResult(true);
                Await(load);
                Assert.Equal(1, notices.Count, "同类型完整主体不应重复宣布版式");
                Assert.True(f.Get("IsReady") is true);
                Assert.True(f.Can("PlayCommand"));
            });

        Test("P1 同类型刷新保留能力通知与内容更新", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            Await(f.Load());
            var shape = Watch(f, "ContinueVisibility");
            var affordance = Watch(f, "CanOpenGenre");
            f.Transport.Catalog["movie"].Name = "updated anonymous movie";
            f.Transport.Catalog["movie"].MediaSources.Clear();
            Await(f.Load());
            Assert.Equal(0, shape.Count, "同类型刷新不重新宣布版式");
            Assert.True(affordance.Count > 0, "能力仍需重新通知");
            Assert.Equal("updated anonymous movie", (string)f.Get("Title")!);
            Assert.Equal(0, ((System.Collections.ICollection)f.Get("Sources")!).Count);
        });

        Test("P1 存根首次主体仍通知版式", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie, stub: true);
            f.Transport.HoldDetail = true;
            var notices = Watch(f, "ContinueVisibility");
            var load = f.Load();
            f.Transport.WaitForDetail();
            Assert.Equal(0, notices.Count, "存根不能冒充预览");
            f.Transport.DetailRelease.TrySetResult(true);
            Await(load);
            Assert.Equal(1, notices.Count);
            Assert.Equal("Collapsed", f.Get("ContinueVisibility")!.ToString());
        });

        Test("P1 同一预览对象被更正类型仍通知新布局", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            f.Transport.HoldDetail = true;
            var notices = Watch(f, "ContinueVisibility");
            var load = f.Load();
            f.Transport.WaitForDetail();
            Assert.Equal(1, notices.Count);
            // Preview 持有的正是这个可变 DTO，比较 Apply 前后的对象属性会漏掉此更正。
            f.Seed.Type = EmbyItemType.Episode;
            f.Transport.DetailRelease.TrySetResult(true);
            Await(load);
            Assert.Equal(2, notices.Count);
            Assert.Equal("Visible", f.Get("ContinueVisibility")!.ToString());
        });

        Test("P1 同类型重新Attach仍重建本访问版式", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            Await(f.Load());
            f.Reattach(f.Seed);
            var notices = Watch(f, "ContinueVisibility");
            Await(f.Load());
            Assert.Equal(1, notices.Count, "旧访问的通知不能代表新访问已就绪");
            Assert.True(f.Can("PlayCommand"));
        });

        Test("P1 版式通知中断后同类型重试必须补齐", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            PropertyChangedEventHandler fail = (_, args) =>
            {
                if (args.PropertyName == "HeroHeight") throw new InvalidOperationException("anonymous notification failure");
            };
            var observable = (INotifyPropertyChanged)f.Vm;
            observable.PropertyChanged += fail;
            Await(f.Load());
            observable.PropertyChanged -= fail;
            Assert.True(f.Get("NoticeOpen") is true);
            var notices = Watch(f, "ContinueVisibility");
            Await(f.Load());
            Assert.Equal(1, notices.Count, "失败的半批通知不能标记为已宣布");
            Assert.True(f.Can("PlayCommand"));
        });

        Test("P1 空类型主体不会命中未初始化的版式记录", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie, stub: true);
            f.Seed.Type = null!;
            var notices = Watch(f, "ContinueVisibility");
            Await(f.Load());
            Assert.Equal(1, notices.Count);
        });

        Test("P1 更正类型通知中断后恢复旧类型也要补齐版式", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Movie);
            Await(f.Load());
            f.Seed.Type = EmbyItemType.Episode;
            PropertyChangedEventHandler fail = (_, args) =>
            {
                if (args.PropertyName == "HeroHeight") throw new InvalidOperationException("anonymous type correction failure");
            };
            var observable = (INotifyPropertyChanged)f.Vm;
            observable.PropertyChanged += fail;
            Await(f.Load());
            observable.PropertyChanged -= fail;
            Assert.True(f.Get("NoticeOpen") is true);
            f.Seed.Type = EmbyItemType.Movie;
            var notices = Watch(f, "ContinueVisibility");
            Await(f.Load());
            Assert.Equal(1, notices.Count, "中途失败后即使恢复旧类型，也不能复用上一批记录");
            Assert.True(f.Can("PlayCommand"));
        });

        Test("P1 同类型剧集刷新仍通知清空与重填集带", () =>
        {
            using var f = new Fixture(assembly, root, Shape.Series);
            Await(f.Load());
            var shape = Watch(f, "ContinueVisibility");
            var episodes = Watch(f, "EpisodeStripVisibility");
            f.Transport.Episodes.Clear();
            Await(f.Load());
            Assert.Equal(0, shape.Count);
            Assert.True(episodes.Count > 0);
            Assert.Equal("Collapsed", f.Get("EpisodeStripVisibility")!.ToString());
            Assert.False(f.Can("PlayCommand"));
        });
    }

    private static List<string?> Watch(Fixture fixture, string property)
    {
        var notices = new List<string?>();
        ((INotifyPropertyChanged)fixture.Vm).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == property) notices.Add(args.PropertyName);
        };
        return notices;
    }
}
