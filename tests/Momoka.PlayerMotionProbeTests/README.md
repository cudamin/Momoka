# PlayerMotionProbe 最终全屏阶段离线回归

此夹具原样编译当前 `PlayerMotionProbe.cs` 的“加载页直接进入全屏再退出”整段调用，以及其真实 `Write`、`Require`、`Until`。构建时若首尾边界缺失或不唯一立即失败，运行时核对编译来源路径与 SHA-256；不是另写一个动效判据。

只替代 PlayerPage 的现有状态/完成任务、HostWindow 窗口读数、取样计时器与日志端点，均由测试控制回调顺序。不创建 UI、加载 native、播放本地/真实媒体、移动鼠标、登录或读取真实设置/凭据。

覆盖已完成同步路径、超过旧 500ms 才到达的同一完成序列（旧源红、新源绿）、窗口/pose/合成交接不同完成顺序、入口未开始、完成后仍未全屏或残留变换、延迟退场、原浏览矩形恢复、取消/异常/两条有界超时失败。等待不以 Identity 为条件；完成后错误姿势仍必须被原断言拒绝。

## 当前源

在此工作树根用本机 SDK 10.0.400 串行运行；默认本地包缓存缺包时停止，不升级或下载替代包：

```powershell
$dotnet = "$HOME/.dotnet/dotnet.exe"
$project = './tests/Momoka.PlayerMotionProbeTests/Momoka.PlayerMotionProbeTests.csproj'
& $dotnet restore $project --source "$HOME/.nuget/packages" --disable-parallel -m:1 -p:BuildInParallel=false -p:NuGetAudit=false
& $dotnet build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $dotnet run --project $project -c Release --no-build --no-restore -- (Get-Location).Path
```

`MotionProbeSource` 可显式指向保存于本树 artifacts 的旧提交源，另用独立 `IntermediateOutputPath` / `OutputPath` 构建后传 `pre-fix-control <本树绝对根>`。这个模式要求原进场断言确实失败，用于红对照；当前修复源不能靠它声明通过。不要用旧程序集代替 current-source 回归。

## 运行覆盖缺口

离线夹具不证明真实 WinUI 帧、原生窗口/任务栏、交换链及关闭进程行为；15 项离线通过不等于 actual motion 通过。主线程须在整合后的最终代码上串行实际运行 `--probe-player-motion <同树本地生成彩条> --screen 1`，按 receipt PID 找唯一匿名 actual 日志，核查新完成信号日志、原断言、其后的自动全屏/关闭回归以及进程退出码。不得用本 worker 的 DLL/PROGRESS 或不同诊断算法冒充最终交付。
