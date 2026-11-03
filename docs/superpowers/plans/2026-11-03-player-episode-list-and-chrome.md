# 播放页剧集列表与控件外观实施计划

**目标：** 集成模式的剧集菜单不再跑出窗口、列表覆盖全季并按播出时间排序、菜单改用亚克力、按钮图标加深色阴影、自动连播不再弹提示；自动连播随全季列表跨季。

**依据（用户令，2026-11-03）：**

1. 「播放页面的本季剧集按钮的集列表菜单不要跑到窗口外面，要始终保持在窗口内」
2. 「本季剧集改名为剧集列表，要显示所有季度的集列表，优先按照播出时间排序」
3. 「集成模式下所有的菜单都要用亚克力背景」
4. 「给集成模式的所按钮都改为白色实心并且加上黑边（除了左上角的返回按钮，和静音按钮这些有边框的）」→ 追问后改为「在原按钮的基础上加阴影就可以」
5. 「自动播放下一集时不要在画面上弄悬浮提示」

**调查结论（决定实现方式的事实）：**

- `Palette.xaml` 于 2026-10-30 把 `MenuFlyoutPresenterBackground` / `FlyoutPresenterBackground` 换成实心 `EgSurfaceBrush`；框架默认是 `DesktopAcrylicBackdrop` + 全透明底。播放页菜单因此不透明，恢复半透明底即是真亚克力（采的是桌面，含播放窗口里的画面）。
- 框架的 `MenuFlyout` 是窗口化弹层（亚克力靠 DWM 采样），不夹在窗口内；`MenuFlyoutPresenter` 默认不限高 → 二十几集的菜单在 720 高窗口里必然顶出窗口。
- 播放器的 `Episodes` 有时是全剧（剧页/集页入口），有时只这一季（季页入口、`FillSiblingsAsync`、跨季落地）。
- 自动连播当前钉死季内（`EpisodeNavigation.StepInSeason`，2026-09-18 用户令），本轮按用户新令改为跟随全季列表跨季。
- 悬浮提示来自 `PlayerViewModel.Transport.cs` 的 `Noticed?.Invoke("自动播放下一集：…")`。

**实现选择：** 全部在现有 WinUI / MVVM 路径内完成，不新增依赖。菜单仍用 `MenuFlyout`（键盘、读屏、子菜单都是现成的），只在打开前把 presenter 的 `MaxHeight` 按锚点上方余量收紧，并给每个菜单挂一支播放页自己的亚克力 presenter 样式。按钮阴影走项目已有的 `TextInk`（`DropShadow` + 元素 alpha 蒙版）。

- [x] Core：`EpisodeNavigation` 退掉「按季收窄」（`Step`／`StepInSeason`／`ResolveAdjacentAsync`），新增 `StepSeries`（整份列表走一步）；主测试同步改写。
- [x] Core：新增 `PlayerMenuFit`（菜单高度上限的纯函数）与测试。
- [x] 视图模型：单集起播后一律把 `Episodes` 规整成「全剧 + 播出顺序」（聚合队列除外）；`ResolveNextEpisodeAsync` 返回全剧列表；自动连播改走 `StepSeries`；删掉自动连播的 `Noticed`。
- [x] 播放页：剧集菜单按锚点收紧高度；六个菜单＋倍速轮盘＋右键菜单挂亚克力样式；传输条与右上窗口键的图标挂 `TextInk` 阴影（返回、静音、跳过三颗按用户令排除）。
- [x] 资源：`PlayerPage_EpisodeButton` 的 tooltip 与读屏名改为「剧集列表」；独占模式推给 uosc 的同一份菜单标题同步。
- [x] 回归：`Momoka.Tests`、`Momoka.NaturalContinuationCallerTests`（跨季用例与源 pins）、`ShellSelfCheck.Reads` 的「跨季相邻单集」改判据、`docs/selfcheck-baseline.txt`。
- [ ] 验收：Release 构建 0 警告、主测试、自检（含 `--dump-ui`）与播放页实屏截图；本轮不合并、不发布。

**已知缺口：** 另一会话的 Momoka 进程长期占用自检互斥与单实例，本树构建的实屏截图（`tools/shot.ps1`）与最终一版自检未能取得；菜单高度上限只有纯函数单测与接线，没有在真实窗口量过弹层矩形。

**验收限制：** 自检与截图用的是诊断窗口与假身份，不能替代真实服务器上的长列表菜单；真实播放会改观看记录，未经授权不跑。
