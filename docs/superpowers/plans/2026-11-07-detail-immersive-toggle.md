# 详情页沉浸模式开关实施计划

> **For agentic workers:** 使用 executing-plans 在当前会话连续实施，最后独立复核。

**Goal:** 新增沉浸模式总开关，关闭后电影、剧、季详情页采用单集宽窗卡片布局，所有窗口尺寸都不铺背景图。

**Architecture:** 复用 DetailViewModel 的 HeroCard 和现有 XAML 卡面；独立保存 EnableImmersive，默认 true，保留 AlwaysImmersive。真实条目类型仍决定标题、海报、选集和艺术图。

**Tech Stack:** C# / .NET 10 / WinUI 3，现有设置行模板，无新依赖。

**Spec:** 用户 2026-11-07 本聊天指示，并明确“不用 demo，直接改”。

## Global Constraints

- 仅电影、剧、季关闭背景；单集宽窄窗维持原行为。
- 同页切换、重入、导入与恢复默认立即更新；隐藏固定沉浸行时保留偏好。
- 背景处理仍适用于单集窄窗，其入口保持可用。
- 保护用户设置及主树已有 PROGRESS.md 改动；不上传。

## Review Focus

- 缺字段旧配置继续默认开启；关闭值可持久化、导入与恢复默认。
- 最大化、普通宽窗、窄窗关闭后均无背景。
- 同类型页面无重新加载即可更新卡面、简介、视频信息及间距。
- 没背景图的条目也能切换卡面。
- 单集、海报尺寸、标题和播放目标语义不变。

### Task 1: 设置、布局与验证

**Files:** AppSettings.cs、SettingsViewModel.DetailCustomization.cs、DetailViewModel.cs、DetailLayoutTests.cs、DetailShapeNotificationTests.cs、ShellNavigationProbe.DetailCustomization.cs、PROGRESS.md。

- [x] 增加设置持久化与真实 VM 的关闭、恢复和宽窄回归，编译当前测试并确认红项。
- [x] 添加 EnableImmersive=true、设置总开关及从属固定沉浸行收放。
- [x] 扩展卡面与背景判据，同页外观通知重算全部受影响布局。
- [x] 扩展实际 WinUI 定向检查，检查三类宽窄、切换恢复及设置模板。
- [x] 当前 Release 编译、完整 Core 测试、定向实际 VM/WinUI、自动化句柄与差异检查通过。
- [x] 独立复核，提交本任务；合入 master 后重验及发布实际快捷方式目录。
