# HomeOrder 运行探针活动期回归

修复 `ShellNavigationProbe.HomeOrder.cs` 的隔离夹具，不改 SettingsPage / SettingsViewModel / MoviePilot 产品行为。

## 根因与边界

- 旧夹具只 Attach VM、Reload 后直接调用 `WatchMoviePilot`，没有经过页面 `OnNavigatedTo` 开启 `_activity`。当前正式页面会拒绝默认/已取消的活动票，因此连接虽成功，原表仍为四行，两个 MP 项永远等不到；Release 后只重挂也一样。
- 探针现复用 Settings 探针的共享 `BeginSettingsFixtureVisit` 活动期入口，离树明确释放，重入时等待当前表。原18处业务/布局断言、六处截图调用与轮播阈值条件均保留，不放宽 scope，不增加固定延时。
- 编译时原样提取当前页面的监测、释放方法、共享活动期入口，以及 HomeOrder 中改变的初入/缓存重入调用片段与实际 `UntilAsync`。缺失/歧义边界使构建失败。WinUI、队列和未使用的内嵌页面是边界替身，不复制活动期或可用性模型。
- VM / HomeCard / RefreshHomeRows / Rows / 排序行 hook 来自显式传入的本树当前 Release Shell DLL。初始仅构建实际 HomeCard，活动期重入走原样的共享入口与页面监测方法；**不执行完整 WinUI 初建、渲染或 Frame 缓存实现**。
- MoviePilot 可用性走真实 `RefreshAvailabilityAsync` / `SetAvailable` / `AvailabilityChanged`，HTTP 仅内存匿名 `dashboard/system` GET。匿名会话缓存是传输边界；禁止登录和任何凭据 Protect/Unprotect。设置全在内存，AppPaths 含非法文件名以拒绝误触盘；无 UI、原生设备、注册表、鼠标、真实媒体、网络或发布。

## 命令

在待验证工作树根目录执行；先成功编译当前 Shell，不能用旧 DLL。
固定 SDK 与包版本不升级。首次缺 assets 时使用本机固定缓存源（配置只写 ignored `work`），关闭审计/证书联网；之后构建与运行均 `--no-restore`。

```powershell
$dotnet = "$HOME/.dotnet/dotnet.exe"
$root = (Get-Location).Path
$shell = Join-Path $root 'src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_CERT_REVOCATION_MODE = 'offline'
& $dotnet restore ./src/Momoka.Shell/Momoka.Shell.csproj --source "$HOME/.nuget/packages" --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& $dotnet restore ./tests/Momoka.HomeOrderProbeTests/Momoka.HomeOrderProbeTests.csproj --source "$HOME/.nuget/packages" --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& $dotnet build ./src/Momoka.Shell/Momoka.Shell.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $dotnet build ./tests/Momoka.HomeOrderProbeTests/Momoka.HomeOrderProbeTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $dotnet run --project ./tests/Momoka.HomeOrderProbeTests/Momoka.HomeOrderProbeTests.csproj -c Release --no-build --no-restore -- $shell $root
```

正常套件覆盖：共享活动期入口、未连接、连接/断线恢复、单表 Rows 稳定、偏好/次序保留、实际排序行移位/保存 hook、释放后重挂拒绝、缓存重入、关闭/恢复期间离树、旧队列拒绝、非主页分类、原样缓存重入调用及实际超时。源码契约另核原可见布局、截图和轮播判据完整保留；不将这些源码检查说成像素验证。

附加参数 `legacy-red` 执行旧夹具的失败路径：真实服务 Available=True，原 Rows=4 / MP=0 / 队列=0，实际 UntilAsync 超时；**预期退出码1**。它是根因复现证据，不是正常套件的失败。

相关既有 `Momoka.SettingsLifetimeTests` 应另编译/运行，结果分开报告。新项目独立运行，不更改方案、总跑器或其他 worker 的测试文件。

## 主线程待验

整合提交后须用最终构建在隔离可见环境运行 `--probe-shell --probe-section moviepilot --screen 1`；重点核对主页混排原 case、1100/760 的设置/主页截图、真实复选框保存、移位、隐藏无空白以及轮播折叠阈值。局部 section 成功不等于全量101项已通过。

编译依赖：共享入口由主树 Settings 提交 `612549dd94fc8fedd3b5c280669131c939896147` 提供，须先整合该提交。base工作树先只读保存到 ignored work，再按授权复制该文件到本树作为不暂存的编译依赖；测试的 `HomeOrderSettingsProbeSource` 也可显式指向保存的副本。这些依赖副本不主合。最终主树默认直接提取实际 `ShellNavigationProbe.Settings.cs`。

本 worker 不启动 UI、不刷新交付目录，不将本树 DLL 或 ignored PROGRESS/日志合入主树，不 push。
