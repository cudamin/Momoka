# P8 系统主题角色覆盖

日期：2026-11-13。独立树 `winui-p8-system-role/Momoka`，起点 `945bef2`。此单元修自检的角色归属识别，不改变材质、颜色、产品主题或基线。

同期简洁详情已采用 `EgDetailContentLayerBrush`：Dark/Light 的 `Color` 用 `ThemeResource LayerFillColorDefault`，HighContrast 用 `ThemeResource SystemColorWindowColor`。这是系统管理的动态色，不能加进 `ThemeHost.Brushes` 改成 Momoka 自涂色。原自检将所有 Palette 的 `Eg*Brush` 一概要求手涂，故将这一个正确别名报红。

新增明确单键声明及 `ThemeHost.IsSystemManagedBrush`：只承认 `EgDetailContentLayerBrush`，且从当前 Palette 的三个实际主题字典逐一核有原生 SolidColorBrush。自检保留手涂缺项和未知 Eg 角色检查，只将通过该核验的明确系统角色分开统计；不读取运行时源码、不嵌入整份 Palette，不以当前颜色字节冒称 ThemeResource 动态性。

对应源码契约验证唯一别名在三字典中的 `Color` 必须是正确系统 ThemeResource，并且不进入 ThemeHost 自涂列表。反例拒绝静态色、StaticResource、错误高对比角色、缺字典、重复键、错类型和扩大别名；纯声明函数覆盖缺三个实际刷、未知键及读源异常。运行时资源类型与编译时动态表达式合同是分开的证据，均不可放宽。最终正式自检仍由主协调会话同轮运行，原主题角色失败报告保留。

2026-11-13同轮实际工具运行：保留协调目录对齐主树e19ef89后Shell编译0警告0错误，run6a460f41767c4e21a6b1cfaf833b526c的主题角色覆盖通过（86手涂/1系统管理）。整体189项中188通过、1失败，失败为同期标题栏命中，与角色检查分开记录；未改基线。证据在主树artifacts/winui-p48-20261113/system-role-tool-final。该运行只完成工具验证，不代表最终交付主树全绿。
