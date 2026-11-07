# 单集沉浸布局实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 开启沉浸模式时，单集页采用剧页的背景、海报和正文布局。

**Architecture:** 复用 DetailHero 的模式判断和 DetailPage 的背景与正文画刷；单集自己的标题、播放目标、选集与卡片偏好保持现有数据链。已有隔离树 66d6 为产品单写入，探针在独立树开发后整合。

**Tech Stack:** WinUI 3、.NET 10、C#；不增加依赖。

**Spec:** 用户本聊天指令“开启沉浸模式时，参考剧页面修改集页面”，随后明确“不要做demo，直接改”。本次跳过 Demo 和设计批准，直接实施。

## Global Constraints

- 有背景且开启沉浸的宽窗单集使用透明正文、260×375 海报上限、剧页头部内边距及对齐；不重复显示右上角艺术图。
- 窄窗使用既有紧凑布局。关闭沉浸的单集保留原宽窗卡面、260×375 海报及窄窗背景语义。
- 本集编号、名称、播放目标、选集列表和单集卡片自定义不改变；开关切换与尺寸变化即时更新。
- 使用匿名原生检查；不播放、不写真实服务器、不修改生产设置、不上传。

## Review Focus

- 无图单集继续卡面，不显示空背景或强制高斯。
- 开关往返恢复海报、角图及正文且不丢单集状态。
- 1024 DIP 断点往返不残留旧尺寸与卡面。
- 父剧图片到达前后的布局通知完整。
- 任意明暗背景、文字阅读保护与正文信息表的局部底保持可读。

### Task 1: 模式与布局

- [x] 扩展 DetailLayoutTests / ItemDetailTests 的单集沉浸类型契约，运行观察预期失败。
- [x] 更新 DetailShapeNotificationTests，实际当前 Shell 检查单集宽窗沉浸、模式/尺寸恢复、无图和播放目标保持。
- [x] 修改 DetailHero、DetailViewModel、DetailPage.Backdrop 与设置说明；复用现有布局、图片缓存和通知。
- [x] 成功编译当前 Release Tests/Core/Shell，运行定向与完整测试。

### Task 2: 原生验证与交付

Ruling: 用户批准与“统一封面大小并取消简介窄窗模式”聊天协调一次发布。封面使用那一轮统一的260×375上限。228e聊天承担最终主树整合、两项原生检查与发布；本聊天交付测试通过的产品和探针提交并等待实际发布回执。

- [x] 整合独立探针树的单集模式、高斯、宽窄、旧卡面与状态保持覆盖，编译最终 Shell。
- [x] 运行 library 匿名原生分区并目视相关宽窄截图，保留失败记录。
- [x] 独立必要缺陷复审，更新开发说明与 PROGRESS，提交本次文件。
- [x] 核实主树写入归属，合入 master、重编测试最终主树、发布到 artifacts/publish/win-x64 并核桌面快捷方式。
