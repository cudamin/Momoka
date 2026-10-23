# R-027：帐号保存期间的新草稿

独立离线回归项目，已最小注册到方案构建，仍需独立运行；不改 `Momoka.Tests/Program.cs`。完整 source-link 生产 `FnNasPasswordViewModel`、`FnNasPreferencesViewModel`、`FnNasSessionViewModel`、`PageViewModel`、`FnNasAdminRows`，以及实际 `LoadGeneration` / 错误文案类型；与 Shell 同用固定 `CommunityToolkit.Mvvm` 8.4.2 的真实属性和命令生成器。没有复制修好的保存模型。

## 运行

在要验证的当前工作树运行，SDK 固定为 `C:\Users\89400\.dotnet\dotnet.exe` 10.0.400：

```powershell
$tree = (Get-Location).Path
$runner = Join-Path $tree 'tests/Momoka.FnNasAccountEditTests/Run-Fixture.ps1'
& $runner -RunName current -Restore -BuildShell -Format
```

首次 restore 将本机既有 `C:\Users\89400\.nuget\packages` 中的固定 `.nupkg` 归档复制到本树 `artifacts/r027-account-edit-generation/fixed-feed` 并比 SHA-256，只用该快照；缺包立即停，不请求网络、不改源、不升级包。`NuGetAudit=false` 仅当次离线还原，不写项目或仓库配置。CLI home、临时文件、NuGet HTTP / plugin cache、日志均在本树 `artifacts/r027-account-edit-generation`，解包缓存在本树短路径 `work/r027/n`，不回写共享缓存。`DOTNET_GENERATE_ASPNET_CERTIFICATE=false`。

Release build 使用 `--no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false`。仅本次构建成功后运行 `--no-build --no-restore`；失败或红项保留日志并退出，不能使用旧 DLL 或改计数。Shell format 的设计期 `Platform=x64` 与实际构建一致；缺少该全局值会错误地找 `Core/bin/Release`，不以带工作区加载警告的退出 0 当作通过。每次运行有独立目录、命令退出码和全部输入 SHA-256。

已还原之后省略 `-Restore`，只跑 VM 项目可省略 `-BuildShell -Format`。不会启动 Shell。不要把本专项成功等同于发布或完整产品验收。

## 同 fixture 基点红

可以从精确 Git blob 提取两个旧 VM 到本树独立目录，不覆盖产品文件。只在这次树内使用以下输入，然后必须重新编译当前源才能报绿：

```powershell
$old = Join-Path $tree 'artifacts/r027-account-edit-generation/baseline-source'
& $runner -RunName baseline -SourceRoot $old `
    -ExpectedPasswordSha256 '200A337511317C6CC5B23A27FF1066C40BECB44E142A601A59B23FAA81D0939D' `
    -ExpectedPreferencesSha256 '01C7CB1120C7E811D01AC664F85E7CC153E423B3781F24B552B89D1DE73B97C7'
```

这些 SHA-256 对应本题基点 `3f86b51f8202cac0328d9a10f9945ce9df15969a` 的 blob `e63999a69bb84328ff2d2ece85fdd7f9034ec71e` / `0f63da971eb37e84aec49fafa91b0c411539e63b`，不自动取得另一棵树或替换生产文件。没有上述输入目录时明确失败；它不是当前验证的依赖。基点最终相同 68 项为 **48 过 / 20 败 / 0 跳过 / 退出 1**；修后 **68 过 / 0 败 / 0 跳过 / 退出 0**。首轮 fixture 的 2 个编译错误、一次含 2 个挂起任务超时的早期红及全部后续红均保留在本题 PROGRESS 与日志，未算作有效最终验证。

## 68 项覆盖

- 直接执行真实 `SaveAsync`，以及生成 `SaveCommand.ExecuteAsync` / `CanExecute`。真实生成属性可在 Busy 期间编辑；两字段、仅一个字段、改后返回原值（编辑代次 ABA）、同值赋值均覆盖。
- 假确认端点挂起 P1 / L1，改 P2 / L2 后才确认；仍只发送首次快照。Busy 的同步属性通知或延迟 Execute 回调也不借用当前草稿构建写入。
- 正常密码成功清本次草稿，后来编辑保留；正常语言成功维持选择和原成功文案。新草稿显示已确认本次提交但尚未保存，并可通过现有“保存修改”显式重试，只有重试追加一次假写，无自动重发。
- 确认已到、UI 收尾尚在队列时再编辑；失败保留原 / 新草稿和错误；语言成功后再次编辑撤掉旧完整成功提示，失败后编辑不掩盖错误。
- 未 Attach、空密码 / 未 Loaded、Busy、未知 SourceKey、非飞牛、撤销来源和 Cancel 时，绕过 CanExecute 调用真实方法仍零新发送。
- 实际 shared base 的 CanUse / Current / LoadGeneration：离页旧成功 / 旧失败、匿名注销、默认来源切换、同源 / 异源重 Attach，以及新请求 Busy 不被旧 finally 结束。显式非默认 source 只按该 source 可用性，不误限成默认源。
- 真实 Reload 的 user / config 两次假读取、Loaded 与 stable Languages、any / null 请求归一化。真实两页 XAML 作为编译嵌入资源，检查双向编辑、密码 PropertyChanged、完整 InfoBar OneWay、SaveCommand 和页 SourceKey / Cancel 接线；运行实际属性通知而非自制生成器。

## 安全边界和不覆盖的运行

仅展示 `Visibility` / `InfoBarSeverity` 两枚举以及外部 `EmbySession` / scope / client 的窄假服务是替身。**项目不引用生产 Core 或 Shell 程序集**，生产 `EmbySession.ChangeFnNasPasswordAsync`、FnNasClient、任何认证恢复 / vault / 持久化 / DPAPI / 配置文件 / 网络 API 都没有装载。相同方法名的 fake Session 只是确认端点，不能据此证明 R-010 或真实密码更换 / 令牌保存 / 自动重登。密码 P1 / P2 只在匿名内存产生，断言不打印值，不读真实 secret。

假写记录请求数和确认数，测试失败也不走真实 HTTP 后备。调度前取消是零发送；调度后已确认即记完成，但旧页面不能回填。这验证的是 VM 对确认的草稿处理及 source/page 边界，**不是生产 Session 生命周期或服务端兼容性验证**。

无 App、WinUI 控件 / 桌面窗口、native、UI 像素、真实属性 binding 运行、真实服务器读写、完整 Core / SelfCheck / 用户级 mutex、播放、发布或推送。源码 XAML 契约与编译通过不代替真实 UI；也不继承已停止 R-001 / R-003 / R-005 / R-031 的受限认证链授权。主线程负责整合后串行验收。

## 主树限定整合

来源 `fae6539d8007bdf120dabfcc4648bba8cb422481`；主树只复制两产品与本正式项目，不搬来源进度/旧DLL/日志。当前主树 `artifacts/r027-account-edit-generation/run-main-integrated-20261024-073721-7591` 固定缓存restore、源码fixture和x64ShellRelease零警告错误，68项全过／0失败／0跳过／退出0；两VM及专项格式0修改、无加载诊断。主树索引/方案格式/26PS及来源哈希见 `artifacts/staged-repair/batch05/r027`。来源同68项48过20败精确blob红仍保原树，不冒主树旧产物。该项目只验证实际VM与生成器的草稿确认边界，没有进入生产认证或凭据持久化链，不替代R010。
