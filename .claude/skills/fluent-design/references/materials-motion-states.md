# 材质、动效与控件状态

来源：Microsoft Learn Materials / Layering / Motion，WinUI 3 `generic.xaml`。材质与动效是 Fluent 最容易「看起来像」却「用错」的部分，先定层级再选材质。

## 1. 层叠模型

| 层 | 内容 | 默认材质 |
| --- | --- | --- |
| base | 窗口、标题栏、导航 | Mica；不支持时实心 |
| content | 页面、分区、卡片 | 透明或 Layer；卡片用 Card 填充 |
| overlay | 对话框、Flyout、菜单、TeachingTip、ToolTip | Acrylic 或实心，加 1px 描边与阴影 |

规则：

- 层级决定圆角：base 的窗口 8（系统绘制），content 的控件 4，overlay 8。
- 层级决定高程：Layer 1、Control 2、Card 8、ToolTip 16、Flyout 32、Dialog/Window 128。
- 一个区域只允许一种表面语言。不要 Mica 上叠 Acrylic，再在 Acrylic 里放 Card。
- 页面根透明，让 base 的 Mica 透出来；只有内容需要独立底色时才铺实心。

## 2. 材质决策树

```
这个表面是什么？
├─ 窗口底层 / 标题栏
│  ├─ Windows 11 且支持 Mica → MicaBackdrop（Base）
│  ├─ 带 Tab 的标题栏，需要更强着色 → MicaKind.BaseAlt
│  └─ 不支持 / 远程桌面 / 高对比 → SolidBackgroundFillColorBaseBrush
├─ 浮层、菜单、对话框
│  ├─ 需要透出背后内容 → Acrylic（AcrylicBackgroundFillColorDefaultBrush）
│  └─ 需要稳定阅读 → SolidBackgroundFillColorQuarternaryBrush
├─ 页面内卡片 / 分区
│  └─ CardBackgroundFillColorDefaultBrush + CardStrokeColorDefaultBrush
└─ 侧栏 / 页面内层
   └─ LayerFillColorDefaultBrush
```

判断口诀：**Mica 给窗口，Acrylic 给浮层，Card 给内容，Layer 给分区。** 页面内不要用 Mica 或 Acrylic。

## 3. Mica

- 用途：长期存在的窗口背景，让桌面壁纸透出轻微色调，减少大面积纯色。
- API：`MicaBackdrop`，`Kind` 取 `MicaKind.Base` 或 `MicaKind.BaseAlt`；挂在 `Window.SystemBackdrop`。
- 支持：Windows 11 22000 及以上。旧系统、远程桌面、透明效果关闭时回退为纯色。
- 回退：`SolidBackgroundFillColorBaseBrush`。不要自己画一张假壁纸。
- 不要把 Mica 用在页面内卡片、侧栏块、对话框内容上；那些位置用 Card 或 Layer。
- 一个窗口只挂一个 Mica。多窗口各自挂各自的。

## 4. Acrylic

- 用途：临时或轻关闭表面，让用户保持对背后内容的上下文感。
- 窗口级：`DesktopAcrylicBackdrop`，用于需要整窗亚克力的场景；普通应用优先 Mica。
- 应用内：`AcrylicBrush`，或用现成令牌 `AcrylicInAppFillColorDefaultBrush`、`AcrylicBackgroundFillColorDefaultBrush`。
- 必须设置回退色（`FallbackColor`），在透明效果关闭或性能不足时用实心替代。
- 不要全屏铺 Acrylic；不要在 Acrylic 上再叠 Acrylic；不要在 Acrylic 上放长文正文。
- 对话框、Flyout、MenuFlyout 的默认样式已经带合适的背景。除非产品明确要求，不要重写。

## 5. 阴影

- 阴影只表达真实层叠。Flyout、Dialog、ToolTip 已内置；卡片默认只有 1px 描边。
- 需要手动加时用 `ThemeShadow`，并设置 `Translation` 的 Z 值；不要用多层 `DropShadow` 堆叠。
- 同一位置只保留高程最高的对象。被完全遮住的表面不要继续渲染阴影。
- 高对比模式下阴影可能被系统关闭；层级必须仍能靠描边与表面色分辨。

## 6. 动效

### 时长与曲线

| 用途 | 曲线 | 时长 | 动什么 |
| --- | --- | --- | --- |
| 直接进入 | `cubic-bezier(0,0,0,1)` | 167 / 250 / 333ms | 位置、缩放、旋转 |
| 点对点（已有元素移动） | `cubic-bezier(0.55,0.55,0,1)` | 167 / 250 / 333ms | 位置、缩放、旋转 |
| 直接退出 | `cubic-bezier(0,0,0,1)` | 167ms | 位置、缩放、旋转，必须同时淡出 |
| 柔和退出 | `cubic-bezier(1,0,1,1)` | 167ms | 位置、缩放 |
| 淡入淡出 | 线性 | 83ms | 不透明度 |
| 强调进入 | 三段关键帧 | 167 + 167 + 333ms | 位置、缩放 |

WinUI 令牌：

- `ControlFasterAnimationDuration` = 83ms
- `ControlFastAnimationDuration` = 167ms
- `ControlNormalAnimationDuration` = 250ms
- `ControlFastOutSlowInKeySpline` = `0,0,0,1`

### 规则

- 只动 `Opacity` 与 `RenderTransform`（`Translation`、`Scale`、`Rotation`）。不要动 `Width`、`Height`、`Margin`、`Padding`，会触发布局重算与抖动。
- 同一状态变化只用一个动画。不要同时做位移、缩放、颜色、阴影四件事。
- 退出必须配淡出；只位移不淡出会显得「被切断」。
- 页面转场用 `NavigationTransitionInfo`；共享元素用 `ConnectedAnimationService`；微交互用 `AnimatedIcon`；不要手写一套转场系统。
- 动效不能遮挡焦点、选中态与错误提示。动画结束后状态必须仍然可见。
- 尊重系统「减少动画」：读 `UISettings.AnimationsEnabled`，关闭时跳过非必要动效，保留即时状态切换。
- 动效不是装饰。不能说明层级、方向或因果的动画，删掉。

## 7. 控件状态

| 状态 | 背景 | 前景 | 高程 | 说明 |
| --- | --- | --- | --- | --- |
| Rest | `ControlFillColorDefaultBrush` | `TextFillColorPrimaryBrush` | 2 | 默认 |
| PointerOver | `ControlFillColorSecondaryBrush` | `TextFillColorPrimaryBrush` | 2 | 只换背景，不加边框 |
| Pressed | `ControlFillColorTertiaryBrush` | `TextFillColorSecondaryBrush` | 1 | 下沉；不要只换色 |
| Selected | 主操作用 `AccentFillColorDefaultBrush`；列表用 `SubtleFillColorSecondaryBrush` 加选中指示条 | 强调底用 `TextOnAccentFillColorPrimaryBrush`，其余用 Primary | 2 | 选中必须与悬停可区分 |
| Disabled | `ControlFillColorDisabledBrush` | `TextFillColorDisabledBrush` | 2 | 不投阴影；不要只降透明度 |
| Focused | 保持原背景 | 保持原前景 | 2 | 外加系统双色焦点环 |

补充：

- 背景色变化用 `BrushTransition`，默认 83ms；不要用关键帧动画做颜色过渡。
- 列表项禁用透明度可用 `ItemContainerDisabledOpacity`（0.3）；普通控件优先换禁用令牌，不要整块降透明度。
- 选中态不能只靠颜色。列表加左侧 3px 强调条或勾选图标；卡片加边框或勾选。
- 悬停与按下都要在深色、浅色、高对比三套下检查。

## 8. 焦点视觉

- 保留 `UseSystemFocusVisuals=True`。
- 系统焦点环是双色的：外环与内环颜色相反，保证在任何背景上都可见；外环较粗、内环较细。不要改成单色 1px，也不要用强调色替代。
- 自定义模板时给焦点目标设 `Control.IsTemplateFocusTarget=True`，或用 `FocusVisualPrimaryBrush` / `FocusVisualSecondaryBrush` 接管。
- `FocusVisualMargin` 用负值把焦点环画到控件外侧。Button 默认 `-3`；自定义控件按圆角与描边调整，别让环压住内容。
- 焦点顺序跟随视觉顺序。不要用 `TabIndex` 打乱；需要跳过装饰元素时设 `IsTabStop=False`。
- 焦点环不能被 `Clip` 裁掉。列表、滚动容器、圆角卡片尤其要检查。

## 9. 高对比与减少动效

- 高对比模式由系统替换主题资源。不要写 `if (IsHighContrast)` 的硬编码颜色分支；用系统令牌即可。
- 高对比下阴影可能消失，必须靠 1px 描边与表面色保持层级。
- 高对比下图标与文字都可能变成系统色；不要假设强调色仍然可辨。
- 减少动效时，页面转场可退化为即时切换；不要留下必须等动画结束才能操作的遮罩。
- 远程桌面、虚拟机可能关闭透明效果；Mica/Acrylic 必须能回退到实心且不影响可读性。

## 10. 反例

| 反例 | 问题 | 修法 |
| --- | --- | --- |
| 页面根铺 Acrylic | 性能差，层级混乱 | 页面透明，窗口用 Mica |
| 卡片上再叠 Mica | 材质套材质 | 卡片用 Card 填充 |
| 对话框用纯透明背景 | 背后内容干扰阅读 | Acrylic 或实心 + 描边 |
| 悬停只改边框颜色 | 与 Fluent 状态语言不符 | 改背景填充 |
| 按下只改颜色不下沉 | 缺少物理反馈 | 高程 2 降到 1 |
| 用 1px 单色焦点框 | 深色背景不可见 | 系统双色焦点环 |
| 动画改 `Width` 做展开 | 布局抖动、掉帧 | 用 `Scale` 或 `Opacity` |
| 所有卡片都加阴影 | 层级噪音 | 只给真实浮起的表面 |
| 关闭动画后功能不可用 | 把操作绑在动画完成事件上 | 状态先落，再播动效 |
