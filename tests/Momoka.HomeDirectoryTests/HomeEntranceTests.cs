using Microsoft.UI.Xaml.Navigation;
using Momoka.Shell.Views;

namespace Momoka.Tests
{
    internal static class HomeEntranceTests
    {
        internal static void Register(Action<string, Func<Task>> test)
        {
            foreach (var mode in new[] { NavigationMode.New, NavigationMode.Back, NavigationMode.Forward })
            {
                foreach (var restored in new[] { false, true })
                {
                    test($"主页货架入场：{mode} 快照={restored}", () =>
                    {
                        var request = new HomeRequest(null!, [], null) { ScrollOffset = 140 };
                        var page = new HomePage(request);
                        page.WireLoaded();
                        page.Visit(mode, restored);
                        page.RaiseLoaded();
                        Assert.Equal(mode == NavigationMode.New && !restored ? 1 : 0, page.EnterCalls,
                            "暖恢复和历史返回不能把现成货架重新藏到入场动画里");
                        Assert.Equal(restored ? 140d : 0d, page.Scroller.Offset);
                        Assert.Equal(restored ? 1 : 0, page.HeaderRestoreCalls,
                            "只有暖返回才把保存位置交给页头恢复边界");
                        if (restored) Assert.Equal(140d, page.HeaderRestoreTarget);
                        Assert.False(page.PlaybackEntranceSuppressed, "普通导航不改变播放返回与折叠抑制位");
                        Assert.Equal(restored ? 1 : 0, page.ViewModel.BackgroundReads);
                        Assert.Equal(restored ? 0 : 1, page.ViewModel.InitialReads);
                        // 生产页头在 RestoreVisit 与 Loaded 各同步一次标题墨色；夹具只要求这一趟确实同步过，
                        // 不把内部调用次数钉死（2026-11-14 页头拆到 HomePage.Header.cs 后是两处）。
                        Assert.True(page.TitlePaintCalls >= 1, "冷访问与暖返回都要同步轮播标题状态");
                        return Task.CompletedTask;
                    });
                }
            }
            test("主页货架入场：暖返回之后新冷访问重新入场", () =>
            {
                var page = new HomePage(new HomeRequest(null!, [], null));
                page.WireLoaded();
                page.Visit(NavigationMode.Back, restored: true);
                page.RaiseLoaded();
                page.Visit(NavigationMode.New, restored: false);
                page.RaiseLoaded();
                Assert.Equal(1, page.EnterCalls);
                return Task.CompletedTask;
            });
            test("主页货架入场：播放返回继续保持静态", () =>
            {
                var page = new HomePage(new HomeRequest(null!, [], null));
                page.WireLoaded();
                page.Visit(NavigationMode.New, restored: false);
                page.SuppressPlaybackEntrance();
                page.RaiseLoaded();
                Assert.Equal(0, page.EnterCalls);
                return Task.CompletedTask;
            });
        }
    }
}

namespace Momoka.Shell.Views
{
    internal static class HomeMotion
    {
        internal static bool AnimationsEnabled { get; set; } = true;
        internal static void Enter(HomePage page, object repeater, int count) => page.EnterCalls++;
    }

    internal sealed partial class HomePage
    {
        private bool _shelfEntranceSuppressed;
        private bool _foldScheduled;
        private bool _overlayAgain;
        private HomeRequest? _request;
        private Momoka.Diagnostics.NavigationTimingRun? _timing;
        private Windowing.HostWindow? _window;
        private double? _restoredScroll;
        private event Action<object?, object?>? Loaded;
        private string Tag { get; set; } = "";
        private object ShelfRepeater { get; } = new();
        private List<object> HomeRows { get; } = [];
        internal EntranceState ViewModel { get; } = new();
        internal ScrollBoundary Scroller { get; } = new();
        internal int EnterCalls { get; set; }
        internal int TitlePaintCalls { get; private set; }
        internal int HeaderRestoreCalls { get; private set; }
        internal double HeaderRestoreTarget { get; private set; }
        // 恢复算法由 HomeFoldCompletionTests 逐字编译 HomePage.Header.cs 验证。
        // 本目录／账号夹具只记录正式 Loaded 调用交出的目标，并用惯性滚动端点接住。
        private void BeginHeaderScrollRestore(double offset)
        {
            HeaderRestoreCalls++;
            HeaderRestoreTarget = offset;
            Scroller.ScrollTo(0, offset,
                new Microsoft.UI.Xaml.Controls.ScrollingScrollOptions(Microsoft.UI.Xaml.Controls.ScrollingAnimationMode.Disabled));
        }
        private void PaintInk() => TitlePaintCalls++;
        internal bool PlaybackEntranceSuppressed => _entranceSuppressed;
        internal void Visit(NavigationMode mode, bool restored)
        {
            ViewModel.Restored = restored;
            var args = new NavigationEventArgs(mode);
            BeginVisit(Request, args);
            if (_foldScheduled || _overlayAgain) throw new InvalidOperationException("新访问不能沿用旧布局欠账");
            RestoreVisit(Request, args, reused: false);
        }
        internal void SuppressPlaybackEntrance() => _entranceSuppressed = true;
        internal void RaiseLoaded() => Loaded?.Invoke(this, null);
        private void UpdateLibraryOverlay() { }
    }

    internal sealed class EntranceState
    {
        internal bool Restored { get; set; }
        internal int InitialReads { get; private set; }
        internal int BackgroundReads { get; private set; }
        internal bool RestoreSnapshot() => Restored;
        internal Task BackgroundRefreshAsync() { BackgroundReads++; return Task.CompletedTask; }
        internal Task ReloadAsync() { InitialReads++; return Task.CompletedTask; }
    }

    internal sealed class ScrollBoundary
    {
        internal double Offset { get; private set; }
        internal void UpdateLayout() { }
        // 生产页头恢复用 ScrollView.ScrollTo 的关联号判断是否受理；桩给出一个非 -1 的受理号。
        internal int ScrollTo(double x, double y, Microsoft.UI.Xaml.Controls.ScrollingScrollOptions options)
        {
            Offset = y;
            return 1;
        }
    }
}

namespace Microsoft.UI.Xaml.Controls
{
    public enum ScrollingAnimationMode { Disabled }
    public sealed class ScrollingScrollOptions(ScrollingAnimationMode mode) { public ScrollingAnimationMode Mode { get; } = mode; }
}
