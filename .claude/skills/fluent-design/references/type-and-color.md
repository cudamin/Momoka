# Fluent 2 排版与颜色

标准：Fluent 2（fluent2.microsoft.design）。Windows 分支：Fluent 2 官方指定的 WinUI 3。本文先给 Fluent 2 令牌，再给 Windows 分支映射；两者不同时以 Fluent 2 的设计意图为准，实现值按 Windows 分支记录。

来源：Fluent 2 Typography / Color；`microsoft/fluentui` `packages/tokens` 提交 `ff7ee75`（2026-11-26）的 `typographyStyles.ts`、`alias/lightColor.ts`、`alias/darkColor.ts`、`global/colorPalette.ts`；WinUI 3 `generic.xaml` 2.7.0。

## 1. 字体

- Fluent 2 主字体：Segoe。Windows 分支使用 `Segoe UI Variable`，web 分支使用 `Segoe UI`。
- `Segoe UI Variable` 是可变字体，光学尺寸（`opsz`）按字号自动匹配，不要手动指定。
- 全应用只用一种正文字体。不要混用 Segoe UI、Arial、微软雅黑；语言回退交给系统。
- 代码、日志、等宽数字用 `Cascadia Mono` 或 `Consolas`，只限代码块与技术信息。

## 2. Fluent 2 字号阶梯

### Web 分支

| 样式 | 字号 / 行高 | 字重 |
| --- | --- | --- |
| Caption 2 | 10 / 14 | Regular |
| Caption 2 Strong | 10 / 14 | Semibold |
| Caption 1 | 12 / 16 | Regular |
| Caption 1 Strong | 12 / 16 | Semibold |
| Caption 1 Stronger | 12 / 16 | Bold |
| Body 1 | 14 / 20 | Regular |
| Body 1 Strong | 14 / 20 | Semibold |
| Body 1 Stronger | 14 / 20 | Bold |
| Subtitle 2 | 16 / 22 | Semibold |
| Subtitle 2 Stronger | 16 / 22 | Bold |
| Subtitle 1 | 20 / 26 | Semibold |
| Title 3 | 24 / 32 | Semibold |
| Title 2 | 28 / 36 | Semibold |
| Title 1 | 32 / 40 | Semibold |
| Large Title | 40 / 52 | Semibold |
| Display | 68 / 92 | Semibold |

### Windows 分支

| 样式 | 字号 / 行高 | 字重 | XAML 样式键 |
| --- | --- | --- | --- |
| Caption | 12 / 16 | Regular | `CaptionTextBlockStyle` |
| Body | 14 / 20 | Regular | `BodyTextBlockStyle` |
| Body Strong | 14 / 20 | Semibold | `BodyStrongTextBlockStyle` |
| Body Large | 18 / 24 | Regular | `BodyLargeTextBlockStyle` |
| Subtitle | 20 / 28 | Semibold | `SubtitleTextBlockStyle` |
| Title | 28 / 36 | Semibold | `TitleTextBlockStyle` |
| Large Title | 40 / 52 | Semibold | `TitleLargeTextBlockStyle` |
| Display | 68 / 92 | Semibold | `DisplayTextBlockStyle` |

规则：

- Windows 界面用 Windows 分支的 8 档。`Caption 2` 10px、`Subtitle 2` 16px、`Title 3` 24px、`Title 1` 32px 是 web 分支，不套到 WinUI 控件。
- 不用 13、15、17 这类中间值。层级不够时改字重或颜色，不要微调字号。
- Windows 分支的 `Subtitle` 是 20 / 28，不是 web 的 20 / 26；`Body Large` 18 / 24 是 Windows 独有。
- WinUI 样式通过 `LineStackingStrategy=MaxHeight` 与 `TextLineBounds=Full` 使用字体度量，默认不写 `LineHeight`。需要精确行高时才显式设置，并配 `LineStackingStrategy=BlockLineHeight`。

## 3. 排版规则

- 字重：Regular、Semibold；web 分支另有 Bold（Stronger）。Windows 分支不用 Bold 与 Italic 做强调，强调用 Semibold。
- 最小可读尺寸：Windows 分支 12px Regular 或 14px Semibold；web 分支允许 `Caption 2` 10px，但不用于本应用。
- 文案用句首大写（sentence case），包括标题与按钮。专有名词保持原样。
- 一行 50–60 个字符最易读。宽屏正文要限制行宽，不要铺满整窗。
- 溢出优先省略号（`TextTrimming=CharacterEllipsis`），不要硬裁；多行正文才换行。
- 左对齐是默认。只有图标下方文字、数字表格等少数场景居中。
- 中英文混排不为中文单独换字体；中英文之间是否加空格跟随产品既有文案。
- 标签与值成对出现时，标签用 `colorNeutralForeground2`，值用 `colorNeutralForeground1`；不要靠字号差异区分。

## 4. Fluent 2 颜色体系

Fluent 2 有三套色板：

| 色板 | 用途 |
| --- | --- |
| Neutral | 表面、文本、描边；用明度建立层级 |
| Shared | 头像、日历、徽标等跨产品组件；少量使用 |
| Brand | 主操作、选中态、链接；不要大面积铺 |

令牌分两层：

- **Global token**：上下文无关的原始值（如 `grey[14]`、`brand[80]`）。
- **Alias token**：带语义的令牌（如 `colorNeutralBackground1`、`colorBrandBackground`）。设计表达与评审用别名令牌，不直接引用 global 值。

## 5. Neutral 令牌

| 令牌 | 浅色 | 深色 | 用途 |
| --- | --- | --- | --- |
| `colorNeutralBackground1` | `#ffffff` | `#292929` | 主画布 |
| `colorNeutralBackground2` | `#fafafa` | `#1f1f1f` | 次级表面 |
| `colorNeutralBackground3` | `#f5f5f5` | `#141414` | 三级表面 |
| `colorNeutralBackground4` | `#f0f0f0` | `#0a0a0a` | 四级表面 |
| `colorNeutralBackground5` | `#ebebeb` | `#000000` | 五级表面 |
| `colorNeutralBackground6` | `#e6e6e6` | `#333333` | 六级表面 |
| `colorNeutralCardBackground` | `#fafafa` | `#333333` | 卡片 |
| `colorNeutralBackgroundDisabled` | `#f0f0f0` | `#141414` | 禁用底 |
| `colorNeutralForeground1` | `#242424` | `#ffffff` | 主文本 |
| `colorNeutralForeground2` | `#424242` | `#d6d6d6` | 次文本 |
| `colorNeutralForeground3` | `#616161` | `#adadad` | 三级文本 |
| `colorNeutralForeground4` | `#707070` | `#999999` | 四级文本 |
| `colorNeutralForegroundDisabled` | `#bdbdbd` | `#5c5c5c` | 禁用文本 |
| `colorNeutralStroke1` | `#d1d1d1` | `#666666` | 控件描边 |
| `colorNeutralStroke2` | `#e0e0e0` | `#525252` | 次级描边 |
| `colorNeutralStroke3` | `#f0f0f0` | `#3d3d3d` | 细分隔 |
| `colorNeutralStrokeAccessible` | `#616161` | `#adadad` | 可访问描边 |
| `colorNeutralStrokeDisabled` | `#e0e0e0` | `#424242` | 禁用描边 |

交互态：

| 状态 | 浅色 | 深色 |
| --- | --- | --- |
| `colorNeutralBackground1Hover` | `#f5f5f5` | `#3d3d3d` |
| `colorNeutralBackground1Pressed` | `#e0e0e0` | `#1f1f1f` |
| `colorNeutralBackground1Selected` | `#ebebeb` | `#383838` |

Fluent 2 的交互态方向：**越交互越深**。Windows 分支相反：**控件越交互越亮**。这是 Fluent 2 Color 页面明确写出的平台差异，评审时不要拿 web 的 hover 规则套 Windows。

## 6. Brand 令牌

| 令牌 | 浅色 | 深色 | 用途 |
| --- | --- | --- | --- |
| `colorBrandBackground` | `#0078d4` | `#106ebe` | 主操作、选中态 |
| `colorBrandBackgroundHover` | `#106ebe` | `#0078d4` | 悬停 |
| `colorBrandBackgroundPressed` | `#004578` | `#004578` | 按下 |
| `colorBrandBackgroundSelected` | `#005a9e` | `#005a9e` | 选中 |
| `colorBrandForeground1` | `#0078d4` | `#2899f5` | 链接、品牌文字 |
| `colorNeutralForegroundOnBrand` | `#ffffff` | `#ffffff` | 品牌底文字 |
| `colorBrandStroke1` | `#0078d4` | `#2899f5` | 品牌描边 |
| `colorBrandStroke2` | `#c7e0f4` | `#004c87` | 品牌浅描边 |

规则：

- 品牌色只用于主操作、选中态、进度、链接。不要铺大面积背景，不要给普通图标上色。
- 一屏最多一个主操作使用实心品牌色按钮；次要操作使用标准按钮或超链接。
- 品牌色由 Fluent 2 的 `BrandVariants` 生成。换品牌色时换整条 ramp，不要只改一个 hex。
- 品牌底上的文字用 `colorNeutralForegroundOnBrand`，不要用 `colorNeutralForeground1`。

## 7. Status 令牌

Fluent 2 的 status 由 shared palette 生成：success → green，warning → orange，danger → cranberry。

| 令牌 | Success 浅 / 深 | Warning 浅 / 深 | Danger 浅 / 深 |
| --- | --- | --- | --- |
| `colorStatus*Background1` | `#f1faf1` / `#052505` | `#fff9f5` / `#4a1e04` | `#fdf3f4` / `#3b0509` |
| `colorStatus*Background2` | `#9fd89f` / `#094509` | `#fdcfb4` / `#8a3707` | `#eeacb2` / `#6e0811` |
| `colorStatus*Background3` | `#107c10` / `#107c10` | `#f7630c` / `#f7630c` | `#c50f1f` / `#c50f1f` |
| `colorStatus*Foreground1` | `#0e700e` / `#54b054` | `#bc4b09` / `#faa06b` | `#b10e1c` / `#dc626d` |
| `colorStatus*Foreground2` | `#094509` / `#9fd89f` | `#8a3707` / `#fdcfb4` | `#6e0811` / `#eeacb2` |
| `colorStatus*Border1` | `#9fd89f` / `#107c10` | `#fdcfb4` / `#f7630c` | `#eeacb2` / `#c50f1f` |
| `colorStatus*Border2` | `#107c10` / `#359b35` | `#f7630c` / `#f98845` | `#c50f1f` / `#d33f4c` |

注意：`colorStatusWarningForeground1` 在浅色主题里被覆写为 shade20 `#bc4b09`，不是 shade10 `#de590b`。按令牌取值，不要手写。

规则：

- 语义色只表达状态，不做装饰。
- 必须同时给图标或文案。红绿对比对色盲用户不可靠。
- 大面积背景用 `Background1` 或 `Background2`，`Background3` 只给图标与小面积强调。
- 深色主题的 status 令牌用另一组 shade / tint：`Background1` 取 shade40、`Background2` 取 shade30、`Foreground1` 取 tint30、`Foreground2` 取 tint40、`Border1` 取 primary、`Border2` 取 tint20。不要沿用浅色值。

## 8. Windows 分支映射

| Fluent 2 令牌 | WinUI 3 资源 | 差异 |
| --- | --- | --- |
| `colorNeutralBackground1` | `SolidBackgroundFillColorBaseBrush` | `#ffffff` vs `#F3F3F3` |
| `colorNeutralBackground2` | `SolidBackgroundFillColorSecondaryBrush` | `#fafafa` vs `#EEEEEE` |
| `colorNeutralBackground3` | `SolidBackgroundFillColorTertiaryBrush` | `#f5f5f5` vs `#F9F9F9` |
| `colorNeutralCardBackground` | `CardBackgroundFillColorDefaultBrush` | `#fafafa` vs `#B3FFFFFF` |
| `colorNeutralForeground1` | `TextFillColorPrimaryBrush` | `#242424` vs `#E4000000` |
| `colorNeutralForeground2` | `TextFillColorSecondaryBrush` | `#424242` vs `#9E000000` |
| `colorNeutralForeground3` | `TextFillColorTertiaryBrush` | `#616161` vs `#72000000` |
| `colorNeutralForegroundDisabled` | `TextFillColorDisabledBrush` | `#bdbdbd` vs `#5C000000` |
| `colorNeutralStroke1` | `ControlStrokeColorDefaultBrush` | `#d1d1d1` vs `#0F000000` |
| `colorNeutralStroke2` | `DividerStrokeColorDefaultBrush` | `#e0e0e0` vs `#0F000000` |
| `colorBrandBackground` | `AccentFillColorDefaultBrush` | `#0078d4` vs 系统强调色 |
| `colorNeutralForegroundOnBrand` | `TextOnAccentFillColorPrimaryBrush` | `#ffffff` vs `#FFFFFF` / `#000000` |
| `colorStatusSuccessBackground3` | `SystemFillColorSuccessBrush` | `#107c10` vs `#0F7B0F` / `#6CCB5F` |
| `colorStatusWarningBackground3` | `SystemFillColorCautionBrush` | `#f7630c` vs `#9D5D00` / `#FCE100` |
| `colorStatusDangerBackground3` | `SystemFillColorCriticalBrush` | `#c50f1f` vs `#C42B1C` / `#FF99A4` |

规则：

- Windows 分支的 WinUI 资源是 Fluent 2 的落地近似值，不是逐值等价。评审记录要写「Fluent 2 令牌 + WinUI 资源 + 差异」。
- 如果设计要求严格匹配 Fluent 2 的 `#ffffff` / `#292929`，Windows 分支需要在 `App.xaml` 覆盖主题资源；这属于主题改造，不能靠单个页面写死颜色。
- 系统强调色会覆盖 `colorBrandBackground` 的固定值；这是 Windows 分支的预期行为，不要为了匹配 `#0078d4` 而禁用用户强调色。

## 9. 对比度

- 正文与背景至少 4.5:1。
- 大字（18.5px 以上粗体，或 24px 以上常规）至少 3:1。
- 图标、控件边界、焦点环至少 3:1。
- 高对比模式下由系统替换颜色；不要写死高对比专用色。
- 语义色不能只靠颜色；配图标与文案。

## 10. 反例

| 反例 | 问题 | 修法 |
| --- | --- | --- |
| `Foreground="White"` | 浅色主题不可见 | `TextFillColorPrimaryBrush` / `colorNeutralForeground1` |
| `Background="#1F1F1F"` | 不随主题与高对比变化 | `SolidBackgroundFillColorBaseBrush` / `colorNeutralBackground1` |
| 把 `colorNeutralBackground1` 当成 `#F3F3F3` | 把 WinUI 默认值当成 Fluent 2 | Fluent 2 浅色是 `#ffffff`；记录 Windows 分支差异 |
| Windows 界面用 `caption2` 10px | web-only 样式，Windows 不可读 | `caption1` 12px |
| Windows 界面用 `title3` 24px | web-only 样式 | `title2` 28px 或 `subtitle1` 20px |
| 用 `Bold` 强调标题 | Windows 阶梯只有 Regular / Semibold | `SemiBold` |
| 深色主题用纯黑 `#000000` 背景 | 与 Mica、阴影层次冲突 | `colorNeutralBackground1` 深色 `#292929` 或 WinUI 系统底 |
| 所有卡片都用品牌色边框 | 品牌色泛滥，层级消失 | 默认卡片描边，选中态才用品牌色 |
| 成功/失败只用红绿圆点 | 色盲不可辨 | 圆点加图标加文案 |
| 正文行宽铺满 2560px | 阅读疲劳 | 限制到 50–60 字符 |
