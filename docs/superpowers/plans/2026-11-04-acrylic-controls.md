# 亚克力按钮与设置卡片 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 完成用户认可的四类元素：详情操作按钮、ShelfStrip 翻页圆钮、服务器账号与聚合工具卡、通知接管与条目卡。

**Architecture:** 使用 WinUI AcrylicBrush 与现有主题角色；按钮保持原模板、尺寸、命令和显隐，卡片复用 CardBackgroundFillColorDefaultBrush。新增按钮三态刷子只在详情操作/翻页按钮局部使用，Dark/Light/HighContrast 和 ThemeHost 一起覆盖，避免全局普通菜单或正文变材质。

**Tech Stack:** 当前 WinUI 3/.NET 10/C#、仓库控制台测试及 Shell 离线探针。

**Spec:** 本文件的目标和下述约束来自本聊天 2026-11-04 的审查结果与用户“可以你做吧”；仅一次实现与交付，没有自动安排额外轮次。

## Global Constraints

- 遵守 CLAUDE.md；独立树 codex/acrylic-controls；未授权上传远端。
- 现有详情正文、普通菜单/对话框、输入框/选择框和播放器均保留现行材质。
- 保留详情播放与连播同底、连播勾选只变图标；所有常态/悬停/按下背景用亚克力，禁用/高对比度保留清楚的实色。
- 手写设置卡片直接复用现有卡片刷子，维持现有边线和布局。
- 不播放真实媒体、不修改真实账号或服务器数据；运行探针使用匿名假服务和临时设置，桌面验证串行。

## Review Focus

- 宽/窄两套详情按钮均接材质；checked 的 ToggleButton 不偷换回强调色底。
- WinUI 模板三态中的实际背景与样式 setter 一致，按钮焦点和点击区域不丢失。
- 每套存活主题能更新新刷子的 tint 和 fallback；高对比度使用系统颜色。
- ShelfStrip 圆钮由 Ellipse 绘制；先核对真实源码/样例能用 AcrylicBrush 填充，再保留圆与描边。
- 卡片材质只改变服务器/通知目标容器，不误改文字和嵌套背景。

### Task 1: scoped material change and regressions

**Files:** Palette.xaml、ThemeHost.cs、DetailPage.xaml、ShelfStrip.xaml、ServersPage.xaml、NotificationsPage.xaml；必要的测试/探针与 docs/开发与验证.md、PROGRESS.md。

**Interfaces:** 产生局部按钮三态亚克力角色，卡片继续消费 CardBackgroundFillColorDefaultBrush；测试和探针读取真实资源与真实模板。

- [x] 新增或更新相关主题/资源测试，先运行并观察目标材质检查失败。
- [x] 实现最小材质修改，不改命令、尺寸、布局与菜单材质。
- [x] 编译 Shell/Core/测试，跑完整主测试及相关专项；失败先查根因。
- [x] 新增一条聚焦材质的离线 Shell 检查或扩展现有适用检查；实际运行，保存五主题和相关宽窄屏幕截图，检查背景刷子与模板。
- [x] 运行适用空白/差异检查，提交产品与测试，发给独立 reviewer 检查。
- [ ] 主代理核对内容后合入当前干净 master，重验最终主树，发布 artifacts/publish/win-x64 并验证桌面快捷方式。
- [ ] 保存本次证据与提交，清理本任务临时树和分支。
