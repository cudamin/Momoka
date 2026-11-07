# 第七批 Plex 观看状态写后确认修复计划

> **For agentic workers:** 使用 executing-plans 完成本项；匿名运行证据可在独立托管树准备。用户已授权自行选择必要修复、连续执行，不按分区暂停。

**Goal:** 修复 R05-002，Plex 已看写成功后补读失败不丢失确认、不重复写入。

**Architecture:** Plex SetWatchedAsync 仅提交原 HTTP 写任务并返回 null，复用现有无正文确认。另取 metadata 用户状态，卡片通过既有 RefreshConfirmed 补全统计；详情立即保留确认位，独立刷新并沿现有 committedNotice 反馈失败。

**Tech Stack:** .NET 10.0.400、C#、WinUI 3、现有匿名 HTTP/真实 Shell VM/隔离 Shell 探针。

**Spec:** [R05-002](../reviews/2026-11-05-full-code-review/R05.md)。本批不改变 scrobble 路径和响应读取语义，不关闭 Session 全局重试，不伪造完整 UserData，不扩大到 ResumeSlot 失败消费策略或 R07-003 详情共享字段排序。

## Global Constraints

- 基点 29cf082；复用 review-recovery-batch3。主树整合、桌面验证和实际发布串行；保护 c6cb 和 6d68 的在途 UI 工作。
- 版本保持 0.2.5.0，无新依赖；不改 UI 设计，不读取真实凭据、账号、服务器或播放/写入，不推送上传。
- SDK C:/Users/89400/.dotnet/dotnet.exe；restore 显式 --packages C:/Users/89400/.nuget/packages。
- Release/x64、单节点、BuildInParallel=false、UseSharedCompilation=false、MSBuildNodeReuse=false、DebugSymbols=false、DebugType=None。
- 中文提交正文加 Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>；最终代码主树重验并刷新桌面交付。

## Review Focus

- 两种目标位；写失败仍失败，预取消无请求，补读错误/取消不重写。
- 补读503警告、403/格式错误仍错误；401同身份重新认证只重读。
- 卡片确认目标位不被过期补读撤销；旧来源、旧页面不应用迟到结果。
- show/season 仍独立回读实际统计，movie/episode 保持 viewCount。
- 原写 HTTP 成功才确认，不宣称响应头到达即成功；续播换卡失败策略和详情全局排序保留原范围。

## Task 1 — 确认写入与补读分离

- [x] 在现有 PlexMediaTests 注册新匿名 Session 回归，记录写后503/401及取消的有效红。
- [x] PlexMediaAdapter 返回无正文确认，增加独立 GetUserDataAsync；EmbyClient 增加 Plex 独立补读入口。
- [x] 更新 WatchedCounts 为写后显式读取，保留原计数断言。
- [x] 就近扩展 ItemCommands 既有补读支持 Plex；详情扩展原飞牛确认/刷新提示到 Plex。
- [x] 使用真实 Attach/Reload 的 PlexAffordance 夹具验证卡片/详情、错误提示和隔离边界。
- [x] 定向编译/回归通过后提交产品与测试。

## Task 2 — 匿名运行与交付

- [ ] 独立树准备实际 WinUI 卡片/详情、宽窄窗口和刷新失败反馈探针，无真实配置与服务。
- [ ] 一次独立只读复审本批最终 diff，必要问题一次修复并回归。
- [ ] 更新审查台账、开发命令与 PROGRESS；完整测试、受影响运行验证和文本检查。
- [ ] 主树干净且无人占用时整合，重编译重验最终提交；发布后核验载荷、DLL、桌面快捷方式。
- [ ] 保留证据、清理可安全归档的独立树；如原生工具阻止则保留并说明。

## Rulings

- 采用已存在的飞牛确认/独立补读路径，避免新通用结果类型或 Session 重试政策；详情共享字段排序属于另项，不混入。
- 真实数据 Demo 要求适用于 UI 设计改变；本批只修已看操作和失败反馈，匿名真实控件作为验证，不替代 UI 设计批准。
- 用户已明确授权本会话自主修复和持续完成；不重复询问修复/主树整合/已有日常交付授权。
