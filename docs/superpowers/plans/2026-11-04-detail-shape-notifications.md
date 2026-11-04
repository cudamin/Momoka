# 详情同类型版式通知去重实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成导航延迟方案 P1 的一个小片：同一次详情访问中，预览、完整主体及刷新类型相同时，不重复发布整批版式通知。

**Architecture:** 在 `DetailViewModel` 保存本访问已经通知过的原始主体类型；`AnnounceShape` 每次仍更新能力，仅在类型首次确定或改变时发布原有版式通知。Attach 清除记录；删除后的空主体仍完整通知。保留集合、图片、窗口尺寸和播放目标各自的通知，不改 Busy/IsReady、加载次序、缓存或转场。

**Tech Stack:** 现有 .NET 10 / C# / WinUI 3 与匿名真 VM 测试夹具，不新增依赖。

**Spec:** [页面切换与封面响应延迟审查及优化方案](../../页面切换与封面响应延迟审查及优化方案-2026-11-04.md)，第 4 节 P1 与第 8 节交接。用户要求继续该方案；本片由实现者按当前源码选择。

## Global Constraints

- 遵守当前 [CLAUDE.md](../../../CLAUDE.md)，在 `codex/p1-detail-shape` 独立树开发。
- 所有真实库操作只读，不播放、不写服务器或真实设置；匿名夹具不登录。
- 同类型去重只减少可证明的重复通知，不把通知次数当作帧时间或承诺提速比例。
- 主体类型取 `_detail.Type`，不能用有 `_seed` 回退的 `ItemType`，否则存根首次 Apply 会漏通知。
- `Busy/IsReady` 保持完整加载契约，能力通知和命令更新仍执行。

## Review Focus

- 非存根预览到完整主体：相同类型少一次版式批次，内容与操作仍更新。
- 存根和服务器纠正类型：首次主体或类型变动必须通知。
- 复用 VM 再 Attach、同一对象 Type 被修改：不能靠对象引用或旧类型推断去重。
- 同类型刷新清空/重填剧集、媒体源及类型文字：集合和能力的独立通知仍生效。
- 取消、主体读取失败、成功删除及旧请求迟到：保留原有失效边界。

## Task 1: 真 VM 回归与版式通知去重

**Files:**
- 修改 `src/Momoka.Shell/ViewModels/DetailViewModel.cs`。
- 扩展 `tests/Momoka.Tests/DetailActionReadinessTests.cs`，新增同类 partial 文件 `DetailShapeNotificationTests.cs`。
- 更新 `docs/开发与验证.md`、原方案和 `PROGRESS.md`。

**Interfaces:** 复用 `detail-action-readiness-fixture <当前树 Release Shell DLL> <当前树根>`，原有 7 项仍执行；不新增产品开关。

- [x] 在真实 VM 的 PropertyChanged 边界观察 `ContinueVisibility`，电影/剧/季/单集 Preview→Apply 同类型只发布一次；完整刷新不再发布相同类型批次。
- [x] 覆盖存根、服务器改类型、VM 重新 Attach、同对象类型修改、能力通知与集合/播放目标刷新。
- [x] 构建现有 Shell 和修改后的测试，运行夹具，保存因重复通知失败的 RED 输出。
- [x] 实现本访问的类型记录及去重；保持 44 个原版式通知与所有调用位置，能力通知在去重判断之前。
- [x] 重新构建并运行扩展夹具、删除夹具和完整主测试，检查零失败零跳过；保存 GREEN 与构建输出。
- [x] 运行 performance/library 匿名 UI 检查，查看真实截图；历史红项保留原报告，区分已查明的旧断言误报与原因未定的尺寸超时。
- [x] 独立审查差异，修复必要问题，完成空白和文档链接检查后提交。

## Task 2: 主树整合与桌面交付

- [x] 核对主树干净、无冲突在途写入后合并；重新构建最终主树、运行受影响夹具/主测试/UI 检查。
- [x] 按规则发布至主树 `artifacts/publish/win-x64`，核对快捷方式、输出哈希及发布结果。
- [x] 记录实测通知次数、运行检查与未覆盖的实际帧延迟；未推送远端。
- [x] 所属聊天已核对独有文档与证据并删除临时分支；工作树因 primary checkout 保护干净保留于 `b726000`，恢复包与清单见 PROGRESS。

最终交付证据见原方案第 9.3 节与 `8738491`；首次材质超时原因未定，原报告保留，当前诊断版检查通过。
