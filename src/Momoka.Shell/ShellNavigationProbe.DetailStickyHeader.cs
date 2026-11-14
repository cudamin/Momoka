using System.Net;
using System.Numerics;
using System.Text.Json;
using CommunityToolkit.WinUI.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Momoka.Emby;
using Momoka.Shell.Views;
using Windows.Foundation;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task DetailStickyHeaderAsync()
    {
        using var fixture = CreateFixture();
        await fixture.SelectAsync();
        var poster = await FixtureImageAsync(260, 375);
        var item = new EmbyItem
        {
            Id = "sticky-movie",
            Name = "悬浮卡片 · 长片名与滚动播放入口",
            Type = EmbyItemType.Movie,
            Genres = ["剧情", "冒险"],
            Overview = string.Join("\n", Enumerable.Repeat("离线剧情说明，用于检验顶部悬浮卡片与正文滚动。", 12)),
            ImageTags = new() { ["Primary"] = "poster" }
        };
        fixture.Transport.Reply = (request, _) =>
        {
            Require(request.Method == HttpMethod.Get, "悬浮卡片探针不得写入服务器");
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/Images/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) });
            if (path.EndsWith("/Items/" + item.Id, StringComparison.Ordinal)) return Task.FromResult(Json(item));
            if (path.EndsWith("/Similar", StringComparison.Ordinal)) return Task.FromResult(Items(8, 8));
            return Task.FromResult(FakeTransport.Standard(request));
        };
        var host = new Grid { Margin = new Thickness(0, 68, 0, 0) };
        var frame = new Frame();
        host.Children.Add(frame);
        _root.Children.Add(host);
        var preference = HomeMotion.PreferenceEnabled;
        var samples = new List<object>();
        DetailPage? page = null;
        try
        {
            HomeMotion.ApplyPreference(true);
            frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, item));
            page = (DetailPage)frame.Content;
            page.AttachWindow(_window!);
            await UntilAsync(() => page.IsReady && page.ViewModel.StillImage is not null);
            foreach (var width in new[] { 1000, 760, 360 })
            {
                await ResizeResponsiveAsync(page, width, 800);
                // VM 的目标宽度可能先于 HWND/XAML 安排更新，必须等本次真实视口到位。
                await UntilAsync(() =>
                {
                    page.UpdateLayout();
                    return Math.Abs(_window!.ClientSize.Width / _root.XamlRoot.RasterizationScale - width) < 1
                        && Math.Abs(page.ActualWidth - width) < 1;
                });
                await LayoutAsync(page);
                var scroll = Get<ScrollView>(page, "Body");
                var card = Get<Border>(page, "StickyHeader");
                var play = Get<Button>(page, "StickyPlayButton");
                var anchor = Get<Button>(page, page.ViewModel.IsCompact ? "CompactPlayButton" : "ColumnPlayButton");
                await ScrollProbeAsync(scroll, 0);
                Require(card.Visibility == Visibility.Collapsed && !card.IsHitTestVisible, "未滚动时卡片错误出现");
                var clipLine = anchor.TransformToVisual(scroll).TransformPoint(new Point(0, anchor.ActualHeight / 3)).Y;
                await ScrollProbeAsync(scroll, clipLine - 6);
                Require(card.Visibility == Visibility.Collapsed, "播放键尚未裁切三分之一时卡片提前出现");
                await ScrollProbeAsync(scroll, clipLine + 1);
                await UntilAsync(() => card.Visibility == Visibility.Visible && card.IsHitTestVisible);
                await Task.Delay(450);
                Require(card.Background is AcrylicBrush, "悬浮卡片缺少系统应用内亚克力");
                Require(ReferenceEquals(play.Command, page.ViewModel.PlayCommand), "悬浮播放未复用原命令");
                Require(AutomationProperties.GetName(play) == page.ViewModel.PlayText, "窄窗播放键缺少真实文案");
                Require(Get<TextBlock>(page, "StickyPlayText").Visibility == (width < 520 ? Visibility.Collapsed : Visibility.Visible),
                    "窄窗没有收起长播放文案");
                var bounds = card.TransformToVisual(page).TransformBounds(new Rect(0, 0, card.ActualWidth, card.ActualHeight));
                Require(bounds.Left >= 0 && bounds.Right <= page.ActualWidth + 1 && bounds.Top >= 0, "卡片超出页面边界");
                // 反例须拒绝实色，恢复同一支系统刷子后继续截图。
                var acrylic = card.Background;
                card.Background = new SolidColorBrush();
                Require(card.Background is not AcrylicBrush, "材质反例未替换成功");
                card.Background = acrylic;
                await SaveProbeScreenAsync($"detail-sticky-{width}-screen.png");
                samples.Add(new { Width = width, ClientWidth = _window!.ClientSize.Width / _root.XamlRoot.RasterizationScale,
                    PageWidth = page.ActualWidth, Bounds = bounds.ToString(), ThirdClipOffset = clipLine,
                    Card = "Acrylic", Name = AutomationProperties.GetName(play) });
                await ScrollProbeAsync(scroll, clipLine - 6);
                await UntilAsync(() => card.Visibility == Visibility.Collapsed && !card.IsHitTestVisible);
                await ScrollProbeAsync(scroll, clipLine + 1);
                await UntilAsync(() => card.Visibility == Visibility.Visible);
                HomeMotion.ApplyPreference(false);
                Require(card.GetValue(Implicit.ShowAnimationsProperty) is null && card.GetValue(Implicit.HideAnimationsProperty) is null,
                    "关动画后仍保留隐式动画");
                var visual = ElementCompositionPreview.GetElementVisual(card);
                Require(visual.Opacity == 1
                    && visual.Properties.TryGetVector3("Translation", out var translation) == Microsoft.UI.Composition.CompositionGetValueStatus.Succeeded
                    && translation == Vector3.Zero, "关动画后卡片仍半透明或有残余位移");
                await ScrollProbeAsync(scroll, 0);
                Require(card.Visibility == Visibility.Collapsed, "关动画后收起未立即落定");
                HomeMotion.ApplyPreference(true);
            }
            Require(fixture.Actions.Plays == 0, "悬浮卡片探针发生起播");
            page.Release();
            await LayoutAsync(page);
            Require(Get<Border>(page, "StickyHeader").Visibility == Visibility.Collapsed, "Release后旧卡片重新出现");
            await File.WriteAllTextAsync(Path.Combine(_options.Paths.LogDirectory, "detail-sticky.json"),
                JsonSerializer.Serialize(new { Samples = samples, Plays = fixture.Actions.Plays, Requests = fixture.Transport.Requests.Count }));
        }
        finally
        {
            page?.Release();
            _root.Children.Remove(host);
            HomeMotion.ApplyPreference(preference);
        }
    }
}
