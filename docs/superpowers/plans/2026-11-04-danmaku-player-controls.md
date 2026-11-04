# 弹幕播放按钮与菜单 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 保留播放页弹幕面板入口，增加独立显示开关；全局禁用、来源未配置或后端不支持时，两条播放管线收起弹幕按钮与右键入口，恢复后保留原偏好。

**Architecture:** 集成页使用现有 Button、ToggleDanmaku 和属性通知；独占页使用 uosc ManagedButton 和既有 momoka-shortcut 动作。两侧播放设置入口共用 PlayerSettingsLinks.Available；能力失去时关闭已经打开的旧菜单，下一次打开按当前条件重建。

**Tech Stack:** C# / WinUI 3 / 随包 uosc Lua / 无窗口 libmpv Lua 夹具。

**Spec:** 根会话 2026-11-04 授权的播放控制子任务；根会话负责设置与配置以及统一运行验收。

## Global Constraints

- 独立工作树 `C:/Users/89400/.codex/worktrees/danmaku-player-controls/Momoka`，不改真实设置、不播放、不启动 Shell UI、不共享发布。
- 保留原 DanmakuButton 打开面板；新增 DanmakuToggleButton 使用 DanmakuShown、DanmakuToggleLabel、DanmakuControlsVisibility。
- VM.Danmaku.cs 与 MpvDanmaku.Shown 由根会话提供；本提交不重复修改。
- 全局关闭收起全部相关播放入口，设置页总开关保持可达；隐藏不清空外观、来源与 Show 偏好。

## Review Focus

- Show=false 时恢复按钮继续可见；禁用后点击旧入口不写设置或发请求。
- 已开右键菜单在能力失去时收起；普通滑块变化与无关菜单保持可用。
- 新按钮在宽窄与全屏缩放后无重叠，尺寸、影子、读屏名称与状态提示完整。
- 独占状态按宿主当前 Show 更新，换片不保留旧能力或旧高亮。
- 全局禁用与来源地址无效、外部后端采用同一可用性判断，恢复保留原值。

## Task 1: 两侧按钮与菜单

- [x] 添加 Lua 的独立 toggle、恢复能力与旧 picture 菜单关闭行为检查；当前版本 47/49，通过项无 Lua 错误，新增两项失败。
- [x] 添加 PlayerSettingsLinks 可用行测试与 mpv-ui-fixture；当前版本 30/31，禁用仍显示 danmaku 行的断言失败。
- [x] 在现有 DanmakuSendAsync 真实页探针中添加按钮、状态、宽窄、菜单禁用/恢复与后端/来源失去断言。
- [x] 实现 PlayerSettingsLinks.Available 与两侧构建、执行守卫和适用自检。
- [x] 增集成 Button 及高亮，接入宽度预算、图标影子、状态通知和已开浮层收起。
- [x] 增独占 ManagedButton、Show 状态接收与禁用关闭 picture 菜单；360px × 1.3 缩放失败用例驱动布局优先保留恢复开关。
- [x] 本树定向 C# 与 Lua 测试、空白/编码、自动化句柄检查；根会话统一执行完整闸门与真实探针截图。
- [ ] 更新 PROGRESS.md 范围与证据并提交，返根会话 SHA。
