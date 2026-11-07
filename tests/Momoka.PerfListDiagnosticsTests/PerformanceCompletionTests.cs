using Microsoft.UI.Xaml.Controls;
using Momoka.Shell;
using Momoka.Shell.Views;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class PerformanceCompletionTests
{
    internal static void Register()
    {
        Test("性能分页触发等待关联完成与新页，不把追加后的位移当滚动失败", () => SingleThreadPump.Run(async () =>
        {
            var scroll = new ScrollView();
            var probe = new ShellNavigationProbe();
            await probe.PageCompletionForFixture(scroll, () => scroll.PageArrived);
            Assert.True(scroll.PageArrived);
            Assert.True(scroll.HorizontalOffset > 0, "the completed request may be followed by append anchoring");
        }));
        Test("性能分页缺新页仍拒绝，不放宽原分页事实", () => SingleThreadPump.Run(async () =>
        {
            var probe = new ShellNavigationProbe();
            await AssertFailure(() => probe.PageCompletionForFixture(new ScrollView(), () => false));
        }));
        Test("性能季跳等待实际落点后才判选中季", () => SingleThreadPump.Run(async () =>
        {
            var page = new DetailPage();
            var season = new Momoka.Emby.EmbyItem { Id = "season19" };
            await new ShellNavigationProbe().SeasonCompletionForFixture(page, season);
            Assert.Equal(season.Id, page.ViewModel.SelectedSeason?.Id);
            Assert.Equal(page.Strip.Requested, page.Strip.Scroll.HorizontalOffset);
        }));
        Test("性能季跳完成后旧季仍拒绝", () => SingleThreadPump.Run(async () =>
        {
            var page = new DetailPage { WrongSeason = true };
            await AssertFailure(() => new ShellNavigationProbe().SeasonCompletionForFixture(page, new() { Id = "season19" }));
        }));

        static async Task AssertFailure(Func<Task> run)
        {
            Exception? failure = null;
            try { await run(); } catch (Exception error) { failure = error; }
            Assert.True(failure is InvalidOperationException);
        }
    }
}
