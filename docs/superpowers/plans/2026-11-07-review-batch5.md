# 第五批后端分页与版本时长修复计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use executing-plans. 按任务执行，用户已授权自行选择实现并连续完成，无分区暂停。

**Goal:** 修复 R05-003 的 Plex 下一集漏页与 R05-001 的飞牛未知版本时长污染。

**Architecture:** Plex 沿既有 On Deck 原始分页寻找指定剧，保留服务器顺序；飞牛只将服务端条目时长补给未知版本，默认版本推导值仅用于详情展示，播放规划取所选版本时长。

**Tech Stack:** .NET 10.0.400、C#、Release/x64、现有匿名内存 HTTP 测试。

**Spec:** ../reviews/2026-11-05-full-code-review/R05.md 的 R05-001、R05-003；当前源码重新核对后执行。

## 约束与范围

- 基点 634d375；复用 review-recovery-batch3 托管工作树。开发与主树验证、发布串行。
- 版本维持 0.2.5.0，不新增依赖，不改 UI 布局，不读取真实设置或凭据，不连接真实服务器、播放或写入，不上传。
- 使用 C:/Users/89400/.dotnet/dotnet.exe；restore 显式 --packages C:/Users/89400/.nuget/packages；构建 -c Release -p:Platform=x64 -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -p:DebugSymbols=false -p:DebugType=None。
- 测试运行当前 tests/Momoka.Tests/bin/x64/Release/net10.0/Momoka.Tests.dll，原红日志保留；主树最终重验并刷新实际桌面交付。

## 复核重点

- Plex 前页无匹配、后页匹配：按原始条目数推进，足量后停止。
- totalSize 缺失、字符串、短页和空页：能终止，不把过滤后数量当分页偏移。
- 后页取消或失败：传播失败，不伪装成没有下一集。
- 飞牛默认版已知而其它版未知：展示可补，播放请求和上报保持未知。
- 服务端条目/play-info 时长与版本自身时长：保留已有回退约定及版本自身优先。

## Task 1: Plex 下一集分页

**Files:** 修改 src/Momoka.Core/Plex/PlexMediaAdapter.cs；新增 tests/Momoka.Tests/PlexMediaTests.NextUp.cs，在 PlexMediaTests.Register 登记。

**Interfaces:** 保持 GetNextUpAsync(string? seriesId, int limit, CancellationToken ct) 与 List<EmbyItem> 不变，使用 GetOnDeckAsync 的现有分页头。

- [ ] 写测试覆盖后页目标、跨页限量与顺序、ResumeRow 替换、空/无目标、缺总数、短页总数、取消/后页失败和零请求输入。
- [ ] 当前 Tests/Core 编译后运行 plex-protocol-fixture，确认旧代码出现预期断言失败并保存日志。
- [ ] 实现按原始页数推进的循环，继续使用现有字段解析，不新增猜测的服务端筛选参数。
- [ ] 重编并运行同专项，要求失败/跳过为零；定向空白检查后提交产品及测试，正文中文并带规定署名。

## Task 2: 飞牛版本时长

**Files:** 修改 src/Momoka.Core/FnNas/FnNasMediaAdapter.cs、src/Momoka.Core/Playback/PlaybackPlanner.cs；在 FnNasMediaTests 登记版本时长回归。

**Interfaces:** 保持 DTO、Plan 和 FnNasPlaybackSelection 接口；采用当前适配器已有的服务端正时长补齐，所选源未知时请求/上报为 0。

- [ ] 写匿名多版本回归，经实际 GetItemAsync → Plan → PlaybackRecord 观察默认 120 秒、未知版、已知 240 秒及条目/play-info 已知对照。
- [ ] 编译并执行 fnnas-protocol-fixture，确认原代码失败并保留证据。
- [ ] 先补服务端条目时长，再用默认版补展示；Planner 飞牛分支不再借展示值填未知源。
- [ ] 重编运行同专项，失败/跳过为零；空白检查后提交产品及测试。

## Task 3: 批次收尾

- [ ] 一次独立只读复审本批范围，必要发现用红绿测试修正。
- [ ] 同步后端技能、支持说明、第五批台账及 PROGRESS，保留其它候选的具体状态。
- [ ] 检查其它聊天与主树状态后整合；主树 Tests/Core/Shell 零警告错误，完整主测试和本批专项通过。
- [ ] 目标程序退出后，主树 tools/publish.ps1 -NoArchive -Shortcut -DotnetPath C:/Users/89400/.dotnet/dotnet.exe；核当前测试/发布 Core 哈希及快捷方式，保存证据。
- [ ] 记录最终交付与验证缺口，不声明真实服务或实际播放通过。
