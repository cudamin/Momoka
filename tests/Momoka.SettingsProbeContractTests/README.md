# Settings 探针契约：160ms Rows 适配（2026-10-27）

本节是当前用法；下方 2026-10-24／25 内容为历史记录，不代表这次验证结果。
本轮独立 managed worktree 从 `60414a7` 起步：
`C:\Users\89400\.codex\worktrees\settings-probe-adaptation\Momoka`。
四个测试文件以主树当时的未提交内容为种子，包括滚动选项边界、同页复位和托管页短动画用例。
提交仅包含 `UiBoundaries.cs`、`Program.cs`、项目文件和本 README；
最新主树生产源码及 Release DLL 的副本只是本树未提交／ignored 验证输入。

## 当前契约与迁移边界

- 保留原 24 项，包括无活动票、缺动画、未 Loaded／未注册和原快速开关的失败夹具；新增 6 项，
  本轮共 **30 项，通过 30、失败 0、跳过 0**。没有删除 Require、延长等待或复制 VM 显隐算法。
- 完整源码链接当前 `SettingsPage.Motion.cs`、`SettingsRowsMotion.cs`、`HomeMotion.cs`，
  页面构造／活动票／Release、探针等待／入场采样／快速切换块仍由构建任务原样提取。
  起止标记缺失或重复仍失败；`SettingsProbeSourceRoot` 默认本树，显式传值时所有生产链接和提取统一用该根。
  构建嵌入输入 SHA-256，运行前逐项检查，源码变化后不能拿旧测试输出冒充当前证据。
- `SettingSection.Rows` 是稳定的可枚举 wrapper，按真实 row 对象缓存包装；Count／Label 和事件均转发。
  封闭泛型携带实际生产 row 类型，保留 `SameRow` 的类型区分；ContainerFromItem 映射回真实 identity，
  幸存行容器不随显隐重建。没有复制 VM setters、属性过滤或偏好恢复逻辑。
- 新边界只提供本地 DependencyProperty、祖先 Visibility、Popup／Canvas／ContentPresenter、
  UISettings 事件、TransformGroup、Rect／相对坐标和可控 Storyboard 时钟。
  `CompleteRows` 只调用 `Storyboard.CompleteAll()`；不再读 `_settling` 或伪造 timer 完成。
- `HasConditionalTransitions` 保留兼容名字，表示可控行动画可用，不表示固定系统 Transition 存在。
  新断言要求 ItemContainerTransitions 为空，真实淡出／补位／Height 请求同为 160ms、无延迟，
  159ms 仍运行、160ms 实际 Completed 落定；还查副本 identity／不可点击／无 Tab／Raw 树、
  旧 Completed 不清理替换动画、Release 清理、祖先隐藏、后台系统 High 优先取消和真实离树退订。
- 旧快速开关反例的**缺接线前件**随新实现迁移：Rows 自己订阅偏好后，仅没有页面活动票不再算缺接线。
  现在省去实际 Rows Loaded handler 并注入未清理的高度／Clip，原生产快速开关块的
  “卡片没有恢复自适应高度或裁剪残留”仍必须抛错；宽窄两项失败夹具均保留。
  缓存页 Hide／Release 只 Stop，真实行 Unloaded 才 Detach，不能把两个生命周期混为一谈。
- 同页分类复位包含 Drain 前重复 Queue／Loaded 不取消唯一复位；拒绝入队或 Layout 抛错不记分类完成，
  重试仍复位。设置页祖先下默认 Reveal 为 180ms／12px，其他媒体页默认时长保持原生产常量。

## 定向离线命令

在本树运行，使用用户级 SDK；只构建相关项目／引用 Core，不执行全 Core／共享自检 mutex、
App、GUI、probe、发布或 push。先成功 restore 和当前 Release build，再用 --no-build：

```powershell
$root = 'C:\Users\89400\.codex\worktrees\settings-probe-adaptation\Momoka'
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$project = Join-Path $root 'tests\Momoka.SettingsProbeContractTests\Momoka.SettingsProbeContractTests.csproj'
$env:TEMP = Join-Path $root 'work\settings-probe-contract-tmp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
& $dotnet restore $project --disable-parallel -m:1 -p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) { throw 'restore失败，停止' }
& $dotnet build $project -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -p:SettingsProbeSourceRoot=$root
if ($LASTEXITCODE -ne 0) { throw '当前Release构建失败，禁止运行旧DLL' }
# 独立树的标准输出目录须预先有主线程确认成功后的current Shell及依赖DLL副本。
$shell = 'C:\Users\89400\Momoka\src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.dll'
& $dotnet run --project $project -c Release --no-build --no-restore -- $shell $root
if ($LASTEXITCODE -ne 0) { throw '契约失败，停止' }
```

普通本树验证可把 `$shell` 换成本树已成功编译的标准 Release 路径。
显式主树输入须与本树副本 SHA-256 一致；仍由共享 ShellRuntime 验证并加载**本树标准路径**，
日志明确注明实际来源为主树 current output，不把复制的 DLL 说成本树 Shell build。
本轮只运行上面的契约，不运行下方历史 `core-settings` 命令。

## 本轮生产输入与结果

相关项目／引用 Core 的 fresh Release build：**0 警告、0 错误**；契约 **30／30**。
源码使用主树当前 resetScroll race 与 LayoutCycle 修复，运行日志和完整输入 SHA-256 清单在：
`C:\Users\89400\.codex\worktrees\settings-probe-adaptation\Momoka\artifacts\settings-probe-adaptation\contract-run.log`。
关键 SHA-256：

| 输入 | SHA-256 |
| --- | --- |
| SettingsRowsMotion.cs | `A0951808F9E82002AC50B0A6B6AE38CCD7FC56FB8910EE9E01515E51DCEF8604` |
| SettingsPage.Motion.cs | `A77211C946A7F3115D33B57E4A3A28E0DAEA53BD91407978CE3D4DE9BDB7F868` |
| HomeMotion.cs | `CD0783443D2AB1F6C36D9BBE8E0A9BA558319195AB243000271F01C54DB0D515` |
| 主树 current Momoka.dll（用户确认新 Release build 成功） | `E44CDB35C6777942E4527F4C6EB5D5F744ECCAB2A03FE513B97E7590D90FDAF3` |

这些是实际控制流、请求时长、边界值与取消行为的离线证据，不是 WinUI 帧时间／真实模板、
滚动像素／焦点或 LayoutCycle 消失的 UI 证明。页面与副本的真实布局／输入仍由主线程串行验证；
不把本轮结果与其他 Rows／Priority 专项累加，也不宣称全 Core 或发布通过。

---

## 历史 worker 记录（保留原失败证据）

> 主线程整合（2026-10-24）：本项目已加入实际方案，与 HomeOrder／motion 专项一起当前编译；下面 worker 数字与前件描述保留历史边界，不代表最终发布已通过。最终 full UI 和自检状态见主树 PROGRESS 及 delivery08 证据，主线程已定点清理 FnNas 所属 Frame。

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

构造器新增弹幕事件边界；VM 的 `OnDanmakuPreferencesChanged` 转发实际方法，不复制逻辑。
复用 SettingsLifetime 的真实签名适配；原活动票、来源/权限、类别源和动效断言均保留。
当前本专项 **23 过/0 败/0 跳过**；SettingsLifetime 50、HomeOrder 16 分别报告，不相加为本专项。

纯合并主 runner 一次当前结果为 **2912 过/0 败/0 跳过**。单独 `core-settings` 按上文命令复跑
当前 154 项得到 **153 过/1 败/0 跳过**，仍是 README 已记录的
`原子写入：同目标并发文本和字节写入不争用临时文件`，`AtomicFile.Write` 的 `File.Move`
抛 `UnauthorizedAccessException`。保存 `run-core-settings-current.log` 的失败，不重复跑洗绿，
不换失败断言、不改 AtomicFile、不以主 runner 的单次通过掩盖此独立红项；本轮不能宣称最终全套交付绿。
