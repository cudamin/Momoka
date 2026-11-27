---
name: fluent2-design
description: "依据 Microsoft Fluent 2 与 Windows 官方指南设计、开发和验收 Windows/WinUI 界面；用于页面与设置布局、视觉样式、控件交互和 Fluent 一致性审查。为 Momoka 提供现有资源、产品例外和验证矩阵的衔接；不将 Fluent Web 组件参数移植为 WinUI 规范。"
---

# Fluent 2 设计与验收

把设计原则转成可实现、可检查的界面决定。目标是任务清楚、层级稳定、操作可达、各状态可辨认；不要把“Fluent 化”简化成增加圆角、模糊或动画。

## 使用范围与依据

- 适用于 Windows/WinUI 的新界面、受影响界面的改进及设计验收。只读审查只给发现和依据；开发请求直接完成授权范围内的实现。不要因加载本技能而扩大重构、替换组件库或恢复历史计划。
- [Fluent Windows](https://fluent2.microsoft.design/components/windows) 是平台入口；控件选择和具体行为以适用版本的 Windows/WinUI 官方指南与源码为准。跨平台原则可以共用，Web 的 token 名称、字号表、阴影参数和 CSS 不能视为 XAML API 或 Windows 默认值。
- 本技能的官方资料核对日期为 **2026-11-27**。涉及新增 API、控件模板、平台默认行为或精确数值时，核对当前项目版本与相应官方页面；设计说明不证明 API 已受支持。
- 在其他项目复用时，先映射该项目的资源和约定；以下 Momoka 路径仅用于本仓库。

## Momoka 的衔接

先读当前工作树的 [CLAUDE.md](../../../CLAUDE.md)。政策、授权、交付级别及已确认例外只在其中维护；上下文压缩后按其要求重读。

| 当前任务 | 联读入口与边界 |
| --- | --- |
| 做设计选择、修视觉一致性 | 本技能的[设计判据](references/design-criteria.md)，结合受影响页面现状 |
| 修改 C# / XAML、控件模板、导航或交互 | [WinUI Shell](../momoka-winui-shell/SKILL.md)；按其触发条件核对官方指南和固定提交的 Gallery 源码 |
| 改材质、透明度或底材 | [材质方法](../momoka-winui-shell/references/materials.md)，项目取舍见 [CLAUDE 的官方优先与偏离确认](../../../CLAUDE.md#winui-官方优先与偏离确认) |
| 做界面审查或验收 | [验收方法](references/acceptance.md)，运行场景和证据边界归 [WinUI 验证矩阵](../momoka-verification/references/winui-validation.md) |
| 执行检查、记录交付结果 | [验证技能](../momoka-verification/SKILL.md)及[开发与验证](../../../docs/开发与验证.md)；本技能不另设闸门 |

在 Momoka 中，先查看以下实际资源与调用者，再决定新增或复用：

- [Styles.xaml](../../../src/Momoka.Shell/Theme/Styles.xaml)：字体、字号、圆角、尺寸和 `Eg*Style`。不要把官方基准表当作批量改写项目资源的授权。
- [Palette.xaml](../../../src/Momoka.Shell/Theme/Palette.xaml)、[ThemeHost.cs](../../../src/Momoka.Shell/Theme/ThemeHost.cs)、[UiThemes.cs](../../../src/Momoka.Core/Theming/UiThemes.cs)：主题角色、初始化、实时更新和高对比度路径。资源存在不等于所有状态的对比度自动合格。
- 主题模式、弹窗实心与亚克力的适用范围、能力显隐及点外取消，分别核对 [CLAUDE 产品例外](../../../CLAUDE.md#winui-官方优先与偏离确认)、[显隐规则](../../../CLAUDE.md#功能可用性与显隐)及[弹窗规则](../../../CLAUDE.md#弹窗)。官方推荐和已确认的项目例外分开记录；不擅自把其中一项覆盖为另一项。

## 开发时

1. 确定本次用户任务、主操作、受影响页面/弹层及现有状态。先看实际界面或可用截图，再追相关样式和绑定；没有运行画面时标明视觉判断的限制。
2. 按[设计判据](references/design-criteria.md)选择相关项。明确哪些来自官方、哪些是项目约定；普通局部修复不要求重新设计整页。
3. 先复用原生控件和项目语义资源，保留原控件的焦点、UIA、输入及主题状态。需要定制时补上实际被替换掉的语义和状态，而非只画一个相似外观。
4. 实现完整的受影响状态：正常、焦点、选中、悬停、按下、禁用，以及相关的加载、空结果、错误和恢复。只选本次能触发的状态，不给无关控件强加状态。
5. 按项目交付级别完成对应检查。需要运行证据而当前不能执行时，写明未验证的界面、状态和补验入口；不以编译或静态样式存在替代视觉、键盘或读屏结果。

## 审查或验收时

先读[验收方法](references/acceptance.md)。每项发现应能对应“界面/状态 → 判据及来源 → 实际证据 → 用户影响 → 修复或补验”。不以审美偏好单独判失败，不用总分或截图数量代替缺失的关键证据。

输出使用简体中文，说明实际改动或发现，以及通过、失败和未验证项。区分本次技能/文档检查通过与产品界面通过；检查清单本身不构成运行验收。
