# EgPicker 展开有效期回归

逐字编译链接当前生产 `Views/EgPicker.cs`；`TrackRow` 使用真实 Core 项目。替身只提供无 HWND/WinRT 的 ComboBox、Popup、DispatcherQueue、布局数值和事件边界。

覆盖同一存活打开的延迟撑宽、快速开关/重开、重复通知、卸载/重载、延迟 Unloaded、重套或缺失模板、派发拒绝、Popup 已关和晚到旧尺寸事件；保留父级限宽、列表封顶恢复、原生 `ShouldConstrainToRootBounds` 与轨道可用性。每项结束核对列表订阅和 MaxWidth 均已清理。

闭合正文边界模拟固定 WinUI 源码 `693f472aeaf89c6dff66256ee6fcd453e5d553b8` 的 `ComboBox_Partial.cpp`：`FinishClosingDropDown` 在 `DropDownClosed` 前通过 `SetContentPresenter` 重写选中项和列表模板。使用真实 `TrackRow` 验证 Text／ShortText 恢复、两行模板清空、快速重开取最新项、重套模板拒绝旧正文，以及未设闭合成员时保留框架显示；这些离线边界不证明当前 WinUI 二进制的最终像素。

运行 `tools/run-regressions.ps1 -Suite Momoka.PickerLifecycleTests`。`EgPickerSource` MSBuild 属性只供修前源码反例回归，默认编译当前生产文件；不在生产添加测试分支。

没有创建窗口、读取保存账号或播放，不证明真实 ComboBox 的键盘/UIA、焦点返回、截图或跨 DPI。实际原生验证应覆盖电影/单集六颗文件选择框与 AniBT 三颗筛选框的短窗展开、Escape 关闭、快速重开和离页；测试替身的基类按固定框架源码在重套模板时关闭展开状态。
