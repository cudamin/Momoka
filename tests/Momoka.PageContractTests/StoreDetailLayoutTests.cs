using System.Text.RegularExpressions;
using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 商店详情版式与「从头开始」入口的源码契约（2026-11-14 用户令：参考微软商店条目页重排详情页，并把
/// 从头开始折进「更多」）。实际父子关系、尺寸和命中区由 Shell 原生探针核对；这里钉的是 XAML 结构和
/// 两条接线，免得改版时入口悄悄消失。
/// </summary>
internal static class StoreDetailLayoutTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        Test("商店预览圆角：从应用资源读取全局键，页面样式仍取局部字典", () =>
        {
            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            var styles = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Theme", "Styles.xaml"));
            Assert.False(page.Descendants().Any(element => (string?)element.Attribute(X + "Key") == "EgCardCornerRadius"),
                "全局圆角被复制进页面，掩盖了资源归属错误");
            Assert.Equal("16", styles.Descendants().Single(element =>
                (string?)element.Attribute(X + "Key") == "EgCardCornerRadius").Value);
            var store = Source("Views/DetailPage.Store.cs");
            Assert.Contains("? (CornerRadius)Application.Current.Resources[\"EgCardCornerRadius\"] : default;", store);
            foreach (var key in new[] { "EgRoundButtonStyle", "EgPillButtonStyle", "EgStoreCoverActionButtonStyle" })
            {
                Assert.True(page.Descendants().Any(element => (string?)element.Attribute(X + "Key") == key),
                    key + " 不在页面局部资源中");
                Assert.Contains("\"" + key + "\"", store);
            }
        });
        Test("商店渐变：窄窗材料与图片共用最终遮罩，离页清缓存，浅色前景同步", () =>
        {
            foreach (var name in new[] { "DetailPage", "MoviePilotSubscriptionPage" })
            {
                var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", name + ".xaml"));
                var acrylic = page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "StoreBackdropAcrylic");
                Assert.Equal("StoreBackdropContent", (string?)acrylic.Parent!.Attribute(X + "Name"));
                Assert.Null(acrylic.Attribute("Margin"));
                var source = Source("Views/" + name + ".xaml.cs");
                Assert.Contains("_storeAmbientBrush.Clear();", source);
                Assert.Contains("_storeTint.Clear();", source);
            }
            var reading = Source("Views/DetailPage.Appearance.cs");
            Assert.Contains("ViewModel.StoreMode && !ThemeHost.Current.IsDark", reading);
            Assert.Contains("? accent : storeInk", reading);
            var mp = Source("Views/MoviePilotSubscriptionPage.Showcase.cs");
            Assert.Contains("MediaViewModel.Plain && !ThemeHost.Current.IsDark", mp);
            Assert.Contains("MediaMeta.Foreground = new SolidColorBrush(heroInk);", mp);
            var trailer = Source("Views/DetailPage.StoreTrailer.cs");
            Assert.Contains("_storeAmbientBrush.Frame", trailer);
            Assert.Contains("_storeVideoBrush.Clear();", trailer);
            Assert.Contains("StoreTrailerFoundation.Background = null;", trailer);
            Assert.Contains("StoreTrailerFoundation.Visibility = Visibility.Collapsed;", trailer);
            Assert.Contains("_storeTrailerUserPaused || _storeTrailerScrollPaused", trailer);
            // 滚过阈值的那一帧只暂停一次：每来一个 ViewChanged 就往播放器/WebView2 塞一条暂停消息，
            // 在商店页连续滚动时会变成每帧一条。
            Assert.Contains("if (_storeTrailerScrollPaused) return;", trailer);
            Assert.Contains("nameof(DetailViewModel.StoreAmbientHeight)", Source("Views/DetailPage.Trailers.cs"));
            var detail = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            Assert.Equal("1", (string?)detail.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "StoreTrailerFade").Attribute("Canvas.ZIndex"));
            var content = detail.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "StoreScrollContent");
            var layers = content.Elements().Select(element => (string?)element.Attribute(X + "Name")).ToArray();
            Assert.True(Array.IndexOf(layers, "StoreTrailerFoundation") < Array.IndexOf(layers, "StoreAmbient")
                && Array.IndexOf(layers, "StoreAmbient") < Array.IndexOf(layers, "StoreTrailerHost"), "视频局部底层遮住了环境图或视频");
        });
        Test("商店详情：四颗键全在日期行下方，三颗图标在上、播放键在下且同宽，连播保留集卡右上", () =>
        {
            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            XElement Named(string name) => page.Descendants()
                .Single(element => (string?)element.Attribute(X + "Name") == name);

            // 整块操作键跟在日期行（StoreRatingRow）后面，排在片名那一栏里。
            var block = Named("StoreActionBlock");
            Assert.Equal("HeroStack", (string?)block.Parent!.Attribute(X + "Name"));
            Assert.Equal("StoreRatingRow", (string?)block.ElementsBeforeSelf().Last().Attribute(X + "Name"));
            Assert.Equal("{x:Bind ViewModel.StoreCoverActionsVisibility, Mode=OneWay}",
                (string?)block.Attribute("Visibility"));
            Assert.Equal("Left", (string?)block.Attribute("HorizontalAlignment"));
            var stack = Named("StoreActionStack");
            Assert.Equal("StoreActionBlock", (string?)stack.Parent!.Attribute(X + "Name"));
            Assert.Equal("Vertical", (string?)stack.Attribute("Orientation"));
            Assert.Equal("8", (string?)stack.Attribute("Spacing"));
            // 三颗图标一行在上，三列等分；宽度由播放键实测宽回写（初值 3×44）。
            var coverActions = Named("StoreCoverActions");
            Assert.Equal("StoreActionStack", (string?)coverActions.Parent!.Attribute(X + "Name"));
            Assert.Equal("132", (string?)coverActions.Attribute("Width"));
            var columns = coverActions.Elements()
                .Single(element => element.Name.LocalName == "Grid.ColumnDefinitions").Elements().ToArray();
            Assert.Equal(3, columns.Length);
            foreach (var column in columns)
                Assert.Equal("*", (string?)column.Attribute("Width"));
            // 主播放键在它们下面，占满整行宽度。
            var playSlot = Named("StorePlaySlot");
            Assert.Equal("StoreActionStack", (string?)playSlot.Parent!.Attribute(X + "Name"));
            Assert.Equal("OnStorePlaySlotSizeChanged", (string?)playSlot.Attribute("SizeChanged"));
            Assert.False(page.Descendants().Any(element => element.Name.LocalName == "TranslateTransform"
                && (string?)element.Parent!.Parent!.Attribute(X + "Name") == "StorePlaySlot"));
            Assert.False(page.Descendants().Any(element => (string?)element.Attribute(X + "Name") == "StoreCoverBadges"));
            var storeButton = page.Descendants().Single(element => (string?)element.Attribute(X + "Key") == "EgStoreCoverActionButtonStyle");
            Assert.Equal("{StaticResource DefaultButtonStyle}", (string?)storeButton.Attribute("BasedOn"));
            Assert.Equal("0", (string?)storeButton.Elements().Single(element => (string?)element.Attribute("Property") == "BorderThickness").Attribute("Value"));
            Assert.False(storeButton.Descendants().Any(element => element.Name.LocalName == "ControlTemplate"));
            Assert.Equal("ColumnActions", (string?)Named("ColumnActionRow").Parent!.Attribute(X + "Name"));
            // 主播放键回到带字的药丸样式：标签不再跟商店模式收起来。
            var play = Named("ColumnPlayButton");
            Assert.Equal("{StaticResource EgPlayButtonStyle}", (string?)play.Attribute("Style"));
            Assert.False(play.Descendants().Any(element => (string?)element.Attribute("Visibility")
                == "{x:Bind ViewModel.ColumnPlayLabelsVisibility, Mode=OneWay}"));
            // 已看那颗键的文字组在商店模式整组收起（用户确认「收成图标」）。
            Assert.Equal("{x:Bind ViewModel.ColumnWatchedLabelsVisibility, Mode=OneWay}",
                (string?)Named("ColumnWatchedLabels").Attribute("Visibility"));
            var store = Source("Views/DetailPage.Store.cs");
            Assert.Contains("MoveToPanel(ColumnPlayButton, StorePlaySlot);", store);
            Assert.Contains("MoveToPanel(ColumnMoreButton, StoreCoverActions);", store);
            Assert.Contains("MoveToPanel(ColumnFavoriteButton, StoreCoverActions);", store);
            Assert.Contains("MoveToPanel(ColumnWatchedButton, StoreCoverActions);", store);
            Assert.Contains("Grid.SetColumn(icons[index], index);", store);
            Assert.Contains("StoreCoverActions.Width = StorePlaySlot.ActualWidth;", store);
            Assert.Contains("PlaceStoreActionButtons(store);", store);
            Assert.Contains("button.Background = (Microsoft.UI.Xaml.Media.Brush)button.Resources[\"ButtonBackground\"];", store);
            Assert.Contains("\"EgPlayButtonStyle\"", store);
            foreach (var name in new[] { "ColumnPlayButton", "ColumnWatchedButton", "ColumnFavoriteButton", "ColumnMoreButton" })
                Assert.NotNull(Named(name).Attribute("AutomationProperties.Name"));
            Assert.Contains("EpisodeHead.Trailing = StoreContinueEntry;", store);
            Assert.Contains("RestoreToActions(ColumnPlayButton, 0);", store);
            Assert.Contains("RestoreToActions(StoreContinueEntry, ColumnActionRow.Children.Count);", store);
            // 连播在商店档是开关（2026-11-16 用户参考图）：左边带「连播」标签，
            // 轨道直接用主题强调色三态，不写死颜色值。
            var continueEntry = Named("StoreContinueEntry");
            Assert.Equal("StackPanel", continueEntry.Name.LocalName);
            Assert.Equal("Horizontal", (string?)continueEntry.Attribute("Orientation"));
            Assert.Equal("8", (string?)continueEntry.Attribute("Spacing"));
            Assert.Equal("{x:Bind ViewModel.StoreContinueVisibility, Mode=OneWay}",
                (string?)continueEntry.Attribute("Visibility"));
            var continueLabel = continueEntry.Elements().Single(element => element.Name.LocalName == "TextBlock");
            Assert.Equal("连播", (string?)continueLabel.Attribute("Text"));
            var continueSwitch = Named("StoreContinueSwitch");
            Assert.Equal("ToggleSwitch", continueSwitch.Name.LocalName);
            Assert.Equal("StoreContinueEntry", (string?)continueSwitch.Parent!.Attribute(X + "Name"));
            Assert.Equal("{x:Bind ViewModel.ContinuePlay, Mode=TwoWay}", (string?)continueSwitch.Attribute("IsOn"));
            Assert.Equal("开", (string?)continueSwitch.Attribute("OnContent"));
            Assert.Equal("关", (string?)continueSwitch.Attribute("OffContent"));
            var switchResources = continueSwitch.Elements().Single(element => element.Name.LocalName == "ToggleSwitch.Resources")
                .Elements().ToArray();
            Assert.Equal(3, switchResources.Length);
            foreach (var resource in switchResources)
            {
                var key = (string?)resource.Attribute(X + "Key");
                var resourceKey = (string?)resource.Attribute("ResourceKey");
                Assert.Equal("StaticResource", resource.Name.LocalName);
                Assert.NotNull(key);
                Assert.NotNull(resourceKey);
                Assert.True(key!.StartsWith("ToggleSwitchFillOn", StringComparison.Ordinal),
                    "连播开关的轨道别名不是三态前缀：" + key);
                Assert.True(resourceKey!.EndsWith("Brush", StringComparison.Ordinal),
                    "连播开关的轨道别名不是主题画刷：" + resourceKey);
            }
            Assert.False(continueSwitch.Descendants().Any(element =>
                element.Name.LocalName == "SolidColorBrush" || element.Attribute("Color") is not null),
                "连播开关写死了颜色，没有走主题强调色");
            // 沉浸档那颗药丸在商店模式让位给开关。
            Assert.Equal("{x:Bind ViewModel.ColumnContinueVisibility, Mode=OneWay}",
                (string?)Named("ColumnContinueButton").Attribute("Visibility"));
            Assert.Contains("StoreBackdropContent.CornerRadius = previewCorner;", store);
            Assert.Contains("StoreTrailerContent.CornerRadius = previewCorner;", store);
            // 加高下方渐变：同一列在 0.36 处已经离开满不透明。
            Assert.True(Momoka.Shell.Views.StoreBackdropBlend.Alpha(0.5, 0.36, ambient: false) < 0.999
                && Momoka.Shell.Views.StoreBackdropBlend.Alpha(0.5, 0.36, ambient: false) > 0.7,
                "右侧剧照的下方渐隐没有提前");
            Assert.True(Momoka.Shell.Views.StoreBackdropBlend.Alpha(0.5, 0.46, ambient: true) < 0.999,
                "环境底的下方渐隐没有提前");
            Assert.True(Momoka.Shell.Views.StoreBackdropBlend.Alpha(0.5, 0.95, ambient: false) > 0,
                "下方渐隐末端提前收成了全透明");
        });
        Test("商店渐变：左边与左上角的淡墨层夹在装饰背景与正文之间，随窗口拉伸", () =>
        {
            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            XElement Named(string name) => page.Descendants()
                .Single(element => (string?)element.Attribute(X + "Name") == name);

            var shade = Named("StoreShade");
            Assert.Equal("StoreScrollContent", (string?)shade.Parent!.Attribute(X + "Name"));
            Assert.Equal("{x:Bind ViewModel.StoreAmbientHeight, Mode=OneWay}", (string?)shade.Attribute("Height"));
            Assert.Equal("{x:Bind ViewModel.Viewport, Mode=OneWay}", (string?)shade.Attribute("MinHeight"));
            Assert.Equal("Collapsed", (string?)shade.Attribute("Visibility"));
            Assert.Equal("False", (string?)shade.Attribute("IsHitTestVisible"));
            // 顺序：静图与环境底之后、预告片与正文之前 —— 压图不压视频、控制键和文字。
            var before = shade.ElementsBeforeSelf().Select(element => (string?)element.Attribute(X + "Name")).ToArray();
            Assert.True(before.Contains("StoreBackdrop") && before.Contains("StoreAmbient"),
                "淡墨层没有压在装饰背景之上");
            var after = shade.ElementsAfterSelf().ToArray();
            Assert.Equal("StackPanel", after[^1].Name.LocalName);
            Assert.True(after.Any(element => (string?)element.Attribute(X + "Name") == "StoreTrailerHost")
                && after.Any(element => (string?)element.Attribute(X + "Name") == "StoreTrailerControls"),
                "淡墨层盖到了预告片或控制键上");
            // 两道线性渐变：左边一道横向、顶上那道竖向，末端都收成透明。
            var brushes = shade.Descendants().Where(element => element.Name.LocalName == "LinearGradientBrush").ToArray();
            Assert.Equal(2, brushes.Length);
            Assert.Equal("0,0.5", (string?)brushes[0].Attribute("StartPoint"));
            Assert.Equal("1,0.5", (string?)brushes[0].Attribute("EndPoint"));
            Assert.Equal("0.5,0", (string?)brushes[1].Attribute("StartPoint"));
            Assert.Equal("0.5,1", (string?)brushes[1].Attribute("EndPoint"));
            foreach (var brush in brushes)
            {
                var stops = brush.Elements().Where(element => element.Name.LocalName == "GradientStop").ToArray();
                Assert.True(stops.Length >= 3 && stops[^1].Attribute("Color")?.Value == "#000C0E11",
                    "渐变末端没有收成透明");
                Assert.True(stops.All(stop => stop.Attribute("Color")!.Value.EndsWith("0C0E11", StringComparison.Ordinal)),
                    "渐变没有用 #0C0E11 那一族墨色");
            }
            // 横着那道左沿透明起坡，和右侧「左上角跟右侧一样的渐变」一致。
            var horizontalStops = brushes[0].Elements().Where(element => element.Name.LocalName == "GradientStop").ToArray();
            Assert.Equal("#000C0E11", horizontalStops[0].Attribute("Color")?.Value);
            Assert.Contains("StoreShade.Visibility = StoreBackdrop.Visibility;", Source("Views/DetailPage.Store.cs"));
            // 2026-11-16 用户令「在左上角加一层跟右侧一样的渐变」：环境底遮罩左端也起坡，订阅页保持满色。
            Assert.Contains("StoreBackdropFadeShape.AmbientSymmetric", Source("Views/DetailPage.Showcase.cs"));
            Assert.Contains("StoreBackdropFadeShape.Ambient);", Source("Views/MoviePilotSubscriptionPage.Showcase.cs"));
            var mask = Momoka.Shell.Views.StoreBackdropBlend.Mask(ambient: true, symmetric: true);
            byte AmbientAlpha(double x) => mask[((int)Math.Round(0.35 * 127) * 128 + (int)Math.Round(x * 127)) * 4 + 3];
            Assert.Equal(0, AmbientAlpha(0));
            Assert.True(AmbientAlpha(0.1) is > 0 and < 255, "环境底左端没有渐隐");
            Assert.Equal(255, AmbientAlpha(0.5));
            // 2026-11-16 用户令「渐变要填满左右两边」：装饰带越过 1840 内容列，用负边距铺到窗口两边。
            var store = Source("Views/DetailPage.Store.cs");
            Assert.Contains("var bleed = store && Body.ActualWidth > columnWidth", store);
            Assert.Contains("? (Body.ActualWidth - columnWidth) / 2 : 0;", store);
            Assert.Contains("StoreAmbient.Margin = bleedMargin;", store);
            Assert.Contains("StoreShade.Margin = bleedMargin;", store);
        });
        Test("商店艺术图：顶部换成封面取色的纯色底，铺满左右上角", () =>
        {
            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            var wash = page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "StoreArtWash");
            Assert.Equal("{x:Bind ViewModel.StoreArtWashHeight, Mode=OneWay}", (string?)wash.Attribute("Height"));
            Assert.Equal("Stretch", (string?)wash.Attribute("HorizontalAlignment"));
            Assert.Equal("Collapsed", (string?)wash.Attribute("Visibility"));
            var store = Source("Views/DetailPage.Store.cs");
            Assert.Contains("ViewModel.StoreMode && ViewModel.StoreArtSelected", store);
            Assert.Contains("StoreArtWash.Margin = bleedMargin;", store);
            Assert.Contains("StoreArtWash.Visibility = artShown ? Visibility.Visible : Visibility.Collapsed;", store);
            Assert.Contains("_storeArtWash.Resolve(ViewModel.StillColorPixels, ThemeHost.Current)", store);
            var style = Source("Views/StoreBackdropStyle.cs");
            Assert.Contains("internal sealed class Wash", style);
            Assert.Contains("ArtworkShade.Sample(pixels.Pixels, pixels.Width, pixels.Height, theme.Colors.Window)", style);
            Assert.Contains("new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) }", style);
            Assert.Contains("theme.Colors.Window.Mix(color, 0.35)", style);
            // 2026-11-17 用户令「过度要平滑啊，上面从浓到淡」：多档递减 alpha，不再是一段实色后突然收掉。
            Assert.Contains("Offset = 0.25, Color = ThemeHost.ToColor(color.WithAlpha(215))", style);
            Assert.Contains("Offset = 0.5, Color = ThemeHost.ToColor(color.WithAlpha(155))", style);
            Assert.Contains("Offset = 0.75, Color = ThemeHost.ToColor(color.WithAlpha(75))", style);
            var vm = Source("ViewModels/DetailViewModel.cs");
            Assert.Contains("private const int StillColorDecodeWidth = 64;", vm);
            Assert.Contains("private HeroPictureLoader.HeroPixels? _stillColorPixels;", vm);
            Assert.Contains("DecodeFirstPixelsAsync(owner, [EmbyImageStore.Primary, EmbyImageStore.Thumb], token)", vm);
            Assert.Contains("RememberStillColor(pixels)", vm);
            Assert.Contains("nameof(ViewModels.DetailViewModel.StillColorPixels)", Source("Views/DetailPage.Appearance.cs"));
        });
        Test("详情头部：片名单行不折、徽标不随商店档缩小、片名栏保底宽度压在剧照上", () =>
        {
            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            XElement Named(string name) => page.Descendants()
                .Single(element => (string?)element.Attribute(X + "Name") == name);

            // 2026-11-16 用户令「片名不要自动换行」：一行到底，放不下时省略号收尾。
            var title = Named("TitleText");
            Assert.Equal("1", (string?)title.Attribute("MaxLines"));
            Assert.Equal("CharacterEllipsis", (string?)title.Attribute("TextTrimming"));
            // 2026-11-16 用户令「徽标不要自动缩小」：与其余页面同一个 280×56 上限，商店档不再单独压到 40。
            var plate = Named("TitlePlate");
            Assert.Equal("56", (string?)plate.Attribute("MaxHeight"));
            Assert.Equal("280", (string?)plate.Attribute("MaxWidth"));
            var store = Source("Views/DetailPage.Store.cs");
            Assert.False(store.Contains("TitlePlate.MaxHeight = store ? 40 : 56", StringComparison.Ordinal),
                "商店档仍把徽标单独压到 40");
            // 2026-11-16「位置不够时不要裁切，直接压在背景图上」：头图那一行按保底宽度的片名栏重算。
            Assert.Contains("HeroContent.Width = store ? ViewModel.StoreHeroWidth : double.NaN;", store);
            Assert.Contains("Math.Min(520, ViewModel.StoreHeroWidth)", store);
            Assert.Contains("{x:Bind ViewModel.StoreTextWidth, Mode=OneWay}", (string?)Named("HeroStack").Attribute("MaxWidth"));
        });
        Test("商店详情：剧情、文件选项和操作键排在封面下方", () =>
        {
            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            var details = page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "StoreHeroDetails");
            Assert.Equal("1", (string?)details.Attribute("Grid.Row"));
            Assert.Equal("2", (string?)details.Attribute("Grid.ColumnSpan"));
            // 正文与导演用内容高度；不得按封面剩余空间撑高。
            var overview = page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "OverviewPanel");
            Assert.True(overview.Descendants().Any(element => (string?)element.Attribute(X + "Name") == "OverviewBody"),
                "简介没有拆出可撑满的正文容器 OverviewBody");
            Assert.True(overview.Descendants().Any(element => (string?)element.Attribute(X + "Name") == "OverviewFooter"),
                "简介没有拆出底部导演行 OverviewFooter");
            // 商店简介的左内边距是 0，而 EgBodyPanelStyle 带 16 的圆角；圆角留着会把第一个字的左上角剪掉
            // （2026-11-15 用户报「剧情说明第一个字的左上角少了一点」）。这块本来就不画底和框，圆角归零。
            Assert.Equal("0", (string?)overview.Attribute("CornerRadius"));
            var store = Source("Views/DetailPage.Store.cs");
            Assert.Contains("StoreHeroDetails.Children.Insert(0, OverviewPanel);", store);
            Assert.Contains("StoreHeroDetails.Children.Add(FileOptions);", store);
            Assert.Contains("StoreHeroDetails.Children.Add(ColumnActions);", store);
            Assert.False(store.Contains("SyncStoreOverviewFill", StringComparison.Ordinal), "仍然按封面右栏撑高简介");
            Assert.False(store.Contains("UpdateStoreOverviewLines", StringComparison.Ordinal), "仍然按剩余高度更改摘要行数");
            var body = overview.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "OverviewBody");
            Assert.True(body.Descendants().Where(element => element.Name.LocalName == "RowDefinition")
                .All(element => (string?)element.Attribute("Height") == "Auto"), "剧情内部仍用固定剩余高度");
            var more = overview.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "DetailReadMoreButton");
            Assert.Equal("{x:Bind ViewModel.ToggleOverviewCommand}", (string?)more.Attribute("Command"));
            var text = overview.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "Overview");
            Assert.True(text.Descendants().Any(element => element.Name.LocalName == "ToolTip"), "原生全文提示没有恢复");
            Assert.Contains("Show(!StoreMode &&", Source("ViewModels/DetailViewModel.cs"));
            var score = page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "ScoreStack");
            var genre = page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "GenreLine");
            var facts = page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "HeroFacts");
            var order = page.Descendants().ToList();
            Assert.True(order.IndexOf(score) < order.IndexOf(genre) && order.IndexOf(genre) < order.IndexOf(facts),
                "头部顺序不是评分、类型、日期时长分级");
        });

        Test("详情头部：去掉工作室且日期时长分级没有丢失，轮播事实保持原格式", () =>
        {
            var item = new Momoka.Emby.EmbyItem
            {
                Type = Momoka.Emby.EmbyItemType.Movie,
                ProductionYear = 2025,
                RunTimeTicks = TimeSpan.FromMinutes(155).Ticks,
                OfficialRating = "PG-13",
                Studios = [new() { Name = "Plan B Entertainment" }]
            };
            Assert.Equal("2025  ·  2 小时 35 分  ·  PG-13", Momoka.Emby.ItemDetail.Facts(item, includeStudio: false));
            Assert.Contains("Plan B Entertainment", Momoka.Emby.ItemDetail.Facts(item));
            var vm = Source("ViewModels/DetailViewModel.cs");
            Assert.Contains("Facts = ItemDetail.Facts(item, includeStudio: false);", vm);
            Assert.Contains("Facts = ItemDetail.Facts(series, includeStudio: false);", vm);
        });

        Test("详情操作：从头开始只在更多菜单里，紧跟继续播放一行", () =>
        {
            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "DetailPage.xaml"));
            foreach (var gone in new[] { "ColumnRestartButton", "CompactRestartButton" })
                Assert.False(page.Descendants().Any(element =>
                        (string?)element.Attribute(X + "Name") == gone
                        || (string?)element.Attribute("AutomationProperties.AutomationId") == gone),
                    "详情操作里仍有 " + gone);
            Assert.False(page.Descendants().Any(element => (string?)element.Attribute("Command") == "{x:Bind ViewModel.RestartCommand}"),
                "从头开始仍是操作键上的一颗按钮");
            var code = Source("Views/DetailPage.xaml.cs");
            Assert.Contains("restart: ViewModel.RestartVisibility == Visibility.Visible", code);
            var commands = Source("Views/ItemCommands.cs");
            Assert.Contains("Text = \"从头开始\"", commands);
            Assert.Contains("var play = rows.FindIndex(row => row is MenuFlyoutItem { Tag: ItemCommand.Play });", commands);
            Assert.Contains("rows.Insert(play < 0 ? 0 : play + 1, entry);", commands);
            Assert.Contains("DetailRestartMenuItem", commands);
            Assert.False(Source("Strings/zh-Hans/Resources.resw").Contains("DetailPage_TextBlock1.Text", StringComparison.Ordinal),
                "旧按钮的本地化字串没有退役");
        });

        Test("商店尾部：预告控制条没上屏时不再预留那 72 的空档", () =>
        {
            var viewModel = Source("ViewModels/DetailViewModel.cs");
            Assert.Contains("StoreTrailerControlsShown && PageWidth >= DetailHero.StoreWideFloor", viewModel);
            Assert.DoesNotContain("TrailerVisibility == Visibility.Visible && PageWidth >= DetailHero.StoreWideFloor",
                viewModel);
            var trailer = Source("Views/DetailPage.StoreTrailer.cs");
            // 控制键真正露头的两处（本地 MediaOpened 与网页预告起播）各接一次；StopStoreTrailer 统一收回。
            Assert.Equal(2, Regex.Matches(trailer,
                @"StoreTrailerControls\.Visibility = Visibility\.Visible;\r?\n\s*ViewModel\.StoreTrailerControlsShown = true;").Count);
            Assert.Contains("ViewModel.StoreTrailerControlsShown = false;", trailer);
        });
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new AssertionException("找不到本次编译所属的工作树");
    }

    private static string Source(string path) =>
        File.ReadAllText(Path.Combine(FindRoot(), "src", "Momoka.Shell", path.Replace('/', Path.DirectorySeparatorChar)));
}
