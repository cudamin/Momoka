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

## 2026-10-25：纯合并基线的旧测试兼容
本轮验证树为 `C:/Users/89400/.codex/worktrees/merged-contract-tests/Momoka`，基点
`4afca26672297aedd7725d675706d7f2b923519c`，产品只来自父树
`mp-translation-interpolation/Momoka` 已 resolved/staged 的 `48c43fc20d5827fe5f5f147eb73e1d1ca7aa2795`
合并差异。导入 patch SHA256：`7ef4a4592f66b219639aa0ca499427da971443a34e9b8fe0f894cc36cffcd680`。
本树 629 份产品源的 Git blob 与父 index 逐份一致；未导入父未暂存的 translation/AppSettings 改动。
产品快照不暂存、不提交，不改共享 Program/Agreements/方案；最终功能再验由父线程完成。

测试 VM 使用本树实际重新编译的标准 x64 Release Shell（不是复制父 DLL）：
`src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll`，
2026-10-25 01:36:41 +08:00，4,743,168 字节，SHA256
`b9125a7ff07a46e236603200c97dd0608fd548a84ba588d39431d99fbb5e67f5`。
父首次合并 DLL 只作来源对照：2026-10-25 00:34:42 +08:00，SHA256
`735c3295886cb0dce9c8e4b92aa265739290959e0c8ce5befca5f9dcd1f97f77`；没有将它称为本树输出。
所有日志/输入清单留在本树 ignored `artifacts/verification/merged-contract-tests/`。

本轮保留原 11 项完整断言，新增 5 项，当前 **16 过/0 败/0 跳过**：
- 原样提取真实 `UpdateCategoryGroups`，不用空 stub；真实 VM `ApplyServerAdministration` 重建的
  分组通知必须通过当前活动票更新实际绑定源，未活动/Release 后不得写源，重入仍使用新活动票。
- 相同源不重复绑定；边界模拟源赋值期间控件写回其它选择，原方法当场恢复原选择，不能靠 VM
  `ReplaceCategories` 的尾部通知掩盖丢失选择。
- ctor 的 `ShellPrefs.DanmakuChanged` 只是内存事件通道；VM 回调转发当前真实 Shell 方法。
  重复 Loaded 不重复订阅，仍在树的 Unloaded 不退订，真实离树必退订。该新增事件用例明确
  将 VM 标记未 ready，验证真实方法自身守卫；不声称运行整页/native 弹幕卡或联网匹配。
- shared `ShellRuntime` 只为实际方法缺省尾参数补 `GetParameters().DefaultValue`，过多或缺必需参数
  仍失败。HomeOrder 的实际 Attach 按名字保留原服务、路径、能力和 MoviePilot，尾参数取真实默认。

复跑仍用上面的 `$shell $root` 命令；先 restore/成功 Release build，不能 no-build 借旧产物。
原业务/布局断言、截图调用和轮播阈值源码检查没有删除或放宽；无实际 UI/鼠标/服务器/播放/发布。
