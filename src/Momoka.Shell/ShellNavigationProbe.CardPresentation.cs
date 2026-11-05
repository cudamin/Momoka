using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.Shell.Views;
using Momoka.Theming;
using Windows.Storage.Streams;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task CardPresentationAsync()
    {
        using var fixture = CreateFixture();
        var photo = await FixtureImageAsync(400, 600);
        fixture.Transport.Reply = (request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(photo) }
                : FakeTransport.Standard(request));
        await fixture.SelectAsync();
        var scope = fixture.Session.Capture();
        var savedTheme = ThemeHost.Current;
        var savedMotion = HomeMotion.PreferenceEnabled;
        var holder = new StackPanel { Margin = new Thickness(24), Spacing = 20,
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var longItem = scope.Bind(new EmbyItem { Id = "card-long", Name = "这是用来核对两行片名与年份位置保持一致的长片名",
            Type = EmbyItemType.Movie, ProductionYear = 2021 });
        var shortItem = scope.Bind(new EmbyItem { Id = "card-short", Name = "短片名",
            Type = EmbyItemType.Movie, ProductionYear = 2021, ImageTags = new() { ["Primary"] = "card-photo" },
            UserData = new() { Played = true } });
        var first = new CardItem(longItem, fixture.Images, 170, sourceLine: true);
        var second = new CardItem(shortItem, fixture.Images, 170, sourceLine: true);
        var left = new PosterCard { Card = first, CardWidth = 170, PosterHeight = 255 };
        var right = new PosterCard { Card = second, CardWidth = 170, PosterHeight = 255 };
        var rowHeight = CardSize.PosterHeight + CardSize.Chrome + CardSize.MediaTitleExtra + CardSize.SourceLine;
        holder.Children.Add(new Border { Height = rowHeight, Child = left, VerticalAlignment = VerticalAlignment.Top });
        holder.Children.Add(new Border { Height = rowHeight, Child = right, VerticalAlignment = VerticalAlignment.Top });
        ThemeHost.Register(holder);
        _root.Children.Add(holder);
        var recycled = new CardItem(scope.Bind(new EmbyItem { Id = "card-recycled", Name = "回收后缺图片名",
            Type = EmbyItemType.Movie }), fixture.Images, 170, sourceLine: true);
        try
        {
            HomeMotion.ApplyPreference(false);
            ResizeProbeClient(600, 560);
            await UntilAsync(() => second.Poster is { PixelWidth: > 0 });
            foreach (var theme in UiThemes.All)
            {
                ThemeHost.Apply(theme);
                // Controlled visual state only; no shared pointer movement and no button invocation.
                var overlay = Get<Grid>(right, "Hover");
                overlay.Visibility = Visibility.Visible;
                overlay.Opacity = 1;
                await LayoutAsync(holder);
                Require(Get<StackPanel>(left, "Placeholder").Visibility == Visibility.Visible,
                    "缺图片名占位未显示");
                Require(Get<TextBlock>(left, "PlaceholderTitle").Text == longItem.Name, "占位片名未跟随当前卡片");
                Require(Get<StackPanel>(right, "Placeholder").Visibility == Visibility.Collapsed,
                    "成功解码后占位仍然显示");
                var longTitle = Get<TextBlock>(left, "TitleLine");
                var shortTitle = Get<TextBlock>(right, "TitleLine");
                Require(longTitle.MaxLines == 2 && longTitle.ActualHeight == 40
                    && shortTitle.ActualHeight == 40, "长短媒体片名没有保留同高两行区域");
                var firstSub = Get<TextBlock>(left, "SubLine").TransformToVisual(holder).TransformPoint(new(0, 0)).Y;
                var secondSub = Get<TextBlock>(right, "SubLine").TransformToVisual(holder).TransformPoint(new(0, 0)).Y;
                Require(Math.Abs(firstSub - secondSub) < 1, "短片名把年份向上挤动");
                foreach (var card in new[] { left, right })
                {
                    var source = Get<TextBlock>(card, "SourceLine");
                    var bounds = source.TransformToVisual(card).TransformBounds(new(0, 0, source.ActualWidth, source.ActualHeight));
                    Require(source.Text.Length > 0 && bounds.Bottom <= rowHeight, "第三行来源超出货架测量高度");
                }
                var play = Get<Button>(right, "PlayButton");
                Require(play.Background is SolidColorBrush background && play.Foreground is SolidColorBrush foreground
                    && background.Color != foreground.Color, "播放按钮前景背景没有对比");
                await SaveProbeScreenAsync($"card-presentation-{theme.Id}-screen.png");
            }

            // Same real control changes Card, exercising production OnCardChanged and x:Bind.
            right.Card = recycled;
            await LayoutAsync(holder);
            Require(Get<StackPanel>(right, "Placeholder").Visibility == Visibility.Visible
                && Get<TextBlock>(right, "PlaceholderTitle").Text == "回收后缺图片名", "回收卡残留旧图或旧标题");
            await SaveProbeScreenAsync("card-presentation-recycled-screen.png");

            // An undecoded source must remain a placeholder until an actual ImageOpened event.
            var pending = new BitmapImage();
            var opened = false;
            var picture = Get<Image>(right, "Poster");
            RoutedEventHandler onOpened = (_, _) => opened = true;
            picture.ImageOpened += onOpened;
            try
            {
                typeof(CardItem).GetProperty(nameof(CardItem.Poster))!.SetValue(recycled, pending);
                await LayoutAsync(holder);
                Require(Get<StackPanel>(right, "Placeholder").Visibility == Visibility.Visible,
                    "仅分配未解码 Source 就收起了占位");
                HomeMotion.ApplyPreference(true);
                using var stream = new InMemoryRandomAccessStream();
                using var writer = new DataWriter(stream);
                writer.WriteBytes(photo);
                await writer.StoreAsync();
                writer.DetachStream();
                stream.Seek(0);
                await pending.SetSourceAsync(stream);
                await UntilAsync(() => opened && picture.Opacity >= 0.999
                    && Get<StackPanel>(right, "Placeholder").Visibility == Visibility.Collapsed);
                await SaveProbeScreenAsync("card-presentation-image-opened-screen.png");
            }
            finally { picture.ImageOpened -= onOpened; }

            // Cache reuse may not emit ImageOpened again; Source/PixelWidth must suffice.
            right.Card = second;
            await UntilAsync(() => second.Poster is { PixelWidth: > 0 });
            await LayoutAsync(holder);
            Require(Get<StackPanel>(right, "Placeholder").Visibility == Visibility.Collapsed
                && Get<TextBlock>(right, "TitleLine").Text == "短片名", "缓存重新出现时残留回收占位");
            await SaveProbeScreenAsync("card-presentation-cache-restored-screen.png");
            Require(fixture.Actions.Plays == 0, "卡片展示检查意外请求播放");
            Require(fixture.Transport.Requests.All(request => request.StartsWith("GET ", StringComparison.Ordinal)
                || request.StartsWith("HEAD ", StringComparison.Ordinal)), "卡片展示检查发送了写请求");
        }
        finally
        {
            first.AbandonPoster(); second.AbandonPoster(); recycled.AbandonPoster();
            _root.Children.Remove(holder);
            HomeMotion.ApplyPreference(savedMotion);
            ThemeHost.Apply(savedTheme);
        }
    }
}
