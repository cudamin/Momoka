# 集成播放器窗口模式退场淡出实施计划

> **For agentic workers:** 按 Task 顺序执行；每个 Task 先跑红、再改、再验证，逐条勾选。

**Goal:** 给集成管线窗口模式的播放器退出补一段 280ms 纯淡出，并把两向时长统一到 280ms；进场维持现状。

**Architecture:** 淡出落在屏幕坐标的分层覆盖层上（`VideoFrameOverlay` 的 `SourceConstantAlpha`），曲线出自 Core 的 `PlayerMotion`；不改页面布局、不改播放内核。

**Tech Stack:** .NET 10、WinUI 3、Win32 `UpdateLayeredWindow`，无新增依赖。

**Spec:** ../specs/2026-11-08-player-exit-fade-design.md（用户 2026-11-08 确认三项选择）

## Global Constraints

- 只动窗口模式退出这条真实路径；全屏退出、`Detach` 溶解、换集、独占模式与 uosc 不动。
- 覆盖层的画面位置在屏上一像素都不许跳：改铺新客户区时画面按 contain 落在原处，四周补 `PlayerStageBrush`。
- 淡出线性；进场淡入保持 140ms 缓出；两向总时长同一个数。
- 不真实播放、不登录、不写服务器；探针只用本地素材与隔离数据目录，跑副屏（`--screen 2`）。

## Review Focus

- 覆盖层 alpha 序列必须「255 起、单调不增、0 收」，且不因为复用 DIB 而重画像素（同尺寸只换 blend）。
- 改铺那一拍与 `RestorePlayerToBrowse` 在同一同步块，不得有 `await` 夹在中间。
- 淡出期间换片/卸载要能中断并释放覆盖层，不能留一层不透明留在屏上。
- 浏览页未就绪时先按住（≤800ms）再淡；抓不到帧时退回原来的当拍收摊。

### Task 1: Core 的时长与曲线（红→绿）

- [x] 在 `tests/Momoka.Tests/PlayerMotionTests.cs` 新增：两向总时长相等、`ExitFadeAlpha(0)=255`、`(1)=0`、中点 128、逐拍单调不增；先跑红。
- [x] `src/Momoka.Core/Playback/PlayerMotion.cs`：`ExitMilliseconds` 由 240 改 280 并与 `EnterMilliseconds` 同一个数；新增 `ExitFadeAlpha(double)` 与注释（线性理由引用 `ExitDimAt`）。
- [x] 编译 Tests/Core，跑 `Momoka.Tests`，0 失败 0 跳过。

### Task 2: 覆盖层的常量 alpha 与留存位图

- [x] `src/Momoka.Shell/Windowing/VideoFrameOverlay.cs`：`Show` 增加 `byte alpha = 255`、`uint surround = 0`；`UpdateLayeredWindow` 改为传 `BLENDFUNCTION`；DIB 与内存 DC 留存，换尺寸才重画；`Dispose` 释放。
- [x] 现有四处调用不改（默认值保持原行为）；Shell 编译 0 警告 0 错误。

### Task 3: 退场序列（红→绿，逐字抽取的专项）

- [x] `tests/Momoka.PlayerExitHandoffTests`：替身覆盖层记录 `bounds/topmost/alpha/surround`，断言「原客户区 → 新客户区 → alpha 255 单调降到 0 → 释放一次」，并覆盖「就绪迟迟不来」与「淡出中途被打断」；先跑红。
- [x] `src/Momoka.Shell/Views/PlayerPage.Transition.cs`：`ReturnToBrowseBeforeStopAsync` 在窗口还原后改铺新客户区（补舞台色），落定后等浏览页就绪，再逐拍淡出；`PlayerPage.xaml.cs` 补 `ExitSurround`/`BrowseReady`/`WaitExitFadeTickAsync` 等小成员。
- [x] `PlayerPage.MotionProbe.cs` 增 `ExitFadeProbeState`。
- [x] 跑 `Momoka.PlayerExitHandoffTests`，全绿。

### Task 4: 运行证据

- [x] 编译 Shell Release/x64，`--probe-player-motion <本地素材> --screen 2`：更新 `ObserveStopHandoffAsync`/`StopOverlayEvidence` 记录两段矩形、存活时长、逐拍 alpha 与屏幕取样，断言淡出真实发生。
- [x] 目视截图，确认窗口模式退出是「最后一帧淡出到主页」而不是硬切。

### Task 5: 收尾与交付

- [x] `dotnet format whitespace --verify-no-changes`、`git diff --check`。
- [x] 完整主测试 0 失败 0 跳过；受影响探针通过。
- [x] 提交（中文正文 + Co-Authored-By），合并主树，主树重验，`tools/publish.ps1` 刷新 `artifacts/publish/win-x64` 并核快捷方式。
- [x] 更新 PROGRESS.md。
