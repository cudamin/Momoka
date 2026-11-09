# 媒体库推送与轮询对齐实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 按四家官方首页更新机制删除通用轮询，补齐飞牛任务驱动目录更新，并准确报告验证边界。

**Architecture:** 原生 WebSocket 保留，HomeViewModel 合并通知并在读取完成后消费。飞牛独立任务观察器跟随首页生命周期，只查询当前管理员来源；任务消失后刷新目录与计数。

**Tech Stack:** .NET 10、C#、WinUI 3；不添加运行依赖。

**Spec:** ../specs/2026-11-10-library-push-parity-design.md

## Global Constraints

- 真实服务器只读、不播放、不扫描、不写媒体；令牌仅进程内请求头。
- 每个工作树一个写入会话；覆盖行为的测试通过后提交，主树重验再发布。
- 不改布局或样式。Plex 不新增原本没有的推荐货架页面。

## Review Focus

- 通知遇首读、后台刷新、指纹基线或另一通知核对时不能丢失。
- 离页/重入、来源退出与旧回调不能借用新生命周期。
- 飞牛普通账号零任务请求，权限撤销后不得继续轮询。
- 任务新增/同 ancestor 状态变化不刷新；消失刷新目录和计数，不定时重读媒体。
- 协议可用、真实连接、保活、真实库事件四种证据不能混称。

## Tasks

- [x] 完成四家官方源码取证、保存固定版本及哈希。
- [x] 写实际首页通知忙碌回归，复现后修复；删除首页通用 timer 与相关过时注释。
- [x] 实现并测试飞牛管理员任务观察器，接入首页目录/计数更新与离页取消。
- [x] 更新协议/文档和测试，核实 Plex 对应页面范围，完成独立复审。
- [ ] 独立树编译测试并提交，主树合入重验，发布并核对快捷方式。
