# R-065 启动登记读取／操作失败契约

## 范围与边界

本专项链接完整 `src/Momoka.Shell/Platform/StartupRegistration.cs`，Core 的 StartupEntry 仍是原纯规则。
测试程序集在相同 namespace 提供内存 Registry／MSIX 问询端点，拒绝其他路径和值名；不会调用实际
Microsoft.Win32.Registry 或 Windows 包身份 API。生产源码与控制流没有拷贝改写。

SettingsViewModel／SettingToggleRow 来自**当前工作树明确指定的 fresh x64 Release Shell DLL**，仅执行
匿名 Attach、实际 StartupRow 初始化、实际生成的 IsOn setter 和原回调。假 ISettingsService 只在内存
计数，不做配置／账号／服务器 IO；其他依赖不附加，不调用整页 Reload、字体、设备枚举、native、播放或应用入口。
初值、回拨、通知都是实际 VM／行属性证据，不是 WinUI 控件、像素、键盘或导航验证。

界面卡的 Available 门控和自检的单条启动状态判断在构建时按明确边界提取原样源码，缺失或歧义拒绝构建。
只替换无窗口行值和报告端点；不执行真正 InterfaceCard（会创建主题画刷）或真正 selfcheck，不占用户 UI／selfcheck mutex。
`StartupEntryTests` 的四项纯规则在专项里重跑，属于既有覆盖，不可与主 Core 套件重复计数。

**状态裁定：读取失败条件下的产品逻辑反例已复现并纠正；真实 ACL、组策略、安全软件拦截、部署登录、
实际 Registry 和 WinUI 没有验证。** 内存异常是输入，不是本机故障证据；不能由 Run 值保留推断真实 Windows 必然自启。
历史审查记录及证据 JSON 保持原样，部署候选前件继续保留。

## 最小实现取舍

- IStartupRegistration 的 Available／IsEnabled／Apply 签名不变。IsEnabled 读失败保留权限／安全／IO异常，
  与不存在的 null 分开；Apply 写入后无法回读仍按原契约返回 false，不逃逸或冒充确认成功。
- StartupRow 保留原二态开关、说明和不可用整行隐藏；不增加未知态、重试按钮、设置偏好或新 UX。
  初读失败沿用原行默认 false，同时通过已有 Error InfoBar 明确“无法确认当前状态，请在系统启动项中核对”。
  此时 false 不是确定关闭事实，这是不新增控件下的明确代价。
- 回读失败撤掉未确认的用户目标，保留上一次成功读取的值；该旧读数同样不保证现在状态，Error 仍明确无法确认。
  回读准确就按原规则回拨；Apply=false 即使准确读数恰好等于目标也报失败，不静默吞掉部分操作。
- 后续本行操作确认成功才撤自己的旧 Error；不新增成功反馈，也不清除别的操作的不同标题／消息通知。
  原行的假设置 TrySave 回调保留，本专项不把它变成真实设置写入。
- 所有调用者已核对：SettingsViewModel、ShellSelfCheck.Settings、ShellNavigationProbe 的 FakeStartup、
  SubtitleSettingsProbe 的 OfflineStartup，以及 ShellServices／SettingsPage／selfcheck Attach 入口。
  两处纯内存 fake 不抛读取异常且不用改签名。自检在原「开机自启那一行」只读一次，读取失败仍记失败，
  不新增／退役检查，不放宽基线。

## Gibbs P2：恢复／导入共享尾部归属

原61项只覆盖单行与自检窄读取，不覆盖 `ReapplyAndReloadAsync` 的恢复收尾。
新增 `startup-reapply-notice-fixture` 在构建时提取**完整原样 ReapplyAndReloadAsync** 和 Reload 的实际前缀，
主题／ShellPrefs／其他整页卡片端点是无操作；StartupRow、通知、播放偏好推送尾部仍调用本树实际匿名 VM。
不执行用户恢复默认、导入、文件框或真实配置；两个真实 caller 另核只 await 同一个尾部。

修前同步 Reload 已报 startup Error，后续成功偏好推送却以 Success／未保存Warning盖掉它；
独立34项为18过16败0跳过。最小补修在 Reload 开始清本轮启动初读失败，StartupRow 初读失败记原因，
共享尾部在等字体／设备 Task **之前**快照本轮原因，再显式交给原播放偏好收尾。
只有本轮原因不为空且原推送成功时保留既有 Error；旧startup错误、别的通知、后续另一行读错不笼统拦成功。
原推送失败／取消仍按原规则处理；没有新UX或接口签名变更。

34项覆盖两caller、Run／Approval权限／安全／IO、成功／未保存Warning、先前错误、另一轮同步/等待归属、
原推送失败／取消以及直接播放偏好尾部无Reload归属时仍可成功。实际反射调用者
`SettingsScreenshotRestoreTests` 同步内部可选参数并独立14项复验；不是复用早期61绿证明共享尾部通过。

## 运行命令

只在当前工作树根执行；缺包停止，不改用网络源，不复制其他树的 obj／DLL 来假装 fresh。
本机离线源和 packages cache 固定为 `C:\Users\89400\.nuget\packages`，SDK 为10.0.400。
还原只针对 Shell 和既有 Momoka.Tests；不跑全 Core／全方案、publish 或真实 UI／selfcheck。

```powershell
$tree = (Get-Location).Path
$dotnet = 'C:\Users\89400\.dotnet\dotnet.exe'
$shell = Join-Path $tree 'src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.dll'
$env:DOTNET_CLI_HOME = Join-Path $tree 'work\r065\dotnet-home'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $tree 'work\r065\nuget-http'

$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $dotnet restore .\src\Momoka.Shell\Momoka.Shell.csproj --source 'C:\Users\89400\.nuget\packages' --packages 'C:\Users\89400\.nuget\packages' --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '离线还原失败，停止；不联网补依赖' }
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $dotnet restore .\tests\Momoka.Tests\Momoka.Tests.csproj --source 'C:\Users\89400\.nuget\packages' --packages 'C:\Users\89400\.nuget\packages' --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '离线还原失败，停止；不联网补依赖' }
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $dotnet build .\src\Momoka.Shell\Momoka.Shell.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前 Shell 编译失败，不运行旧 DLL' }
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $dotnet build .\tests\Momoka.Tests\Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '当前测试编译失败，不运行旧测试' }
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $dotnet run --project .\tests\Momoka.Tests\Momoka.Tests.csproj -c Release --no-build --no-restore -- startup-state-contract-fixture $shell $tree
if ($LASTEXITCODE -ne 0) { throw '启动状态契约回归失败' }
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $dotnet run --project .\tests\Momoka.Tests\Momoka.Tests.csproj -c Release --no-build --no-restore -- startup-reapply-notice-fixture $shell $tree
if ($LASTEXITCODE -ne 0) { throw '恢复／导入启动读取失败共享尾部回归失败' }
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& $dotnet run --project .\tests\Momoka.Tests\Momoka.Tests.csproj -c Release --no-build --no-restore -- settings-screenshot-restore-fixture $shell $tree
if ($LASTEXITCODE -ne 0) { throw '相关截图恢复反射调用者回归失败' }
```

`NuGetAudit=false` 仅这次固定缓存的离线还原，不修改仓库源或审计设置。构建用原包／分析器，没有新工程或包。
`desired-invariants` 可作为上述测试开关的末尾兼容参数，仍执行同一套正确契约；**正式入口不再提供将旧错误算通过的模式**。
错参数退出2，错误树／非标准程序集路径在加载前拒绝；不会退回全 Core 跑器。

## 本树红／绿证据

树：`C:/Users/89400/.codex/worktrees/r065-startup-state-contract/Momoka`。
基线：`cfde3c746bbdb4664122bfe3b4cbbe9096c8b13e`。证据目录仅为本树 `work/r065`，日志／产物不入 Git。

| 证据 | 实际结果与含义 |
| --- | --- |
| `build-red-shell.log`、`build-red-tests-2.log` | 未改产品的当前基线编译，0警告0错误。 |
| `red-startup-contract-1.log` | 初版正确契约34项：17过17败0跳过，退出1；含真实 VM 拒删+Run回读失败无通知及 Approval错误初值。 |
| `desired-invariants-final39-red.log` | 扩充正确契约39项：19过20败0跳过，退出1；加入拒清Approval+回读失败。 |
| `candidate-confirmation-final39.log` | 当时39绿仅确认旧错误/旧护栏可重复，**不是修复通过**；旧模式源码只保留在本地 baseline 证据。 |
| `build-green-shell-final.log`、`build-green-tests-final.log` | 第一轮61项窄 patch fresh Release，0警告0错误；不是共享尾部P2通过。 |
| `green-startup-contract-final61.log` | 第一轮单行／自检61过0败0跳过、退出0；不覆盖Gibbs共享尾部P2。 |
| `green-related-settings-page-state-final.log` | 既有共享保存/目录实际VM回归27过0败0跳过，独立报告，不合并为R-065新增覆盖。 |
| `red-startup-reapply-p2.log` | 实际共享尾部34项18过16败0跳过，退出1；最终Success／Warning覆盖本次Reload的startup Error。 |
| `build-reapply-p2-green-shell-1.log`、`build-reapply-p2-green-tests-1.log` | 含P2补修的最终当前Shell／Tests Release，0警告0错误。 |
| `green-startup-reapply-p2.log` | 最终共享尾部34过0败0跳过、退出0，两caller同一原样生产tail。 |
| `green-reapply-p2-related-startup61.log` | 最终源码下既有启动61过0败0跳过，不冒充共享尾部覆盖。 |
| `green-reapply-p2-related-screenshot.log` | 必需反射caller同步后相关截图恢复14过0败0跳过。 |
| `green-reapply-p2-related-settings-state.log` | 最终已有设置页状态27过0败0跳过，独立相关回归。 |

61项包含4项旧 StartupEntry 纯规则、实际VM初始化/正常写/失败与恢复、内存平台注册读写、Available原门控及自检原样窄判断。
最终61与共享尾部34、截图恢复14、设置页状态27独立报告，旧39和原Core不相加；这不是最终统一交付闸门。

关键旧回执为 `Run仍在=True；行=False；Apply=False；通知=False`，修后为
`Run仍在=True；行=True；Apply=False；通知=True`，并明确“无法确认当前状态”；不是保证登录启动。
Run可读+Approval读取失败的旧VM显示开启、无通知，修后原二态默认值伴随明确读取错误。

初次fixture生成缺 `#nullable` 的两警告、候选确认模式对null消息的断言调试9败，以及首次产品patch缺Diagnostics导入
引发的编译/XAML失败单列保留，**不计产品反例**。所有有效产品红均在产品改动前运行、对应基线源码SHA-256，
见 `baseline-source-sha256.txt`／`red-run-manifest.txt`；最终证据hash另记录在本树。

未修改真实注册表、真实设置、凭据、账号、服务器或历史审查文档／JSON；未运行native、播放、产品窗口、自检mutex；
未publish／push／改快捷方式。主线程后续统一同步编译／验收，不把独立本地提交称为完整交付。
