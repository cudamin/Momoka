# R-011 同身份当前权限专项（无窗口／无生产认证）

## 验证对象与边界

本项目直接使用当前 Core 的 `EmbySession`／`EmbySessionScope`／`ServerCapabilities`，直接编译链接完整四个飞牛管理 VM、共用 Session/Page VM、Rows 与 Shell 同版 Toolkit 8.4.2 生成器。Settings 使用显式传入的本树当前 x64 Release Shell VM；页面的订阅、选择回调、管理目录刷新、退订和队列处理在每次编译时原样提取，不复制权限算法。UI 的枚举／导航值和调度队列只是边界替身，不创建 WinUI、App、HWND 或 native。

匿名来源只在内存安装；`ReplaceClient` 是实际生产类中的窄无认证／无持久化副作用更新缝，拒绝失效或非同身份来源。测试绝不调用真实 `Authenticate`、`TryRestore`、`TryReauthenticate`、改密、vault、DPAPI 或保存配置。假 HTTP 只接受固定 `.invalid` 来源及明确登记的能力／管理读写，拒绝没有真实网络后备；保护器被调用或未登记请求出现即失败。假写仅核本地执行守卫，不代表服务器越权成功。

新 `AccountCompatibility` 子项目完全不引用 Core／Shell 程序集，只链接完整 password/preferences VM、当前 FnNasSessionVM、PageVM、Rows、纯 LoadGeneration／Failure 及固定生成器；Session/Client 确认、本地保存和展示枚举都是窄 fake。当前源码 9 项与 R010 已提交源码兼容 14 项分开统计，重复覆盖不累加。

R010 模式仅从明确授权提交 `e229a2d75289704d78644a4b41552fad8f061e7e` 读取完整 PasswordVM 与 `ISettingsPersistence` 合同到本树 artifacts；Git blob 逐字核对后编译，使用当前 FnNasSessionVM 和窄确认／本地保存 fake。**不改本树生产 PasswordVM、Page、Store、Service、保存接口或共享 R027/R010 fixture**，不执行任何生产保存／密码链。

## 当前树可重复命令

首次准备固定本机包归档，缺包立即停止，不补网。脚本固定 SDK 10.0.400、Release、单节点、禁 shared compilation/node reuse，设置 `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`。临时目录、包副本、日志和配置仅本树 artifacts。

```powershell
$root = (Get-Location).Path # 必须在要验证的当前工作树根目录
$fixture = Join-Path $root 'tests\Momoka.CurrentPermissionSnapshotTests'
& (Join-Path $fixture 'Run-Fixture.ps1') -RunName current -Restore -BuildShell -Related -Format
& (Join-Path $fixture 'Run-AccountCompatibility.ps1') -Mode All -RunName current -Restore -Format
& (Join-Path $fixture 'Test-SourceContracts.ps1') -RunName current
& (Join-Path $root 'tools\check-scripts.ps1') -Path @(
    (Join-Path $fixture 'Run-Fixture.ps1'),
    (Join-Path $fixture 'Run-AccountCompatibility.ps1'),
    (Join-Path $fixture 'Test-SourceContracts.ps1'))
# 提取拒绝反例可能改变中间生成文件；必须当前重编译后才能再次执行 no-build。
& (Join-Path $fixture 'Run-Fixture.ps1') -RunName after-contracts -BuildShell
```

`-Related` 仅运行本树现有 `Momoka.FnNasDefaultPermissionTests`／`Momoka.SettingsLifetimeTests` 匿名项目，不运行主 Core 套件或 selfcheck，不争用用户级 mutex。首次 restore 用本机归档快照；`NuGetAudit=false` 只用于这次离线还原，不是永久配置。没有改主共享方案或 `tests/Momoka.Tests/Program.cs`，主线程整合后按批次收尾流程登记本项目。

格式检查独立验证专项、Core 和 Shell 的适用文件；MSBuildWorkspace 的 format 可能清空生成文件，因此脚本在 format 后重新编译再测试和读取真实绑定，不用 backup 冒当前生成。

## 覆盖

- 同 server/user identity 的匿名客户端 true→false→true 与 null、同值新确认、ABA；Session 当前读数、旧 Scope 当前权限、能力缓存立即一致；旧 Connection 原令牌／绑定媒体、Identity、Lifetime 保留。
- 默认与显式非默认来源、单／多服务器，管理页重入、实际 RunAsync 首次 Allowed 读取后重入降权、旧在途 Probe／插件、通知排队及 Attach/Cancel/Dispose 活动期；降权本地命令直接调用零写，升权恢复。
- 四个完整飞牛管理 VM 及真实命令生成器，完整 Content 绑定容器、命令通知、偏好／集合保留、旧确认框和读取 ABA；旧队列不能取消已开始的新权限读取。
- 实际 Settings VM／原样 Page 的权限通知接线，降权收完整组并离开旧管理页、升权恢复、显式旧入口即时拒绝；选择通知先交新 SelectedEntry，避免回退分类仍打开旧管理页。
- 完整帐号 password/preferences：`TracksPermissions=false` 不订阅权限／来源变化、不因管理员降升取消确认或清草稿、不重发；R010 本地保存监听与重试合同保持，退订只作用于自己的活动期，不删除其他监听者。
- 17 个源码／绑定／边界检查含真实 Shell XAML 的 ContentVisibility 更新分支与 Toolkit 命令、静态生产成功替换通知接线、非权限认证安全方法逐字不改、SettingsPage 只有权限 hunk、禁写文件和 UTF8/LF、missing/ambiguous 两个提取拒绝。

修前有效 34 项：4 过／30 败／0 跳过；当前行为扩展为 54 项，源码/绑定17、帐号Current9／R01014、既有DefaultPermission36／SettingsLifetime39分别报告。所有红／绿／编译／格式证据均保留在 artifacts，不改审查 records、不盲改 caller50 pins。

## 主线程最小整合

1. 复核本提交的七个产品文件和专项，不复制整个 PROGRESS 或 SettingsPage 覆盖主线程在途工作。
2. `SettingsPage.xaml.cs` 仅三处权限 hunk：当前 `_session` 订阅旁加 `PermissionsChanged`，`StopSourceMonitoring` 中对称退订，再加 `OnPermissionsChanged` 转交已有 `OnSourceListChanged`。保留 R010 独立 `WatchSettingsSaveState`／`StopSettingsSaveState`／`OnSavedState` 及 Navigate/Release 接线，不合并两类事件。
3. R027/R010 的窄 fake Session 需声明 `event EventHandler<EmbySessionScope>? PermissionsChanged` 及已有 `SourcesChanged`，fake Scope 需 `bool? IsAdministrator`／`long PermissionRevision`（可初值 true／0；主动权限切换才递增并通知）。不需要为帐号测试复制生产 ReplaceClient、认证或 vault。完整帐号 `TracksPermissions=false` 不订阅这些事件；保存合同／确认hook仍按 R010 维护。
4. 主线程逐份复核 Session/Scope diff 后，显式只更新 caller50 对应 hashes 并重新执行 caller50／提取边界；本树没有改 `tests/Momoka.NaturalContinuationCallerTests/SourcePins.json`（pins），也未执行该被 pin 阻止的专项。
5. 合入 R010 后仅新专项的 `PermissionMemorySettings` 补合同事件；源码契约的基点／SettingsPage 权限 hunk 断言按两条明确提交分别审查，不能直接接受主树新整份快照。实际 Shell VM 重新编译，匿名专项再验。

## 未覆盖与代价

- 不执行真实 WinUI 像素、输入、焦点、实际窗口隐藏／重开；属性及生成绑定不是视觉验收。没有生产登录／令牌恢复／重认证、真实服务器调权／写入、播放/native/进度或 credentials/DPAPI/config IO。
- 没有全 Core、自检、caller50、发布、push、快捷方式更新；独立改动交回不等于产品完整交付。
- 同身份每个新确认客户端递增权限代次，旧能力／管理读取被拒绝；旧在途管理操作取消后可在恢复权限下显式刷新/重试，已发送的服务器写入仍不能由取消证明撤销。既有帐号保存/播放快照不受管理员权限事件驱动。
- 从属组和页面按现有 Settings 刷新重建，草稿卡集合保留；管理分类被收走时退回本机卡，升权恢复入口但不会自动回跳已离开的管理页。

## 主树组合与追加覆盖（2026-10-24）

来源907cb8d的六产品/16专项以精确blob限定复制；SettingsPage只手工三处权限hunk，保R010保存事件。主树注册两个正常Release项目，窄PermissionMemorySettings补保存事件但仍拒任何保存；旧R027确认fake补权限事件/读数。当前帐号项目默认链接真实ISettingsPersistence，因此Current/R010均14项，证据标识实际当前源码与显式提交快照，非重复加总为28。

当前主树权限54/源码绑定提取17、完整帐号Current14/明确R010快照14、Defaults36/SettingsLifetime45绿；实际Store＋完整密码VM追加权限变更不取消确认/本地重试/保存监听两项，41绿。再追加Dispose后派生Attach监听泄漏（42项41过1败），仅base protected IsDisposed与派生Attach首守卫两行修后42绿，其余PasswordVM/Store/Service合同与5fa9c96逐字相同，源码合同只准这一行守卫，不放宽认证或持久化实现。

caller50的19份来源只根据真实Session/Scope权限diff逐份复核更新两hash，另外17原样保留，当前50全过；提取边界和最终方案/当前组合重新编译见主树`artifacts/staged-repair/batch05/r011`。Contracts在最后预期失败子进程后会遗留LASTEXITCODE=1，主线程保存首17检查全绿但child1记录，成功ledger明确exit0，真正检查失败仍抛/拒绝。各红绿和未执行边界保留，不把来源或旧二进制当主树输出。
