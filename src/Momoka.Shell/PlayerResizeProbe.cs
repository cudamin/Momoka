using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Momoka.Configuration;
using Momoka.Playback;
using Momoka.Shell.Composition;
using Momoka.Shell.Interop;
using Momoka.Shell.Views;
using Momoka.Shell.Windowing;

namespace Momoka.Shell;

/// <summary>Local integrated resize acceptance, sampled independently of the UI/composition submission.</summary>
internal static class PlayerResizeProbe
{
    internal static async Task RunAsync(StartupOptions options)
    {
        Directory.CreateDirectory(options.Paths.LogDirectory);
        using var report = new StreamWriter(Path.Combine(options.Paths.LogDirectory, "resize-probe.txt")) { AutoFlush = true };
        using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = stopping.Token;
        var media = CompositionPlaybackProbe.LocalFile(options.ProbePlayerMotionFile);
        await using var services = ShellServices.Build(options.Paths);
        services.GetRequiredService<AppSettings>().Mpv.Pipeline = VideoPipelineKind.Integrated;
        var shell = services.GetRequiredService<ShellPage>();
        shell.Attach(services);
        using var window = new HostWindow { Content = shell };
        window.Closed += stopping.Cancel;
        IPlaybackHandle? handle = null;
        try
        {
            window.Show(false, options.Screen, new WindowBounds(120, 120, 1000, 650));
            window.TopMost = true;
            shell.AttachWindow(window);
            var page = shell.PlayerRoot;
            var surface = (CompositionVideoTarget)page.VideoSurface!;
            page.ViewModel.Title = "集成缩放离线验收";
            page.ViewModel.CoverUp = true;
            page.BeginMotionProbe(false);
            page.ProbeSourceAspect(16d / 9);
            page.ProbePictureAspect(16d / 9);
            var backend = new LibMpvBackend(new MpvSettings { Pipeline = VideoPipelineKind.Integrated },
                new PlaybackSettings(), () => surface);
            handle = await backend.StartAsync(new PlaybackRequest
            {
                MediaUrl = media,
                Title = "集成缩放离线验收",
                SubtitlesDisabled = true,
                PlayerOptions = [new("mute", "yes"), new("loop-file", "inf"), new("audio-file-auto", "no"),
                    new("sub-auto", "no"), new("access-references", "no"), new("load-unsafe-playlists", "no"),
                    new("demuxer-lavf-o", "protocol_whitelist=file")]
            }, token);
            var control = (IPlayerControl)handle;
            await Until(() => control.Status.PictureStarted && surface.IsContentReady, "首帧和缓冲就绪");
            page.ProbePictureStarted();
            page.ViewModel.CoverUp = false;
            await Until(() => page.MotionProbeState.CoverHidden, "加载层退场");
            var monitor = VideoFrameOverlay.FullscreenRect(window.Handle);
            Native.SetWindowPos(window.Handle, IntPtr.Zero, monitor.Left + 32, monitor.Top + 120, 920, 530,
                Native.SwpNoZOrder | Native.SwpNoActivate);
            page.ProbePictureAspect(16d / 9);
            await Until(() => surface.IsContentReady, "验收窗口位于目标屏幕内");
            Task pauseWrite = Task.CompletedTask;
            page.PauseRequested = paused => pauseWrite = handle.SetPropertyAsync("pause", paused, token);
            Write($"PID={Environment.ProcessId} HWND=0x{window.Handle:X} exe={Environment.ProcessPath}");
            Write("范围：离线本地素材、集成libmpv、真实播放页；不登录、不上报；后台取屏不等待UI提交。");
            Native.GetWindowRect(window.Handle, out var start);
            var client = window.ClientSize;
            var frameWidth = start.Width - client.Width;
            var frameHeight = start.Height - client.Height;
            await CompositionPlaybackProbe.SaveScreenAsync(window, Path.Combine(options.Paths.LogDirectory, "resize-before.png"));

            foreach (var edge in options.ProbeResizeManual ? new[] { ResizeEdge.None } : new[] { ResizeEdge.BottomRight, ResizeEdge.TopLeft })
            {
                using var sampling = CancellationTokenSource.CreateLinkedTokenSource(token);
                var samples = new List<(long At, int Width, int Height, bool Stable, int Errors, int Leg)>();
                var badFrames = new List<CompositionPlaybackProbe.ResizeScreenFrame>();
                var leg = 0;
                var windowChanges = 0;
                var nativeLoops = 0;
                void Changed(NativeRect bounds) => windowChanges++;
                void Loop(bool active) { if (active) nativeLoops++; }
                window.ClientSizeChanged += Changed;
                window.InteractiveResizeChanged += Loop;
                var clock = Stopwatch.StartNew();
                var hwnd = window.Handle;
                var sampler = Task.Run(async () =>
                {
                    while (!sampling.IsCancellationRequested)
                    {
                        var sample = CompositionPlaybackProbe.CaptureResizeFrame(hwnd, clock.ElapsedMilliseconds);
                        var errors = sample.Errors;
                        samples.Add((sample.At, sample.Width, sample.Height, sample.Stable, errors, Volatile.Read(ref leg)));
                        if (sample.Stable && errors > 0 && badFrames.Count < 4) badFrames.Add(sample);
                        await Task.Delay(4, CancellationToken.None);
                    }
                }, CancellationToken.None);
                try
                {
                    if (options.ProbeResizeManual)
                    {
                        Volatile.Write(ref leg, 1);
                        Write("人工拖边就绪：40秒内通过真实鼠标从左上角缩小、放大；持续后台采样。");
                        await Task.Delay(TimeSpan.FromSeconds(40), token);
                        Write($"人工输入证据：原生拖边事务={nativeLoops}，真实尺寸事件={windowChanges}");
                        if (nativeLoops < 2 || windowChanges < 4) throw new InvalidOperationException("真实拖边输入未覆盖往返");
                    }
                    else
                    {
                        foreach (var (from, to) in new[] { (1d, 0.64), (0.64, 1d), (1d, 0.7), (0.7, 1d) })
                        {
                            Interlocked.Increment(ref leg);
                            for (var step = 1; step <= 24; step++)
                            {
                                if (sampler.IsFaulted) await sampler;
                                var scale = from + (to - from) * step / 24;
                                var width = (int)Math.Round(client.Width * scale) + frameWidth;
                                var height = (int)Math.Round(client.Height * scale) + frameHeight;
                                var left = edge == ResizeEdge.TopLeft ? start.Right - width : start.Left;
                                var top = edge == ResizeEdge.TopLeft ? start.Bottom - height : start.Top;
                                Resize(window, new NativeRect { Left = left, Top = top, Right = left + width, Bottom = top + height }, edge);
                                await Task.Delay(16, token);
                            }
                        }
                        await pauseWrite;
                        if (!control.Status.Paused) throw new InvalidOperationException("拖动期间原生播放器没有暂停");
                    }
                    Volatile.Write(ref leg, 0);
                    Native.SendMessage(hwnd, Native.WmExitSizeMove, IntPtr.Zero, IntPtr.Zero);
                    await page.ResizeChange.WaitAsync(TimeSpan.FromSeconds(3), token);
                    await Until(() => surface.IsContentReady && !surface.IsInteractiveResize && !page.ResizeFrameVisible,
                        "松手收尾");
                    await Until(() => !control.Status.Paused, "松手恢复播放");
                    await pauseWrite;
                }
                finally
                {
                    window.ClientSizeChanged -= Changed;
                    window.InteractiveResizeChanged -= Loop;
                    sampling.Cancel();
                    try { await sampler; }
                    finally
                    {
                        foreach (var sample in badFrames)
                            await sample.SaveAsync(Path.Combine(options.Paths.LogDirectory, $"resize-{edge}-bad-{sample.At}.png"));
                        await File.WriteAllLinesAsync(Path.Combine(options.Paths.LogDirectory, $"resize-{edge}.csv"),
                            new[] { "at_ms,width,height,stable,errors,leg" }.Concat(samples.Select(sample =>
                                $"{sample.At},{sample.Width},{sample.Height},{sample.Stable},{sample.Errors},{sample.Leg}")), CancellationToken.None);
                    }
                }
                var stable = samples.Where(sample => sample.Stable).ToArray();
                var bad = stable.Where(sample => sample.Errors > 0).ToArray();
                Write($"{edge}: samples={samples.Count} stable={stable.Length} ambiguous={samples.Count - stable.Length} bad={bad.Length} elapsed={clock.ElapsedMilliseconds}ms");
                if (stable.Count(sample => sample.Leg > 0) < 24
                    || Enumerable.Range(1, options.ProbeResizeManual ? 1 : 4).Any(part => stable.Where(sample => sample.Leg == part)
                        .Select(sample => sample.Width).Distinct().Count() < 3))
                    throw new InvalidOperationException("连续缩放各方向有效变化帧不足");
                if (bad.Length > 0) throw new InvalidOperationException($"{edge} 连续缩放发现 {bad.Length} 帧露底或裁切");
            }
            await CompositionPlaybackProbe.SaveScreenAsync(window, Path.Combine(options.Paths.LogDirectory, "resize-after.png"));
            Write("通过：连续双向缩放中间采样无露底或裁切，松手恢复且保留层已撤回。");
        }
        catch (Exception error)
        {
            Write($"失败：{error}");
            throw;
        }
        finally
        {
            try
            {
                if (handle is not null)
                {
                    try { await handle.StopAsync(); }
                    finally { await handle.DisposeAsync(); }
                }
            }
            finally
            {
                try { await shell.ShutdownAsync(); }
                finally { window.Closed -= stopping.Cancel; }
            }
        }

        void Write(string text) => report.WriteLine($"{DateTimeOffset.Now:O} {text}");
        async Task Until(Func<bool> ready, string stage)
        {
            var clock = Stopwatch.StartNew();
            while (!ready())
            {
                if (clock.ElapsedMilliseconds > 6000) throw new TimeoutException(stage);
                await Task.Delay(16, token);
            }
            Write($"通过：{stage}");
        }
    }

    private static void Resize(HostWindow window, NativeRect proposed, ResizeEdge edge)
    {
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            Marshal.StructureToPtr(proposed, buffer, false);
            Native.SendMessage(window.Handle, Native.WmEnterSizeMove, IntPtr.Zero, IntPtr.Zero);
            Native.SendMessage(window.Handle, Native.WmSizing, new IntPtr((int)edge), buffer);
            var next = Marshal.PtrToStructure<NativeRect>(buffer);
            if (!Native.SetWindowPos(window.Handle, IntPtr.Zero, next.Left, next.Top, next.Width, next.Height,
                    Native.SwpNoZOrder | Native.SwpNoActivate)) throw new InvalidOperationException("原生拖边失败");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
