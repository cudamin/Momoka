# 设置延迟保存定向回归（PERF03）

## 输入与运行

先成功编译本树当前 Release Shell、Core 和此测试项目，再运行。运行器必须显式取得本树标准 Release Shell DLL 和根目录；不会调用应用入口。所有文件只写入本树 `artifacts/perf-settings/data/<随机目录>`，凭据读写替身直接拒绝任何调用。

本次 Windows 根目录：`C:\Users\89400\.codex\worktrees\perf-settings\Momoka`；SDK：`C:\Users\89400\.dotnet\dotnet.exe`（10.0.400）。

```powershell
$root = 'C:\Users\89400\.codex\worktrees\perf-settings\Momoka'
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$shell = Join-Path $root 'src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.dll'
& $dotnet restore (Join-Path $root 'tests\Momoka.SettingsDeferredSaveTests\Momoka.SettingsDeferredSaveTests.csproj') --disable-parallel -m:1 -p:BuildInParallel=false
& $dotnet build (Join-Path $root 'src\Momoka.Shell\Momoka.Shell.csproj') -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $dotnet build (Join-Path $root 'tests\Momoka.SettingsDeferredSaveTests\Momoka.SettingsDeferredSaveTests.csproj') -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $dotnet run --project (Join-Path $root 'tests\Momoka.SettingsDeferredSaveTests\Momoka.SettingsDeferredSaveTests.csproj') -c Release --no-build --no-restore -- $shell $root
```

Shell 没有还原结果时先单独 restore Shell；任何 build 失败即停止，不运行旧输出。主套件、原生窗口、自检和共享发布不是该运行器的职责。

## 实际源码与边界

- 加载本树当前 Shell 中正式 SettingsViewModel、MVVM 生成的 SettingSliderRow.Value、Slider/UnitSlider、Cancel/FlushSettingsAsync；Core 使用正式 SettingsService、SettingsSaveQueue、SettingsStore 和原子文件保存。
- 构建时原样提取 App.PrepareWindowCloseAsync/OnWindowClosed 和 SettingsWindow.Hide。替代的只有窗口、播放收尾和容器解析边界；Hide 到正式 VM.Cancel 的接线实际执行。完整 SettingsPage/HostWindow 和真实 WinUI 关闭消息由父集成验证负责。
- 150 个值分别验证内存、预览回调计数、最后值、一捕获/一成功写入，以及捕获在单线程 UI 所有者、落盘不在 UI 所有者。日志中的成功写计数与实际重读文档同时断言；不是低层文件系统写调用数。
- 覆盖静默自动保存、持续输入最长调度边界、并发 flush、真实文件锁失败、NaN 捕获失败、队列拒绝、明确重试、过期快照与同步保存版本、独立列表和旧失败通知重入新保存。
- 特别补上旧写失败收尾前的新编辑、捕获失败通知重入新滑块编辑；同代失败停止自动循环，新代次不被旧失败取消，失败日志不被抹掉。
- 不登录、不访问真实库、媒体、凭据，不创建 native/WinUI 窗口，不跑真实播放。

## 保持不变的集成 API

- SettingsService.ConfigureDeferredSave(Func<Action, bool>)：组成根配置一次；委托必须投递到活设置所有者线程，不能在线程池内联捕获。
- ISettingsService / SettingsService.SaveDeferred()：滑块热路径仅登记，未配置的替代服务保留同步 Save 默认语义。
- ISettingsService / SettingsService.FlushAsync()：Task<bool>，调用方 await；false 保留欠项。
- SettingsViewModel.FlushSettingsAsync()：internal Task，隐藏/Dispose 仍排空共享服务，但不回填已停用页面的提示。

200 ms 静默、1 s 上界是调度阈值，不是磁盘完成期限；UI 队列阻塞、正在写入或磁盘故障仍可能更久。捕获完整 JSON 保持在 UI 所有者，首次 JIT/序列化成本仍存在。同步非滑块保存及退出时最终几何量保存保持原语义。Dispose 仅释放 timer，不冒充排空；退出调用点必须先 await FlushAsync。

## 本次保留的失败证据

旧快照列表断言首轮失败、旧失败取消新编辑的复现、隐藏页 flush 回填的生命周期失败均留在本树 artifacts，不更新基线或删失败断言。最新结果及完整命令、路径由同树 PROGRESS 和 artifacts/perf-settings/takeover-handoff.md 记录。
