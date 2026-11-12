# WinUI P3：EgPicker 延迟撑宽有效期

2026-11-12，Asia/Shanghai。基线 `2f74559f858b3c38669be96eb17e337bbba17b3a`；实现树为 `winui-p3-picker/Momoka`，主控整合树为 `winui-p3/Momoka`。本页记录离线子批次，不代表主树整合或桌面交付。

原 `DropDownOpened` 直接排队 `WidenToDropDown`。打开后立即关闭、卸载或重套模板时，旧回调仍可设置 `Width` 和挂载列表尺寸事件；快速重开还会让上一轮回调借新 Popup 执行。撑宽只应属于存活打开的同一轮。

本次继续使用 ComboBox 原生模板、`PopupPart` 诊断入口和 `ShouldConstrainToRootBounds=true`。不更换控件、不修改材料参数、业务选择、草稿或页面活动票。队列记录打开代次和 Popup；关闭、真正卸载及重套模板先失效旧请求并释放尺寸责任；Loaded/仍存活打开的同轮请求可以恢复撑宽。延迟 Unloaded 但仍 Loaded 不终止当前责任；迟到旧列表尺寸事件不能改当前 Popup。父级剩余宽度、闭合宽度保留、列表 MaxWidth 封顶/解除算法照旧。

当前九个声明调用者是 DetailPage 六颗媒体源/音频/字幕框和 AniBtSearchPanel 三颗筛选框。SettingsPage 仍用普通 ComboBox。已有 `DetailPage.ProbePickerTree` 与 ShellNavigationProbe 的 PopupPart、弹层宽度及无效轨道检查接口保持可用；没有更改其通过判据。

源码对照：WinUI Gallery 固定 `6de91feccadfbb0a796b71caedead5a2284f730e` 的 `WinUIGallery/Samples/ComboBox/ComboBoxPage.xaml` 和 `.xaml.cs` 使用原生 ComboBox、ItemsSource/ItemTemplate/SelectionChanged，编辑项另通过 TextSubmitted 校验；未复制它的实验依赖。框架参考克隆固定 `e38939f2680f0da968d7536a0b7863617ab5793e`，`dxaml/xcp/dxaml/lib/ComboBox_Partial.cpp` 的 OnApplyTemplate 先 ReleaseMembers 再关闭展开；SetupPopupPart 设置窗口化边界，KeyDown/PopupKeyDown 和 RaiseAutomationPeerExpandCollapse 仍归框架。该框架源码只支持机制判断，未证明与本机 WinUI 2.3.9 二进制完全一致。

离线验证使用 `Momoka.PickerLifecycleTests`，完整链接真实 EgPicker 和真实 Core TrackRow，控件/布局/队列为无 HWND/WinRT 替身：

| 检查 | 实际结果 |
| --- | --- |
| 当前 Core/测试 Release 构建 | 0 警告、0 错误，SDK 10.0.400，单节点及共享编译关闭 |
| 同一18项测试＋基线EgPicker源 | 9通过、9失败、0跳过，退出1；快速关/重开、卸载、旧模板/尺寸回调检出旧行为 |
| 当前生产源统一专项入口 | 18通过、0失败、0跳过，退出0；每项结束核对列表订阅和MaxWidth清理 |
| 八个改动文本 | check-diff通过，UTF-8/LF、末尾换行、BOM与Git空白检查均通过 |

原失败及修后日志保存在本树 `artifacts/picker-lifecycle/`，修前源单独保留；没有以 `--no-build` 运行未成功编译的输出，也没有修改产品添加测试开关。新测试项目登记方案和 `regression-suites.json`；`EgPickerSource` 仅为测试项目的修前源码替换参数。

本树没有启动 WinUI/桌面、读取账号或设置、播放、完整 Core 主套件或全量探针。实际 API 编译和交付仍由主控对最终整合 Shell 执行；真实 Escape/Enter/方向键、ExpandCollapse/Selection UIA、焦点返回、短窗翻转、跨 DPI 和像素未验证。建议实际匿名检查电影/单集宽窄框、AniBT 筛选框的开→关→重开及离页，保留已有长名/不可用条目/同树材质读数；必要截图按主控图片上限安排。

本修改未改变 D02 弹窗取消或 D07 核心选择/激活语义，没有新的官方冲突或实质产品取舍，不触发重新确认。设置折叠组原生 Expander/状态自动化作为后续独立候选，本轮不替换。
