# 视频补帧独立测试与父集成契约

本项目不修改主测试入口，也不启动 Momoka、WinUI 窗口、真实媒体或服务器。已随补帧功能注册进 `Momoka.sln`。

## 父任务提供的设置契约（2026-10-26 已落地）

枚举在 `Momoka.Mpv`，由产品文件 `FrameInterpolationOptions.cs` 定义：

- `FrameInterpolationMode`：`Off = 0`、`Smooth = 1`、`MotionCompensated = 2`。
- `FrameInterpolationQuality`：`Fast = 0`、`Balanced = 1`、`High = 2`。
- `VideoSettings.FrameInterpolationMode` 默认 `Off`。
- `VideoSettings.FrameInterpolationFps` 为 `int`，默认 `60`，存储归一范围 `24..120`。
- `VideoSettings.FrameInterpolationQuality` 默认 `Balanced`。

三个属性在 `AppSettings.cs` 的 `VideoSettings` 上；`SettingsMigration.Normalize` 把认不出的枚举拨回关闭／均衡，帧率用下发端同一个 `ClampFps` 夹住。缺键的旧设置读出来就是这三个默认值，不需要迁移版本。

挂接位置（均已接线）：

1. `SettingsViewModel.cs` 的 `Sections.Add(VideoCard())` 之后加入 `FrameInterpolationCard()`。
2. `SettingsViewModel.Video.cs` 的 `RefreshVideoBackend()` 调用 `RefreshFrameInterpolationRows()`。
3. 新方法 `RefreshFrameInterpolationRows()` 只更新本卡；新模式回调 `RefreshFrameInterpolationMode()` 先刷新本卡，再调用基础卡刷新，不互相递归。
4. 基础卡展示的有效同步从 `MpvOutputOptions.ResolveSync(video)` 读取；有效硬解从 `FrameInterpolationOptions.Resolve(video)` 读取。不能仍只按保存的 `VideoSync == "audio"` 显示“插值关闭”：新 Smooth 会为本次播放强制 display-resample，新 Motion 则采用 audio 并关闭相邻帧混合。
5. 导入／恢复默认重建卡；后端、预设及自定义配置切换通过父刷新重算显隐。外部 mpv 的版本／滤镜可用性仍由父可用性层负责，不能根据本机 DLL 支持推断用户指定的 mpv.exe 也支持。

## 输出规则

- **Off** 不关闭旧基础插值。默认预设沿用旧省略语义；同窗换片由既有 `InlineSwitch.FilmScoped` 明确恢复 `vf`、同步和插值默认值。命名画质预设下显式发送 interpolation=no／必要的 audio，防止预设残留；旧插值启用时仍发送其原有 tscale。
- **Smooth** 是相邻帧混合，不是运动补偿。有效值为 display-resample／interpolation=yes／用户原有 tscale，基础的高帧率和高刷新率回退仍适用，不改旧插值开关和同步偏好。
- **MotionCompensated** 使用 `@momoka-frame-interpolation:lavfi=[minterpolate=fps=...:mi_mode=mci:mc_mode=...:me_mode=...:vsbmc=...]`。Fast：obmc／bidir／0；Balanced：aobmc／bilat／0；High：aobmc／bilat／1。质量档是参数取舍，不是实测性能等级。
- CPU 滤镜的有效硬解转换为 copy 或软件解码；关闭后恢复用户原值，保存对象不变。Vulkan 没有本功能已验证的复制路径，使用软件解码。
- 完整 `vf` 合并在 HDR 的 `@momoka-hdr:format=...` 后，不改 GLSL 列表／着色器前置参数。内置 `mpv_set_option_string` 不接受命令行式 `vf-append`，因此不能简单增加该键。
- 所核对 FFmpeg 的 minterpolate 只支持 8-bit 像素格式。已识别 HDR 或位深 >8 时跳过 Motion，不悄悄量化；元数据未知时不能保证识别。Smooth 不受此 CPU 滤镜限制。
- 原片 fps 已达到目标时跳过 Motion，避免 FFmpeg 以低目标 fps 丢弃原有帧。
- 内置独占模式的自定义 mpv.conf 后加载，可能覆盖本页选择；卡片明确说明不保证最终生效。

## 可复现命令

从本独立树根运行，SDK 使用 `global.json` 所要求的用户级版本：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.FrameInterpolationTests/Momoka.FrameInterpolationTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.FrameInterpolationTests/Momoka.FrameInterpolationTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.FrameInterpolationTests/Momoka.FrameInterpolationTests.csproj -c Release --no-build --no-restore
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.FrameInterpolationTests/Momoka.FrameInterpolationTests.csproj -c Release --no-build --no-restore -- regression
# 唯一额外参数是本机 DLL 绝对路径，不接受媒体、URL 或脚本。
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.FrameInterpolationTests/Momoka.FrameInterpolationTests.csproj -c Release --no-build --no-restore -- native (Join-Path (Get-Location) 'libmpv-2.dll')
```

### 测试边界

- 默认入口：33 项补帧规则与设置卡源码契约，覆盖 Off／Smooth／Motion、fps／枚举边界、copy／软件硬解、HDR／原片帧率回退、完整 vf 合并、预设覆盖与双向复位、有效规则不改存值，以及设置行的隐藏／恢复、清空、保存和 after 回调。
- UI 部分编译链接新 partial，并原样提取本树真实 `SettingSection`、Choice／Number 行及工厂。替代部分仅是未执行的 WinUI 类型、保存计数与父刷新入口，不把离线控制流说成真实控件／像素／键盘验证。
- `regression` 入口：23 项原有 VideoOutputTests／InlineSwitchTests 原样链接，不修改原测试。内部列表编码器源码链接，基线截取方法原样提取；与主套件重叠，不与全量套件重复累加。
- `native`：隔离的新本地控制台宿主，config=no、scripts禁用、ytdl禁用、协议白名单file、audio/video=no、ao/vo=null，验证 vo 候选接受后、初始化前再次固定为 null；不调用 loadfile，不给 mpv 媒体或 URL。使用本机 DLL 的滤镜注册表和逐项帮助查询，再核对三档的选项接受／vf读回／硬解／同步，以及 Off→Smooth→Motion 完整属性复位。帮助查询本身按 mpv 约定返回负“退出解析”结果；断言帮助日志包含所需候选，不把负返回误报成滤镜成功执行。
- 无媒体句柄不会创建／运行视频滤镜图，选项接受和滤镜登记不证明帧输出、GLSL执行、实时性能、HDR显示或外部后端可用。

## 本次真实验证（2026-10-25）

- .NET SDK 10.0.400；本树 Core／独立测试 Release 构建（临时父配置契约下）：0 警告、0 错误。
- 默认入口：33 通过，0 失败，0 跳过；原有回归：23 通过，0 失败，0 跳过。
- 本树 Shell Release 编译（临时父配置契约下）：0 警告、0 错误。没有运行 Shell／桌面。
- 行尾与 whitespace 校验通过；自动化定位扫描报告 434 个交互元素、0 个缺少句柄，新卡复用既有行模板。这不是运行中 UIA 验证。
- 探针拒绝路径已实际执行：未知入口／缺路径退出 2，URL／UNC 路径退出 1；在 native 初始化和网络访问前拒绝。
- 无媒体原生探针：184 个断言通过，退出码 0。
- 核实 DLL：mpv `v0.41.0-1163-g270367e8a`；FFmpeg `N-127522-gc705686a3`。
- DLL SHA256：`FE583AF31036BF3F3ADACF6659E1BD448B568091B0334675573A3E303EC89D17`。DLL只读加载，没有复制、下载或升级。

未测：真实帧生成／画面／快动作伪影／性能／HDR屏／真实硬解路径、集成交换链／独占窗口／uosc同窗实际换片、外部 mpv 进程及自定义配置实际覆盖。CPU与内存复制、额外渲染和延迟的成本不提供虚构毫秒或fps数字。

## 官方核查来源

只使用 primary docs／源码；本次直接 HTTP 读取，不依赖第三方教程：

- FFmpeg 官方滤镜文档：https://ffmpeg.org/ffmpeg-filters.html#minterpolate
- 对照 tag 的参数文档：https://github.com/FFmpeg/FFmpeg/blob/n8.0/doc/filters.texi
- 随包 FFmpeg 对应提交的像素格式与参数表：https://github.com/FFmpeg/FFmpeg/blob/c705686a3/libavfilter/vf_minterpolate.c
- mpv 0.41 稳定手册：https://mpv.io/manual/stable/
- 固定 v0.41.0 的同步／插值／copy硬解说明：https://github.com/mpv-player/mpv/blob/v0.41.0/DOCS/man/options.rst
- 固定 v0.41.0 的lavfi图引用语法：https://github.com/mpv-player/mpv/blob/v0.41.0/DOCS/man/vf.rst
- 原生选项与帮助查询区别：https://github.com/mpv-player/mpv/blob/v0.41.0/player/client.c 、https://github.com/mpv-player/mpv/blob/v0.41.0/player/command.c
