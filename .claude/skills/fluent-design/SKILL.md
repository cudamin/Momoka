---
name: fluent-design
description: Fluent Design 规范与 WinUI 3 落地数值：字号阶梯、颜色角色与主题令牌、4px 栅格与间距、控件高度、圆角、高程分层、Mica/Acrylic 材质、动效时长与曲线、控件状态与焦点视觉、图标尺寸，以及可执行的验收清单。用于设计、实现、评审或审计 Windows 应用界面，判断界面是否符合 Fluent 规范，或在 WinUI 3 中选择主题资源与数值。
agent_created: true
---

# Fluent Design 规范

项目政策以 [CLAUDE.md](../../../CLAUDE.md) 为准；本技能只提供 Fluent 设计数值与判定规则，与项目规则冲突时按项目规则执行。

## 用途

提供 Windows 应用（WinUI 3 / Windows App SDK）可执行的 Fluent 设计数值与判定规则。目标不是复述设计理念，而是让另一个会话在不查文档的情况下直接回答：这个字号该是多少、这个表面该用哪个令牌、这个间距对不对、这个浮层该不该用亚克力、这个状态还缺什么。

## 适用与不适用

适用：

- 设计或实现新的 WinUI 3 页面、卡片、对话框、浮层、设置页。
- 评审现有界面是否符合 Fluent，编写验收记录。
- 在多个主题资源之间做选择（Mica / Acrylic / 实心，Layer / Card / Control）。
- 判断某个自定义控件是否偏离平台规范。

不适用：

- 品牌视觉设计（Logo、插画、营销页）。
- Web、iOS、Android 的 Fluent 2 实现细节；本技能只做概念映射。
- 具体控件 API 用法与项目脚手架，那属于 `winui-app` 技能。

## 三个前提

1. **先分清两套东西。** Fluent 2（fluent2.microsoft.design）是跨平台设计系统；Windows 桌面上的实际实现是 WinUI 3 主题资源加 Windows 11 几何。两者数值不完全相同，例如小元素圆角在 Fluent 2 是 2px，在 Windows 控件上统一是 4px。本技能以 **WinUI 3 实现值** 为准，Fluent 2 只用于跨端对照。项目已有设计系统时，项目令牌优先；本技能提供平台基线。
2. **能用令牌就不写字面值。** 颜色、字号、圆角、间距优先 `{ThemeResource ...}` / `{StaticResource ...}`。字面值只用于设计稿标注、一次性装饰，或确实没有对应令牌的场景，并在提交说明里写原因。
3. **验收必须覆盖状态与主题。** 只看浅色静止态不算通过；至少覆盖浅色与深色、Rest / PointerOver / Pressed / Disabled / Focused，以及窄宽。

## 快速数值卡

| 项目 | 值 | 令牌或备注 |
| --- | --- | --- |
| 正文字号 | 14 / 20 | `BodyTextBlockStyle` |
| 辅助文字 | 12 / 16 | `CaptionTextBlockStyle` |
| 区块标题 | 20 / 28 Semibold | `SubtitleTextBlockStyle` |
| 页面标题 | 28 / 36 Semibold | `TitleTextBlockStyle` |
| 控件圆角 | 4 | `ControlCornerRadius` |
| 浮层与对话框圆角 | 8 | `OverlayCornerRadius` |
| 栅格 | 4 epx 的倍数 | 文字不受限 |
| 标准控件高度 | 32 | Button / TextBox / ComboBox 默认 |
| 紧凑控件高度 | 24 | 需显式设置，非默认 |
| 触摸目标 | 不小于 40 × 40 | 相邻目标间距不小于 8 |
| 断点 | 不超过 640 / 641–1007 / 不小于 1008 | Small / Medium / Large |
| 常规动效 | 250ms | `ControlNormalAnimationDuration` |
| 快速动效 | 167ms | `ControlFastAnimationDuration` |
| 淡入淡出 | 83ms | `ControlFasterAnimationDuration` |
| 图标 | 16 / 20 / 24 / 32 / 48 | Segoe Fluent Icons |

每个值的出处和用法见 `references/` 下对应文件。

## 工作流

1. **定层级。** 先判断元素属于 base（窗口、导航）、content（页面、卡片）还是 overlay（对话框、菜单、提示）。层级决定材质、圆角、高程和阴影，不要先挑颜色。
2. **定表面。** 按 `references/materials-motion-states.md` 的决策树选 Mica、Acrylic、实心、Card 或 Layer。一个区域只允许一种表面语言。
3. **排版。** 用 `references/type-and-color.md` 的字号阶梯和文本色阶；用 `references/layout-spacing-shape.md` 的 4px 刻度排间距、控件高度和圆角。
4. **补状态。** 每个可交互元素补齐 Rest / PointerOver / Pressed / Selected / Disabled / Focused。检查键盘焦点是否可见，关闭动画后是否仍可操作。
5. **验收。** 按 `references/review-checklist.md` 逐项记录「界面 / 状态 / 判据 / 结果 / 证据」。未实际观察到的项标为未验证，不能算通过。

## 路由

| 任务 | 读什么 |
| --- | --- |
| 字号、行高、字重、文本色、背景色、强调色、语义色、对比度、Fluent 2 映射 | `references/type-and-color.md` |
| 间距、页面边距、控件高度、圆角、高程、断点、图标尺寸、页面结构 | `references/layout-spacing-shape.md` |
| Mica / Acrylic、层叠、阴影、动效时长与曲线、控件状态、焦点视觉、高对比 | `references/materials-motion-states.md` |
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

退出码：0 无发现，1 有发现，2 参数错误。规则 F001–F005 见脚本头注释。脚本只做文本扫描，能证明「写了字面值」，不能证明「渲染不对」；最终判定仍要回到画面与 UIA。

## 十条硬规则

1. 颜色只用主题令牌；不写 `#RRGGBB`、`White`、`Black`。
2. 字号只用阶梯值；不出现 13、15、17 这类中间值。
3. 不用 `Bold`；需要强调用 `SemiBold`。不用 `Italic` 做强调。
4. 所有尺寸、边距、位置取 4 的倍数；图标与文字之间可用 2 或 8。
5. 控件圆角 4，浮层与对话框 8；相接的元素不圆角。
6. 窗口底层用 Mica，浮层用 Acrylic 或实心；页面内卡片不用材质。
7. 一个区域只允许一层表面；不要在卡片外再套一层 Border。
8. 动效只动 `Opacity` 和 `RenderTransform`；不动 `Width`、`Height`、`Margin`。
9. 按下状态要有下沉感（高程 2 降到 1）；键盘焦点要有双色环。
10. 语义色必须配图标或文案；不能只靠颜色传达状态。

## 常见错误

| 错误 | 正确做法 |
| --- | --- |
| 深色主题下写死白色文字 | `TextFillColorPrimaryBrush` |
| 用 `Bold` 强调标题 | `SemiBold`（`BodyStrong` / `Subtitle` / `Title`） |
| 卡片外面再包一层带圆角的 Border | 去掉外层，用间距和标题分组 |
| 页面背景用 Acrylic | 窗口用 Mica；Acrylic 只给浮层 |
| 用 13px 或 15px 微调 | 回到 12 / 14 / 16 阶梯 |
| 给每个图标配不同颜色 | 图标跟随前景色；语义色只给状态图标 |
| 只用颜色区分成功与失败 | 颜色加图标加文案 |
| 焦点框被自定义模板裁掉 | 保留 `UseSystemFocusVisuals`，检查 `FocusVisualMargin` |
| 动画改变布局属性导致抖动 | 只动 `Opacity`、`Translation`、`Scale` |
| 只验收浅色静止态 | 补深色、按下、禁用、焦点、窄宽 |

## 来源与版本

- WinUI 3 主题资源：`Microsoft.WindowsAppSDK.WinUI 2.7.0` 的 `Microsoft.WinUI/Themes/generic.xaml`（本机 NuGet 缓存，2026-11 核对）。
- Microsoft Learn：Typography、Geometry、Layering、Motion、Screen sizes and breakpoints、Iconography、Accessibility。
- Fluent 2：Color、Shapes、Iconography，仅用于跨端映射。
- 数值会随 WinUI 版本变化。升级 Windows App SDK 后重新核对 `generic.xaml`，不要凭记忆沿用。
