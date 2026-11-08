# 集成播放器窗口模式的进退场过渡

状态：2026-11-08 用户确认三项选择（退场＝纯淡出、进场保持现状、只做窗口模式）；已实装，见下文「实装结果」。

## 请求与范围

用户要求：集成管线里以**窗口模式**运行的播放器页面，进入与退出播放页面时都要有过渡 UI 动画。进入用淡入或滑入等自然过渡，退出用淡出或滑出等对应离场效果；动画要流畅不卡顿、不改动原有布局与功能、与整体 UI 风格一致；进入与退出的时长与缓动要协调统一；动画期间不能闪屏或元素错位。

用户对三处选择给了明确答复：退场用**纯淡出**（不做位移、不做缩放）；**进场保持现状**（280ms，前 140ms 淡入，整段上浮 12px + 1.8% 收拢）；**只做窗口模式**，全屏退出维持现在的直接返回。

范围是 `PlayerViewModel.PrepareStopAsync` 接的那一条真实退出路径（返回箭头、Esc、窗口关闭都经它）。不改播放内核、上报、换集、全屏进出、独占模式与 uosc。

## 已读取的实现

- `src/Momoka.Core/Playback/PlayerMotion.cs`：进退场节奏的唯一出处。`EnterMilliseconds = 280`、`EnterFadeMilliseconds = 140`、`ExitMilliseconds = 240`、`DepthScale = 1.018`、`ExitScale = 0.985`、`ExitTravel = 8`、`ExitDimAt` 为线性。
- `src/Momoka.Shell/Views/PlayerPage.Transition.cs`：`TransitionPage` 用 16ms 计时器逐拍写 `Opacity`/`CompositeTransform`（注释记明为何不用 Storyboard 驱动 RenderTransform）；`ReturnToBrowseBeforeStopAsync` 是返回/Esc/关窗走的那一趟；`ExitWhenBrowseReady` + `BeginPlayerExitDissolve` 是 `Detach`/关停走的 240ms 溶解。
- `src/Momoka.Shell/Views/PlayerPage.xaml.cs`：`EnterPlayer` 在「不自动全屏」时调 `TransitionPage(entering: true, …)`，`LeavePlayer` 起退场底并按 `_exitSnap` 分叉。
- `src/Momoka.Shell/Windowing/VideoFrameOverlay.cs`：屏幕坐标的分层窗口（`UpdateLayeredWindow`，`ULW_ALPHA`，`pblend` 传 `IntPtr.Zero`），每次 `Show` 现建 DIB、用完即删。
- `tests/Momoka.PlayerExitHandoffTests`：逐字抽取 `ReturnToBrowseBeforeStopAsync`，用替身替掉窗口、抓帧、`WaitFramesAsync` 与 DWM 边界，三种几何各跑一遍。
- `src/Momoka.Shell/PlayerMotionProbe.cs`：`ObserveStopHandoffAsync` 每 4ms 采样本进程的 `MomokaVideoFrame` 窗口，`StopOverlayEvidence` 断言覆盖层矩形等于「移动宿主前的客户区」。

## 源码结论（本次改动前的事实）

| 方向 | 实际行为 |
| --- | --- |
| 进场（窗口模式） | 有：280ms，前 140ms 不透明度 0→1，整段 `Scale 1.018→1`、`Y +12→0`，16ms 计时器逐拍 |
| 退出（返回/Esc/关窗） | **没有**：抓一帧铺成屏幕坐标覆盖层 → 还原窗口 → `_exitSnap` 让页面当拍收摊 → 两帧后销毁覆盖层。屏上是「最后一帧 → 主页」的硬切 |
| 退出（Detach/关停） | 有：240ms 整页淡出 + 留帧逐拍压暗（`ExitWhenBrowseReady`） |

`ReturnToBrowseBeforeStopAsync` 里那条 `_exitSnap` 是 2026-09-25 用户令「退出播放窗口化的一瞬间会有视频的残留画面，不能直接切回主页吗」的产物：把窗口还原藏在一块屏幕坐标的覆盖层底下，画面就不会跟着窗口一起缩。代价是这一趟连退场动画一起没了。

## 实施方式与取舍

退场动画必须落在**覆盖层**上，不能在页面上：页面淡出淡不动画面（画面是挂在 `VideoHost` 上的 SpriteVisual），而且此刻页面已经收摊、盖在屏上的是那块覆盖层。

1. **覆盖层支持整层不透明度**（`VideoFrameOverlay`）。`Show` 增加 `alpha` 与 `surround` 两个可选参数（默认 255 / 透空，现有四处调用一字不改），并把 DIB 留存下来：同尺寸重复调用复用同一块位图，只有换尺寸或换底色才重画。
   **淡出用的是 `SetLayeredWindowAttributes(LWA_ALPHA)`，不是 `UpdateLayeredWindow` 的 `BLENDFUNCTION.SourceConstantAlpha`** —— 2026-11-08 用独立探针（400×400 纯红分层窗口、同一块位图不换）实测：ULW 的 `SourceConstantAlpha` 从 255 一路降到 0，屏上像素一个数都没变，`AlphaFormat` 取 0 或 1 都一样；同一个窗口改调 `SetLayeredWindowAttributes`，255→200→100→0 逐档变淡，读到的是「红 → 混进桌面底色 → 桌面原色」。所以淡出走 SLA，位图那一侧完全不动；前提是这一层此后不再重传像素，退场淡出正是这样（内容在改铺那一拍就定了），别的几处从头到尾都传 255。
2. **窗口还原之后把覆盖层改铺到新客户区**。画面按 contain 摆进新客户区：窗口按 `AspectLock.Fit` 保持宽度与中心，所以画面在屏上的位置与尺寸分毫不动；四周多出来的地方补舞台色（`PlayerStageBrush`，`PlayerPalette.Film`，`#0C0E11`），读作「播放页还在，只是化了」。不补底色的话那几道会透出主页，成为一条悬空的画面带。这一步与窗口还原在同一个同步块里，中间没有 `await`，所以不会有一帧露出未铺满的窗口。
3. **淡出整段 `PlayerMotion.ExitMilliseconds`，线性**。线性不是随手挑的：`ExitDimAt` 的注释记着 2026-09-20 的实测结论 —— 溶解配缓出曲线会在头几十毫秒掉掉大半、尾巴上再拖一条还读得出内容的残影。进场那段淡入保持 140ms 缓出（它的职责是抢在定位动画之前把浏览页盖住），两头共用**同一个总时长**（把 `ExitMilliseconds` 由 240 提到 280，与进场一致）。`PlayerMotion.ExitFadeAlpha(progress)` 是这条曲线的唯一出处，退场逐拍只读它。
4. **等浏览页就绪再淡**。沿用 `ExitWhenBrowseReady` 早就量过的那条判据与 800ms 宽限（`ActiveContentReady`）：图还没到就淡完，露出来的是一块发灰的空页面。宽限到点照常淡出。
5. 抓不到帧（`frame is null`）或系统关闭动画时，仍是原来的当拍收摊 —— 没有可淡的东西，不为它硬造一层。

取舍与已知代价：

- 淡出期间窗口已经还给浏览几何，主页可点。280ms 内点一张海报会与正在进行的停止抢一拍，和改动前「两帧」那条路的性质相同，只是窗口长了一点。
- 全屏退出仍是硬切：整屏覆盖层每拍重传一整帧（4K 约 33MB）不划算，且 2026-09-25 用户已拍板那条路「直接切回主页」。按用户本轮选择只做窗口模式。
- 淡出的是**常量 alpha**，覆盖层里的画面不做位移与缩放（用户选的「纯淡出」）。`ExitScale`/`ExitTravel` 那对镜像姿势因此只服务 `Detach` 那条溶解，不在这条路上。

## 测试与证据

- `Momoka.Tests`（`PlayerMotionTests`）：两向总时长相等、`ExitFadeAlpha` 两头与中点的数值、逐拍单调不增。
- `Momoka.PlayerExitHandoffTests`：逐字执行 `ReturnToBrowseBeforeStopAsync`，替身覆盖层记录每次 `Show` 的 `bounds/topmost/alpha/surround`，断言「先铺原客户区 → 改铺新客户区 → alpha 由 255 单调降到 0 → 释放一次」，并覆盖「浏览页迟迟不就绪」与「淡出中途被新一轮播放打断」两条分支。
- `--probe-player-motion`（隔离本地素材、副屏）：`ObserveStopHandoffAsync` 记录覆盖层两段矩形与存活时长，读 `ExitFadeProbeState` 的逐拍 alpha，并用屏幕取样证明那层确实在屏上变暗。
- 门禁按 CLAUDE.md：批次内先编译 Tests/Core，跑受影响测试；收尾跑完整主测试与受影响探针；合入主树后重验并刷新 `artifacts/publish/win-x64`。
