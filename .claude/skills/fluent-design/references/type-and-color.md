# 排版与颜色

来源：Microsoft Learn Typography / Color，WinUI 3 `generic.xaml`（`Microsoft.WindowsAppSDK.WinUI 2.7.0`）。色值写作 `#AARRGGBB`，随 Windows 版本与用户强调色变化，只用于核对，不要在代码里写死。

## 1. 字体

- 字体家族：`Segoe UI Variable`（Windows 11）。XAML 默认 `XamlAutoFontFamily`，会按系统语言回退。
- `Segoe UI Variable` 是可变字体，光学尺寸（`opsz`）按字号自动匹配，不要手动指定。
- 全应用只用一种正文字体。不要混用 Segoe UI、Arial、微软雅黑；语言回退交给系统。
- 代码、日志、等宽数字用 `Cascadia Mono` 或 `Consolas`，只限代码块与技术信息，不用于界面正文。

## 2. 字号阶梯

| 样式 | 字号 / 行高 | 字重 | XAML 样式键 | 典型用途 |
| --- | --- | --- | --- | --- |
| Caption | 12 / 16 | Regular | `CaptionTextBlockStyle` | 时间戳、徽标、辅助说明、图表轴标签 |
| Body | 14 / 20 | Regular | `BodyTextBlockStyle` | 正文、列表副行、设置项说明 |
| Body Strong | 14 / 20 | Semibold | `BodyStrongTextBlockStyle` | 列表主行、强调正文、表单标签 |
| Body Large | 18 / 24 | Regular | `BodyLargeTextBlockStyle` | 引导段、空状态说明 |
| Body Large Strong | 18 / 24 | Semibold | `BodyLargeStrongTextBlockStyle` | 引导段强调 |
| Subtitle | 20 / 28 | Semibold | `SubtitleTextBlockStyle` | 区块标题、卡片组标题 |
| Title | 28 / 36 | Semibold | `TitleTextBlockStyle` | 页面标题 |
| Title Large | 40 / 52 | Semibold | `TitleLargeTextBlockStyle` | 首屏、空状态主标题 |
| Display | 68 / 92 | Semibold | `DisplayTextBlockStyle` | 极少使用；仅品牌化首屏 |

补充事实：

- WinUI 样式通过 `LineStackingStrategy=MaxHeight` 与 `TextLineBounds=Full` 使用字体度量，默认不写 `LineHeight`。需要精确行高时才显式设置，并配 `LineStackingStrategy=BlockLineHeight`。
- 字号资源键：`CaptionTextBlockFontSize` 12、`BodyTextBlockFontSize` 14、`BodyLargeTextBlockFontSize` 18、`SubtitleTextBlockFontSize` 20、`TitleTextBlockFontSize` 28、`TitleLargeTextBlockFontSize` 40、`DisplayTextBlockFontSize` 68。
- 不用 13、15、17 这类中间值。视觉层级不够时改字重或颜色，不要微调字号。

## 3. 排版规则

- 字重只用 `Regular` 与 `SemiBold`。`Bold` 与 `Italic` 不属于 Windows 字体阶梯；强调用 `SemiBold`。
- 最小可读尺寸：12px Regular 或 14px Semibold。更小或更细的字号在部分语言下不可读。
- 文案用句首大写（sentence case），包括标题与按钮。专有名词保持原样。
- 一行 50–60 个字符最易读。宽屏正文要限制行宽，不要铺满整窗。
- 溢出优先省略号（`TextTrimming=CharacterEllipsis`），不要硬裁；多行正文才换行。
- 左对齐是默认。只有图标下方文字、数字表格等少数场景居中。
- 中英文混排不为中文单独换字体；中英文之间是否加空格跟随产品既有文案，不在同一产品内混用两种做法。
- 标签与值成对出现时，标签用 Secondary 色，值用 Primary 色；不要靠字号差异区分。

## 4. 颜色模型

按三层组织，先定层再选令牌：

| 层 | 内容 | 默认表面 |
| --- | --- | --- |
| base | 窗口、标题栏、导航 | Mica；不支持时 `SolidBackgroundFillColorBaseBrush` |
| content | 页面、分区、卡片 | 透明（露出 base）或 `LayerFillColorDefaultBrush`；卡片用 `CardBackgroundFillColorDefaultBrush` |
| overlay | 对话框、菜单、浮层、提示 | Acrylic 或 `SolidBackgroundFillColorQuarternaryBrush`，加 1px 描边与阴影 |

规则：

- 一个区域只允许一层表面。不要在卡片外再套带背景与描边的容器。
- 页面根默认透明，让 Mica 透出来。只有内容需要独立底色时才铺 `SolidBackgroundFillColorBaseBrush`。
- 深色主题里，越重要的表面越亮；浅色主题里，越重要的表面越白。不要反过来。
- 分隔优先用间距与标题，其次用 `DividerStrokeColorDefaultBrush`，最后才用卡片边框。

## 5. 文本色

| 令牌（Brush） | 深色 | 浅色 | 用途 |
| --- | --- | --- | --- |
| `TextFillColorPrimaryBrush` | `#FFFFFF` | `#E4000000` | 标题、正文、主要信息 |
| `TextFillColorSecondaryBrush` | `#C5FFFFFF` | `#9E000000` | 次要说明、副标题、标签 |
| `TextFillColorTertiaryBrush` | `#87FFFFFF` | `#72000000` | 占位符、时间戳、元数据 |
| `TextFillColorDisabledBrush` | `#5DFFFFFF` | `#5C000000` | 禁用文字 |
| `TextOnAccentFillColorPrimaryBrush` | `#000000` | `#FFFFFF` | 强调色底上的文字 |
| `AccentTextFillColorPrimaryBrush` | `SystemAccentColorLight3` | `SystemAccentColorDark2` | 链接、可点击文字 |
| `AccentTextFillColorSecondaryBrush` | `SystemAccentColorLight3` | `SystemAccentColorDark3` | 链接悬停 |
| `AccentTextFillColorTertiaryBrush` | `SystemAccentColorLight2` | `SystemAccentColorDark1` | 链接按下 |

规则：

- 正文与背景的对比度至少 4.5:1；18.66px 以上粗体或 24px 以上文字至少 3:1；图标与控件边界至少 3:1。
- 禁用态同时降前景与填充，不要只把文字变淡而背景不变。
- 图片上的文字必须加遮罩或使用 `ControlOnImageFillColorDefaultBrush` / `SecondaryBrush` / `TertiaryBrush`，不要直接压 Primary 文字。

## 6. 表面与控件填充

| 令牌（Brush） | 深色 | 浅色 | 用途 |
| --- | --- | --- | --- |
| `SolidBackgroundFillColorBaseBrush` | `#202020` | `#F3F3F3` | 窗口兜底底色 |
| `SolidBackgroundFillColorSecondaryBrush` | `#1C1C1C` | `#EEEEEE` | 次级实心表面 |
| `SolidBackgroundFillColorTertiaryBrush` | `#282828` | `#F9F9F9` | 三级实心表面 |
| `SolidBackgroundFillColorQuarternaryBrush` | `#2C2C2C` | `#FFFFFF` | 浮层实心底、输入区 |
| `LayerFillColorDefaultBrush` | `#4C3A3A3A` | `#80FFFFFF` | 侧栏、分区、页面内层 |
| `CardBackgroundFillColorDefaultBrush` | `#0DFFFFFF` | `#B3FFFFFF` | 卡片默认底 |
| `CardBackgroundFillColorSecondaryBrush` | `#08FFFFFF` | `#80F6F6F6` | 卡片内次级块 |
| `ControlFillColorDefaultBrush` | `#0FFFFFFF` | `#B3FFFFFF` | 输入框、按钮默认底 |
| `ControlFillColorSecondaryBrush` | `#15FFFFFF` | `#80F9F9F9` | 悬停底 |
| `ControlFillColorTertiaryBrush` | `#08FFFFFF` | `#4DF9F9F9` | 按下底 |
| `ControlFillColorDisabledBrush` | `#0BFFFFFF` | `#4DF9F9F9` | 禁用底 |
| `ControlFillColorInputActiveBrush` | `#B31E1E1E` | `#FFFFFF` | 输入聚焦时的底 |

## 7. 描边与分割

| 令牌（Brush） | 深色 | 浅色 | 用途 |
| --- | --- | --- | --- |
| `ControlStrokeColorDefaultBrush` | `#12FFFFFF` | `#0F000000` | 控件默认边框 |
| `ControlStrokeColorSecondaryBrush` | `#18FFFFFF` | `#29000000` | 控件较强边框 |
| `ControlStrongStrokeColorDefaultBrush` | `#8BFFFFFF` | `#72000000` | 复选框未选中框等 |
| `CardStrokeColorDefaultBrush` | `#19000000` | `#0F000000` | 卡片 1px 边框 |
| `DividerStrokeColorDefaultBrush` | `#15FFFFFF` | `#0F000000` | 分割线 |
| `SurfaceStrokeColorDefaultBrush` | `#66757575` | `#66757575` | 对话框、Flyout 外框 |
| `FocusStrokeColorOuterBrush` | `#FFFFFF` | `#E4000000` | 焦点环外圈 |
| `FocusStrokeColorInnerBrush` | `#B3000000` | `#B3FFFFFF` | 焦点环内圈 |

边框一律 1px（`Thickness=1`）。不要用 2px 边框表达层级；层级用表面与高程。

## 8. 强调色

| 令牌 | 来源 | 用途 |
| --- | --- | --- |
| `AccentFillColorDefaultBrush` | 深色取 `SystemAccentColorLight2`，浅色取 `SystemAccentColorDark1` | 主操作按钮、选中态、进度 |
| `AccentFillColorSecondaryBrush` | 同上，90% 不透明 | 悬停 |
| `AccentFillColorTertiaryBrush` | 同上，80% 不透明 | 按下 |
| `AccentFillColorDisabledBrush` | `AccentFillColorDisabled` | 禁用 |
| `AccentFillColorSelectedTextBackgroundBrush` | `SystemAccentColor` | 文本选中背景 |
| `SystemFillColorAttentionBrush` | 深色取 `SystemAccentColorLight2`，浅色取 `SystemAccentColor` | 需要注意的信息图标 |

规则：

- 强调色只用于主操作、选中态、进度、链接。不要铺大面积背景，不要给普通图标上色。
- 一屏最多一个主操作使用实心强调色按钮；次要操作使用标准按钮或超链接。
- 强调色由系统与用户设置生成。不要写死 `#0078D4` 之类的值；用户换主题色时要跟着变。
- 强调色底上的文字用 `TextOnAccentFillColorPrimaryBrush`，不要用 `TextFillColorPrimaryBrush`。

## 9. 语义色

| 语义 | 深色 | 浅色 | 配套背景 |
| --- | --- | --- | --- |
| Success | `#6CCB5F` | `#0F7B0F` | `SystemFillColorSuccessBackgroundBrush` |
| Caution | `#FCE100` | `#9D5D00` | `SystemFillColorCautionBackgroundBrush` |
| Critical | `#FF99A4` | `#C42B1C` | `SystemFillColorCriticalBackgroundBrush` |
| Neutral 背景 | `#08FFFFFF` | `#06000000` | `SystemFillColorNeutralBackgroundBrush` |

规则：

- 语义色只表达状态，不做装饰。
- 必须同时给图标或文案。红绿对比对色盲用户不可靠。
- 大面积背景用对应的 `*BackgroundBrush`，前景色只给图标与小面积强调。
- 高对比模式下语义色由系统替换；不要写死高对比专用颜色，必要时才用 `IsHighContrast` 做布局分支。

## 10. Fluent 2 对照

只做概念映射，数值以 WinUI 3 为准。

| Fluent 2 token | WinUI 3 对应 | 备注 |
| --- | --- | --- |
| `colorNeutralBackground1` | `SolidBackgroundFillColorBaseBrush` | 最底层画布 |
| `colorNeutralBackground2` | `SolidBackgroundFillColorSecondaryBrush` | 次级表面 |
| `colorNeutralForeground1` | `TextFillColorPrimaryBrush` | 主文本 |
| `colorNeutralForeground2` | `TextFillColorSecondaryBrush` | 次文本 |
| `colorNeutralForeground3` | `TextFillColorTertiaryBrush` | 三级文本 |
| `colorNeutralStroke1` | `ControlStrokeColorDefaultBrush` | 控件描边 |
| `colorNeutralStroke2` | `DividerStrokeColorDefaultBrush` | 分割线 |
| `colorBrandBackground` | `AccentFillColorDefaultBrush` | 品牌主色 |
| `colorNeutralForegroundOnBrand` | `TextOnAccentFillColorPrimaryBrush` | 品牌底文字 |
| `borderRadiusMedium` 4px | `ControlCornerRadius` | 控件 |
| `borderRadiusLarge` 8px | `OverlayCornerRadius` | 浮层 |
| `spacingHorizontalS` 8px | 8 | 同刻度 |
| `spacingHorizontalM` 12px | 12 | 同刻度 |
| `spacingHorizontalL` 16px | 16 | 同刻度 |

差异要记住：

- Fluent 2 的小元素圆角是 2px，Windows 控件是 4px。
- Fluent 2 的交互态通常越交互越深；Windows 深色主题相反，越交互越亮。以 WinUI 主题资源为准。
- Fluent 2 的 `colorNeutralBackground1` 在浅色是纯白，WinUI 的 `SolidBackgroundFillColorBase` 是 `#F3F3F3`。不要逐值套用。

## 11. 反例

| 反例 | 问题 | 修法 |
| --- | --- | --- |
| `Foreground="White"` | 浅色主题不可见 | `TextFillColorPrimaryBrush` |
| `Background="#1F1F1F"` | 不随主题与高对比变化 | `SolidBackgroundFillColorBaseBrush` 或 `LayerFillColorDefaultBrush` |
| 正文 13px | 不在阶梯内，缩放后发虚 | 12 或 14 |
| 标题 `FontWeight="Bold"` | 过重，非 Fluent 字重 | `SemiBold` |
| 所有卡片都用强调色边框 | 强调色泛滥，层级消失 | 默认卡片边框，选中态才用强调色 |
| 成功/失败只用红绿圆点 | 色盲不可辨 | 圆点加图标加文案 |
| 正文行宽铺满 2560px | 阅读疲劳 | 限制到 50–60 字符 |
| 深色主题用纯黑 `#000000` 背景 | 与 Mica、阴影层次冲突 | `SolidBackgroundFillColorBaseBrush` |
