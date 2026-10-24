# R-042：current identify 来源调用契约定向验证

## 结论

起点 `cfde3c746bbdb4664122bfe3b4cbbe9096c8b13e` 上，**R037／R039 的既有修复已覆盖 R042 指向的 canContinue 写入边界**。本题不改 AniBtService、VM、UX、历史报告或 records，只补定向回归和证据。

- current：**31 项，31 通过、0 失败、0 跳过，退出 0**；18 项 Core／scope 合同，13 项 caller／VM／惰性 Dialog 合同。
- 合入前精确源码对照：**13 项，7 通过、6 失败、0 跳过，退出 1**；失败是旧服务在委托已失效后仍发 Apply，或 Apply 已发后继续 Refresh。编译均 0 警告／0 错误。13 项是 current 的子集，不能与 31 相加。
- 当前被测 [AniBtService](../../src/Momoka.Core/AniBt/AniBtService.cs) blob 与既有提交 `1b37ca14bbbdf5699ac686aac891a7b2e14c9bdf` 完全相同：Git blob `548f4006b1689fa6f7b3e4d6f6f5c09290f60e4a`，SHA-256 `B41E15273BC8586DF224EFAF0B5F76F76F7A5056E27BB2918D179CB356ACEE4A`。没有为了数量重新修已有保护。
- 历史 R042 的**真实部署／模态可达性仍保持条件性**。匿名 HTTP 等待点能构造实际调用前件，不证明真实 ContentDialog 中独立设置窗可切来源，也不证明真实服务器已经触发或造成损失。

## 实际 current 调用链

以下行号对应本题起点；输入 SHA-256 见 [current-input-manifest](evidence/current-input-manifest.json)。

1. [ItemCommands](../../src/Momoka.Shell/Views/ItemCommands.cs):437–439 在 AniBtScrape 分派前核 `scope.IsCurrent`。[ShellPage.AniBt](../../src/Momoka.Shell/Views/ShellPage.AniBt.cs):33–42 同样要求 current scope、可用 MP，再构造实际 Dialog。外层这两处是嵌入真实源码的唯一标记／内容合同断言，**不是菜单或 ShellPage 的运行测试**。
2. [AniBtScrapeDialog](../../src/Momoka.Shell/Views/AniBtScrapeDialog.xaml.cs):25–37 的实际 primary handler 区分预览与确认；Dialog 的 InitializeComponent、事件和控件值是惰性替代，没有 HWND、WinRT、模态或真正 XAML 编译。
3. [AniBtScrapeViewModel](../../src/Momoka.Shell/ViewModels/AniBtScrapeViewModel.cs):52–56、96–123、131–147、187–188 实际执行 `CanPreview/CanSubmit`、`PreviewAsync/SubmitAsync`；传入服务的 `Current` 同时核活动期、`scope.IsCurrent`、捕获连接身份和选择版本，提交还核封存候选一致。MVVM 使用与 Shell 一样的固定 CommunityToolkit.Mvvm 8.4.2 生成器，不复制 VM 状态机。
4. [EmbySessionScope](../../src/Momoka.Core/Emby/EmbySessionScope.cs) → [EmbySession](../../src/Momoka.Core/Emby/EmbySession.cs):94–105、126–128、471–489。夹具调用**实际 `SelectSource`**；聚合开关为 true 时旧 A 仍 available、lifetime 未取消，B current。`ExecuteAsync` 只守 available 并链接 lifetime，原本允许固定 A 的后台任务完成；不能给它全局加 current 守卫。设置保存是本题内存 IO 端点，未读写任何设置文档。
5. [AniBtService](../../src/Momoka.Core/AniBt/AniBtService.cs):181、217 在只读准备开始／结束核 canContinue；243–252 在提交入口、整批 history 重核后／Claim 前核 canContinue 和封存连接；304–307 在 transfer 后的 identify 前再次核；492–497 在 Apply 前、Apply 后／Refresh 前核。封存 identity 核对在 86–89，扫描另保原来源委托。**这些保护都来自 R037／R039，不是本题产品补丁。**

[MoviePilotDialog](../../src/Momoka.Shell/Views/MoviePilotDialog.cs) 只监控 MP 可用性／连接，不能把它当 Emby 来源变化或真实 modal 可达性的证明。本题对此也有当前源码合同断言。

## 重点回归

- identify-only 初始 `canContinue=false`：RemoteSearch、Apply、Refresh 均 0。
- 当前实际 RemoteSearch 等待时聚合 A→B：A 仍 available，最终不返有效确认凭证，Apply／Refresh 0。**已发出的读取不能倒算成 0**；本题不为只读尾项添加产品守卫。
- 一／两项 transfer 的实际 `preview:false` 回话等待时委托失效：保留已经成功的回执，identify／旧库扫描 0；首项后切换也不发后项。
- Apply 已发且回话未交回时切来源／取消选择版本：Apply 1、Refresh 0，不重放 Apply；不把既有 R024 的识别结果未确认文案算成本题修复。
- 当前 VM 的 Load、已准备未确认、预览等待、混合提交、Apply 等待分别执行实际 scope／connection／version 合同。
- 非聚合 `SelectSource` 真实走 PruneOthers 撤销旧 lifetime，等待读取取消；保留普通取消行为。
- 明确没有 canContinue 的固定 A 后台任务仍可完成，且不能把 A 的封存计划交给 B client；不会写错服务器。这个支持契约对照不是 AniBT 当前 VM 路径漏洞。

## 运行

本项目**不注册方案或共享测试 Program**，由主线程统一同步验证时决定是否纳入。只执行本题 31 项，不运行 R017、R065、R037／R039 全套、全 Core、UI、自检或用户级 mutex。

从当前独立工作树根目录执行：

~~~powershell
$root = (Get-Location).Path
& (Join-Path $root 'tests/Momoka.AniBtSourceContractTests/Run-Fixture.ps1') -Label 'current'
# 已有本树固定 local-feed 时可避免复制，但每次仍重新离线 restore、新 obj/bin：
& (Join-Path $root 'tests/Momoka.AniBtSourceContractTests/Run-Fixture.ps1') -ReuseLocalFeed -Label 'current-again'
# 预期退出 1，是原红；不是 current 失败，更不是“跳过”或通过：
& (Join-Path $root 'tests/Momoka.AniBtSourceContractTests/Run-Fixture.ps1') -HistoricalRed -ReuseLocalFeed -Label 'historical-red'
~~~

SDK 固定 `C:\Users\89400\.dotnet\dotnet.exe` **10.0.400**；Release、`--no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false`。第一次只复制本机已缓存的四个固定 nupkg；restore config 只含本树 feed、无 fallback，缺包立即停止，NuGetAudit=false 仅当次 restore。CLIhome／temp／packages／HTTP cache／每轮 obj、bin、生成文件／日志都在本树 `artifacts/r042/`，`DOTNET_GENERATE_ASPNET_CERTIFICATE=false`。当前成功编译后才执行该轮 DLL；不跑旧输出，不 publish／push／改快捷方式。

`-HistoricalRed` 只把 `3f86b51f8202cac0328d9a10f9945ce9df15969a` 的四份精确 Git blob（AniBtService、VM、Dialog code-behind 与 XAML）复制到本树该轮 evidence 子目录；不 checkout／restore／reset，不覆盖工作源码。版本桥只选择 actual old Submit 或 current Prepare/Submit，**不模拟业务规则**。其它 Core 是 current，故对照不能冒充完整历史应用；旧 caller 的新契约用例不参与 13 项红测。

## 规范与证据

本轮只读取 [AniBT 公开 OpenAPI](https://wiki.anibt.net/openapi.yaml)，原件在本树 `artifacts/r042/public/AniBT-openapi-2026-10-24.yaml`，179513 字节，SHA-256 `3ADB5D6D886F40B49529F9608010AAFC142E55040680AD130DA607CA58A503FA`。读过 matcher 的匿名 POST rawTitle／直接 version3／candidates 相关窄段，并联读仓库 AniBtTests、AniBtIntegrationTests、AniBtReceiptStateTests 的相关契约；没有访问真实 AniBT matcher、RSS、下载、发布、MP／Emby 服务器，也未据公开规格推用户部署版本。

- [current 结果](evidence/current-result.json)／[current 测试](evidence/current-test.txt)／[current 编译](evidence/current-build.txt)。
- [原红结果](evidence/historical-result.json)／[原红测试](evidence/historical-test.txt)／[原红编译](evidence/historical-build.txt)。
- [全部执行与覆盖边界](evidence/verification.json)／[固定包哈希](evidence/package-manifest.json)／[原红输入](evidence/historical-input-manifest.json)。
- 入库的 `.txt` 是**保持原文字的 LF 规范化副本**，原始日志和其原 SHA-256 在 ignored artifacts 原位保留；verification 分别记录原日志与规范化副本哈希，不把两者字节冒充相同。
- [首轮夹具失败日志](evidence/current-initial-test.txt)与[历史首次编译失败](evidence/historical-first-build-failure.txt)单独保留。首轮 current 31 项 30 过／1 失败是夹具把非聚合保存次数预期写成 1，实际 SelectSource＋PruneOthers 为 2；早退导致共享 UiQueue 迟到 Post 异常，已修正夹具。历史首次项目 Compile 排除失效造成 15 编译错误，未跑旧输出；改显式条件 Compile 后真实红测 13 项 7 过／6 败。输入manifest误猜文件名和一次未执行的编辑命令语法错也在 verification 中保留；不拿这些工具失败当产品反例。

## 未验证／不改变

真实 modal／WinUI 焦点、设置窗口交互、XAML 生成绑定、像素、真实线程 check-send 极短竞态、A→B→A 瞬时回切、真实服务器接受／完成／原子性、登录／重新认证／凭据／DPAPI／媒体文件／native／播放均未验。伪 HTTP 和惰性调度只能证明此 current caller 合同，不证明真实用户路径触发。历史 R042 继续条件性；所有正式产品代码和历史 records 保持不动。主线程负责最终 Core／方案同步，本题不是发布交付，快捷方式仍旧版。
