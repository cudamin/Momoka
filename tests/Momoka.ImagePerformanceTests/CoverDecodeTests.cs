using System.Diagnostics;
using Momoka.Playback;
using Momoka.Shell.ViewModels;
using static Momoka.Tests.Program;

namespace Momoka.Tests;

internal static class CoverDecodeTests
{
    internal static void Register()
    {
        foreach (var (width, height) in new[] { (1280, 720), (3840, 2160), (7680, 4320), (2048, 2048), (1, 40000), (40000, 1), (640, 2000) })
        {
            Test($"实际WinRT：{width}×{height}客户端宽/高/总像素硬预算与烘色", async () =>
            {
                var png = ImageData.Png(width, height);
                var clock = Stopwatch.StartNew();
                var frame = await CoverBackdropImage.DecodeAsync(png, PlayerPalette.Film).WaitAsync(TimeSpan.FromSeconds(30));
                Assert.NotNull(frame);
                var expected = CoverBackdropSize.Fit((uint)width, (uint)height);
                Assert.Equal(expected.Width, frame!.Width);
                Assert.Equal(expected.Height, frame.Height);
                Assert.True(frame.Width <= 1280 && frame.Height <= 1280 && (long)frame.Width * frame.Height <= 921600);
                Assert.Equal(frame.Width * frame.Height * 4, frame.Pixels.Length);
                Assert.Equal(frame.Width * 4, frame.Stride);
                Assert.Equal((byte)0xFF, frame.Pixels[3]);
                // RGB(100,80,60) baked against Film RGB(12,14,17), opacity .5.
                Assert.True(Math.Abs(frame.Pixels[0] - 38) <= 1);
                Assert.True(Math.Abs(frame.Pixels[1] - 47) <= 1);
                Assert.True(Math.Abs(frame.Pixels[2] - 56) <= 1);
                Measurements.Add(new { kind = "actual-WinRT-bounded-cover", width, height, inputBytes = png.Length,
                    outputWidth = frame.Width, outputHeight = frame.Height, retainedFrameBytes = frame.Pixels.Length, elapsedMs = clock.Elapsed.TotalMilliseconds });
            });
        }

        Test("遮罩尺寸：uint极值/零值/万种比例不溢出且不放大", () =>
        {
            Assert.Equal(default, CoverBackdropSize.Fit(0, 1));
            Assert.Equal(default, CoverBackdropSize.Fit(1, 0));
            Assert.Equal(new CoverBackdropSize(64, 32), CoverBackdropSize.Fit(64, 32));
            var random = new Random(4);
            for (var i = 0; i < 10000; i++)
            {
                var width = i == 0 ? uint.MaxValue : (uint)random.NextInt64(1, (long)uint.MaxValue + 1);
                var height = i == 0 ? uint.MaxValue : (uint)random.NextInt64(1, (long)uint.MaxValue + 1);
                var fit = CoverBackdropSize.Fit(width, height);
                Assert.True(fit.Width is >= 1 and <= 1280 && fit.Height is >= 1 and <= 1280);
                Assert.True((long)fit.Width * fit.Height <= 921600 && fit.Width <= width && fit.Height <= height);
            }
            return Task.CompletedTask;
        });

        Test("实际WinRT：预取消与在途取消不伪装空图", async () =>
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Cancelled(CoverBackdropImage.DecodeAsync([1], PlayerPalette.Film, cancelled.Token));
            var png = ImageData.Png(7680, 4320);
            using var leaving = new CancellationTokenSource();
            var decoding = CoverBackdropImage.DecodeAsync(png, PlayerPalette.Film, leaving.Token);
            leaving.Cancel();
            await Cancelled(decoding);
        });

        Test("实际WinRT：坏数据退空，不尝试无界第二份解码", async () =>
        {
            Assert.Null(await CoverBackdropImage.DecodeAsync([], PlayerPalette.Film));
            Assert.Null(await CoverBackdropImage.DecodeAsync([0, 1, 2, 3], PlayerPalette.Film));
        });

        Test("实际WinRT：CPU解码烘色不占调用消息循环，心跳持续响应", async () =>
        {
            using var ui = new TestUiContext();
            var png = ImageData.Png(7680, 4320);
            var heartbeats = 0;
            var peakDelay = 0d;
            var stop = Pending<bool>();
            var heartbeat = Task.Run(async () =>
            {
                while (!stop.Task.IsCompleted)
                {
                    var started = Stopwatch.GetTimestamp();
                    var tick = Pending<bool>();
                    ui.Post(_ => { peakDelay = Math.Max(peakDelay, Stopwatch.GetElapsedTime(started).TotalMilliseconds); heartbeats++; tick.TrySetResult(true); }, null);
                    await tick.Task.WaitAsync(Deadline);
                    await Task.Delay(1);
                }
            });
            try
            {
                await ui.Run(async () =>
                {
                    Assert.Equal(ui.ThreadId, Environment.CurrentManagedThreadId);
                    for (var i = 0; i < 5; i++)
                    {
                        Assert.NotNull(await CoverBackdropImage.DecodeAsync(png, PlayerPalette.Film));
                        Assert.Equal(ui.ThreadId, Environment.CurrentManagedThreadId, "调用尾仍回消息循环");
                    }
                }).WaitAsync(TimeSpan.FromSeconds(30));
            }
            finally { stop.TrySetResult(true); await heartbeat.WaitAsync(Deadline); }
            Assert.True(heartbeats >= 3, "解码期间心跳继续而非只在结束后一次");
            Assert.True(peakDelay < 1000, "仅是宽松不挂死门槛，不冒充实际WinUI帧时限");
            Measurements.Add(new { kind = "actual-WinRT-managed-queue", inputWidth = 7680, inputHeight = 4320,
                samples = 5, processedHeartbeats = heartbeats, heartbeatMaxDispatchDelayMs = peakDelay, uiThread = ui.ThreadId });
        });
    }
}
