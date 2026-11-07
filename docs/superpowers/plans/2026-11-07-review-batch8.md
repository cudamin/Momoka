# 第八批 Jellyfin 最新剧集不分组修复计划

> **For agentic workers:** 使用 executing-plans 在现有隔离树执行；用户已授权自行选择必要修复和连续完成，不再按分区暂停。

**Goal:** 修复 R04-003，最新剧集请求明确关闭分组，同剧新入库的两集分别保留。

**Architecture:** 仅修正 EmbyClient.GetLatestAsync 的布尔查询编码，false 明确发送 false，true 保持 true。使用实际 Core 请求、现有真实 Release LibraryViewModel 夹具及匿名真实 LibraryPage 验证；不重构货架、不增加产品功能。

**Tech Stack:** .NET 10.0.400、C#、WinUI 3、匿名内存 HTTP。

**Spec:** [R04-003](../reviews/2026-11-05-full-code-review/R04.md)。固定官方 10.10.7/aefb9b2c 与 12.4/83d0d0e7 的控制器默认 true，Episode 容器为 Series，false 禁止合并。

## Global Constraints

- 基点 de16a66；复用 review-recovery-batch3，主树和桌面验证串行，保护其它聊天的在途改动。
- 版本保持 0.2.5.0；不改 UI 设计、依赖、路由政策、GroupItemsIntoCollections；不使用真实凭据、服务器或播放/写入，不推送上传。
- SDK C:/Users/89400/.dotnet/dotnet.exe；restore 显式 --packages C:/Users/89400/.nuget/packages。
- Release/x64、单节点、BuildInParallel=false、UseSharedCompilation=false、MSBuildNodeReuse=false、DebugSymbols=false、DebugType=None。
- 中文提交正文加 Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>；最终主树重验并刷新桌面交付。

## Review Focus

- Jellyfin 10.10.7 与 12.4 的实际请求保留用户作用域、代理前缀及参数，不改变现有版本分派。
- 同剧两集与不同剧均逐集保留；默认和显式 true 继续分组。
- 最新电影同样明确 false；已看过滤、ParentId、Limit、Fields 不变。
- 货架调用与消费走实际 VM/页面；匿名分组模型仅模拟固定上游边界，不宣称运行官方服务器。
- 不重开已修 R04-001/002/004、R05、R17 项，不扩大全库复审。

## Task 1: 请求与货架回归

- [x] 新增 JellyfinLatestTests，注册进 JellyfinProtocolTests；两版同剧/不同剧 false、默认/显式 true、电影与过滤参数使用实际 HTTP 请求。
- [x] 用实际 Release LibraryViewModel 推荐页验证两版同剧两集及来源身份，复用已有匿名生命周期夹具，仅扩展测试传输边界。
- [x] 编译当前测试/Core/Shell，运行专项，保留遗漏参数及折叠单集的有效红；预期失败来自缺 GroupItems=false。
- [x] 修改 EmbyClient.cs 一处 false 编码，重新编译运行专项；预期失败0、跳过0。
- [x] 测试和定向文本检查通过后提交产品与测试。

## Task 2: 匿名界面与主树交付

- [x] 给既有 Shell 探针增加独立 jellyfin-latest 区域，实际 LibraryPage 两版建议货架、宽窄截图，拒绝真实外部动作。
- [x] 用旧产品与修复产品运行新探针，保留有效红和绿；复审一次最终 diff。
- [x] 更新开发文档、台账、PROGRESS；完整测试及受影响运行检查通过。
- [x] 核对主树写入空档后整合、重新编译测试最终 master，完成匿名页面运行及截图。
- [x] 发布实际主树目录、核对载荷与快捷方式以及发布 Core 字节，保留证据和交付回执。

## Rulings

- 用户已把必要修复、实现选择和连续执行交给助手，沿用已有合入及日常交付授权；不重新请求设计或执行批准。
- 完整测试包含共享运行锁检查，在其它聊天仍有诊断进程时先进行不占锁的请求/VM专项，桌面运行等待释放；不擅自结束其它进程。
- 两版固定控制器都保留旧路由；本批按客户端现有版本政策验证，不把技能旧描述当物理删除的证据。
