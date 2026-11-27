# Windows / WinUI 设计判据

按受影响的设计维度阅读；这些是设计与检查依据，不是对所有页面强制套用的固定尺寸表。官方链接核对日期：2026-11-27。数值区分平台基准、项目资源和实测结果。

## 布局、间距与适配

- 先分清页面标题、导航、内容组、主操作与次操作。相关项靠近，同级内容对齐；优先用留白表达分组，再考虑分隔线或容器，避免每一段都套卡片。
- Fluent 间距以 4 为基数，常用 4、8、12、16、24、32；官方也包含 2、6、10，用于图标内边距及光学校准。复用现有 spacing/Thickness 资源，不把“4 倍数”推广到所有边框、字距、图片比例或计算尺寸。
- XAML 布局使用有效像素 epx（通常称 DIP），不是截图物理像素。检查原生窗口 API 时另核对单位；报告注明窗口有效尺寸、缩放和物理截图尺寸，避免重复乘 DPI。
- 断点看应用可用宽度及内容需要。窄窗通过换行、重排、收起次操作或导航模式调整保持核心任务可达；不用固定高度裁掉放大后的文字，不靠把全部文字缩小解决溢出。现有最小窗宽与断点是项目输入，不因官网示例自动重设。
- 选择长中文、长英文、空值及多行说明核对布局；视觉顺序与键盘/读屏顺序应相容。

依据：[Fluent Layout](https://fluent2.microsoft.design/layout)、[Windows 有效像素与断点](https://learn.microsoft.com/en-us/windows/apps/design/layout/screen-sizes-and-breakpoints-for-responsive-design)。

## 字体、图标与内容

- 使用语义角色表达标题、正文和辅助信息，不仅靠颜色区分层级。先映射已有文本样式，避免新增孤立字号或反复用粗体制造强调。
- 官方 **Windows** 字体基准示例：Caption 12/16、Body 14/20、Body large 18/24、Subtitle 20/28、Title 28/36（字号/行高）。它与 Web 表不同，且不是要求 Momoka 重写 `Eg*` 字号、字体和行距。Windows 默认字族方向是 Segoe UI Variable；中文回退及项目字体需验证可读性，不强制替换已用字族。
- 标题说清内容或任务；按钮使用能说明结果的短动词。输入框保留可识别标签，不能仅靠 placeholder。省略长内容时保留查看完整值的可达方式；不要把全部说明藏入仅悬停可见的 ToolTip。中英文布局和无障碍名称一起核对。
- 图标使用一致来源、视觉重量与光学尺寸，沿用项目已有图标封装；线框/填充切换应表达状态，而不是随意混用。小图标的可见大小与按钮命中区域分开设计。纯图标按钮必须提供可访问名称；带文字的控件也要核对其名称与可见标签一致。含义不熟悉时保留文字提示。

依据：[Fluent Typography 的 Windows 表](https://fluent2.microsoft.design/typography)、[Windows 图标指南](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/icons)。

## 色彩、主题与对比度

- 将前景、次级文字、表面、描边、强调及危险/警告/成功映射到语义角色；不要把浅色背景上的色值直接反相当深色主题。状态色配合文字、图标或形状表达含义。品牌色不代替所有选中、错误和焦点状态。
- Fluent 的 token 是角色和值分离的方法；在 WinUI 使用已存在的 ThemeResource/资源和状态样式，不假定 Web token 名是可用 XAML key。核对默认、悬停、按下、选中和焦点组合，不能只验默认状态。
- 常规可见文字默认核对至少 **4.5:1**；Fluent 对满足其大字定义的文字允许 **3:1**，对有信息含义的交互/非文字元素与邻接色要求 **3:1**。只有明确满足所用标准的字号/字重条件才采用大字门槛，不能把 Semibold 自动当 Bold，也不能因 DPI 物理像素变大就认定为大字。未核实大字资格时按 4.5:1 检查，并注明该前提。
- Windows 的文字专项指南对可见文字采用更保守的 4.5:1，并列出非信息装饰、logo、非活动 UI 等例外。报告写清采用的判据；不要把装饰线、阴影或禁用内容机械纳入同一门槛，也不要用禁用例外掩盖仍需读取的错误和帮助文字。
- 对比度来自实际合成的前景/背景。海报、剧照、半透明和动态背景需检查受影响的明暗/繁杂背景及回退；单个画刷的十六进制值或一张截图不能证明全部背景通过。
- 高对比度使用系统角色核对文字、选择、边界和焦点，不只验证配色变了。已满足常规对比度的界面仍需保留高对比度语义。

依据：[Fluent Color](https://fluent2.microsoft.design/color)、[Design tokens](https://fluent2.microsoft.design/design-tokens)、[Fluent Accessibility](https://fluent2.microsoft.design/accessibility)、[Windows 可访问文字](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements)。

## 几何、层次与材质

- 圆角表达组件与容器的关系。Windows 基准常见为页内控件 4、临时上层容器 8、相交直边 0；ToolTip 等有自身规则。先查该控件的框架/项目资源，再判断偏差；不把所有卡片、海报、按钮和顶层窗口改成同一圆角。
- 嵌套容器保留可辨的内外边距和相容轮廓，避免边角露底、阴影裁切、描边重叠。最大化、贴靠和原生窗口边框按系统行为处理。
- 用表面、描边和适量阴影表达层次。Fluent 明确 Windows 与其他平台的阴影组成有差异；不照抄 Web 的 shadow token 参数到 WinUI，也不为每一层内容添加悬浮阴影。
- Windows 材质有用途边界：Mica 用于窗口等长驻背景语境，Acrylic 在官方 Windows 指南中用于临时、可轻关闭的表面。**Momoka 的既定实心表面及允许亚克力的范围是已记录的产品取舍**，只从 [CLAUDE](../../../../CLAUDE.md#winui-官方优先与偏离确认)读取，不因本段建议改变。
- 系统底材与应用内画刷分开选择；视觉模糊不证明能采样原生视频或 WebView。透明不可用、窗口失活等回退的实现和取证沿用[现有材质方法](../../momoka-winui-shell/references/materials.md)。

依据：[Windows Geometry](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/geometry)、[Fluent Elevation](https://fluent2.microsoft.design/elevation)、[Windows Materials](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/materials)。

## 原生控件、导航与反馈

- 先按语义选控件：执行动作用 Button，立即启停某项功能用 ToggleSwitch，可独立勾选的选项用 CheckBox，互斥选择用 RadioButtons 或适合项目的单选控件。不能仅凭设置是否即时保存决定替换 ToggleSwitch/CheckBox；同时看表达的是功能开关还是选项选择。不要让 Border/文本加点击事件承担按钮、选择器或导航的全部职责。定制外观不能丢掉原生焦点、命令、键盘和 UIA 语义。
- 内容导航、筛选与当前页动作区分，保留所在位置、返回和选中状态。次操作可进菜单；关键操作不能只在悬停后出现，也要考虑键盘和触摸入口。
- Dialog 用于需要中断并处理的任务或决定；Flyout/MenuFlyout 用于就地轻量内容/命令；ToolTip 只作补充。按控件类型验证焦点和关闭，不把所有弹层当模态。Momoka 的点外取消是[项目规则](../../../../CLAUDE.md#弹窗)，不能声称为 ContentDialog 原生默认。
- 加载时表达正在做什么；空结果区分没有内容和筛选无结果；失败给出可理解原因与恢复动作。防止重复提交，反馈与实际完成结果一致。能力不可用的显隐和入口保留按[项目规则](../../../../CLAUDE.md#功能可用性与显隐)，不另发明统一禁用策略。

依据：[Windows 控件与模式](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/)、[ToggleSwitch 与 CheckBox 选择](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/toggles)、[Dialog 指南](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/dialogs)。新交互的源码对照方法见 [WinUI Shell](../../momoka-winui-shell/SKILL.md)。

## 输入、可访问性与动效

- 按控件模式验证 Tab/Shift+Tab、方向键、Enter/Space、Esc；不要强制每个复合控件的所有子项都进入 Tab 序列。焦点可见，离开弹层回到仍有效的来源，删除/刷新后移到合理目标。
- UIA 名称、角色、值/选中/展开状态与画面一致；错误和加载结束的必要反馈能被辅助技术获取。更换模板后检查真实 UIA 树；只有 AutomationId 不代表读屏可用。
- 若本次界面涉及触摸，检查有效命中区域，而非把图标做大。Windows 将 40×40 epx，或宽至少 120 且高至少 32 epx，定义为可触摸；面向触摸优化时建议考虑 44×44 epx 和至少 4 epx 的目标间距。按使用场景和原生控件选用，不把所有桌面按钮统一成 44 方块。
- 动效解释位置、层级或状态变化，时长与距离相配；避免为装饰延迟输入和完成反馈。优先沿用原生转场及项目动效资源，不编造全局统一时长。
- 尊重系统动画偏好及项目已有设置。无动画路径也要完成状态、焦点、选择及最终布局；快速反向操作、离页或关闭时应安全终止/替换动效。不为“符合本技能”额外新增设置开关。

依据：[Windows Keyboard](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-interactions)、[Windows Touch](https://learn.microsoft.com/en-us/windows/apps/develop/input/touch-interactions)、[Fluent Motion](https://fluent2.microsoft.design/motion)、[Windows Motion](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/motion)、[UISettings.AnimationsEnabled](https://learn.microsoft.com/en-us/uwp/api/windows.ui.viewmanagement.uisettings.animationsenabled)。
