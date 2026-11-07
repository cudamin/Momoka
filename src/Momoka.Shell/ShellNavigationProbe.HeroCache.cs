using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Diagnostics;
using Momoka.Emby;
using Momoka.Infrastructure;
using Momoka.MoviePilot;
using Momoka.Shell.Media;
using Momoka.Shell.Views;

namespace Momoka.Shell;

internal sealed partial class ShellNavigationProbe
{
    private async Task PerformanceHeroCacheAsync(Fixture fixture, Frame frame)
    {
        ResizeProbeClient(1280, 900);
        var bytes = await FixtureImageAsync(1280, 720);
        var seed = new EmbyItem
        {
            Id = "hero-cache",
            Name = "背景复用回归",
            Type = EmbyItemType.Movie,
            Overview = "同图再次进入直接复用，图片标签改变或清理后重新处理。",
            BackdropImageTags = ["first"]
        };
        fixture.Session.Capture().Bind(seed);
        fixture.Transport.Reply = (request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.Contains("/Images/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : request.RequestUri.AbsolutePath.EndsWith("/Items/" + seed.Id, StringComparison.Ordinal)
                    ? Json(seed) : FakeTransport.Standard(request));
        var cache = fixture.Services.GetRequiredService<DetailHeroCache>();
        cache.Clear();
        using var sink = new NavigationTimingFileSink(Path.Combine(_options.Paths.LogDirectory, "hero-cache-timing.ndjson"));
        NavigationTiming.Start(new NavigationTimingRecorder(sink));
        BitmapImage? first = null;
        long steadyBytes = 0;
        try
        {
            for (var index = 0; index < 30; index++)
            {
                NavigationTiming.Click("probe-hero-cache");
                Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, seed)), "缓存详情导航失败");
                var page = (DetailPage)frame.Content!;
                await UntilAsync(() => page.ViewModel.HeroImage is not null && page.IsReady);
                await LayoutAsync(page);
                if (index == 0)
                {
                    first = page.ViewModel.HeroImage as BitmapImage;
                    Require(first is not null, "背景不是实际 BitmapImage");
                    steadyBytes = cache.Bytes;
                    Require(steadyBytes == 1280L * 720 * 4 * 2, "原像素和最终位图未一起记账");
                }
                else
                {
                    Require(ReferenceEquals(first, page.ViewModel.HeroImage), "重复详情未复用相同背景对象");
                    Require(cache.Bytes == steadyBytes, "同图30次访问缓存持续增长");
                }
                if (index == 1) await SaveProbeScreenAsync("performance-hero-cache-1280-screen.png");
                page.Release();
                frame.Content = null;
                frame.BackStack.Clear();
            }
            seed.BackdropImageTags = ["changed"];
            Require(frame.Navigate(typeof(DetailPage), DetailRequest.For(fixture.Services, seed)), "换图详情导航失败");
            var changed = (DetailPage)frame.Content!;
            await UntilAsync(() => changed.ViewModel.HeroImage is not null && changed.IsReady);
            await LayoutAsync(changed);
            Require(!ReferenceEquals(first, changed.ViewModel.HeroImage), "图片标签变更仍沿用旧背景");
            var clearedPicture = changed.ViewModel.HeroImage;
            var clearedPixels = changed.ViewModel.BackdropPixels;
            Require(clearedPixels is not null, "背景缓存检查缺少实际像素");
            var oldTicket = cache.Begin(fixture.Session.Capture(), ItemArtwork.Hero(seed)[0], 1280);
            var settingsPaths = new AppPaths(Path.Combine(_options.Paths.Root, "hero-cache-settings"));
            settingsPaths.EnsureCreated();
            using var moviePilot = new MoviePilotClient(new RejectNetwork());
            var settingsPage = CreateMotionSettingsPage(fixture, moviePilot, settingsPaths);
            try
            {
                await settingsPage.ViewModel.ReloadAsync();
                settingsPage.ViewModel.UseConfirm((_, _, _) => Task.FromResult(true));
                var clear = typeof(ViewModels.SettingsViewModel).GetMethod("ClearImageCacheAsync",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                await (Task)clear.Invoke(settingsPage.ViewModel, null)!;
                Require(cache.Bytes == 0, "真实设置清除未清理详情背景缓存");
            }
            finally { settingsPage.ViewModel.Dispose(); }
            foreach (var (width, viewport, strength) in new[] { (1280d, 300d, 100d), (760d, 900d, 0d), (1280d, 900d, 20d) })
            {
                changed.ViewModel.PageWidth = width;
                changed.ViewModel.Viewport = viewport;
                fixture.Settings.Ui.BackdropOpacity = strength;
                changed.ViewModel.UpdateHeroBlur();
                ShellPrefs.ApplyAppearance(fixture.Settings.Ui);
                await LayoutAsync(changed);
                Require(ReferenceEquals(clearedPicture, changed.ViewModel.HeroImage)
                    && changed.ViewModel.GaussianBackdropReady && !changed.ViewModel.GaussianBackdropApplied,
                    "清理后窗口或外观调整仍重新模糊原图");
                Require(cache.Bytes == 0, "清理前取得的像素在外观更新后重新填回缓存");
            }
            // 缓存边界独立验证旧票迟到：真实Remember入口必须拒绝清理前的原像素/位图。
            cache.RememberPixels(oldTicket, clearedPixels!);
            cache.RememberPicture(oldTicket, clearedPixels!, 0, (BitmapImage)clearedPicture!);
            Require(cache.Bytes == 0, "清理后迟到的原图结果重新填充缓存");
            ResizeProbeClient(760, 900);
            await LayoutAsync(changed);
            await SaveProbeScreenAsync("performance-hero-cache-760-screen.png");
            changed.Release();
            frame.Content = null;
            frame.BackStack.Clear();
            Require(fixture.Actions.Plays == 0, "背景检查触发播放");
            await WritePerformanceEvidenceAsync("performance-hero-cache.json", new
            {
                visits = 30,
                reused = 29,
                steadyBytes,
                budgetBytes = DetailHeroCache.DefaultBudgetBytes,
                changedTagReloaded = true,
                settingsClearExecuted = true,
                clearRejectedOldPixels = true,
                appearanceRetainedOriginal = true,
                realNetwork = false,
                mediaLoaded = false
            });
        }
        finally
        {
            NavigationTiming.Stop();
            if (frame.Content is IShellContent content) content.Release();
            frame.Content = null;
            frame.BackStack.Clear();
            ResizeProbeClient(1280, 900);
        }
    }
}
