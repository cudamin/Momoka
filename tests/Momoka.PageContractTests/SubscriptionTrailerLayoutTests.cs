using System.Xml.Linq;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

/// <summary>
/// 订阅详情的背景渐变、商店尾部避让与预告片入口。实际的尺寸、命中与自动播放由 Shell 原生探针核对；
/// 这里钉住 XAML 层级和几条生产接线，免得改版时入口或避让悄悄消失。
/// </summary>
internal static class SubscriptionTrailerLayoutTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static void Register()
    {
        Test("订阅详情：商店尾部把正文纸面让到剧照和预告控制排下面", () =>
        {
            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionPage.xaml"));
            XElement Named(string name) => page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);

            var tail = Named("StoreTail");
            Assert.Equal("{x:Bind MediaViewModel.StoreTailHeight, Mode=OneWay}", (string?)tail.Attribute("Height"));
            // 尾部必须在量高度的那一叠外面：量的是 HeroBlock，尾部挂在它下面才不回灌成布局回路。
            Assert.True(tail.Ancestors().Any(element => (string?)element.Attribute(X + "Name") == "HeroSection"));
            Assert.False(tail.Ancestors().Any(element => (string?)element.Attribute(X + "Name") == "HeroBlock"));
            var order = page.Descendants().ToList();
            Assert.True(order.IndexOf(tail) < order.IndexOf(Named("BodySheet")), "尾部没有排在正文纸面之前");

            var viewModel = Source("ViewModels/MoviePilotMediaViewModel.cs");
            Assert.Contains("public double StoreTailHeight => Plain && Backdrop is not null", viewModel);
            Assert.Contains("DetailHero.StoreTailHeight(StoreBackdropHeight, HeroBandHeight, TrailerControlsRoom, DetailHero.StorePreviewGap)", viewModel);
            Assert.Contains("OnPropertyChanged(nameof(StoreTailHeight));", viewModel);
        });
        Test("订阅详情：商店剧照与媒体详情页同一套右边距、圆角和环境底", () =>
        {
            var store = Source("Views/MoviePilotSubscriptionPage.Store.cs");
            Assert.Contains("StoreBackdrop.Margin = MediaViewModel.StorePreviewMargin;", store);
            Assert.Contains("StoreBackdrop.Height = MediaViewModel.StoreBackdropHeight;", store);
            Assert.Contains("StoreBackdrop.Width = MediaViewModel.StoreBackdropWidth;", store);
            Assert.Contains("StoreAmbient.Height = MediaViewModel.StoreAmbientHeight;", store);
            Assert.Contains("StoreBackdropContent.CornerRadius = previewCorner;", store);
            Assert.Contains("StoreTrailerContent.CornerRadius = previewCorner;", store);

            var viewModel = Source("ViewModels/MoviePilotMediaViewModel.cs");
            Assert.Contains("public Thickness StorePreviewMargin => Plain ? new(0, 0, DetailHero.StorePreviewGap, 0) : default;", viewModel);
            Assert.Contains("public double StoreAmbientHeight => Math.Max(StoreBackdropHeight, HeroBandHeight);", viewModel);

            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionPage.xaml"));
            var backdropElement = page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == "StoreBackdrop");
            var brushes = backdropElement.Descendants().Where(element => element.Name.LocalName == "ImageBrush").ToArray();
            Assert.True(brushes.Length > 0
                && brushes.All(brush => (string?)brush.Attribute("AlignmentX") == "Right"),
                "商店剧照没有和媒体详情页一样靠右上对齐");
        });
        Test("订阅详情：图下沿渐变与媒体详情页同一支 smoothstep，不再是一条 64 像素黑边", () =>
        {
            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionPage.xaml"));
            XElement Named(string name) => page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);
            var fade = Named("DetailBackdropFade");
            Assert.Equal("{x:Bind MediaViewModel.BackdropHeight, Mode=OneWay}", (string?)fade.Attribute("Height"));
            Assert.Null(fade.Attribute("Margin"));

            var code = Source("Views/MoviePilotSubscriptionPage.xaml.cs");
            Assert.Contains("Offset = 0.8 + 0.2 * t,", code);
            Assert.Contains("ThemeHost.Current.Colors.Window", code);
            Assert.DoesNotContain("BackdropFadeStops", code);
            Assert.DoesNotContain("BackdropFadeMargin", Source("ViewModels/MoviePilotMediaViewModel.cs"));
        });
        Test("订阅详情：预告片从当前媒体库条目取，商店宽版式自动铺、沉浸档留入口", () =>
        {
            var trailer = Source("Views/MoviePilotSubscriptionPage.Trailer.cs");
            Assert.Contains("StoreTrailerPreview.CreateAsync(subject, token)", trailer);
            Assert.Contains("TrailerPreview.YouTubeId(ItemTrailers.Remote(subject))", trailer);
            Assert.Contains("internal void SetStorePreviewActive(bool active)", trailer);
            Assert.Contains("MediaViewModel.StoreWide", trailer);
            Assert.Contains("ScrollStoreTrailer();", trailer);

            var viewModel = Source("ViewModels/MoviePilotMediaViewModel.cs");
            Assert.Contains("internal void AttachPlayback(EmbySessionScope? scope)", viewModel);
            Assert.Contains("client.FindByProviderAsync(provider, detail.Media.MediaId!, series, cancellationToken)", viewModel);
            Assert.Contains("ItemTrailers.HasAny(item, scope.Connection.ServerKind)", viewModel);
            Assert.Contains("public Visibility TrailerButtonVisibility => Show(!Plain && TrailerSubject is not null);", viewModel);

            var page = XDocument.Load(Path.Combine(FindRoot(), "src", "Momoka.Shell", "Views", "MoviePilotSubscriptionPage.xaml"));
            XElement Named(string name) => page.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);
            Assert.Equal("{x:Bind MediaViewModel.TrailerButtonVisibility, Mode=OneWay}",
                (string?)Named("PlaySubscriptionTrailer").Attribute("Visibility"));
            Assert.Equal("OnTrailerButtonClick", (string?)Named("PlaySubscriptionTrailer").Attribute("Click"));
            var host = Named("StoreTrailerHost");
            Assert.True(host.Elements().Any(element => (string?)element.Attribute(X + "Name") == "StoreTrailerContent"));
            Assert.Equal("1", (string?)Named("StoreTrailerFade").Attribute("Canvas.ZIndex"));
            var content = Named("StoreScrollContent");
            var layers = content.Elements().Select(element => (string?)element.Attribute(X + "Name")).ToArray();
            Assert.True(Array.IndexOf(layers, "StoreTrailerFoundation") < Array.IndexOf(layers, "StoreAmbient")
                && Array.IndexOf(layers, "StoreAmbient") < Array.IndexOf(layers, "StoreBackdrop")
                && Array.IndexOf(layers, "StoreBackdrop") < Array.IndexOf(layers, "StoreTrailerHost"),
                "预告片局部底、环境底、剧照与视频的层级顺序不对");

            var shell = Source("Views/ShellPage.xaml.cs");
            Assert.Contains("(ContentFrame.Content as MoviePilotSubscriptionPage)?.SetStorePreviewActive(!playing);", shell);
            Assert.Contains("PlayTrailer: item => ((IShellActions)this).PlayAsync(item,", shell);
        });
        Test("订阅详情：离页与进播放都收预告片，滚动只暂停一次", () =>
        {
            var page = Source("Views/MoviePilotSubscriptionPage.xaml.cs");
            Assert.Contains("StopStoreTrailer();", page);
            var showcase = Source("Views/MoviePilotSubscriptionPage.Showcase.cs");
            Assert.Contains("ScrollStoreTrailer();", showcase);
            Assert.Contains("if (args.PropertyName is nameof(MoviePilotMediaViewModel.TrailerSubject))", showcase);
            var trailer = Source("Views/MoviePilotSubscriptionPage.Trailer.cs");
            Assert.Contains("if (_trailerScrollPaused) return;", trailer);
            Assert.Contains("_trailerUserPaused || _trailerScrollPaused", trailer);
        });
    }

    private static string Source(string relative) => File.ReadAllText(Path.Combine(
        FindRoot(), "src", "Momoka.Shell", relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Momoka.sln"))) return directory.FullName;
        throw new InvalidOperationException("找不到当前工作树");
    }
}
