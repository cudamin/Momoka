using System.Collections.Concurrent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Momoka.Shell.Media;
using Momoka.Shell.Views;
using Windows.UI.ViewManagement;
using static Momoka.Tests.TestHarness;

namespace Momoka.Tests;

internal static class AnimatedPosterTests
{
    public static void Register()
    {
        Test("原生帧预算耗尽时等待，取消不占容量，释放后继续且可重复释放", () => BudgetAsync().GetAwaiter().GetResult());

        static async Task BudgetAsync()
        {
            var budget = new AnimationSurfaceBudget(16);
            var first = await budget.ReserveAsync(16, default);
            using var cancelled = new CancellationTokenSource();
            var abandoned = budget.ReserveAsync(1, cancelled.Token);
            var next = budget.ReserveAsync(8, default);
            Assert.False(next.IsCompleted);
            cancelled.Cancel();
            try { await abandoned; Assert.True(false, "预算等待应取消"); }
            catch (OperationCanceledException) { }
            first.Dispose();
            first.Dispose();
            using var second = await next.WaitAsync(TimeSpan.FromSeconds(1));
            using var third = await budget.ReserveAsync(8, default).WaitAsync(TimeSpan.FromSeconds(1));
            using var leaving = new CancellationTokenSource();
            var full = budget.ReserveAsync(1, leaving.Token);
            Assert.False(full.IsCompleted, "重复释放不能扩大容量");
            leaving.Cancel();
            try { await full; Assert.True(false, "预算等待应取消"); }
            catch (OperationCanceledException) { }
        }
        Test("网格播放槽满时悬浮封面和徽标仍能播放", () =>
        {
            using var ui = new UiPump();
            PosterCache.Clear();
            AnimatedPngFrames.Decode = (_, _) => Task.FromResult<AnimatedPngFrames?>(Fixture.Frames);
            var images = new List<Image>();
            try
            {
                for (var index = 0; index < 26; index++)
                {
                    var image = new Image();
                    images.Add(image);
                    image.Load();
                    _ = new AnimatedPoster(image, preview: index >= 24);
                    var source = new BitmapImage();
                    PosterCache.TrackAnimation(source, [2]);
                    image.Source = source;
                    image.SetViewport(new(0, 0, 100, 100));
                }
                ui.Until(() => images.All(image => image.Source is WriteableBitmap), "网格不能占住预览的两个播放槽");
            }
            finally
            {
                foreach (var image in images) image.Unload();
                // Cancellation is asynchronous; don't leave occupied slots for the following case.
                ui.Until(() => HomeMotion.Subscriptions == 0 && Available("ActiveSlots") == 24 && Available("PreviewSlots") == 2,
                    "离树未释放监听或播放槽");
                PosterCache.Clear();
            }

            static int Available(string name) => ((SemaphoreSlim)typeof(AnimatedPoster)
                .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null)!).CurrentCount;
        });
        Test("原生尺寸位图缓存按像素预算淘汰，GIF与APNG均识别为动图", () =>
        {
            PosterCache.Clear();
            for (var index = 0; index < 5; index++)
                PosterCache.Remember($"native-{index}", new BitmapImage { PixelWidth = 2048, PixelHeight = 2048 });
            Assert.False(PosterCache.TryGet("native-0", out _));
            Assert.True(PosterCache.TryGet("native-4", out _));
            Assert.True(PosterCache.IsAnimated(new BitmapImage { IsAnimatedBitmap = true }));
            var apng = new BitmapImage();
            PosterCache.TrackAnimation(apng, [2]);
            Assert.True(PosterCache.IsAnimated(apng));
            Assert.False(PosterCache.IsAnimated(new BitmapImage()));
            PosterCache.Clear();
        });
        Test("悬浮图无需淡入包装也按原生尺寸播放，同图身份不随帧改变", () =>
        {
            using var fixture = new Fixture(withArrival: false);
            AnimatedPngFrames.Decode = (_, _) => Task.FromResult<AnimatedPngFrames?>(new(1200, 8, 0,
                [new(new byte[1200 * 8 * 4], TimeSpan.FromMilliseconds(40)),
                 new(new byte[1200 * 8 * 4], TimeSpan.FromMilliseconds(40))]));
            var original = fixture.Original();
            original.DecodePixelWidth = 100;
            fixture.Image.XamlRoot!.RasterizationScale = 1.5;
            fixture.Image.Source = original;
            fixture.InView();
            var surface = fixture.Surface();
            Assert.Equal((1200, 8), (surface.NativeWidth, surface.NativeHeight));
            Assert.True(ReferenceEquals(original, fixture.Animation.OriginalSource));
            fixture.Ui.Until(() => surface.PaintedPixels.Count >= 2, "悬浮图没有更新第二帧");
            Assert.Equal(0, Storyboard.Created.Count);
            fixture.Image.Source = null; // Popup Reset 清源也必须立即取消。
            Assert.Null(fixture.Animation.OriginalSource);
            Assert.Equal(0, HomeMotion.Subscriptions);
        });
        Test("逐帧解码期间关闭预览取消旧帧，迟到像素不回填", () =>
        {
            using var fixture = new Fixture(withArrival: false);
            var pending = new TaskCompletionSource<AnimatedPngFrame?>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken readToken = default;
            AnimatedPngFrames.Decode = (_, _) => Task.FromResult<AnimatedPngFrames?>(Fixture.Frames with
            {
                Read = (index, token) =>
                {
                    if (index == 0) return Task.FromResult<AnimatedPngFrame?>(Fixture.Frames.Frames[0]);
                    readToken = token;
                    return pending.Task;
                }
            });
            fixture.Image.Source = fixture.Original();
            fixture.InView();
            var surface = fixture.Surface();
            fixture.Ui.Until(() => readToken.CanBeCanceled, "第二帧没有进入解码");
            fixture.Image.Source = null;
            Assert.True(readToken.IsCancellationRequested);
            var handled = fixture.Ui.Handled;
            pending.SetResult(Fixture.Frames.Frames[1]);
            fixture.Ui.Until(() => fixture.Ui.Handled > handled, "迟到帧没有返回");
            Assert.Null(fixture.Image.Source);
            Assert.Equal(1, surface.PaintedPixels.Count);
        });
        Test("有限循环停在最后一帧，解码失败恢复原图", () =>
        {
            using var fixture = new Fixture(withArrival: false);
            AnimatedPngFrames.Decode = (_, _) => Task.FromResult<AnimatedPngFrames?>(Fixture.Frames with { Plays = 1 });
            fixture.Image.Source = fixture.Original();
            fixture.InView();
            var surface = fixture.Surface();
            fixture.Ui.Until(() => surface.PaintedPixels.Count == 2, "有限循环没有完成");
            Assert.Equal((byte)22, surface.PaintedPixels[^1]);

            var reads = 0;
            AnimatedPngFrames.Decode = (_, _) => Task.FromResult<AnimatedPngFrames?>(Fixture.Frames with
            {
                Read = (_, _) =>
                {
                    reads++;
                    return Task.FromResult<AnimatedPngFrame?>(null);
                }
            });
            var failed = fixture.Original();
            fixture.Image.Source = failed;
            fixture.Ui.Until(() => reads > 0, "失败帧未实际执行");
            Assert.True(ReferenceEquals(failed, fixture.Image.Source));
        });
        Test("静态图和折叠分支保留原始封面且不执行动画解码", () =>
        {
            using var fixture = new Fixture();
            var visits = 0;
            AnimatedPngFrames.Decode = (_, _) =>
            {
                Interlocked.Increment(ref visits);
                return Task.FromResult<AnimatedPngFrames?>(Fixture.Frames);
            };
            var still = new BitmapImage();
            fixture.Image.Source = still;
            fixture.InView();
            Assert.True(ReferenceEquals(still, fixture.Image.Source));
            Assert.Equal(1, HomeMotion.Subscriptions, "仅普通淡入订阅偏好");
            Storyboard.Created.Single().Finish();

            var parent = new FrameworkElement { Visibility = Visibility.Collapsed };
            fixture.Image.Parent = parent;
            fixture.Image.Source = fixture.Original();
            fixture.InView();
            Assert.True(fixture.Image.Source is BitmapImage);
            Assert.Equal(0, Volatile.Read(ref visits));
            Assert.Equal(1, HomeMotion.Subscriptions, "折叠分支只保留原有淡入订阅");
        });
        Test("可见 APNG 按顺序更新同一画面，进出视口不重复淡入", () =>
        {
            using var fixture = new Fixture();
            var original = fixture.Original();
            fixture.Image.Source = original;
            var fade = Storyboard.Created.Single();
            fixture.InView();
            var bitmap = fixture.Surface();
            fixture.Ui.Until(() => bitmap.PaintedPixels.Count >= 2, "第二帧没有更新像素");
            Assert.Equal((byte)11, bitmap.PaintedPixels[0]);
            Assert.Equal((byte)22, bitmap.PaintedPixels[1]);
            Assert.True(ReferenceEquals(bitmap, fixture.Image.Source));
            Assert.Equal(1, Storyboard.Created.Count);
            Assert.True(fade.Running);
            fade.Finish();
            Assert.Equal(1d, fixture.Image.Opacity);

            fixture.Image.SetViewport(new(30, 30, 0, 0));
            Assert.True(ReferenceEquals(original, fixture.Image.Source), "零尺寸视口位于图内也没有可见面积");
            var painted = bitmap.PaintedPixels.Count;
            fixture.InView();
            var next = fixture.Surface();
            Assert.False(ReferenceEquals(bitmap, next));
            Assert.Equal(painted, bitmap.PaintedPixels.Count);
            Assert.Equal(1, Storyboard.Created.Count);
        });
        Test("换图拒绝已取消的旧解码，活动帧也不能覆盖更新绑定", () =>
        {
            using var fixture = new Fixture();
            var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var oldDecode = new TaskCompletionSource<AnimatedPngFrames?>(TaskCreationOptions.RunContinuationsAsynchronously);
            AnimatedPngFrames.Decode = (bytes, token) =>
            {
                if (bytes[0] != 1) return Task.FromResult<AnimatedPngFrames?>(Fixture.Frames);
                started.SetResult(token);
                return oldDecode.Task; // 模拟 WinRT 已启动、无法及时取消的解码结果。
            };

            fixture.Image.Source = fixture.Original(1);
            fixture.InView();
            fixture.Ui.Until(() => started.Task.IsCompletedSuccessfully, "旧图没有进入解码");
            var still = new BitmapImage();
            fixture.Image.Source = still;
            Assert.True(started.Task.Result.IsCancellationRequested);
            var handled = fixture.Ui.Handled;
            oldDecode.SetResult(Fixture.Frames);
            fixture.Ui.Until(() => fixture.Ui.Handled > handled, "旧解码回调没有返回到 UI");
            Assert.True(ReferenceEquals(still, fixture.Image.Source));

            fixture.Image.Source = fixture.Original(2);
            var surface = fixture.Surface();
            fixture.Image.Source = still;
            fixture.OutOfView();
            Assert.True(ReferenceEquals(still, fixture.Image.Source), "停止旧画面时没有覆盖后来绑定的静图");
            Assert.Equal((byte)11, surface.PaintedPixels[0]);
        });
        Test("回收幻影保留播放，真正离树停止并在挂回后恢复", () =>
        {
            using var fixture = new Fixture();
            var original = fixture.Original();
            fixture.Image.Source = original;
            fixture.InView();
            var first = fixture.Surface();
            fixture.Image.Unload(phantom: true);
            Assert.True(ReferenceEquals(first, fixture.Image.Source));
            fixture.Ui.Until(() => first.PaintedPixels.Count >= 2, "回收幻影不应停止播放");
            fixture.Image.Unload();
            Assert.True(ReferenceEquals(original, fixture.Image.Source));
            Assert.Equal(0, HomeMotion.Subscriptions);

            fixture.Image.Load();
            fixture.InView();
            Assert.False(ReferenceEquals(first, fixture.Surface()));
            Assert.Equal(1, Storyboard.Created.Count);
        });
        Test("系统和应用关闭动画时原地退回静图，再开启可恢复", () =>
        {
            using var fixture = new Fixture();
            var original = fixture.Original();
            fixture.Image.Source = original;
            fixture.InView();
            var first = fixture.Surface();
            // Each actual UISettings instance receives the system event.
            var settings = UISettings.Created.ToArray();
            foreach (var system in settings) system.Change(false);
            Assert.True(ReferenceEquals(original, fixture.Image.Source));
            Assert.False(fixture.Fade.IsRunning);
            foreach (var system in settings) system.Change(true);
            Assert.False(ReferenceEquals(first, fixture.Surface()));

            HomeMotion.Change(false);
            Assert.True(ReferenceEquals(original, fixture.Image.Source));
            HomeMotion.Change(true);
            _ = fixture.Surface();
            Assert.Equal(1, Storyboard.Created.Count);
        });
        Test("窗口最小化停止帧更新，恢复时不依赖重新滚动", () =>
        {
            using var fixture = new Fixture();
            var original = fixture.Original();
            fixture.Image.Source = original;
            fixture.InView();
            var first = fixture.Surface();
            var window = fixture.Image.XamlRoot!;
            Assert.Equal(1, window.Subscriptions);

            window.SetHostVisible(false);
            Assert.True(ReferenceEquals(original, fixture.Image.Source));
            var painted = first.PaintedPixels.Count;
            window.SetHostVisible(true);
            Assert.False(ReferenceEquals(first, fixture.Surface()));
            Assert.Equal(painted, first.PaintedPixels.Count);
            Assert.Equal(1, Storyboard.Created.Count);

            fixture.Image.Unload();
            Assert.Equal(0, window.Subscriptions);
            window.SetHostVisible(true);
            Assert.True(ReferenceEquals(original, fixture.Image.Source));
        });
        Test("动图缓存逐出后重新取原图，静态缓存仍命中", () =>
        {
            using var fixture = new Fixture();
            var staticPoster = new BitmapImage();
            var animatedPoster = fixture.Original();
            PosterCache.Remember("static-poster", staticPoster);
            PosterCache.Remember("animated-poster", animatedPoster);
            Assert.True(PosterCache.TryGet("animated-poster", out var cached) && ReferenceEquals(animatedPoster, cached));

            // The source budget counts bytes per entry, even when this fixture shares a buffer.
            var largeSource = new byte[24 * 1024 * 1024];
            for (int i = 0; i < 3; i++)
                PosterCache.TrackAnimation(new BitmapImage(), largeSource);

            Assert.False(PosterCache.TryGet("animated-poster", out _));
            Assert.True(PosterCache.IsAnimated(animatedPoster), "原料逐出不改变APNG身份");
            Assert.True(PosterCache.TryGet("static-poster", out cached) && ReferenceEquals(staticPoster, cached));
            var reloaded = fixture.Original();
            PosterCache.Remember("animated-poster", reloaded);
            Assert.True(PosterCache.TryGet("animated-poster", out cached) && ReferenceEquals(reloaded, cached));
            fixture.Image.Source = cached;
            fixture.InView();
            _ = fixture.Surface();
        });
    }

    private sealed class Fixture : IDisposable
    {
        internal static readonly AnimatedPngFrames Frames = new(1, 1, 0,
            [new([11, 0, 0, 255], TimeSpan.FromMilliseconds(40)), new([22, 0, 0, 255], TimeSpan.FromMilliseconds(40))]);

        internal Fixture(bool withArrival = true)
        {
            Assert.Equal(0, HomeMotion.Subscriptions);
            Storyboard.Created.Clear();
            PosterCache.Clear();
            AnimatedPngFrames.Decode = (_, _) => Task.FromResult<AnimatedPngFrames?>(Frames);
            Image.Load();
            Fade = new(Image);
            if (!withArrival) Fade.Dispose();
            Animation = new AnimatedPoster(Image, withArrival ? Fade : null);
        }

        internal UiPump Ui { get; } = new();
        internal Image Image { get; } = new();
        internal ImageArrivalFade Fade { get; }
        internal AnimatedPoster Animation { get; }

        internal void InView() => Image.SetViewport(new(0, 0, 100, 100));
        internal void OutOfView() => Image.SetViewport(new(200, 0, 100, 100));

        internal BitmapImage Original(byte id = 2)
        {
            var original = new BitmapImage();
            PosterCache.TrackAnimation(original, [id]);
            return original;
        }

        internal WriteableBitmap Surface()
        {
            Ui.Until(() => Image.Source is WriteableBitmap, "可见封面没有开始播放");
            return (WriteableBitmap)Image.Source!;
        }

        public void Dispose()
        {
            Image.Unload();
            Fade.Dispose();
            UISettings.Enabled = true;
            HomeMotion.PreferenceEnabled = true;
            PosterCache.Clear();
            Ui.Dispose();
        }
    }

    // The fixture pumps real async continuations on one thread, like the WinUI dispatcher.
    // Only view/layout events and decoded pixels are supplied by the test.
    private sealed class UiPump : SynchronizationContext, IDisposable
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = [];
        private readonly SynchronizationContext? _original = Current;

        internal UiPump() => SetSynchronizationContext(this);

        internal int Handled { get; private set; }

        public override void Post(SendOrPostCallback callback, object? state) => _pending.Enqueue((callback, state));

        internal void Until(Func<bool> ready, string message)
        {
            Assert.True(SpinWait.SpinUntil(() =>
            {
                while (_pending.TryDequeue(out var work))
                {
                    work.Callback(work.State);
                    Handled++;
                }
                return ready();
            }, TimeSpan.FromSeconds(3)), message);
        }

        public void Dispose() => SetSynchronizationContext(_original);
    }
}
