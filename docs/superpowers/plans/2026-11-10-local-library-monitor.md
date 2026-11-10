# 多库监控 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 多个本机目录独立自动识别刮削入库，首页按库展示。

**Architecture:** Core LocalMediaMonitor 一库一实例，共享持久 LocalMediaLibrary；Shell LocalLibraryCoordinator 管理应用生命周期和识别委托；设置编辑多个配置，首页展示对应库卡片与索引页。

**Tech Stack:** .NET 10 / WinUI 3 / 现有元数据客户端，无新增依赖。

**Spec:** ../specs/2026-11-10-local-library-monitor-design.md

## Global Constraints

- 已有与新增都自动处理，必须先启用该库监控；不在诊断/自检中加载真实库监控。
- 不能处理正在写入的文件，不能自动重放结果不明或部分成功的写操作。
- 本地媒体库与Emby/Jellyfin/飞牛/Plex/MP订阅库独立；库身份由持久ID决定。

## Review Focus

- 两库事件和身份不能混用；一个目录不能重复建立两份规则。
- 重启/事件重复/自身rename不能重复执行。
- 修改配置、停监控及退出必须阻止旧计划继续写。
- NFO失败不冒入库，图片失败如实保留部分状态。
- 首页新本地卡不能被MP订阅卡同步清理。

## Tasks

- [x] Core监控与持久索引：真实临时Watcher事件、稳定等待、多库、取消、partial和restart测试先行；提交独立变更。
- [x] 多库设置与应用协调：旧单目录迁移、识别依赖、逐库启动/停止和手动结果登记，保持现有测试。
- [x] 首页逐库卡片与本地库页：事件实时更新、身份导航、海报和路径、空/错状态、取消释放。
- [ ] 集成验收：实际Watcher自动处理已有/新增，正确NFO与索引归属；匿名真实UI宽窄与多库；最终主树统一编译测试发布。
