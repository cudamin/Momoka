# 最终交付 Settings 探针契约回归（worker，2026-10-24）

这是独立离线测试项目，不加入或修改共享方案／Tests Program。worker 基树是
`75b42f5bb83c5dcc3f6c9d476092fcc481840bc6`，独立树为
`C:\Users\89400\.codex\worktrees\delivery-settings-probe\Momoka`，分支
`codex/delivery-settings-probe`。主树是
`C:\Users\89400\.codex\worktrees\staged-review-continue\Momoka`。

## 改动与根因

只修改 `src/Momoka.Shell/ShellNavigationProbe.Settings.cs` 和实际包含
`SettingsConditionalRowsAsync` 的 `src/Momoka.Shell/ShellNavigationProbe.SettingsMotion.cs`；
仓库没有 `ShellNavigationProbe.SettingsConditional.cs`。产品 VM／控件／XAML、共享 Probe、
SignIn、Sidebar、HomeOrder 和 AniBt 都不改。

- 旧夹具直接 `SettingsViewModel.Attach/ReloadAsync`，却不进入 SettingsPage 的活动期。
  实际构造器在 `_activity.IsCurrent(_activityToken)` 为假时拒绝 Loaded 与分类事件，
  页面层的管理目录刷新也直接返回。Reload 已经正确 await；不是通过延长 sleep 修复。
- `BeginSettingsFixtureVisit` 仅在探针中补齐真实导航使用的活动票、VM `Activate`、
  实际 `ApplySettingsMotionPreference` 订阅和初次应用，不引入另一个产品导航／UX 入口。
  每次接线在原 try/finally 中；管理刷新也挪入其中，失败仍释放该页。
- Hide/Release 后再次探测必须开启新活动期。旧排队回调仍被原 generation 拒绝；不直接复活
  被取消的访问，不重新 Reload 或替换草稿。临时托管请求读完后收回，不留给后续本机卡。
- 页面有布局尺寸、行的 `IsLoaded` 为真，不证明 Loaded 事件中的接线已完成。等待真实页面
  Loaded；从属行等待实际 `_rowMotions` 注册；Reload 后仍按原动画落定条件等待。
  没有新增更长固定等待，也没有放宽淡入／上浮／归位／队列清理判据。

## 执行的真实方法与测试边界

- 构建时原样提取当前 SettingsPage 的构造器事件、页面刷新、分组源更新和 ReleaseHosted；
  原样提取新的探针活动入口、真实等待谓词、原入场采样／Require 和整个快速切换／Require 块。
  起点缺失或重复即构建失败，没有复制产品实现的备用路径。
- 完整源码链接 `SettingsPage.Motion.cs`、`SettingsRowsMotion.cs`、`HomeMotion.cs`，因此
  generation、Loaded 条件、真实淡入／位移请求、偏好订阅与清理执行的是当前实际方法。
  Storyboard 的完成、DispatcherQueue、布局值、XAML 绑定、Frame 和 WinRT 值是无桌面边界替身。
  不执行优先级拖拽，误用该边界立即抛错。
- source-link 复用已有 `Momoka.SettingsLifetimeTests` 的 ShellRuntime／LifetimeFixture，
  显式加载本树标准 Release `Momoka.dll` 并调用实际 SettingsViewModel／SettingToggleRow／
  ScreenshotCard／RefreshScreenshotCard。行的显隐、集合复用和偏好恢复不在替身里复制；
  UI label 绑定只投影这些实际行，不证明真 XAML 像素。
- 身份只通过已有匿名夹具在内存建立，不调用登录、注销、SelectSource、Persist、生产凭据或
  注册表；假 HTTP handler 只返回允许的读取，拒绝真实网络。TEMP/TMP 指向本树 work，
  既有 Core Settings IO 也不出该树。不会运行 Momoka App、桌面 UI、鼠标、native 或播放。

23 项包括旧 VM-only 夹具的不执行反例、Emby/Jellyfin 管理权限消失与恢复、真实多来源默认
收起及撤权、合法旧 fake 空 Sources、与原多服 fake 相同的混产品输入、开始回调与结束态
区分、宽窄原判据成功和缺动画失败、快速开关原清理判据成功和旧缺接线失败、Loaded 前件、
行 IsLoaded 先到但 handler 未注册的等待、实际 WebP→无损→恢复／PNG→WebP 行与偏好恢复、
Hide 迟到结果／排队回调拒绝、重入不重复订阅和不替换草稿、屏外与系统关动画边界。
**这些是控制流契约，不是正式自包含 publish 的 UI／动画验收。**

## fake capabilities 与真实来源的区分

- `IServerCapabilities.Sources` 默认空表是合法的旧 fake 单服路径。SettingsViewModel 用
  `IsConnected/InterfaceFlavor/IsAdministrator` 判断；页面活动期未开时根本不会调用它。
- 多服探针明确提供三条 `ServerSettingsSource`，作用域取真实 `fixture.Session.Sources`；
  availability、当前身份、权限 revision 与来源键集合仍由实际 Scope/VM 检查。
  原夹具三个 `SelectAsync` 默认均恢复为 Emby，只在 fake capability 中把 Kind 指定为
  Emby/Jellyfin/FnNas。新离线回归保留并验证这个输入，不把它冒充三产品真实会话。
  多服插件调用走接口默认空表，因此没有证明每服插件传输；既有真实 capability lifetime
  回归另覆盖迟到读取。没有依据把这两条目录失败归因 WinUI 版本或生产服务器数据。

## worker 验证命令

先在当前树成功编译 Tests/Core 和 Shell。沿用 SDK `10.0.400` 及所有原 pinned 包；新树只从
固定缓存离线 restore。`NuGetAudit=false` 仅用于该次本地还原，不改仓库审计策略或依赖。

```powershell
$root = (Get-Location).Path
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$cache = 'C:\Users\89400\.nuget\packages'
$project = Join-Path $root 'tests\Momoka.SettingsProbeContractTests\Momoka.SettingsProbeContractTests.csproj'
$shell = Join-Path $root 'src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.dll'
$tests = Join-Path $root 'tests\Momoka.Tests\bin\Release\net10.0\Momoka.Tests.dll'
$env:TEMP = Join-Path $root 'work\settings-probe-contract-tmp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null

& $dotnet restore $project --source $cache --packages $cache --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '离线 restore 失败，停止' }
& $dotnet build (Join-Path $root 'tests\Momoka.Tests\Momoka.Tests.csproj') -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前 Release 构建失败，禁止运行旧 DLL' }
& $dotnet build (Join-Path $root 'src\Momoka.Shell\Momoka.Shell.csproj') -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前 Release 构建失败，禁止运行旧 DLL' }
& $dotnet build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前 Release 构建失败，禁止运行旧 DLL' }
& $dotnet run --project $project -c Release --no-build --no-restore -- $shell $root
if ($LASTEXITCODE -ne 0) { throw '测试失败，保留日志并停止' }
& $dotnet run --project $project -c Release --no-build --no-restore -- core-settings $tests $root
if ($LASTEXITCODE -ne 0) { throw '测试失败，保留日志并停止' }
```

第二条 run 反射调用**本树已成功编译当前 Tests DLL** 的 SettingsTests／ServerSettingsTests／
StartupEntryTests.Register 和其实际 TestHarness，不修改共享 runner，不复制或筛掉失败用例。
共 154 项；它不是全 Core（全 Core 含用户禁止的回环网络／共享自检 mutex 等路径）。
正式使用脚本须在每条 build/restore 失败时立即停止，不能执行旧 DLL。

## 本次证据与未通过项

本树 `artifacts/delivery-settings-probe/` 保存 restore／Release build／各 run 的日志与退出码；
不会合并这些 DLL 或报告到主树。Tests/Core、Shell、SettingsProbeContractTests、
SettingsLifetimeTests、PageContractTests 当前 Release 构建均 0 警告／0 错误。

| 离线套件 | 本次结果（互不累加为全套） |
| --- | --- |
| Settings 探针新契约 | 23 过／0 败／0 跳过 |
| Core Settings／ServerSettings／StartupEntry 相关 | **153 过／1 败／0 跳过** |
| SettingsLifetime 原页面／VM | 45 过／0 败／0 跳过 |
| PageContract 源链接页面 | 98 过／0 败／0 跳过 |
| settings-page-state | 27 过／0 败／0 跳过 |
| settings-applicability | 29 过／0 败／0 跳过 |
| startup-state-contract | 61 过／0 败／0 跳过 |
| startup-reapply-notice | 34 过／0 败／0 跳过 |
| settings-screenshot-restore | 14 过／0 败／0 跳过 |

Core 红项是 `SettingsTests`「原子写入：同目标并发文本和字节写入不争用临时文件」：
`AtomicFile.Write` 的 `File.Move` 抛 `UnauthorizedAccessException`；保留一次失败的日志与
退出码 1，不重跑洗绿，不改该产品实现。本任务两份探针没有修改这条路径；不能据此宣称
全 Core 或最终交付通过。范围外原因和发生条件需要属主另行调查。

原正式可见 full 101 项中服务器页的「根里还有别的页（2个孩子）」有独立范围外源码证据：
`ShellNavigationProbe.FnNasLibrary.cs` 的 FnNasLibrarySpacingAsync 向共享 root Add(frame)，
finally 仅 `frame.Content = null`，没有 Remove(frame)。该用例位于 Settings 之前；
留下的空 Frame 可解释这个计数，但未用真实 UI 重新确认。这两份 Settings 文件没有
清空共享 root，也没有放宽 `ServersPageSummaryAsync` 的唯一直接孩子断言。需主线程在
该属主文件补正确清理，并对正式最终 full 确认。

普通 framework-dependent bin 通过、带读数 diagnostic 自包含 section=shell 通过，均不能
替代正式最终 full。当前无法凭证据归因 runtime 版本；首窗口显示／布局负载的时序解释
仍只是待验证推测。真实自包含 Settings 动画／宽窄导航／截图，以及最终 full 由主线程
串行完成；worker 没有运行这些 UI，也没有发布／改快捷方式／push。
