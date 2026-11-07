# 第六批验证入口与源码关联修复计划

> **For agentic workers:** 使用 executing-plans 按任务执行；R17-009 在独立托管树并行实现。用户已授权自行选择必要修复并连续完成。

**Goal:** 让测试入口拒绝错误参数、x64 检查读取实际构建树、站点网格既有检查真正执行。

**Architecture:** 保留现有参数分派，在默认登记前拒绝剩余非空参数；只恢复文档明确的三个兼容尾参。Perf 抽取记录项目根与实际源并验证哈希和来源。SiteGrid 在 search-sites 返回前登记。

**Tech Stack:** .NET 10.0.400、C#、PowerShell、既有无窗口测试。

**Spec:** ../reviews/2026-11-05-full-code-review/R17.md 的 R17-003、009、011；现行 StartupStateContractTests.md 的兼容参数契约。

## Global Constraints

- 基点 4eaec0b；复用 review-recovery-batch3，Perf 独立托管树。主树整合与共享交付由本会话串行完成。
- 版本保持 0.2.5.0，无新依赖，无 UI 改动，无真实凭据、服务器、播放、写入或上传。
- SDK C:/Users/89400/.dotnet/dotnet.exe；还原显式 --packages C:/Users/89400/.nuget/packages。
- Release/x64、单节点、BuildInParallel=false、UseSharedCompilation=false、MSBuildNodeReuse=false；DebugSymbols=false、DebugType=None。
- 直接运行当轮对应输出 DLL；编译失败不运行旧输出。保留原红、拒绝场景和最终绿证据。
- 中文提交正文及 Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>；整合主树后重验并刷新桌面实际交付。

## Review Focus

- 错名、缺参、空参数、多参不得执行默认套件；无参保留默认行为。
- 原合法入口与变长 fake-mpv 子进程保持，兼容尾参不泛化到其他套件。
- x64 与默认输出均从实际构建根读源，陈旧哈希与外树相同字节输入拒绝。
- 原缺方法和守卫改变构建负例继续拒绝，不改真实源制造反例。
- SiteGrid 十二项在默认和专项各执行一次；遗漏登记的日志必须被检查拒绝。

## Task 1: 主测试参数分派

**Files:** tests/Momoka.Tests/Program.cs、TestEntryPointTests.cs、StartupStateContractTests.md；docs/开发与验证.md。

**Interfaces:** 原参数分支与 Run 方法不变；仅剩余非空 args 返回 2，兼容尾参调用原正确契约。

- [x] 新增真实子进程拒绝测试，先只在 verification-harness-fixture 登记；见默认标题即停止本用例子进程，避免旧入口递归。
- [x] 编译当前 Tests/Core 并运行专项，旧入口必须出现明确失败。
- [x] 增加拒绝守卫及三个精确 desired-invariants 兼容分支，绿后登记默认。
- [x] 重编专项零失败零跳过，编译当前 Shell 后实际执行三个原入口及兼容入口；更新运行命令，检查并提交。

## Task 2: Perf 源码根

**Files:** tests/Momoka.PerfListDiagnosticsTests 的抽取任务、源码关联及 README；必要行为验证脚本。

**Interfaces:** 既有方法抽取与 FixtureSourceHash 保留；构建根来自 MSBuildProjectDirectory，SourcePath 限定本树。

- [x] 原 x64 检查读源两项失败并保存。
- [x] 记录明确根、实际输入路径；默认/x64 正常检查、哈希陈旧、外树输入、缺方法和守卫变化逐项验证。
- [x] 恢复正式构建输入、检查并提交；父会话核对提交和证据后整合。

## Task 3: SiteGrid 登记

**Files:** tests/Momoka.ResourceBrowserTests/Program.cs、README.md；tools/test-resource-site-registration.ps1。

**Interfaces:** 在 search-sites 返回前登记 SiteGridContractTests.Register(sourceRoot)，anibt-layout 保持独立。

- [x] 日志验收脚本要求十二个完整名字各一次且通过；实际旧专项日志必须被拒绝。
- [x] 增加登记，编译后运行默认与 search-sites，零失败零跳过；错误树拒绝与日志漏项、失败反例均验证。
- [x] 编码、PowerShell 语法、定向空白通过后提交。

## Task 4: 整批收尾

- [ ] 一次独立只读整批复审，核对必要缺陷及边界，不扩成全库复审。
- [ ] 核对其他活动聊天与主树状态，整合本批提交；主树 Tests/Core/Shell 与受影响项目 fresh Release 编译。
- [ ] 完整主套件、三处受影响成功与失败场景通过；保存开发原红绿逐 SHA 验证。
- [ ] 程序已退出后主树 publish.ps1 -NoArchive -Shortcut，验证载荷、哈希、快捷方式及体积。
- [ ] 记录最终交付与未覆盖边界，释放主树占用，原生归档完成的独立 Perf 树。
