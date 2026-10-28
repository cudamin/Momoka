# 可选 SVP 接入

Momoka 的「设置 → 视频输出 → 补帧」提供默认关闭的「接入 SVP」。视频输出按基础输出、画质与着色器、HDR 与杜比视界、补帧排序，补帧在最下面。

## 使用

1. 自行安装并运行 SVP Manager，在 SVP 安装器中添加 64 位 mpv／VapourSynth 组件；需要 RIFE 时在 SVP 中安装并选择相应引擎和模型。
2. 按 SVP 的说明配置匹配的 Python／VapourSynth 环境，修改环境后重启 Momoka。Momoka 不安装或下载这些组件，也不修改系统环境、SVP 配置或其他播放器。
3. Momoka 使用内置播放后端，打开「接入 SVP」。集成与独占管线均可接入；外部 mpv 后端不显示本功能。独占模式启用自定义 mpv.conf 时，SVP 接入暂不生效，保留开关和路径。
4. 「VapourSynth 组件」留空自动查找，或填写本机 x64 `VSScript.dll` 的完整路径。支持 SVP 安装目录、已注册的 `VSSCRIPT_PATH` 与 PATH 中的本地目录；拒绝 URL、网络盘、重解析路径和其他架构。
5. 选择算法、目标帧率、质量和可选 RIFE 模型都在 SVP Manager 中操作。播放后在 SVP 窗口确认接管；Momoka 的「已找到组件」只说明检测到本地 x64 文件，实际起播还要检查 API 初始化和管道身份。

## 行为和限制

- 使用 SVP 官方 Windows 通道 `mpvpipe`。通道只在该次 SVP 播放上下文存活；起播前验证管道属于当前 Momoka 进程，不向其他播放器发送请求。关闭上下文后管道清理。
- 开启 SVP 表示允许本机同用户的 SVP 读取 mpv 播放属性并修改滤镜。播放地址和认证头继续通过进程内 API，接入不把它们放入启动参数、临时配置或日志。
- 有效接入时不叠加 CPU 运动补偿或相邻帧混合，使用音频同步与复制硬解／软件解码，关闭 `hr-seek-framedrop`。这些只改变本次有效配置，关闭 SVP 后恢复原有偏好。
- 接管期间，播放器菜单隐藏清空／修改视频滤镜、插值、同步及非复制硬解入口；软件／复制硬解、字幕、音频、截图和着色器仍可用。命令入口也拒绝会打断 SVP 的操作。
- 首版在换片／换集时重建播放上下文和独占视频窗口，让上一片的异步滤镜不落到下一片。
- 组件缺失、API／Python 初始化失败或 `mpvpipe` 被其他播放器占用时，明确提示并停止本次起播；可关闭 SVP 后正常播放。SVP 未运行或未接管时不会凭空产生补帧。
- 运行时模块在 Momoka 进程内保持绑定；更换组件路径后需要重启。HDR、动态元数据、画面伪影与实时性能由所用 SVP 引擎和组件决定，没有据此承诺 4K／60fps 或 HDR 原样保持。

## 离线验证

在当前工作树使用 `global.json` 所要求的用户级 .NET SDK，先编译再用 `--no-build`：

```powershell
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- svp-fixture
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- svp-native-fixture (Join-Path (Get-Location) 'libmpv-2.dll')
```

`svp-fixture` 检查规则、路径、菜单保护与匿名本地管道成功／拒绝／取消；`svp-native-fixture` 使用随包内核无媒体句柄、匿名通道与关闭音视频输出，确认有效选项、编译能力、当前进程管道身份与销毁清理。不会加载用户配置、脚本、媒体或启动 SVP。

当前 Release Shell 的 `--probe-shell --probe-section svp --screen 1` 使用临时配置和真实设置控件，检查宽窄卡片顺序、开关、路径失焦保存、状态和恢复，分别保存渲染与实际屏幕截图。播放服务和原生句柄的 SVP 所有权／换片检查在 `Momoka.ShaderCommandGenerationTests` 的托管 native seam 中执行。

离线检查不代替真实 SVP 插件、真实视频、硬件速度、HDR 屏或服务器播放验收；真实媒体库验证仍遵循 [开发与验证](开发与验证.md) 的授权边界。

## 官方来源

本次接入依据 SVP 官方 mpv 与 Plex/libmpv 说明，以及随包 mpv 对应提交的 VSScript 动态加载实现：

- https://www.svp-team.com/wiki/SVP:mpv
- https://www.svp-team.com/wiki/SVP:Plex_Media_Player
- https://www.svp-team.com/wiki/RIFE_AI_interpolation
- https://github.com/mpv-player/mpv/blob/270367e8af/video/filter/vf_vapoursynth.c
- https://github.com/mpv-player/mpv/blob/270367e8af/input/ipc-win.c
