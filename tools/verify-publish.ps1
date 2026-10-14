[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishRoot,

    [switch]$SelfContained,

    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

function Require-File([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "发布验证失败：缺少 $Description：$Path"
    }
}

$root = (Resolve-Path -LiteralPath $PublishRoot).Path
Require-File (Join-Path $root 'Momoka.exe') '主程序'
Require-File (Join-Path $root 'Momoka.deps.json') '依赖清单'
Require-File (Join-Path $root 'Momoka.runtimeconfig.json') '运行时配置'
Require-File (Join-Path $root 'libmpv-2.dll') '内置 libmpv'
# libmpv-2.dll 的静态导入里唯一一个不属于 Windows 的 dll（2026-09-04 起来自仓库的 assets\mpv-runtime，
# 从前是从用户自己那套便携版 mpv 里现拷的）。缺了它 libmpv 连加载都失败，一帧都放不出来 —— 而四道闸门
# 里没有一关会真的起播，所以这一行是它唯一的守卫。
Require-File (Join-Path $root 'vulkan-1.dll') 'libmpv 依赖的 Vulkan loader'

# 许可正文必须随二进制走：libmpv-2.dll 是 LGPL（mpv 自身 LGPL-2.1-or-later，它静态链入的 FFmpeg 是
# LGPL-3.0，判定依据见 assets\mpv-runtime\NOTICE.txt），vulkan-1.dll 是 Apache-2.0，项目自身是 MIT，
# 三者都要求把许可文本连同二进制一起交给接收者。2026-10-14 之前发布包里一份许可文本都没有 ——
# libmpv-2.dll 就躺在 exe 旁边，许可正文却不在，这是发行上的缺口。四道闸门里没有一关会读许可证，
# 所以只能在这里守。
Require-File (Join-Path $root 'LICENSE') '项目自身的 MIT 许可正文'
Require-File (Join-Path $root 'LICENSE.LGPL-2.1.txt') 'libmpv-2.dll 的 LGPL-2.1 许可文本'
Require-File (Join-Path $root 'LICENSE.LGPL-3.0.txt') 'libmpv-2.dll 链入的 FFmpeg 的 LGPL-3.0 许可文本'
Require-File (Join-Path $root 'LICENSE.GPL-3.0.txt') 'LGPL-3.0 正文引用的 GPL-3.0 条款'
Require-File (Join-Path $root 'LICENSE.Apache-2.0.txt') 'vulkan-1.dll 的 Apache-2.0 许可文本'
Require-File (Join-Path $root 'THIRD-PARTY-NOTICES.md') '第三方组件归属清单'

# 那三份 LGPL/GPL 文本在仓库里有两个位置：assets\mpv-runtime（随 *.txt 拷到 exe 根，给 libmpv-2.dll）
# 与 assets\shaders（着色器源文件指向的那一份）。发布件里同样是两处 —— exe 根与 shaders\。改了一处
# 忘了另一处谁都不会发现，所以两头都比：仓库里那两份必须一致，发布根的那一份必须与源头一致。
$repositoryPath = (Resolve-Path -LiteralPath $RepositoryRoot).Path
foreach ($shared in @('LICENSE.LGPL-2.1.txt', 'LICENSE.LGPL-3.0.txt', 'LICENSE.GPL-3.0.txt')) {
    $runtimeCopy = Join-Path $repositoryPath "assets\mpv-runtime\$shared"
    $shaderCopy = Join-Path $repositoryPath "assets\shaders\$shared"
    Require-File $runtimeCopy '运行时许可正文'
    Require-File $shaderCopy '着色器许可正文'
    $runtimeHash = (Get-FileHash -LiteralPath $runtimeCopy -Algorithm SHA256).Hash
    if ($runtimeHash -ne (Get-FileHash -LiteralPath $shaderCopy -Algorithm SHA256).Hash) {
        throw "发布验证失败：同一份许可正文在仓库的两个位置内容不一致：$shared"
    }
    if ($runtimeHash -ne (Get-FileHash -LiteralPath (Join-Path $root $shared) -Algorithm SHA256).Hash) {
        throw "发布验证失败：发布件里的许可正文与仓库不一致：$shared"
    }
}

$shaderSource = Join-Path (Resolve-Path -LiteralPath $RepositoryRoot).Path 'assets\shaders'
$shaderRoot = Join-Path $root 'shaders'
$expectedShaders = @(Get-ChildItem -LiteralPath $shaderSource -Recurse -File |
    Where-Object { $_.Extension -in @('.glsl', '.hook', '.txt') })
if (@($expectedShaders | Where-Object { $_.Extension -in @('.glsl', '.hook') }).Count -eq 0) {
    throw "发布验证失败：仓库 assets\shaders 没有可核对的着色器清单。"
}
foreach ($license in @('LICENSE.LGPL-3.0.txt', 'LICENSE.GPL-3.0.txt', 'LICENSE.LGPL-2.1.txt', 'NOTICE.txt')) {
    Require-File (Join-Path $shaderSource $license) '着色器许可与归属说明'
}
$expectedPaths = @{}
foreach ($source in $expectedShaders) {
    $relative = $source.FullName.Substring($shaderSource.Length + 1)
    $expectedPaths[$relative] = $true
    $target = Join-Path $shaderRoot $relative
    Require-File $target '着色器或许可文件'
    if ((Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) {
        throw "发布验证失败：着色器或许可文件内容与仓库不一致：$relative"
    }
}
foreach ($file in @(Get-ChildItem -LiteralPath $shaderRoot -Recurse -File)) {
    if ($file.Extension -notin @('.glsl', '.hook', '.txt')) { continue }
    $relative = $file.FullName.Substring($shaderRoot.Length + 1)
    if (-not $expectedPaths.ContainsKey($relative)) {
        throw "发布验证失败：出现清单之外的着色器或许可文件：$relative"
    }
}
$shaderCount = @($expectedShaders | Where-Object { $_.Extension -eq '.glsl' }).Count
$hookCount = @($expectedShaders | Where-Object { $_.Extension -eq '.hook' }).Count

# 独占模式视频窗的 Lua UI（uosc 嵌入版，assets\mpv-ui）。缺入口脚本时 LibMpvBackend 只会降级成
# 「没有屏幕控件的独占播放」，而四道闸门里没有一关会真的起播，所以装箱完整性只能在这里守。
Require-File (Join-Path $root 'mpv-ui\scripts\uosc\main.lua') '独占模式 Lua UI 入口脚本'
Require-File (Join-Path $root 'mpv-ui\scripts\uosc\lib\utils.lua') 'uosc 工具库'
Require-File (Join-Path $root 'mpv-ui\scripts\uosc\elements\Timeline.lua') 'uosc 时间轴控件'
Require-File (Join-Path $root 'mpv-ui\fonts\uosc_textures.ttf') 'uosc 贴图字体'
Require-File (Join-Path $root 'mpv-ui\fonts\MaterialIconsRound-Regular.otf') 'uosc 图标字体'
Require-File (Join-Path $root 'mpv-ui\LICENSE.LGPL') 'uosc 许可证文本'

# Momoka.pri 必须把框架那几份 .pri 并进来，否则程序在 App.xaml 那一步就崩：
# 「Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'」。
#
# 2026-09-05 的第一版归因是错的（写成「第二次发布就好」），2026-09-11 挖到底：并进来这一步的输入是 obj 里的
# pri.resfiles，它来自 @(_PriFilesFromPayload)，而那个变体读的是 **@(PriOutputs)** —— 由 GetPriOutputs 填，
# 只含项目自己的 xbf/内容加 ProjectReference。框架那三份（Microsoft.UI.Xaml.Controls.pri 在 WinUI 包里、
# Microsoft.UI.pri 在 InteractiveExperiences 包里、Microsoft.WindowsAppRuntime.pri 在 Foundation 包里）
# 走的是 runtimes-framework 那套 NuGet 资产，**从来不进 PriOutputs**，所以 pri.resfiles 恒为 0 字节、
# Momoka.pri 只有 103~135 KB（并进来是 2.2 MB），发布件少 2 MB，程序一启动就死。
#
# **跟「清没清过 bin」「跑第几次」无关**：2026-09-11 对着刚清过的 bin 连跑四次发布，四次都是 135 KB，
# 没有一次「第二次就好」。修法在 Shell 项目的 csproj 里（AddWindowsAppSdkFrameworkPriToPayload 目标，
# 把三份框架 .pri 手动塞进 PriOutputs），不是在这里重跑。
#
# 谁都不会去数 Momoka.pri 有多大，而这个坏法的唯一症状是「程序打不开」，所以这一关在这里守。
# 判据是它索引里有没有 themeresources 这个名字，不是大小 —— 大小会随 XAML 增减浮动，那个名字不会。
$appPri = Join-Path $root 'Momoka.pri'
Require-File $appPri '应用资源索引'
$priBytes = [System.IO.File]::ReadAllBytes($appPri)
# GetEncoding(28591) 而不是 [Encoding]::Latin1：这个脚本跑在 Windows PowerShell 5.1（.NET Framework）上，
# 那里没有 Latin1 这个静态属性。要的只是「一个字节一个字符」，好在二进制里找一个 ASCII 名字。
$priText = [System.Text.Encoding]::GetEncoding(28591).GetString($priBytes)
if ($priText -notmatch 'themeresources') {
    $kb = [math]::Round($priBytes.Length / 1KB)
    throw ("发布验证失败：Momoka.pri（$kb KB）里没有框架的 themeresources，程序启动时会在 App.xaml 崩掉。" +
        "原因是框架那三份 .pri 从来没被并进来（PriOutputs 里没有它们）。修法在 Shell 项目的 csproj 里：" +
        "AddWindowsAppSdkFrameworkPriToPayload 目标。**重跑这个发布脚本没用** —— 2026-09-11 连跑四次都是这个大小。")
}

if ($SelfContained) {
    Require-File (Join-Path $root 'Microsoft.WindowsAppRuntime.Bootstrap.dll') 'Windows App SDK 自包含运行时'
    Require-File (Join-Path $root 'hostfxr.dll') '.NET 自包含运行时'
}

$solution = Join-Path (Resolve-Path -LiteralPath $RepositoryRoot).Path 'Momoka.sln'
$legacyApp = Join-Path (Resolve-Path -LiteralPath $RepositoryRoot).Path 'src\Momoka.App'
if (Test-Path -LiteralPath $legacyApp) {
    throw "发布验证失败：迁移期 WinForms 目录仍存在：$legacyApp"
}
if (Test-Path -LiteralPath $solution -PathType Leaf) {
    $solutionText = Get-Content -LiteralPath $solution -Raw
    if ($solutionText -match 'Momoka\.App|src[\\/]Momoka\.App') {
        throw "发布验证失败：解决方案仍包含已移除的 WinForms 外壳。"
    }
}

$files = @(Get-ChildItem -LiteralPath $root -Recurse -File)
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
Write-Output ("发布验证通过：{0} 个文件，{1:N1} MB，{2} 个 GLSL、{3} 个 HOOK 着色器（含许可逐文件校验）。" -f $files.Count, ($bytes / 1MB), $shaderCount, $hookCount)
