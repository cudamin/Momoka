# Fluent 2 布局、间距与形状

标准：Fluent 2（Layout / Shapes / Elevation）。Windows 分支：Fluent 2 官方指定的 WinUI 3。标注「Fluent 2」的是设计标准；标注「Windows 分支」的是平台落地值。

来源：Fluent 2 Layout / Shapes / Elevation / Iconography；`microsoft/fluentui` `packages/tokens` 提交 `ff7ee75` 的 `spacings.ts`、`borderRadius.ts`、`strokeWidths.ts`、`utils/shadows.ts`；Microsoft Learn Geometry / Screen sizes and breakpoints / Iconography；WinUI 3 `generic.xaml` 2.7.0。

## 1. Fluent 2 间距 ramp

Fluent 2 的基准单位是 4px，但 ramp 故意包含 2、6、10 三个非 4 倍数值，用来补偿 Fluent 图标的额外内边距，让图标与 4px 栅格对齐。间距从元素的边界框量起。

| 设计 token | 值 | 代码 token | 用途 |
| --- | --- | --- | --- |
| `sizeNone` | 0 | `spacing*None` | 无间距 |
| `size20` | 2 | `spacing*XXS` | 图标内边距、紧贴 |
| `size40` | 4 | `spacing*XS` | 基础单位、复选框与标签 |
| `size60` | 6 | `spacing*SNudge` | 图标对齐 |
| `size80` | 8 | `spacing*S` | 图标与文字、按钮内图标与文字 |
| `size100` | 10 | `spacing*MNudge` | 图标对齐 |
| `size120` | 12 | `spacing*M` | 组件内相关元素、按钮组 |
| `size160` | 16 | `spacing*L` | 卡片内边距、列表项内边距 |
| `size200` | 20 | `spacing*XL` | 区块之间 |
| `size240` | 24 | `spacing*XXL` | 区块之间、页面边距 |
| `size280` | 28 | — | 设计 ramp |
| `size320` | 32 | `spacing*XXXL` | 大区块 |
| `size360` | 36 | — | 页面边距 |
| `size400` | 40 | — | 页面边距 |
| `size480` | 48 | — | 大留白 |
| `size520` | 52 | — | 大留白 |
| `size560` | 56 | — | 大留白 |

规则：

- 用 ramp 里的值。不要用 14、18、22、26、30 这类不在 ramp 的间距。
- 2 / 6 / 10 只用于图标对齐与紧贴，不用来随意微调布局。
- 代码令牌只到 `spacing*XXXL` 32；28–56 用于设计稿与大留白，代码里用字面值或组件自有间距，并在评审记录说明。
- 同一层级的间距必须一致。用间距分组，不要用边框分组。

## 2. 栅格

- Fluent 2 常用 12 列栅格：列、gutter、margin 三部分；gutter 与 margin 取 4 的倍数。
- 基线网格用于多列文本：文本基线对齐，建立垂直节奏。
- 内容按「区域」组织：最重要的内容占最大列宽；不要把所有内容平均分栏。
- 响应式技巧：重排（reposition）、缩放（resize）、回流（reflow）、显示/隐藏（show/hide）、重构（re-architect）。先丢次要信息，再收导航，最后才缩小字号。

## 3. 断点

### Fluent 2 跨端断点

| 尺寸类 | 范围 |
| --- | --- |
| small | 320–479 |
| medium | 480–639 |
| large | 640–1023 |
| x-large | 1024–1365 |
| xx-large | 1366–1919 |
| xxx-large | 1920 以上 |

### Windows 分支

| 尺寸类 | 宽度 |
| --- | --- |
| Small | 不超过 640 |
| Medium | 641–1007 |
| Large | 不小于 1008 |

规则：

- Momoka 是 Windows 桌面应用，布局验收用 Windows 分支的 640 / 1008；设计稿可以标注 Fluent 2 的 size class 作为跨端参考。
- 按**应用窗口的内容区宽度**判断，不按物理屏幕宽度。侧栏占掉的宽度不算内容区。
- `NavigationView` 默认阈值与 Windows 分支一致：`CompactModeThresholdWidth=641`、`ExpandedModeThresholdWidth=1008`。
- Small：单列、页面边距 16、隐藏次要命令、导航收成 `LeftMinimal` 或底部。
- Medium：两列或收窄的导航；保留主操作。
- Large：展开导航、多列、完整命令区；正文仍限制行宽。

## 4. 圆角

Fluent 2 的圆角令牌：

| 令牌 | 值 | 典型用途 |
| --- | --- | --- |
| `borderRadiusNone` | 0 | 贴边、相接元素、导航条 |
| `borderRadiusSmall` | 2 | 小徽标、小于 32px 的元素 |
| `borderRadiusMedium` | 4 | 按钮、下拉框、输入框 |
| `borderRadiusLarge` | 6 | 大按钮、较大的控件 |
| `borderRadiusXLarge` | 8 | 大按钮、卡片、浮层 |
| `borderRadius2XLarge` | 12 | sheet、popover、按钮面板 |
| `borderRadius3XLarge` | 16 | 大容器 |
| `borderRadius4XLarge` | 24 | 大面板 |
| `borderRadius5XLarge` | 32 | 特大容器 |
| `borderRadius6XLarge` | 40 | 特大容器 |
| `borderRadiusCircular` | 10000px | 头像、圆形按钮 |

Fluent 2 Shapes 页面的简化映射：

- 小于 32px 的元素：2
- 按钮、下拉：4
- 大按钮：8
- 按钮面板、popover：12
- 人物头像：50%

Windows 分支：

| 值 | 用途 | 令牌 |
| --- | --- | --- |
| 8 | 窗口、对话框、Flyout、MenuFlyout、TeachingTip | `OverlayCornerRadius` |
| 4 | Button、CheckBox、ComboBox、TextBox、ListView 项、进度条、滚动条、Slider、ToolTip | `ControlCornerRadius` |
| 0 | 相接元素（SplitButton 两半）、贴窗口边缘、窗口贴边或最大化 | 无 |
| 2 | Fluent 2 小元素；WinUI 不强制，按需 | 无 |

规则：

- 不要给每个容器自造圆角。用 Fluent 2 令牌或 Windows 分支的全局圆角。
- 相接的两个元素之间不留缝、不圆角，否则会出现缺口。
- 图片圆角与所在卡片一致；卡片 8 时图片也用 8，不要图片 4、卡片 8。
- 12 是 `borderRadius2XLarge`，用于 sheet / popover；不要给普通卡片或按钮用 12。

## 5. 描边

| Fluent 2 令牌 | 值 | 用途 |
| --- | --- | --- |
| `strokeWidthThin` | 1 | 默认描边、分割线 |
| `strokeWidthThick` | 2 | 焦点环、强调边界 |
| `strokeWidthThicker` | 3 | 大元素强调 |
| `strokeWidthThickest` | 4 | 特殊图形 |

Windows 分支默认 1px。不要用 2px 边框表达层级；层级用表面与高程。焦点环用 2px（Fluent 2 `strokeWidthThick`）。

## 6. 高程与阴影

Fluent 2 用 `shadow2` / `shadow4` / `shadow8` / `shadow16` / `shadow28` / `shadow64` 表示高程。Windows 分支用 1px 描边代替 key shadow，并用高程值表达层级：

| 表面 | Fluent 2 阴影档位 | Windows 分支高程 |
| --- | --- | --- |
| Layer（页面底层） | 无 | 1 + 1px 描边 |
| Control | `shadow2` | 2 + 1px 描边 |
| Card | `shadow2`（浅）/ `shadow4`（深） | 8 + 1px 描边 |
| ToolTip | `shadow8` | 16 + 1px 描边 |
| Flyout / MenuFlyout | `shadow8`–`shadow16` | 32 + 1px 描边 |
| Dialog | `shadow64` | 128 + 1px 描边 |

Fluent 2 Elevation 页面明确说明：**Windows uses strokes instead of key shadows to outline an object.** 所以 Windows 分支的卡片默认只有 1px 描边，不加阴影；需要浮起（拖拽、选中预览）时才加阴影。

完整阴影 CSS 值见 `materials-motion-states.md`。

## 7. 控件高度与点击区

Fluent 2 按钮尺寸（由 `useButtonStyles.styles.ts` 的 line-height、padding、1px stroke 推导）：

| 尺寸 | 高度 | 字号 / 行高 | 上下 padding | 图标 |
| --- | --- | --- | --- | --- |
| small | 24 | 12 / 16 | 3 | 20 |
| medium | 32 | 14 / 20 | 5 | 20 |
| large | 40 | 16 / 22 | 8 | 24 |

Windows 分支：

| 项 | 值 | 说明 |
| --- | --- | --- |
| 标准控件高度 | 32 | WinUI Button、TextBox、ComboBox 的默认 `MinHeight` |
| 紧凑控件高度 | 24 | 需显式设置；只用于高密度表格或工具条 |
| 大按钮高度 | 40 | 主操作、触控场景 |
| 触摸目标 | 不小于 40 × 40 | Windows 分支；Fluent 2 web/iOS 建议 44 × 44，Android 48 × 48 |
| 相邻目标间距 | 不小于 8 | 避免误触 |

规则：

- 不要用 `Height` 写死控件高度；用 `MinHeight` 加内边距，让内容与本地化文本能撑开。
- 紧凑密度只在数据密集区域使用；设置页、对话框保持标准密度。
- 同一组控件高度一致；主操作可以更大，但不要在同一行混用 24 与 40。

## 8. 图标

- Fluent 2 系统图标有 Regular 与 Filled 两套：Regular 用于导航与操作，Filled 用于选中态或需要更大视觉重量的场景。
- Fluent 2 建议 12px 图标只用于信息提示，太小不适合交互；图标尺寸随使用场景变化，交互图标要保证点击区。
- Windows 分支：`Segoe Fluent Icons`，1px 单线；常用档位 16 / 20 / 24 / 32 / 48（推荐，不是平台强制）。
- 同一界面只用一个档位；需要强调时改颜色或容器，不要混用 16 与 24。
- 图标跟随前景色。语义色只给状态图标，不要给每个功能图标配不同颜色。
- 图标与文字间距 8；纯图标按钮必须提供 `AutomationProperties.Name` 与 `ToolTip`。

## 9. 页面结构

| 区域 | 推荐做法 |
| --- | --- |
| 页面标题 | `title2` 28 / 36（Windows 分支 `TitleTextBlockStyle`），左对齐，与内容间距 24 |
| 页面副标题 | `body1` 14 / 20，`colorNeutralForeground2`，与标题间距 4–8 |
| 区块标题 | `subtitle1` 20 / 26（Windows 分支 20 / 28），与上一区块间距 32，与内容间距 12 |
| 正文 | `body1` 14 / 20，行宽 50–60 字符 |
| 命令区 | 页面标题右侧或内容上方；主操作不超过一个 |
| 页面边距 | Fluent 2 ramp：16 / 24 / 36；Windows 分支按断点取 16 / 24 / 36 |

- 内容左对齐是默认。不要把正文居中，除非是空状态或引导页。
- 标题层级最多三级（页面标题、区块标题、卡片标题）。第四级用 `BodyStrong`，不要再造一个字号。
- 页面根默认透明，让窗口的 Mica 透出来；只有需要独立底色的内容区才铺实心或 Layer。

## 10. 反例

| 反例 | 问题 | 修法 |
| --- | --- | --- |
| `Margin="14,18,14,18"` | 不在 Fluent 2 ramp | 用 12 / 16 / 20 |
| 看到 6px 或 10px 就判错 | 2 / 6 / 10 是 ramp 的图标对齐值 | 按用途判断，不按「是否 4 的倍数」 |
| 按钮圆角 6 | Fluent 2 按钮用 `borderRadiusMedium` 4 | 4 |
| 普通卡片圆角 12 | 12 是 `borderRadius2XLarge`，用于 sheet / popover | 4 或 8 |
| 卡片加阴影又加边框 | Windows 分支用描边代替 key shadow | 只留 1px 描边 |
| 用 `Height="32"` 写死按钮 | 本地化文本被裁 | `MinHeight="32"` |
| 页面边距固定 48 | 窄窗口内容被挤没 | 按断点降到 24 或 16 |
| 图标 16 与 24 混排 | 视觉重量不一致 | 统一 16 或 20 |
| 只看窗口宽度切布局 | 侧栏占宽后内容区过窄 | 用内容区宽度判断 |
| 窄窗口缩小字号 | 可读性下降 | 保持字号，减少列与次要信息 |
