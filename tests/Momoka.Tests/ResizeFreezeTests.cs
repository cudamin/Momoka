using Momoka.Playback;

namespace Momoka.Tests;

/// <summary>
/// 「拖窗口边沿改大小时自动暂停 mpv，松手接着放」这条判据的两半：纯函数本身，以及它那几处接线还在不在。
/// （用户令 2026-09-23；2026-11-08 用户令删掉「调整窗口大小后继续播放」那一格、固定为开，松手这一半从此没有
/// 第二个答案。）
/// <para>
/// 这一条在屏上的样子是「拖的时候画面停住、松手接着放」，而它坏掉的样子一个都不显眼：把用户自己按下的暂停
/// 当成我们的而「恢复」掉、在浏览页拖窗口时凭空暂停一次、或者拖动结束后播放再也没回来（一笔没人还的账）。
/// 前两件纯函数就能钉死（这正是 <see cref="ResizeFreeze"/> 住在 Core 的理由），第三件是源码守卫 ——
/// 「冻结那笔账有没有人放开」没有任何编译器会说话，删掉一行照样全绿。
/// </para>
/// </summary>
internal static class ResizeFreezeTests
{
    internal static void Register()
    {
        // 开始那一问：三问缺一不可，而第一问是「不许碰用户自己按的暂停」。
        TestHarness.Test("拖边冻结：用户自己暂停着就一根手指都不碰", () =>
        {
            Assert.False(ResizeFreeze.Freezes(userPaused: true, onStage: true, pictureInHostWindow: true),
                "他按的暂停不是我们的，不许拿它当「本来在放」");
            Assert.True(ResizeFreeze.Freezes(false, true, true), "集成模式、在台上、本来放着 —— 这就是要冻的那一趟");
        });

        // 窗口不是我们的那两条：浏览页拖窗口（没在台上）、独占模式（画面在 mpv 自己的窗里）都不该暂停。
        TestHarness.Test("拖边冻结：浏览中与独占模式都不冻", () =>
        {
            Assert.False(ResizeFreeze.Freezes(false, onStage: false, pictureInHostWindow: true), "没在播放页上");
            Assert.False(ResizeFreeze.Freezes(false, onStage: false, pictureInHostWindow: false), "既没进场也不在宿主里");
            Assert.False(ResizeFreeze.Freezes(false, onStage: true, pictureInHostWindow: false),
                "独占模式：拖的是 mpv 那扇窗，暂停 mpv 不是这条令要的");
        });

        // 松手那一半：2026-11-08 起固定接着放，设置里不再有那一格。守住的是「放开只对真的冻过的那一趟」——
        // 挪窗（尺寸没变、没冻过）与用户自己按的暂停都不许被一次收尾解开；那两条路径各由页面开头的
        // `if (!_resizePaused) return;` 与 PlayerInputRecoveryTests 的「原本暂停不发恢复」钉着。
        TestHarness.Test("拖边放开：固定接着放，设置里不再有这一档", () =>
        {
            var shell = Source("src", "Momoka.Shell");
            var page = shell[Path.Combine("Views", "PlayerPage.ClientRect.cs")];
            Assert.Contains("if (!_resizePaused) return;", page);
            Assert.Contains("var resume = _freezePauseAccepted;", page);
            Assert.DoesNotContain("ResizeFreeze.Resumes", page);

            foreach (var file in new[] { "SettingsViewModel.cs", "PlayerViewModel.cs" })
                Assert.DoesNotContain("ResumeAfterWindowResize",
                    shell[Path.Combine("ViewModels", file)], "设置行已删，Shell 里不该再有这一档的读法");
        });

        // 源码守卫：四条接线少任何一条，屏上的症状分别是「拖完再也不动」「拖动时不停」「挪窗把暂停解开」
        // 「暂停指令失败了也照样放开」，而它们各自都能在测试全绿的情况下发生。
        TestHarness.Test("拖边冻结的四条接线都在", () =>
        {
            var shell = Source("src", "Momoka.Shell");
            var page = shell[Path.Combine("Views", "PlayerPage.ClientRect.cs")];

            Assert.Contains("ResizeFreeze.Freezes(", page);
            Assert.Contains("if (_resizeLoop) BeginResizeFreeze();", page);

            // 冻结的触发点必须是「客户区真的报出新尺寸」那一拍：WM_ENTERSIZEMOVE 拖标题移动窗口时也会来。
            Assert.Contains("_resizeLoop = true;", page);
            Assert.Contains("_resizeLoop = false;", page);

            // 那笔账的放行口：拖边收尾与取消。
            Assert.Contains("ReleaseResizeFreezeAsync", page);

            // 发「暂停／恢复」的唯一出口，以及它的默认分支。探针在离线跑的时候把出口接到自己那个句柄上
            // （它绕过 PlaybackService，`_current` 是空的），所以「没接出口时走固定文件暂停命令」
            // 这一行没有任何运行路径会踩到 —— 只能靠这里钉住，它是这条令在真实播放里唯一的落点。
            Assert.Contains("internal Action<bool>? PauseRequested { get; set; }", page);
            Assert.Contains("ViewModel.SetFrozenPausedAsync(_resizeFreezeContext, paused);", page);
            Assert.Contains("SetPaused(true);", page);
            Assert.Contains("SetPaused(false);", page);
        });
    }

    /// <summary>仓库根（<c>Momoka.sln</c> 所在）下按子目录拼出来的那些源码文件，按相对路径索引。</summary>
    private static Dictionary<string, string> Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Momoka.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var folder = Path.Combine([directory!.FullName, .. parts]);
        Assert.True(Directory.Exists(folder), $"找不到源码目录 {folder}");

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            files[Path.GetRelativePath(folder, file)] = File.ReadAllText(file);
        return files;
    }
}
