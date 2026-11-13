# P4 详情卡片副行可读性

日期：2026-11-13。独立树：`codex/winui-p4-music`，调查起点 `12c0ce24231d693f56fa1537dc29705edf2c65a8`；根代理已经整合P4音乐分页，本单元不再修改音乐代码。原生报告为根代理运行的 `shell-probe-20261112-172921686-15104`，`InspectDetailResponsive`失败：CastRow副行读到`#FFE8EDF7`，预期`#FFC9D0D9`。原报告保留，本修复不更改期望颜色、容差或基线。

## 源码事实与根因

`CastRow`是`DetailPage.xaml`的`ShelfStrip`实例，不是单独的CastRow控件。`DetailPage.Showcase.SettleShowcase`给EpisodeStrip/SeasonRow/CastRow/SimilarRow设置`OnScrim`，`ShelfStrip.PaintInk`传给实际PosterCard；`PosterCard.OnScrimChanged`把SubLine样式换到`EgOnScrimDataStyle`。该样式的Foreground指向`EgOnScrimDimBrush`（`#FFC9D0D9`），2026-11-08的产品注释和`AcrylicControlsTests`都明确要求这条副行层级。

后来`7807dbc70a67cc8db0536f156a614ed664e1d7a3`为首页订阅/媒体库标题新增了PosterCard全局`CaptionOnScrim`视觉状态。该状态又给`SubLine.Foreground`设置`EgCardCaptionSecondaryBrush`，Dark和Light字典都把它别名到`EgTextBrush`。因此视觉状态覆盖原样式；当前midnight主题的正文色恰为`#FFE8EDF7`，与实际失败一致。之后`86a40f1`、`cbb5406`删除首页实色底板、整排遮罩/模糊时仍保留这条全局文字状态。首页变更侵入详情原有文字职责，是生产回归；`RequireCardInk`仍符合详情原意，不是需要改成新正文色的陈旧判据。

## 最小修复与保留范围

PosterCard区分两个图上文字状态：CollectionFolder库导航卡继续使用`CaptionLibrary`及原`EgCardCaption*`，保留已确认的首页透明标题与主题正文墨；其他媒体/人员卡用`CaptionOnScrim`及新的`EgMediaCaption*`，分别别名到原标题/副行固定压图画刷。高对比字典的新角色均接`SystemColorWindowTextColor`，无顶层同名角色遮挡系统覆盖。

`PosterCard.Caption.cs`集中状态选择与样式应用。`OnScrimChanged`和`OnCardChanged`都调用同一入口，回收同一容器从首页库卡换成人员卡时不能保留原Library状态；关闭OnScrim恢复主题正文/暗字，Upcoming保留原Caption样式。未恢复任何首页底板或模糊，未改变CardItem图片/身份/请求生命周期。

固定画刷组整体提前到Palette.ThemeDictionaries之前，以供主题别名读取。所有原固定色值不变、资源键不重复。XAML编译器拒绝ResourceDictionary默认Items在ThemeDictionaries前后分两段，故不能只移动两个画刷而留其他固定色在后方；这是本树实际WMC0035证据，不推断所有资源前向解析都受同样限制。

## 覆盖与实际边界

PageContractTests新增3项，直接compile-link正式`PosterCard.Caption.cs`，使用真实CardItem和匿名会话验证五类媒体/人员、库导航、回收换型、开关恢复与Upcoming样式；再解析实际XAML视觉状态、Palette Dark/Light/HighContrast角色链、原`EgOnScrimDataStyle`与固定副行色，并注入错误首页角色证明测试拒绝覆盖。入口检查覆盖生产OnScrimChanged/OnCardChanged接线，控件/VisualStateManager是离线边界，不能证明实际优先级、像素和系统高对比表现。

本树Core/Shell/PageContractTests Release编译0警告0错误，页面契约210项、acrylic-controls-fixture39项均通过、0失败、0跳过；原首页三卡/透明标题契约也仍通过。原AcrylicControlsTests副行契约同步跟随真实方法搬到`PosterCard.Caption.cs`，继续核同一个EgOnScrimDataStyle/原固定副行色，不改预期值。主控须在最终整合代码重新运行`InspectDetailResponsive`，查看所有受影响主题的截图，并验证现有RequireCardInk仍拒绝把副行改成首页正文色；原始红报告不得删除或重写。没有驱动桌面、读取凭据、访问真实服务器或实际播放。

```powershell
./tools/run-regressions.ps1 -Suite Momoka.PageContractTests
```

主树最终原生运行、截图及发布结果由根代理另记，本单元离线通过不等于最终交付通过。
