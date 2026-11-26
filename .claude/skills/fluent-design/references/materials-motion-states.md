# Fluent 2 材质、动效与控件状态

标准：Fluent 2（Elevation / Motion / Color）。Windows 分支：Fluent 2 官方指定的 WinUI 3。材质与动效是 Fluent 最容易「看起来像」却「用错」的部分，先定层级再选材质。

来源：Fluent 2 Elevation / Motion / Color；`microsoft/fluentui` `packages/tokens` 提交 `ff7ee75` 的 `durations.ts`、`curves.ts`、`utils/shadows.ts`；Microsoft Learn Materials / Layering / Motion；WinUI 3 `generic.xaml` 2.7.0。

## 1. 层叠模型

| 层 | 内容 | 默认表面 |
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

Fluent 2 的 Windows 页面明确让 Windows 应用使用 WinUI 组件；下面的材质是 Fluent 2 在 Windows 分支的落地方式。

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

Fluent 2 的阴影由 ambient（柔和、扩散）与 key（方向性、定义边缘）两层组成。令牌实现（`utils/shadows.ts`）：

| 令牌 | 浅色 | 深色 |
| --- | --- | --- |
| `shadow2` | `0 0 2px rgba(0,0,0,0.12), 0 1px 2px rgba(0,0,0,0.14)` | `0 0 2px rgba(0,0,0,0.24), 0 1px 2px rgba(0,0,0,0.28)` |
| `shadow4` | `0 0 2px rgba(0,0,0,0.12), 0 2px 4px rgba(0,0,0,0.14)` | `0 0 2px rgba(0,0,0,0.24), 0 2px 4px rgba(0,0,0,0.28)` |
| `shadow8` | `0 0 2px rgba(0,0,0,0.12), 0 4px 8px rgba(0,0,0,0.14)` | `0 0 2px rgba(0,0,0,0.24), 0 4px 8px rgba(0,0,0,0.28)` |
| `shadow16` | `0 0 2px rgba(0,0,0,0.12), 0 8px 16px rgba(0,0,0,0.14)` | `0 0 2px rgba(0,0,0,0.24), 0 8px 16px rgba(0,0,0,0.28)` |
| `shadow28` | `0 0 8px rgba(0,0,0,0.12), 0 14px 28px rgba(0,0,0,0.14)` | `0 0 8px rgba(0,0,0,0.24), 0 14px 28px rgba(0,0,0,0.28)` |
| `shadow64` | `0 0 8px rgba(0,0,0,0.12), 0 32px 64px rgba(0,0,0,0.14)` | `0 0 8px rgba(0,0,0,0.24), 0 32px 64px rgba(0,0,0,0.28)` |

品牌色表面用 `shadow*Brand`，ambient `rgba(0,0,0,0.30)`、key `rgba(0,0,0,0.25)`；不要在主阴影 ramp 上直接套品牌色。

Windows 分支：

- **Windows uses strokes instead of key shadows.** 卡片默认只有 1px 描边，不加阴影；需要浮起时才加。
- 需要手动加时用 `ThemeShadow`，并设置 `Translation` 的 Z 值；不要用多层 `DropShadow` 堆叠。
- 同一位置只保留高程最高的对象。被完全遮住的表面不要继续渲染阴影。
- 高对比模式下阴影可能被系统关闭；层级必须仍能靠描边与表面色分辨。

## 6. 动效

### Fluent 2 时长

| 令牌 | 值 | 典型用途 |
| --- | --- | --- |
| `durationUltraFast` | 50ms | 极小反馈、颜色微变 |
| `durationFaster` | 100ms | 小元素进入/退出 |
| `durationFast` | 150ms | 控件状态、短位移 |
| `durationNormal` | 200ms | 常规过渡 |
| `durationGentle` | 250ms | 较大的元素 |
| `durationSlow` | 300ms | 面板、浮层 |
| `durationSlower` | 400ms | 大范围转场 |
| `durationUltraSlow` | 500ms | 页面级、强调转场 |

### Fluent 2 曲线

| 令牌 | 值 | 用途 |
| --- | --- | --- |
| `curveAccelerateMax` | `cubic-bezier(0.9,0.1,1,0.2)` | 快速退出 |
| `curveAccelerateMid` | `cubic-bezier(1,0,1,1)` | 退出 |
| `curveAccelerateMin` | `cubic-bezier(0.8,0,0.78,1)` | 轻退出 |
| `curveDecelerateMax` | `cubic-bezier(0.1,0.9,0.2,1)` | 快速进入 |
| `curveDecelerateMid` | `cubic-bezier(0,0,0,1)` | 进入、直接进入 |
| `curveDecelerateMin` | `cubic-bezier(0.33,0,0.1,1)` | 轻进入 |
| `curveEasyEaseMax` | `cubic-bezier(0.8,0,0.2,1)` | 大元素移动 |
| `curveEasyEase` | `cubic-bezier(0.33,0,0.67,1)` | 常规移动 |
| `curveLinear` | `cubic-bezier(0,0,1,1)` | 淡入淡出、旋转 |

规则：

- 进入用 decelerate，退出用 accelerate；已有元素移动用 easyEase。
- 元素越大、移动距离越远，时长越长；不要给小图标 500ms。
- 只动 `Opacity` 与 `RenderTransform`（`Translation`、`Scale`、`Rotation`）。不要动 `Width`、`Height`、`Margin`、`Padding`。
- 同一状态变化只用一个动画。不要同时做位移、缩放、颜色、阴影四件事。
- 退出必须配淡出；只位移不淡出会显得「被切断」。
- 页面转场用 `NavigationTransitionInfo`；共享元素用 `ConnectedAnimationService`；微交互用 `AnimatedIcon`。
- 尊重系统「减少动画」：读 `UISettings.AnimationsEnabled`，关闭时跳过非必要动效，保留即时状态切换。

### Windows 分支映射

| Windows 值 | 近似 Fluent 2 档位 | 用途 |
| --- | --- | --- |
| 83ms | `durationFaster` 100ms | 淡入淡出、颜色过渡 |
| 167ms | `durationFast` 150ms | 快速进入/退出 |
| 250ms | `durationGentle` 250ms | 常规过渡 |
| 333ms | `durationSlow` 300ms | 较大元素、强调进入 |

曲线：Windows 的 `0,0,0,1` 对应 `curveDecelerateMid`；`0.55,0.55,0,1` 对应 point-to-point；`1,0,1,1` 对应 `curveAccelerateMid`；淡入淡出用线性。Windows 分支的实际值不是 Fluent 2 的逐值等价，验收按平台分支记录。

## 7. 控件状态

Fluent 2 的交互态方向：**越交互越深**（rest → hover → pressed/selected）。Windows 分支相反：**控件越交互越亮**。

| 状态 | Fluent 2 令牌 | Windows 分支 |
| --- | --- | --- |
| Rest | `colorNeutralBackground1` / `colorSubtleBackground` | `ControlFillColorDefaultBrush` |
| PointerOver | `colorNeutralBackground1Hover`（浅 `#f5f5f5` / 深 `#3d3d3d`） | `ControlFillColorSecondaryBrush` |
| Pressed | `colorNeutralBackground1Pressed`（浅 `#e0e0e0` / 深 `#1f1f1f`） | `ControlFillColorTertiaryBrush`；高程 2 → 1 |
| Selected | `colorNeutralBackground1Selected`（浅 `#ebebeb` / 深 `#383838`）或品牌色 | `SubtleFillColorSecondaryBrush` + 3px 品牌色指示条 |
| Disabled | `colorNeutralBackgroundDisabled` + `colorNeutralForegroundDisabled` | `ControlFillColorDisabledBrush` + `TextFillColorDisabledBrush` |
| Focused | `colorStrokeFocus1` / `colorStrokeFocus2`，2px | 系统双色焦点环 |

补充：

- 背景色变化用 `BrushTransition`，默认 83ms；不要用关键帧动画做颜色过渡。
- 列表项禁用透明度可用 `ItemContainerDisabledOpacity`（0.3）；普通控件优先换禁用令牌，不要整块降透明度。
- 选中态不能只靠颜色。列表加左侧 3px 强调条或勾选图标；卡片加边框或勾选。
- 悬停与按下都要在深色、浅色、高对比三套下检查。

## 8. 焦点视觉

- Fluent 2 的焦点指示用 `strokeWidthThick`（2px）与 `colorStrokeFocus1` / `colorStrokeFocus2`；两者是反色，保证在任何背景上都可见。
- Windows 分支保留 `UseSystemFocusVisuals=True`。系统焦点环是双色的：外环与内环颜色相反；外环较粗、内环较细。不要改成单色 1px，也不要用品牌色替代。
- 自定义模板时给焦点目标设 `Control.IsTemplateFocusTarget=True`，或用 `FocusVisualPrimaryBrush` / `FocusVisualSecondaryBrush` 接管。
- `FocusVisualMargin` 用负值把焦点环画到控件外侧。Button 默认 `-3`；自定义控件按圆角与描边调整，别让环压住内容。
- 焦点顺序跟随视觉顺序。不要用 `TabIndex` 打乱；需要跳过装饰元素时设 `IsTabStop=False`。
- 焦点环不能被 `Clip` 裁掉。列表、滚动容器、圆角卡片尤其要检查。

## 9. 高对比与减少动效

- 高对比模式由系统替换主题资源。不要写 `if (IsHighContrast)` 的硬编码颜色分支；用系统令牌即可。
- 高对比下阴影可能消失，必须靠 1px 描边与表面色保持层级。
- 高对比下图标与文字都可能变成系统色；不要假设品牌色仍然可辨。
- 减少动效时，页面转场可退化为即时切换；不要留下必须等动画结束才能操作的遮罩。
- 远程桌面、虚拟机可能关闭透明效果；Mica/Acrylic 必须能回退到实心且不影响可读性。

## 10. 反例

| 反例 | 问题 | 修法 |
| --- | --- | --- |
| 页面根铺 Acrylic | 性能差，层级混乱 | 页面透明，窗口用 Mica |
| 卡片上再叠 Mica | 材质套材质 | 卡片用 Card 填充 |
| 对话框用纯透明背景 | 背后内容干扰阅读 | Acrylic 或实心 + 描边 |
| 悬停只改边框颜色 | 与 Fluent 2 状态语言不符 | 改背景填充 |
| 按下只改颜色不下沉 | 缺少物理反馈 | 高程 2 降到 1 |
| 用 1px 单色焦点框 | 深色背景不可见 | 系统双色焦点环 |
| 动画改 `Width` 做展开 | 布局抖动、掉帧 | 用 `Scale` 或 `Opacity` |
| 所有卡片都加阴影 | Windows 分支用描边代替 key shadow | 只给真实浮起的表面 |
| 把 web 的 hover 变深套到 Windows | Windows 控件越交互越亮 | 用 WinUI 状态令牌 |
| 关闭动画后功能不可用 | 把操作绑在动画完成事件上 | 状态先落，再播动效 |
