# 后端审查修复实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use checkbox syntax for tracking.

**Goal:** 修复已确认的播放认证泄露和 Plex 多段时间轴错误。

**Architecture:** 保留现有 Planner/Backend/Service 分层。认证传输在后端执行，Plex 版本与分段模型在适配器及纯规划层表达。

**Tech Stack:** .NET 10、C#、libmpv、现有离线控制台测试。

**Spec:** ../specs/2026-11-10-backend-review-fixes-design.md

## Global Constraints

- 以当前 CLAUDE.md 为规则；用户已委托实现判断，无需再次请求方案许可。
- 同树只有一位写入者。两个实现子代理使用各自隔离树，根代理串行整合。
- 固定上游源码先读后改；令牌只用占位值；不真实播放或写入服务器。
- 当前 SDK 为 C:/Users/89400/.dotnet/dotnet.exe；Release、单节点，编译成功才运行对应测试。

## Review Focus

- 后续 Range/重连发生新跳转时仍去除认证。
- 跨来源后回跳不恢复认证，HTTPS 降级和异常地址拒绝。
- EDL 每段均经安全传输，不能只保护第一段。
- 多 Media 是多个版本，多个 Part 才串联；非法/缺失时长不变成假完成。
- 同窗切换、取消和字幕不能继续借用旧认证或旧传输。

### Task 1: 播放认证边界

**Files:** Playback/LibMpvBackend.cs、Playback/MpvProcessBackend.cs、Mpv/MpvArgumentBuilder.cs，以及必要的独立传输类型和测试。

**Interfaces:** 消费 PlaybackRequest 的原始来源与结构化媒体分段；只给原生播放器安全的资源标识。

- [x] 补同源、跨源、回跳、降级、Range、取消和字幕的失败先行回归。
- [x] 依据固定源码及原生占位令牌探针实现可持续执行的认证边界。
- [x] 验证集成/独占/外部接线与同窗释放路径，相关测试通过后提交。

### Task 2: Plex 多段影片

**Files:** Plex/PlexMediaAdapter.Models.cs、Plex/PlexPlaybackSelection.cs、Emby/EmbyItem.cs、Playback/PlaybackPlanner.cs、播放请求模型及相关测试。

**Interfaces:** 一 Media 一 Source；结构化顺序 Part 保持 URI/时长，供 Task 1 映射后构造播放目标。

- [x] 固定两个 3600 秒 Part 的 4500 秒续播、中间 EOF、两个版本、单段与坏时长回归。
- [x] 实现版本/分段映射及整体播放时间轴，核对内核 EDL 的时长和 EOF 行为。
- [x] 测试整片进度/完成判定、字幕和轨道策略，相关测试通过后提交。

### Task 3: 整合与交付

- [x] 独立复核两个实现，根代理解决接口交叉，不扩大修复范围。
- [x] 保护主树已有改动，将产品提交整合到当前 master。
- [x] 重新编译当前 Core/Tests/Shell，运行完整主测试、受影响专项和匿名原生探针。
- [x] 检查差异、日志、编码和提交范围；更新 PROGRESS.md 的准确证据与限制。
- [x] 目标未运行时发布到主树实际交付目录，核验快捷方式和产物，保留证据后归档本任务子树；未授权上传不推送。
