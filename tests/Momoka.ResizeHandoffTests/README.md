# 集成窗口切换与拖边交接回归

每次编译通过 `RoslynCodeTaskFactory` 唯一首尾定位，逐字提取当前 `PlayerPage.ClientRect.cs` 的全屏、最大化、拖边尺寸变更、缓冲收尾、暂停与取消方法，以及 `PlayerViewModel.Interaction.cs` 的真实播放/文件代次守卫。窗口侧执行逐字提取的 `HostWindow.LockAspectDuringResize`、待矩形恢复和取消；`NativeRect` 也来自当前源文件。定位丢失或歧义会直接使编译失败。

仅窗口/覆盖层、GPU抓帧和播放命令的末端使用匿名内存边界。任务续体在受控 `SynchronizationContext` 队列运行，验证 `Task.Yield` 之后才开始抓帧，使窗口先登记待矩形。真实 Core 的比例约束、冻结规则、播放身份和值类型参与执行。没有启动 WinUI、mpv、DWM、网络或媒体，不读取生产设置、不移动指针。

拖边用例覆盖暂停确认、窗口登记、连续目标合并、离页或换片拒绝迟到帧、错误回退、原暂停保留和新拖动接手。拖回原尺寸且缓冲已正确时，仍等待最后呈现提交才撤承接帧。

窗口切换用例用受控 Task 暂停命令、抓帧和恢复，覆盖首次 NowPlaying 发布、离页、换片与停播、快速反向合并、用户原先暂停、暂停拒绝，以及旧事务清算期间重新入场和提交新窗口请求。

默认源根是本项目所在工作树的 `../..`。并行开发可临时传 `-p:SourceRoot=<产品工作树绝对路径>`，只在本测试树生成输出；该参数不会构建或修改所指产品树。最终整合后应省略它，再验证当前树。

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.ResizeHandoffTests/Momoka.ResizeHandoffTests.csproj --source "$HOME/.nuget/packages" --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.ResizeHandoffTests/Momoka.ResizeHandoffTests.csproj -c Release --no-restore -p:Platform=x64 -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -nodeReuse:false
& "$HOME/.dotnet/dotnet.exe" ./tests/Momoka.ResizeHandoffTests/bin/x64/Release/net10.0/Momoka.ResizeHandoffTests.dll
```

已登记 `Momoka.sln` 和 `tools/regression-suites.json`，整合后可用 `tools/run-regressions.ps1 -Suite Momoka.ResizeHandoffTests` 统一执行。此项目只证明托管控制流、目标矩形和所有权边界，不证明真实 GPU 帧率、DWM 层显示、WinUI 尺寸/指针命中或屏幕黑边；实际图形验收由交付会话另跑隔离探针。

2026-11-10 在测试独立树针对当轮实施树源文件运行：Release/x64 编译 0 警告 0 错误，16/16 通过、0 跳过。三个隔离源副本分别只移除 `Task.Yield`、把覆盖层交接改成仍须 `_resizeLoop`、去掉同当前尺寸但已有待目标的更新条件；三次均编译成功，各有恰好 1 项对应行为失败，之后真实源码重新编译并全绿。副本只在本树 `artifacts`，没有改产品源码。初夹具未模拟 UI 续体队列所报的时序错误不计产品红。
