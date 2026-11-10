# 本机链接整理 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Momoka 直连 TMDB 或 MP 识别本机媒体，预览确认后直接改名或创建规范命名链接及刮削旁车文件。

**Architecture:** 独立 LocalMedia Core 负责扫描、密封计划、链接/改名及 NFO；MoviePilotService 扩展文件名识别，Metadata/TmdbClient 提供独立 TMDB 识别；WinUI「本机整理」设置页提供预览、手动校正和确认。文件系统、两种识别与 UI 分开并行，使用独立工作树。

**Tech Stack:** 现有 .NET 10、C#、WinUI 3，无新增包。

**Spec:** ../specs/2026-11-10-local-media-links-design.md

## Global Constraints

- 链接方式原文件名、位置和内容不变；直接改名按用户新增要求处理视频和字幕名称。
- 仅手动扫描和确认，不自动监控、不上传媒体、不调用 MP 写入端点。
- 失败不覆盖文件、不降级链接方式；真实媒体不作为测试夹具。

## Review Focus

- 同名目标、不同版本冲突：规划或提交前拒绝，保留源和既有目标。
- 目录重解析或源在预览后改变：提交前拒绝；扫描不跟随链接目录。
- MP 连接切换或迟到响应：旧计划不可提交。
- 特别篇季号 0、多集范围、其他来源编号：不猜季集、不混写 TMDB ID。
- 中途取消或图片失败：逐条结果准确，用户能看清链接与刮削的实际完成情况。

## Task 1: 文件系统与 NFO

**Files:** 新建 `src/Momoka.Core/LocalMedia/*`、`tests/Momoka.Tests/LocalMediaTests.cs`。

**Interfaces:** `LocalMediaSource` 保存扫描快照，`LocalMediaMetadata` 保存可信作品及单集字段；`LocalMediaOrganizer.Scan`、`Prepare`、`ExecuteAsync` 返回只读清单、单次计划及逐项回执。已落实为 Scan(root, recursive, token)、Prepare(source, targetRoot, metadata, mode, token) 和 ExecuteAsync(plan, imageFetch, token)，UI 与测试使用同一签名。

- [x] 先写临时文件与 XML 测试，运行得到缺功能的失败。
- [x] 实现安全扫描、命名、字幕链接、真实软/硬链接、单次消费计划和逐项结果。
- [x] 运行专项及 Core 编译，记录证据并提交。

## Task 2: MP 识别与界面

**Files:** 新建 `MoviePilotService.LocalMedia.cs`、`Metadata/TmdbClient.cs`、识别模型、`LocalMediaViewModel.cs`、`LocalMediaPage.xaml*`；接入 SettingsPage 和独立 LocalMediaSettings。确认使用已有 ContentDialog 全局样式。

- [x] 匿名夹具先覆盖实际 recognize 请求、身份、S00、缺季集和多个编号。
- [x] 实现只读识别及完整详情读取，复用图片获取和连接隔离。
- [x] 新建源/目标选择、链接方式、递归开关、预览列表、明确确认和逐项结果；表单修改使预览失效。
- [x] 编译并运行专项，更新文档和源接线测试。

## Task 3: 整合验证与交付

- [x] 独立审查本机写入边界和 WinUI 状态，修复确认问题。
- [x] 最终代码 Release Tests/Core/Shell 构建和完整默认测试。
- [x] 匿名 UI 宽窄截图、可用性与取消检查，新增控件自动化检查。
- [x] 提交、主树整合后重验，按 CLAUDE.md 发布；实际阻塞如实记录。
