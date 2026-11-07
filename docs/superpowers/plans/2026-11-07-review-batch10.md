# 第十批交付工具修复 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans。用户已授权自行决定必要修复、连续执行，不重新询问方案或逐项暂停。

**Goal:** 修复 R01-002 状态查询写入和 R01-003 安装包绕过完整发布验收。

**Architecture:** 保留生产脚本与现有验收器。在所有写分支前拒绝 Status 与写开关组合；Status 路径只读目标签名后返回。安装器在编译前调用完整 verify-publish，显式传递自包含模式。

**Tech Stack:** Windows PowerShell 5.1、PowerShell 7、现有匿名载荷夹具。

**Spec:** 本文件短规格；依据 R01.md 两项已确认问题及用户“你自己决定要修哪些，不要自动暂停”的授权。

## Global Constraints

- 只修 R01-002/003；不改 UI、应用版本、批准清单或哈希要求。
- 匿名非 PE 输入、完整生产控制流；仅替换签名/证书/注册表/编译器等外部边界，不执行真实签名、安全设置写入、安装、播放或联网。
- 文件保持 LF；PowerShell UTF-8 BOM。每项红绿测试通过后提交；主树串行整合、最终测试及实际桌面交付，不上传。
- FrameworkDependent 输出必须显式按非自包含验收；签名后字节变化不能获得哈希豁免，当前验收明确拒绝。

## Review Focus

- Status 加有效文件、递归目录、无证书及空目录，必须没有签名或写入。
- Status 与 Setup、TrustPublisher、SetAppCheck、SetShellSmartScreen 组合，必须在写入前拒绝。
- 普通签名与写分支仍可达（仅匿名边界）；状态查询不能让其测试变成不可达假阳性。
- SkipPublish 只剩 exe、缺 native、DLL、许可或资源、字节被改，必须在 ISCC 前失败。
- 完整匿名发布件通过、模式错误拒绝，普通发布失败不得调用编译器。

### Task 1: 签名状态只读边界

**Files:** tools/sign-release.ps1；新 tools/test-sign-release.ps1；docs/开发与验证.md。
**Interfaces:** 原 Path/string[] 与 Status/switch 保持；无新增生产依赖。

- [x] 写完整脚本匿名运行夹具，记录外部写入、目标哈希、签名读取与退出码，覆盖两宿主；旧代码在 Status+Path 与冲突组合红。
- [x] 运行 `pwsh -NoProfile -File tools/test-sign-release.ps1 -Phase red`，112 项中 32 失败，完整日志保留。
- [x] 做上述最小修正，运行相同 green 命令，112/112 通过。
- [x] 更新帮助、文档与本计划完成项，脚本/差异检查后提交。

### Task 2: 安装器完整验收

**Files:** tools/installer.ps1；新 tools/test-installer.ps1；docs/开发与验证.md。
**Interfaces:** 消费 verify-publish.ps1 的 PublishRoot、RepositoryRoot、SelfContained；不改变批准规则。

- [x] 复用完整非 PE 载荷夹具与生产验收器，仅替换外部发布器/ISCC；记录调用顺序、文件状态与退出码。
- [x] 夹具接参和 XML 初始化错误保留，修正后 red-compiler 174 项中 88 失败，旧脚本仍调用 ISCC。
- [x] SkipPublish 编译前完整验收，按 `-not $FrameworkDependent` 传 SelfContained；正常 publish 已验收不重复，green 174/174。
- [x] 更新文档说明签名后哈希拒绝与模式要求，检查并提交。独立复核修正回执为实际执行副本 SHA；旧红原回执与更正说明分别保留。

### Task 3: 批次收尾

- [x] 一次独立只读范围复核，无待修产品发现；证据来源字段已修正，不反复扩大旧审查。
- [x] 重新检查其他聊天/主树状态，串行合入 master；最终 Tests/Core Release/x64 编译零警告错误，完整4135/4135、本批112/112及174/174通过。
- [x] 用户关闭确认后核实实际程序退出，主树 publish -NoArchive -Shortcut成功；完整验收、已测/发布Core和构建/发布Shell哈希及快捷方式通过。台账TENTH-REPAIRS.md，不生成真实安装包或签名。
