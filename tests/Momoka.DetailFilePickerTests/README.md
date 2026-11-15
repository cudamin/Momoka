# 详情文件选择生命周期回归

逐字编译链接当前生产 `Views/DetailFilePicker.cs`，使用真实 Core `SourceRow`、`TrackRow` 和媒体流模型。`UiBoundaries.cs` 只提供依赖属性、事件、布局数值、XAML 端点与系统通知替身；选择提交、分组、来源身份守卫、订阅和排队重开逻辑均执行正式文件。

覆盖按钮内标签与值、当前/其他分组的原对象身份、打开预选与焦点移动不提交、点击和 Enter/Space 提交、单选事件入口、点外/Escape、不可用音轨、有效“不使用字幕”、自动占位空音轨、集合 Clear/Add/Replace/Remove/Move、外部选择和换来源、卸载/重载、隐藏/禁用、延迟 Closed 的快速重开、排队失效、六态画刷引用、高对比系统色及宽度上限。系统事件端点固定拒绝订阅，验证控件仍能 Loaded、复用页面外观刷新入口、卸载后拒绝刷新，并始终没有局部系统事件订阅。

标准入口：`tools/run-regressions.ps1 -Suite Momoka.DetailFilePickerTests`。`DetailFilePickerSource` MSBuild 属性仅用于修前源码反例，默认链接当前生产文件，不给产品代码添加测试开关。

该控制台测试不创建 WinUI 窗口，不读取保存账号，不连接真实服务器，不播放媒体，也不修改 Windows 系统偏好。替身的 UIA 单选端点只触发原生控件对应的选择事件，不能证明真实 `ISelectionItemProvider`、焦点返回、原生 GroupStyle 绑定、键盘事件顺序、像素材质、Flyout 放置回退或跨 DPI。原生运行检查需继续验证电影/剧/季/单集详情的六颗文件选择栏、长短文字、窄窗与短窗、无音轨空态、点外/Escape、快速关开及换来源/离页。

参考源码：WinUI Gallery 固定提交 `6de91feccadfbb0a796b71caedead5a2284f730e` 的 DropDownButton、Flyout 和 ListView 页（XAML 与 code-behind）；本地平台模板为 WinUI 2.3.9。延迟 Closed 边界另参考 microsoft-ui-xaml `2a73274618c3f1042ee617dca5f64be782f93261` 的 `FlyoutBase_partial.cpp`，该源码是支持性证据，不宣称与本机包二进制一致。
