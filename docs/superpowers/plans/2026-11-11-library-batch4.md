# 媒体库第四批 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. 各并行任务使用独立 managed worktree，主控统一整合、审查和桌面验证。

**Goal:** 补齐飞牛服务器字幕、Jellyfin 本地字幕上传、Emby/Jellyfin 花絮特典入口。

**Architecture:** 沿用各后端原生协议、既有字幕对话和详情播放链路；业务身份与写入结果留在服务层。飞牛字幕用独立服务与对话，避免混入 Emby 版本/流索引协议。Jellyfin 扩展已有字幕对话，花絮作为详情附加内容入口。

**Tech Stack:** C# / .NET 10 / WinUI 3，当前仓库锁定依赖，不升级版本。

**Spec:** 用户指定 `C:/Users/89400/.codex/visualizations/2026/11/10/01a1c47a-7be0-7073-a8f8-d3c7aa113dcc/momoka-library-roadmap.md` 第四批。

## Global Constraints

- 按 `C:/Users/89400/Momoka/CLAUDE.md`；直接修改正式功能，不做 Demo 或另加设计批准步骤。
- 上游协议先读实际源码/前端产物，记录固定版本和定位；无真实服务器写入、删除或播放验证授权。
- 各后端权限分别计算，无效入口隐藏；身份、页面和版本变化拒绝迟到结果与旧操作。
- 已确认写入不因回读失败而重发；不确定回执交给只读恢复，保留父页面刷新债务。
- 新增交互必须稳定定位、点外取消；删除明确确认且取消零写入。
- 每项新增行为和边界测试；提交含中文正文和规定 coauthor。子树不发布、不推送、不操作共享桌面。

## Review Focus

- 多版本视频字幕写到错误版本：测试源 ID/路径变化与非首版本。
- 请求后切服/注销/离页：测试旧身份及迟到回调不落入新页面。
- 服务端已成功但刷新失败：测试仅重读，不重复写入。
- 普通获权账号与管理员分支混淆：分别测试拒绝、授予和恢复，飞牛服务器路径仅管理员。
- 花絮空、错误、重复、不可播放与伪造来源：测试过滤、可重试和源绑定，原媒体续播点不混入附加项。

### Task 1: 飞牛服务器字幕完整链

**Files:** `src/Momoka.Core/FnNas/FnNasClient.Subtitles.cs`、独立字幕服务/模型、`src/Momoka.Shell/Views/FnNasSubtitleDialog*`、现有 ItemCommands/ItemMenu 和权限链、对应 tests、专项证据文档。

**Interfaces:** Consume 当前 FnNasClient、EmbySessionScope 与 FnNas MediaSource 映射；Produce 专属搜索/下载/上传/删除操作和管理员服务器文件分支，现有菜单进入。

- [ ] 读取飞牛官方前端当前可用固定产物，列真实路由、字段、权限及回执语义。
- [ ] 为搜索、下载、本地上传、删除、管理员指定服务器文件以及版本/身份/刷新失败写失败测试并验证。
- [ ] 实现协议、服务、独立对话和菜单；使用原有权限来源，不猜管理员能力。
- [ ] 编译本树 Core/Tests/Shell，运行专项测试，补匿名原生探针供主控串行运行。
- [ ] 自审、记录证据与待主控项，提交。

### Task 2: Jellyfin 原生本地字幕上传

**Files:** `src/Momoka.Core/Emby/EmbyClient*.cs`、`ServerSubtitles.cs`、`src/Momoka.Shell/Views/SubtitleDialog*`、`ItemCommands.Server.cs`、权限链、字幕测试与文档。

**Interfaces:** Consume 既有 ServerSubtitles、SubtitleDialog 写保护/刷新债务；Produce 固定版本的上传方法与本地文件选择入口，只对支持的 Jellyfin 获权用户呈现，保留管理员网页入口。

- [ ] 读取 Jellyfin 服务端与网页实现，确定 JSON 上传格式、编码/语言/标志、权限和版本目标。
- [ ] 为普通授权、权限拒绝/收回、非首源、文件校验、切服和已成功后回读失败写测试并验证红项。
- [ ] 实现上传和现有对话扩展，不弱化删除、下载未确认、点外取消及忙态保护。
- [ ] 编译本树并跑相关测试，提供真实控件匿名上传探针（禁止实际文件选择器/网络副作用）。
- [ ] 自审、记录上游与证据，提交。

### Task 3: Emby/Jellyfin 花絮和特典

**Files:** Core EmbyItem/EmbyClient 附加内容、`DetailViewModel`/`DetailPage` 专属 partial、相关测试与 `ShellNavigationProbe`。

**Interfaces:** Consume 现有详情生命周期、来源和 PlayRequest；Produce 按各后端读取可播放附加项、详情入口、空态隐藏与错误重试。

- [ ] 读取两家实际实现，核对 SpecialFeatures/Extras 数据和路由，不假定共用协议。
- [ ] 为协议、过滤、零起点、来源/离页/迟到拒绝、空态恢复和错误重试写测试并验证。
- [ ] 实现入口和读取/播放选择；不改播放管线、不实现 AdditionalParts 多段时钟。
- [ ] 编译并跑专项，新增匿名宽窄原生探针（假播放委托，真实请求为零）。
- [ ] 写 AdditionalParts 独立技术评估，记录缺口与后续范围，自审提交。

### Task 4: 集成审查、主树重验和交付

- [ ] 审查三个任务的提交及测试证据，解决冲突并检查写入回执、来源与权限全链。
- [ ] 编译当前 Release Tests/Core/Shell、完整主测试及受影响专项，检查格式/自动化句柄。
- [ ] 串行运行新原生 UI 探针，检查宽窄截图、权限恢复、取消和失败状态；报告实服覆盖边界。
- [ ] 在主树干净且无并行写入冲突时合入，重新执行适用检查；发布实际交付目录并核对桌面快捷方式。
- [ ] 保存验收报告与提交，归档本批子工作树；未获上传授权，不推送。
