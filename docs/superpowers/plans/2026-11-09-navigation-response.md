# 浏览导航即时交接实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Steps use checkbox syntax for tracking.

**Goal:** 去掉首页、媒体库、详情切换时的平台旧页离场等待，暖首页及历史返回直接显示货架。

**Architecture:** Shell 的浏览转场统一 Suppress。首页独立记录本次是否允许货架入场；缓存恢复及 Back/Forward 不播放货架入场，原折叠与播放返回抑制位保持自己的语义。数据加载、缓存容量与页面实例生命周期保持现行规则。

**Tech Stack:** C#、WinUI 3、.NET 10；现有生产方法提取夹具与匿名 WinUI 探针。

**Spec:** 本文下方设计，依据本会话用户授权与 CLAUDE.md。

## Design

普通浏览导航立即交接新页。首页冷态 New 导航保留原货架入场；有快照或历史返回时首屏与迟到货架保持最终透明度和位置。手动刷新可以重新入场。此片只消除明确的动画等待，不声称已经达到网页端速度；真实首屏与网页比较由父会话完成。

## Global Constraints

- 只写 navigation-response 独立工作树；不运行 UI、不发布、不写主树、不推送。
- 生产策略必须由实际生产源提取测试验证，不用字符串包含测试替代行为。
- 保留 LoadGeneration、来源身份、滚动与播放返回语义；不启用 NavigationCacheMode.Required。

## Review Focus

- 快照未命中但 Back/Forward 仍立即显示货架。
- 同一页面再次冷态 New 导航可重新入场。
- 暖恢复后后台新增货架不补播入场。
- 播放返回保持静态，普通暖恢复不禁用后续折叠。
- 新导航与返回历史都使用立即交接策略。

### Task 1: 浏览转场与首页入口

**Files:** ShellPage.xaml.cs、ShellPage.xaml、HomePage.xaml.cs；HomeDirectoryTests；WarmTiming/HomeReturn/Foundation 探针；PROGRESS.md。

**Interfaces:** 消费现有 RestoreSnapshot、HomeMotion.Enter/StopEnter、NavigationMode；不新增共享业务接口。

- [ ] 增加生产转场与主页导航尾段/Loaded 回调提取测试，验证导航传入 Suppress 及冷暖/历史/播放返回行为。
- [ ] 运行当前 Release HomeDirectoryTests，确认新增用例因现行动画策略失败（RED）。
- [ ] 实施最小策略与独立货架标记，运行专项为 GREEN；运行现有翻档专项。
- [ ] 扩充实际匿名探针断言，构建 Shell；父会话稍后运行 UI。
- [ ] 检查最终 diff、更新 PROGRESS，提交本片，报告证据及未完成运行验收。

**Verification:** 用户级 dotnet.exe、Release、单节点、禁共享编译。HomeDirectoryTests 与 HomeFoldCompletionTests 退出 0/失败 0；Shell 构建 0 警告 0 错误；编码及差异检查通过。
