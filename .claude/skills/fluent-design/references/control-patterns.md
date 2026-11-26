# Fluent 2 常见控件用法

标准：Fluent 2 组件规范。Windows 分支：Fluent 2 官方指定的 WinUI 3。下表同时给 Fluent 2 规格与 Windows 分支默认值；没有特别说明时，Windows 分支的数值来自 WinUI 3 `generic.xaml` 2.7.0。

## 1. 按钮

Fluent 2 按钮按 small / medium / large 三档，默认 medium；圆角 `borderRadiusMedium` 4；图标 20（large 24）；高度由行高、上下 padding 与 1px 描边推导。

| 尺寸 | Fluent 2 高度 | Fluent 2 字号 / 行高 | Fluent 2 上下 padding | Windows 分支 |
| --- | --- | --- | --- | --- |
| small | 24 | 12 / 16 | 3 | 显式 `MinHeight=24` |
| medium | 32 | 14 / 20 | 5 | 默认 `MinHeight=32`，`Padding=11,5,11,6` |
| large | 40 | 16 / 22 | 8 | 显式 `MinHeight=40` |

| 类型 | 用法 | 关键点 |
| --- | --- | --- |
| 标准按钮 | 次要操作、取消、普通命令 | `ControlFillColorDefaultBrush` / `ControlStrokeColorDefaultBrush` |
| 品牌按钮 | 一屏一个主操作 | `AccentButtonStyle`；Fluent 2 用 `colorBrandBackground` |
| 图标按钮 | 工具栏、命令栏 | 图形 20 或 24，点击区 32 或 40；必须有名称与 ToolTip |
| 拆分按钮 | 主操作加下拉选项 | 两半相接，中间不圆角 |
| 切换按钮 | 视图切换、筛选开关 | 选中用品牌色；不要只改文字颜色 |

规则：

- 按钮文案用具体动词。不要只写「确定」而不说明确定什么。
- 同一组按钮高度一致；主操作在右侧（对话框）或最前（命令栏），跟随产品既有习惯。
- 禁用按钮保留在原位，不要消失；不可用的整组动作才隐藏。
- 按钮内图标与文字间距 8；只有图标时，`AutomationProperties.Name` 必填。

## 2. 输入控件

Fluent 2 组件普遍提供 small / medium / large 三档，默认 medium；Windows 分支的 TextBox、PasswordBox、NumberBox、ComboBox、AutoSuggestBox 默认 `MinHeight=32`。

| 控件 | Windows 分支几何 | 关键令牌 | 注意 |
| --- | --- | --- | --- |
| TextBox | `MinHeight=32` | `ControlFillColorDefaultBrush`，聚焦 `ControlFillColorInputActiveBrush` | 占位符用 `TextFillColorTertiaryBrush`；错误用 `SystemFillColorCriticalBrush` 加文案 |
| PasswordBox | 同 TextBox | 同上 | 提供显示密码的切换；不要记录明文 |
| NumberBox | 同 TextBox | 同上 | 范围与步进写清楚；无效输入给内联错误 |
| ComboBox | `MinHeight=32` | `ControlFillColorDefaultBrush`，下拉用 Acrylic/实心 | 选项按逻辑排序；长列表可搜索 |
| AutoSuggestBox | `MinHeight=32` | 同上 | 输入即筛选；无结果给空状态，不要静默 |
| SearchBox | `MinHeight=32` | 同上 | 搜索范围要可见；清除按钮可达 |

规则：

- 标签在控件上方或左侧，保持一致；不要同一表单两种排法。
- 必填项用文字标注，不要只用红色星号。
- 错误提示紧贴控件，说明原因与修法；不要只把边框变红。
- 输入框宽度按内容与最长有效值设定，不要铺满整窗。

## 3. 选择控件

| 控件 | 用途 | 注意 |
| --- | --- | --- |
| CheckBox | 多选、独立开关 | 标签可点击；不确定态只在确实需要时使用 |
| RadioButton | 互斥的少量选项 | 默认选中一项；选项不超过 7 个 |
| ToggleSwitch | 立即生效的开关 | 标签描述「开启后会发生什么」；不要用于需要提交的表单 |
| Slider | 连续数值 | 显示当前值；提供键盘步进 |
| RatingControl | 评分 | 允许清除；只读时不要伪装成可点 |

规则：

- 选择控件的点击区包含标签，不要只有小方块可点。
- 开关状态变化立即生效时，不要再加「保存」按钮。
- 互斥选项多到需要滚动时，改用 ComboBox。

## 4. 卡片与列表

| 项 | Fluent 2 | Windows 分支 |
| --- | --- | --- |
| 内容卡片圆角 | `borderRadiusXLarge` 8（或按层级用 4） | 8，或按产品统一为 4 |
| 列表项圆角 | `borderRadiusMedium` 4 | `ListViewItemCornerRadius` 4 |
| 列表项最小高度 | 按组件规范 | `ListViewItemMinHeight` 40 |
| 卡片内边距 | `spacingHorizontalL` 16 | 16；紧凑卡片 12 |
| 卡片边框 | `colorNeutralStroke2` 1px | `CardStrokeColorDefaultBrush` 1px |
| 列表选中 | `colorNeutralBackground1Selected` + 品牌指示条 | `SubtleFillColorSecondaryBrush` + 3px 强调条 |

规则：

- 卡片是一层表面，不要在外面再包一层带背景与边框的容器。
- 卡片内标题用 `BodyStrong` 或 `Subtitle`，正文 14；不要用 16 或 18 造中间层级。
- 列表项的主行与副行用 14 / 12，行高分别 20 / 16；不要靠加粗整行来区分。
- 列表滚动由列表自己拥有；页面已滚动时，横向海报架用横向 `ScrollViewer` 加 `ItemsRepeater`，不要嵌套 `GridView`。

## 5. 对话框

| 项 | Windows 分支 | 说明 |
| --- | --- | --- |
| 最小宽度 | 320 | `ContentDialogMinWidth` |
| 最大宽度 | 548 | `ContentDialogMaxWidth` |
| 圆角 | 8 | `OverlayCornerRadius` |
| 内容边距 | 24 | `ContentDialogPadding` |
| 按钮 | 主操作品牌色，次操作标准，关闭用「取消」 | 主操作在右 |

规则：

- Fluent 2 的对话框规格以组件页为准；Windows 分支用上面的 WinUI 默认值，不把 548 当成跨平台规范。
- 标题说明要做什么，正文说明后果；不要用「提示」当标题。
- 破坏性操作把动词写进按钮（「删除」「覆盖」），不要用「确定」。
- 对话框点外关闭只返回取消，不触发保存；`Closing` 防护要保留。
- 内容超过一屏时，正文区域自己滚动，标题与按钮固定。
- 不要用对话框承载长表单；改用页面或 Flyout。

## 6. 浮层与菜单

| 项 | Fluent 2 | Windows 分支 |
| --- | --- | --- |
| 圆角 | `borderRadius2XLarge` 12（sheet/popover）；菜单可用 8 | `OverlayCornerRadius` 8 |
| 边框 | `colorNeutralStroke2` 1px | `SurfaceStrokeColorDefaultBrush` 1px |
| 背景 | Acrylic 或 `colorNeutralBackground1` | Acrylic 或 `SolidBackgroundFillColorQuarternaryBrush` |
| 内容边距 | `spacingHorizontalL` 16 | `FlyoutContentPadding` 16,15,16,17 |
| 阴影 | `shadow16` / `shadow28` | 内置阴影；高程 32 |

规则：

- 菜单项高度与命令栏一致；图标 16，文字 14。
- 危险操作放最后，用 `SystemFillColorCriticalBrush` 图标或文字，不要整项铺红。
- 浮层不要嵌套浮层；需要二级菜单时用子菜单，不要新开 Flyout。
- 交互 `Popup` 必须启用 `IsLightDismissEnabled`；点外关闭只取消。

## 7. 导航

| 项 | Windows 分支 | 说明 |
| --- | --- | --- |
| 展开面板宽度 | 320 | `SplitViewOpenPaneThemeLength` |
| 紧凑面板宽度 | 48 | `NavigationViewCompactPaneLength` |
| 折叠阈值 | 641 | `CompactModeThresholdWidth` |
| 展开阈值 | 1008 | `ExpandedModeThresholdWidth` |
| 选中指示 | 3px 强调条 + `SubtleFillColorSecondaryBrush` | 与列表一致 |

规则：

- 导航项图标 16，文字 14；选中项加粗可用 `BodyStrong`，不要换字号。
- 导航层级不超过两级；更多层级用页面内 Tab 或分区。
- Small 宽度用 `LeftMinimal` 或底部导航；不要保留被压扁的展开面板。
- 切换页面保留滚动位置与选中项；返回时不要重置。
- Fluent 2 的跨端导航尺寸以组件页为准；Windows 分支用上面的 WinUI 值。

## 8. 状态与提示

| 控件 | 用途 | 关键点 |
| --- | --- | --- |
| InfoBar | 持续状态、可关闭提示 | 语义色背景 + 图标 + 文案 + 一个动作 |
| TeachingTip | 首次引导 | 只出现一次；可跳过；不要阻断主流程 |
| ProgressBar | 已知进度 | 显示百分比或阶段；不确定进度用 indeterminate |
| ProgressRing | 短时等待 | 超过 1 秒才显示；避免闪烁 |
| ToolTip | 补充说明 | 不能承载关键信息；键盘聚焦也要能出现 |
| Empty state | 无数据 | `Large Title` 或 `Subtitle` + 说明 + 主操作 |

规则：

- 加载超过 1 秒给进度；超过 10 秒给阶段说明与取消。
- 错误提示给恢复动作；不要只显示错误码。
- 空状态不要只放图标与「暂无数据」；说明为什么空、下一步做什么。
- 成功提示不要用对话框；用 InfoBar 或页面内状态。

## 9. 设置页

- 用卡片分组，每张卡片一个主题；卡片内边距 16，卡片之间 16。
- 每行左侧是标签与说明（14 / 12），右侧是控件；行高至少 40。
- 说明文字用 `colorNeutralForeground2`；不要用 ToolTip 代替说明。
- 开关立即生效；需要重启或重新登录的设置要明确写出。
- 不可用的设置整行隐藏或禁用并给原因；不要留下空白分组。
- 恢复默认放在分组底部或页面底部，不要和每项设置混排。

## 10. 反例

| 反例 | 问题 | 修法 |
| --- | --- | --- |
| 每个按钮都用品牌色 | 主操作失焦 | 一屏一个品牌按钮 |
| 按钮圆角 8 | Fluent 2 按钮用 `borderRadiusMedium` 4 | 4 |
| 对话框宽度随内容拉伸到 900 | 阅读困难，按钮离正文太远 | Windows 分支最大 548，长内容滚动 |
| 卡片内再放一张卡片 | 层级重复 | 用分隔线或间距 |
| 列表项 48 高又加 16 内边距 | 密度过低 | 标准 40，紧凑 32 |
| 导航项用 24 图标配 12 文字 | 比例失衡 | 16 图标配 14 文字 |
| 设置项开关旁没有说明 | 用户不知道后果 | 标签写清「开启后会怎样」 |
| 用 ToolTip 承载必读信息 | 触屏与读屏不可达 | 直接写在界面上 |
| 空状态只有「暂无数据」 | 没有下一步 | 说明加主操作 |
