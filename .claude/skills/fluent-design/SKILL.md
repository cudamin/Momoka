---
name: fluent-design
description: 以 Fluent 2 为标准的 Windows/WinUI 3 界面规范：Fluent 2 设计令牌（颜色、字号、间距、圆角、阴影、动效）、Windows 分支（WinUI 3）映射、控件用法、状态与验收清单。用于设计、实现、评审或审计 Windows 应用界面，判断界面是否符合 Fluent 2，或在 Fluent 2 令牌与 WinUI 主题资源之间做选择。
agent_created: true
---

# Fluent 2 设计规范

项目政策以 [CLAUDE.md](../../../CLAUDE.md) 为准；本技能以 Fluent 2 为标准，与项目规则冲突时按项目规则执行。

## 用途

提供以 Fluent 2 为判据的设计数值与验收规则。目标不是复述设计理念，而是让另一个会话在不查文档的情况下直接回答：这个字号该是多少、这个表面该用哪个令牌、这个间距对不对、这个浮层该不该用亚克力、这个状态还缺什么。

## 标准与分支

| 项目 | 标准 | Windows 分支 |
| --- | --- | --- |
| 是什么 | Fluent 2 设计系统 | Fluent 2 官方指定的 WinUI 3 |
| 管什么 | 设计意图、令牌、验收判据 | 实际渲染、控件默认值、平台限制 |
| 典型值 | `colorNeutralBackground1`、`spacingHorizontalM`、`borderRadiusMedium`、`shadow4`、`durationNormal` | `SolidBackgroundFillColorBaseBrush`、8/12/16 间距、`ControlCornerRadius`、`ThemeShadow`、167/250ms |

规则：

- **设计表达用 Fluent 2 令牌名，代码用 Windows 分支的主题资源。** 评审时先问「Fluent 2 令牌是什么」，再问「Windows 分支怎么落地」。
- **两者不一致时，先看 Fluent 2 的设计意图，再看 Windows 分支为什么不同。** 例如 Fluent 2 的 `colorNeutralBackground1` 浅色是 `#ffffff`，WinUI 的 `SolidBackgroundFillColorBase` 是 `#F3F3F3`；Windows 分支用系统底色，评审要记录这个差异，不能把 WinUI 默认值当成 Fluent 2 规范。
- **Windows 分支不是另一套标准。** Fluent 2 的 Windows 页面明确让 Windows 应用使用 WinUI 组件，所以 WinUI 的默认值是 Fluent 2 在 Windows 上的官方落地值，但不能反过来覆盖 Fluent 2 的跨平台令牌。

## 适用与不适用

适用：

- 设计或实现新的 WinUI 3 页面、卡片、对话框、浮层、设置页。
- 评审现有界面是否符合 Fluent 2，编写验收记录。
- 在 Fluent 2 令牌与 WinUI 主题资源之间做选择。
- 判断某个自定义控件是否偏离 Fluent 2。

不适用：

- 品牌视觉设计（Logo、插画、营销页）。
- Web、iOS、Android 的 Fluent 2 实现细节；本技能只做跨端对照。
- 具体控件 API 用法与项目脚手架，那属于 `winui-app` 技能。

## 快速数值卡

| 项目 | Fluent 2 标准 | Windows 分支 |
| --- | --- | --- |
| 正文 | `body1` 14 / 20 | `BodyTextBlockStyle` |
| 辅助文字 | `caption1` 12 / 16 | `CaptionTextBlockStyle` |
| 区块标题 | `subtitle1` 20 / 26（web）；Windows 用 20 / 28 | `SubtitleTextBlockStyle` |
| 页面标题 | `title2` 28 / 36 | `TitleTextBlockStyle` |
| 间距 | 0 / 2 / 4 / 6 / 8 / 10 / 12 / 16 / 20 / 24 / 28 / 32 / 36 / 40 / 48 / 52 / 56 | 4px 倍数；2 / 6 / 10 只用于图标对齐 |
| 圆角 | 0 / 2 / 4 / 6 / 8 / 12 / 16 / 24 / 32 / 40 / 50% | 控件 4，浮层与对话框 8 |
| 阴影 | `shadow2` / `4` / `8` / `16` / `28` / `64` | 1px 描边 + 高程 1 / 2 / 8 / 16 / 32 / 128 |
| 动效 | 50 / 100 / 150 / 200 / 250 / 300 / 400 / 500ms，9 条曲线 | 83 / 167 / 250 / 333ms |
| 主画布 | `colorNeutralBackground1` `#ffffff` / `#292929` | `SolidBackgroundFillColorBase` `#F3F3F3` / `#202020` |
| 主文本 | `colorNeutralForeground1` `#242424` / `#ffffff` | `TextFillColorPrimary` `#E4000000` / `#FFFFFF` |
| 品牌色 | `colorBrandBackground` `#0078d4` / `#106ebe` | `AccentFillColorDefaultBrush`（系统强调色） |
| 断点 | 320 / 480 / 640 / 1024 / 1366 / 1920 | 640 / 1008 |

每个值的出处和用法见 `references/` 下对应文件。

## 工作流

1. **定层级。** 先判断元素属于 base（窗口、导航）、content（页面、卡片）还是 overlay（对话框、菜单、提示）。层级决定材质、圆角、高程和阴影，不要先挑颜色。
2. **选 Fluent 2 令牌。** 用 `references/type-and-color.md` 选字号、文本色、背景色、品牌色；用 `references/layout-spacing-shape.md` 选间距、圆角、描边；用 `references/materials-motion-states.md` 选阴影、材质、动效。
3. **映射 Windows 分支。** 按映射表落到 WinUI 主题资源；记录 Fluent 2 值与 WinUI 值的差异，不要把差异当成规范。
4. **补状态。** 每个可交互元素补齐 Rest / PointerOver / Pressed / Selected / Disabled / Focused。注意 Windows 分支的交互态是变亮，不是变暗。
5. **验收。** 按 `references/review-checklist.md` 逐项记录「界面 / 状态 / 判据 / 结果 / 证据」。未实际观察到的项标为未验证，不能算通过。

## 路由

| 任务 | 读什么 |
| --- | --- |
| Fluent 2 字号、文本色、背景色、品牌色、语义色、对比度、Windows 映射 | `references/type-and-color.md` |
| Fluent 2 间距、栅格、圆角、描边、高程、断点、图标、页面结构 | `references/layout-spacing-shape.md` |
| Fluent 2 材质、阴影、动效、控件状态、焦点视觉、高对比 | `references/materials-motion-states.md` |
| 按钮、输入、卡片、列表、对话框、浮层、导航、设置页的具体用法 | `references/control-patterns.md` |
| 验收清单、证据模板、常见违规与修法 | `references/review-checklist.md` |
| 扫描 XAML 中的硬编码颜色、字号、圆角、粗体 | `scripts/audit_fluent.py` |

## 脚本

扫描一个目录或若干 XAML 文件：

```bash
py -3 scripts/audit_fluent.py <目录或文件> [--format text|json] [--rules F001,F002]
```

脚本自带离线回归，改脚本后先跑：

```bash
py -3 scripts/test_audit_fluent.py
```

退出码：0 无发现，1 有发现，2 参数错误。规则 F001–F005 见脚本头注释。脚本按 Windows 分支的 12px 最小字号判定；Fluent 2 web 的 `caption2` 10px 不在本应用使用。脚本只做文本扫描，能证明「写了字面值」，不能证明「渲染不对」；最终判定仍要回到画面与 UIA。

## 十条硬规则

1. 设计表达用 Fluent 2 令牌名；代码用 WinUI 主题资源。不写 `#RRGGBB`、`White`、`Black`。
2. 字号用 Fluent 2 的 Windows 阶梯；不用 web-only 的 `caption2` 10px、`title3` 24px、`title1` 32px。
3. 间距用 Fluent 2 全局 ramp；2 / 6 / 10 只用于图标对齐，不用 14 / 18 / 22。
4. 圆角用 Fluent 2 令牌；Windows 控件 4、浮层 8；相接元素不圆角。
5. 阴影按 Fluent 2 `shadow*` 档位；Windows 分支用 1px 描边代替 key shadow。
6. 动效用 Fluent 2 duration / curve；Windows 分支按 83 / 167 / 250 / 333ms。
7. 一个区域只允许一层表面；不要在卡片外再套一层 Border。
8. 交互态完整；Windows 分支越交互越亮，web 分支越交互越深。
9. 按下状态要有下沉感（高程 2 降到 1）；键盘焦点要有双色环。
10. 语义色必须配图标或文案；不能只靠颜色传达状态。

## 常见错误

| 错误 | 正确做法 |
| --- | --- |
| 把 WinUI 默认值当成 Fluent 2 规范 | 先查 Fluent 2 令牌，再看 Windows 分支差异 |
| 在 Windows 界面用 `caption2` 10px | 用 `caption1` 12px |
| 在 Windows 界面用 `title3` 24px | 用 `title2` 28px 或 `subtitle1` 20px |
| 看到 6px 或 10px 间距就判错 | Fluent 2 ramp 允许 2 / 6 / 10，用于图标对齐 |
| 所有按钮都用 8px 圆角 | Fluent 2 `borderRadiusMedium` 4；Windows 控件 4 |
| 卡片圆角 12 | 12 是 x-large，用于 sheet / popover；卡片按层级选 4 或 8 |
| 把 web 的 hover 变深套到 Windows | Windows 分支控件越交互越亮 |
| 给卡片加阴影 | Fluent 2 可用 `shadow4`；Windows 分支优先 1px 描边 |
| 动效固定 200ms | 按平台分支：Windows 用 250ms 常规、167ms 快速 |
| 只验收浅色静止态 | 补深色、按下、禁用、焦点、窄宽 |

## 来源与版本

- Fluent 2：Typography、Color、Layout、Shapes、Elevation、Motion、Iconography、Design tokens（fluent2.microsoft.design，2026-11 核对）。
- Fluent 2 令牌实现：`microsoft/fluentui` `packages/tokens`，提交 `ff7ee75`（2026-11-26）的 `spacings.ts`、`borderRadius.ts`、`durations.ts`、`curves.ts`、`strokeWidths.ts`、`typographyStyles.ts`、`alias/lightColor.ts`、`alias/darkColor.ts`、`utils/shadows.ts`。
- Windows 分支：Microsoft Learn Typography / Geometry / Layering / Motion / Screen sizes and breakpoints / Iconography，WinUI 3 `Microsoft.WindowsAppSDK.WinUI 2.7.0` 的 `generic.xaml`。
- Fluent 2 与 WinUI 都会更新。升级 Windows App SDK 或调整 Fluent 2 令牌后重新核对上表，不要凭记忆沿用。
