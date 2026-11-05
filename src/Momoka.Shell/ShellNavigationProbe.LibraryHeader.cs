using System.Net;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Momoka.Emby;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task LibraryHeaderPolishAsync()
    {
        foreach (var kind in new[] { MediaServerKind.Emby, MediaServerKind.Jellyfin })
        {
            using var fixture = CreateFixture();
            var poster = await FixtureImageAsync();
            fixture.Transport.Reply = (request, _) => Task.FromResult(kind == MediaServerKind.Jellyfin
                ? JellyfinResponse(request, poster) : ResumeResponse(request));
            await fixture.SelectAsync(kind: kind);
            var frame = new Frame();
            _root.Children.Add(frame);
            try
            {
                frame.Navigate(typeof(LibraryPage), LibraryRequest.ForRow(fixture.Services, HomeLayout.HomeRowTarget.Resume));
                var page = (LibraryPage)frame.Content;
                await UntilAsync(() => page.ViewModel.IsReady && !page.ViewModel.Busy);
                foreach (var width in new[] { 1132, 760, 600 })
                {
                    ResizeProbeClient(width, 800);
                    await LayoutAsync(page);
                    var toolbar = Get<Grid>(page, "ToolbarSurface");
                    var controls = toolbar.TransformToVisual(page).TransformBounds(new(0, 0, toolbar.ActualWidth, toolbar.ActualHeight));
                    var title = Get<TextBlock>(page, "LibraryTitle");
                    Require(title.FontSize == (double)Application.Current.Resources["EgTitleFontSize"], "继续观看标题没有使用20DIP刻度");
                    Require(Math.Abs(controls.Left - Get<Grid>(page, "PageRoot").Margin.Left) <= 2, "继续观看工具条没有左对齐");
                    RequireResumeGap(page, controls.Bottom);
                    if (width == 1132)
                    {
                        var content = Get<Grid>(page, "EmbyContent");
                        var margin = content.Margin;
                        var rejected = false;
                        try
                        {
                            content.Margin = new Thickness(0, 128, 0, 0);
                            await LayoutAsync(page);
                            try { RequireResumeGap(page, controls.Bottom); }
                            catch (InvalidOperationException) { rejected = true; }
                        }
                        finally { content.Margin = margin; }
                        Require(rejected, "继续观看大空档反例未被检测");
                        await LayoutAsync(page);
                        RequireResumeGap(page, controls.Bottom);
                    }
                    await SaveProbeScreenAsync($"library-resume-header-{kind}-{width}-screen.png");
                }
            }
            finally
            {
                (frame.Content as IShellContent)?.Release();
                frame.Content = null;
                _root.Children.Remove(frame);
            }

            HttpResponseMessage ResumeResponse(HttpRequestMessage request)
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("/Images/", StringComparison.Ordinal))
                    return new(HttpStatusCode.OK) { Content = new ByteArrayContent(poster) };
                if (path.EndsWith("/Items/Resume", StringComparison.Ordinal))
                    return Json(new { Items = new[] { Item("resume", EmbyItemType.Movie) }, TotalRecordCount = 1 });
                return FakeTransport.Standard(request);
            }
        }
    }

    private static void RequireResumeGap(LibraryPage page, double toolbarBottom) =>
        Require(Math.Abs(PosterTop(page) - toolbarBottom - 28) <= 2,
            "继续观看仍有隐藏行占位或不是28DIP间距");
}
