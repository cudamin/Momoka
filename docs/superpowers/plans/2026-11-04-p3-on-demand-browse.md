# P3 按需浏览实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 长剧只构建被访问的卡，普通媒体库深返回恢复附近窗口。

**Architecture:** 全剧业务索引与卡片实例化分开；媒体库窗口保留绝对服务端起点和稳定集合。复用当前来源、查询取消和快照生命周期。

**Tech Stack:** C# / .NET 10 / WinUI 3；不增加依赖。

**Spec:** ../specs/2026-11-04-p3-on-demand-browse-design.md

## Global Constraints

- 按 CLAUDE.md；独立工作树、测试后提交、最终合入 master 并发布；未获上传授权不 push。
- 快照每份最多 400 项、登录 owner 隔离、10 分钟 TTL。
- 完整播放顺序、最早未看、特别篇和季跳不变；不进行真实播放/写服务器。
- 构建单节点；桌面/共享诊断 mutex 检查串行。

## Review Focus

- WinUI 将只读 IEnumerable 复制为数组而意外全量创建；用真实 Frame 的已创建数断言。
- 未实例化项目被删除、旧账号/旧加载迟到；分别覆盖索引和代次。
- 深窗口上翻后滚动锚点与新列数；宽窄窗口运行验证。
- 完整播放清单遗漏窗口前缀、字母跳转索引混淆；假动作和绝对查询偏移断言。
- 后台核对遇插入/删除/空页/权限失效；保留反馈并拒绝错来源回填。

### Task 1: 长剧卡片按需创建

**Files:** CardShelf.cs、DetailViewModel.cs、ResumeSlot.cs；新增按索引延迟集合与相关测试；详情运行探针。
**Interfaces:** CardShelf.Cards 保持可索引/变化通知，普通操作保持 Add/Insert/Remove/Move；详情增加 deferred 填充与 Materialized 清理、DTO 删除入口；_episodes 不缩减。

- [ ] 先补 2000 集深处访问、未创建删除、Clear/Cancel、普通货架操作的失败测试并记录 RED。
- [ ] 实现非泛型 IList 投影、一次重置、按访问造卡；纯 DTO 排序移到安全后台边界。
- [ ] 执行相关测试与实际 Frame 探针：保留全剧数量/索引，构建卡数仅覆盖布局访问；季跳和 S00 入口正确。
- [ ] 通过检查后提交。

### Task 2: 普通库深返回窗口

**Files:** BrowseSnapshotStore.cs、LibraryRequest.cs、LibraryViewModel.cs、LibraryPage.xaml.cs；快照/VM测试、深返回运行探针。
**Interfaces:** 快照窗口 StartIndex 与锚点；VM 暴露窗口起点、前向补页、全局/局部索引转换；页面保存并恢复锚点，所有查询保留 query/load guard。

- [ ] 先补 2000 项/1500 位置返回的请求次数、400 项上限、双向读取与取消失败测试并记录 RED。
- [ ] 实现附近快照、双向分页、范围计数、后台核对、字母定位、完整播放清单与删除回填。
- [ ] 验证 Emby/Jellyfin/FnNas/Plex 适用分页假传输和排除路径；真实 Frame 宽窄上/下翻与返回锚点。
- [ ] 通过检查后提交。

### Task 3: 集成审查与交付

- [ ] 审查完整差异，修复重要回归；核实新鲜 Release 全测和必要专项。
- [ ] 串行运行受影响 library/performance 探针，目视宽窄截图，记录覆盖限制。
- [ ] 合入当前 master，重验整合状态，更新主树 artifacts/publish/win-x64 和桌面快捷方式。
- [ ] 同步方案/PROGRESS；保留证据，清理已合并临时分支及允许移除的工作树。

## 决策与证据记录

- 2026-11-04：在本聊天已附加且干净的 ba08 工作树创建 codex/p3-on-demand-browse，基线 d20a832；P2 不重做。
- 首次进程检查发现 3b75 工作树 Momoka PID 34908；尚未启动本轮桌面验证。
