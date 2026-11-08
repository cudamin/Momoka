namespace Momoka.PlayerExitHandoffTests;

/// <summary>
/// 逐字执行 <c>PlayerPage.ReturnToBrowseBeforeStopAsync</c> 与它调用的 <c>FadeExitAsync</c>，只替身窗口、抓帧、
/// 覆盖层、时钟与 DWM 边界。
/// <para>
/// <b>2026-11-08 起要证的不止「覆盖层用对了矩形」，还有退场真的淡出去</b>：覆盖层先铺原客户区、再改铺新客户区、
/// 然后 alpha 由 255 单调走到 0，最后只释放一次。改动前那条写法在第一条 Show 之后两帧就 Dispose —— 下面四条
/// 一条都过不了。
/// </para>
/// </summary>
internal static class Program
{
    private const uint Stage = 0xFF0C0E11;

    private static async Task<int> Main()
    {
        var failures = 0;
        foreach (var bounds in new[]
        {
            new NativeRect(200, 100, 1000, 550), new NativeRect(0, 0, 1920, 1080), new NativeRect(-1600, 80, -800, 530)
        })
        {
            failures += await RunAsync($"窗口化 {bounds.Width}x{bounds.Height}：改铺、淡出、撤层", async () =>
            {
                var page = Prepare(bounds);
                await page.ReturnToBrowseBeforeStopAsync();
                await page.ExitFade;

                var shows = VideoFrameOverlay.Receipts;
                Require(shows.Count >= 4, $"只画了 {shows.Count} 次，退场淡出没有跑起来");
                Require(shows[0].Bounds == bounds && shows[0].Topmost && shows[0].Alpha == 255 && shows[0].Surround == 0,
                    "第一层必须铺在移动宿主前的客户区，并且按改动前的不透明写法");
                Require(shows[1].Bounds == VideoFrameOverlay.Bounds && shows[1].Surround == Stage && shows[1].Alpha == 255,
                    "窗口还原之后要改铺到新客户区，并补上舞台色");

                var fade = shows.Skip(1).ToList();
                for (var index = 1; index < fade.Count; index++)
                {
                    Require(fade[index].Bounds == fade[0].Bounds && fade[index].Surround == Stage,
                        $"第 {index} 拍改了矩形或底色");
                    Require(fade[index].Alpha <= fade[index - 1].Alpha,
                        $"第 {index} 拍亮了回去：{fade[index - 1].Alpha} → {fade[index].Alpha}");
                }
                Require(fade.Count >= 8, $"淡出只有 {fade.Count} 拍，读起来还是硬切");
                Require(fade[^1].Alpha == 0, $"最后一拍应当是 0，实际 {fade[^1].Alpha}");
                var mid = fade.Skip(1).SkipLast(1).Count(show => show.Alpha is > 0 and < 255);
                Require(mid >= 3, $"中间档只有 {mid} 拍，不是逐拍淡");
                Require(VideoFrameOverlay.Disposals == 1, $"撤层 {VideoFrameOverlay.Disposals} 次");
                Require(page.FramesWaited == 2 && page.Left, "退场要等两帧再让后端停、并且真的离开了播放页");
                Require(page.FadeLog.Contains("退场淡出"), "淡出那一趟没有留下日志");
            });
        }

        failures += await RunAsync("浏览页迟迟不就绪：先按住，就绪了才淡", async () =>
        {
            var page = Prepare(new NativeRect(0, 0, 1200, 800), readyAfterTicks: PlayerPage.TicksFor(320));
            await page.ReturnToBrowseBeforeStopAsync();
            await page.ExitFade;
            // 第一档 alpha 要到第二拍才降下来（起手那拍仍是 255），所以「开始淡」比「等够」多一拍。
            Require(VideoFrameOverlay.FirstFadeWaits >= PlayerPage.TicksFor(320)
                && VideoFrameOverlay.FirstFadeWaits <= PlayerPage.TicksFor(320) + 2,
                $"就绪之前就开始淡了（等到第 {VideoFrameOverlay.FirstFadeWaits} 拍）");
            Require(VideoFrameOverlay.Receipts[^1].Alpha == 0, "等就绪之后仍要走完淡出");
        });

        failures += await RunAsync("浏览页一直不就绪：宽限到点照常淡", async () =>
        {
            var page = Prepare(new NativeRect(0, 0, 1200, 800), readyAfterTicks: int.MaxValue);
            await page.ReturnToBrowseBeforeStopAsync();
            await page.ExitFade;
            Require(VideoFrameOverlay.FirstFadeWaits >= PlayerPage.TicksFor(PlayerPage.ReadyCeiling)
                && VideoFrameOverlay.FirstFadeWaits <= PlayerPage.TicksFor(PlayerPage.ReadyCeiling) + 2,
                $"宽限没用满就放手了（等到第 {VideoFrameOverlay.FirstFadeWaits} 拍）");
            Require(VideoFrameOverlay.Receipts[^1].Alpha == 0, "宽限到点必须照常淡完，不能卡在退场里");
        });

        failures += await RunAsync("淡到一半换片：撤层，不再画", async () =>
        {
            var page = Prepare(new NativeRect(0, 0, 1200, 800), interruptAfter: 4);
            await page.ReturnToBrowseBeforeStopAsync();
            await page.ExitFade;
            var fade = VideoFrameOverlay.Receipts.Skip(1).ToList();
            Require(VideoFrameOverlay.Disposals == 1, "被打断也要撤层 —— 屏上不许留一层按着旧画面");
            Require(fade.Count <= 6 && fade[^1].Alpha > 0, $"打断之后还在往下画（{fade.Count} 拍，最后一档 {fade[^1].Alpha}）");
        });

        failures += await RunAsync("全屏退出不淡（用户 2026-11-08 只做窗口模式）", async () =>
        {
            var page = Prepare(new NativeRect(0, 0, 2560, 1440), fullscreen: true);
            await page.ReturnToBrowseBeforeStopAsync();
            await page.ExitFade;
            Require(VideoFrameOverlay.Receipts.Count == 1, $"全屏退出画了 {VideoFrameOverlay.Receipts.Count} 次，应当只有抓帧那一层");
            Require(VideoFrameOverlay.Receipts[0].Surround == 0 && VideoFrameOverlay.Disposals == 1,
                "全屏退出保持改动前的当拍收摊");
        });

        failures += await RunAsync("系统关了动画：当拍收摊", async () =>
        {
            var page = Prepare(new NativeRect(0, 0, 1200, 800), animations: false);
            await page.ReturnToBrowseBeforeStopAsync();
            await page.ExitFade;
            Require(VideoFrameOverlay.Receipts.Count == 1 && VideoFrameOverlay.Disposals == 1,
                "关掉系统动画之后不该再有任何淡出");
        });

        Console.WriteLine($"共 8 项，通过 {8 - failures}，失败 {failures}，跳过 0；原样生产方法，未运行 DWM/WinUI/播放。");
        return failures == 0 ? 0 : 1;
    }

    private static PlayerPage Prepare(NativeRect bounds, int readyAfterTicks = 0, int interruptAfter = 0,
        bool fullscreen = false, bool animations = true)
    {
        VideoFrameOverlay.Receipts.Clear();
        VideoFrameOverlay.Disposals = 0;
        VideoFrameOverlay.FirstFadeWaits = -1;
        VideoFrameOverlay.Bounds = bounds;
        PlayerPage.Reset(readyAfterTicks, interruptAfter, animations);
        return new PlayerPage(fullscreen);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task<int> RunAsync(string name, Func<Task> run)
    {
        try
        {
            await run();
            Console.WriteLine($"通过：{name}");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine($"{name}：{error.Message}");
            return 1;
        }
    }
}
