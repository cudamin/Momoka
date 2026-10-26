# 设置从属行 160ms 动画专项

在独立工作树 `C:/Users/89400/.codex/worktrees/settings-rows-motion/Momoka` 完成，起点 `840aa64`。
写集只有生产 `SettingsRowsMotion.cs` 和本目录；不修改 SettingsPage、搜索、既有 probe 边界、
主测试注册入口或解决方案。进度行放在本文末尾供主代理整合，避免同时写根目录 PROGRESS.md。

## 实现

- 固定时长的 `AddDeleteThemeTransition` / `RepositionThemeTransition` 不再注册；
  `ItemContainerTransitions` 始终为空。不是提前结束一个报告落定的计时器。
- 实际新增行 Opacity、删除行视觉副本 Opacity、保留行模板根 Y、卡片 Height 的动画请求
  都在同一个本地 Storyboard 内，160ms、Cubic EaseOut、无延迟 / stagger。
  没有 `_settling` 或另一个猜测系统过渡时长的计时器；Completed 才清理并报告落定。
- Rows 原地同步增删 / 移动，正常行、上级开关、ItemsControl 和卡片都不搬树、不重建。
  只有删除行在 Popup 中有短暂 ContentPresenter 副本；递归关闭命中和 Tab、设为 Raw
  accessibility view。副本不是 ItemsSource，也不维持数据适用性。
- 运行中反向切换保存当前高度、位移和透明度，一轮队列合并到最终布局；过期 queue / Completed
  不能结束或重启替代的动画。关闭减少动画、隐藏分组 / 任一父级、Stop / 真正 Unloaded /
  Detach / Dispose 都清理动画、副本、固定高度与 Clip。
- 副本只占视口和当前卡片内的可见切片，跟随滚动；横向窗口变化直接落定。
  取消恢复 Opacity / RenderTransform 的原本地值（原来 Unset 的仍为 Unset）。

## 离线行为验证

完整源码链接当前树的 `src/Momoka.Shell/Views/SettingsRowsMotion.cs`，没有复制生产的计时、
快速切换、取消或 Dispose 算法。仅 FrameworkElement / Storyboard 时钟 / DispatcherQueue /
行数据及偏好事件是边界替身。没有用源码搜索代替行为断言。

本专项 44 个用例：

- 对实际 Storyboard.Children 断言淡入 / 淡出 / 补位 / Height 请求都是 160ms、无延迟；
  受控时钟前进到 159ms 仍运行、160ms 收到实际 Completed 并落定。
- 删除数据同步可用性、Rows / 上级实例稳定、保留偏好；副本不命中、无 Tab、Raw 树；
  等高换类型、同标签同类型恢复、Move、同轮快切和十二次运行中快切。
- 中途反向继续当前高度 / alpha / 位移；Stop、取消排队、已投递旧 Completed；
  应用与系统减少动画（含后台事件优先级）、父级隐藏、离树、重复 Unloaded、Detach / Dispose。
- Dispatcher 拒绝、Begin / UpdateLayout 抛错、宽度变化、动态剪裁、滚动切片；
  原本地透明度 / Transform 与 Unset 恢复，以及清空最后一行的即时清理。

仅运行此项目，不运行全 Core / SelfCheckRun mutex 套件。无 GUI、WinRT 实例、网络、真实设置 IO、
播放或发布。项目引用 Core 只供编译，不注册或启动它的全套测试。

在本工作树根目录先还原、编译当前 Release，再运行：

```powershell
& 'C:\Users\89400\.dotnet\dotnet.exe' restore ./tests/Momoka.SettingsRowsMotionTests/Momoka.SettingsRowsMotionTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& 'C:\Users\89400\.dotnet\dotnet.exe' build ./tests/Momoka.SettingsRowsMotionTests/Momoka.SettingsRowsMotionTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& 'C:\Users\89400\.dotnet\dotnet.exe' run --project ./tests/Momoka.SettingsRowsMotionTests/Momoka.SettingsRowsMotionTests.csproj -c Release --no-build --no-restore
```

不登记到 `Momoka.sln` 或改 `tests/Momoka.Tests/Program.cs`，因为共享入口属于主代理写集。
集成后需要单独运行上述项目；源码链接不能替代主代理串行的真实 UI 检查。

## 现有 source-linked probe 的最小迁移

1. `CompleteRows()` 保留 `Storyboard.CompleteAll()`，删除通过反射读取 `_settling` 的循环。
   `IsAnimating` 仍覆盖 queue 和真正的 Storyboard，正常完成 / Stop 都解除 Completed 订阅。
2. `SettingsRowsMotion.HasTransitions` 保留兼容名字；`SettingsPage.HasConditionalTransitions`
   无需修改调用。现在它表示可控行动画启用，而不是系统 Transition 的存在。
   与 `HomeMotion.AnimationsEnabled` 比较的偏好断言可保留；类型断言改为
   `ItemContainerTransitions` 为空、真实动画请求 160ms。
3. 现有 `SettingSection` wrapper 的 `Rows` 不能仅暴露 `INotifyCollectionChanged`：还需要
   可枚举的 SettingRow、Count、标签和类型身份，以及转发 Section 的 PropertyChanged。
   wrapper 的 ContainerFromItem 映射须保留真实数据对象身份，不复制数据 / 显隐算法。
4. 新 UI API 边界见本目录 `UiBoundaries.cs`：ContainerFromItem、LayoutUpdated / Unloaded /
   SizeChanged.PreviousSize、DependencyProperty 本地值保存 / 清除、祖先 Visibility 回调、
   TransformGroup、Popup / Canvas / ContentPresenter、TransformPoint、ScrollView / ScrollViewer、
   UISettings.AnimationsEnabledChanged、IsTabStop 和 AccessibilityView.Raw。
   这些仅供主代理选择性扩充既有 fixture；不直接把两个互相重复的 UI 类型文件同时编译。

真实 probe 保留原快速切换、Rows / 上级控件身份、偏好恢复、父页隐藏和 Release 清理断言。
还需检查删除副本真实模板加载、滚动裁剪 / 位置、真实键盘焦点、HWND / WinUI 帧时间和像素；
离线边界不证明它们。若在中间帧查视觉行，要区分不可交互副本和 Rows 的即时状态。

## 供主代理整合的 PROGRESS 行

- 2026-10-27，独立树 `settings-rows-motion`（起点 `840aa64`），写集 `SettingsRowsMotion.cs` + `Momoka.SettingsRowsMotionTests`：移除系统固定过渡 / settling timer，行淡入淡出、补位与卡片高度统一 160ms 本地 Storyboard；同步 Rows / 上级实例保持，减少动画、父级隐藏及离树 / Dispose 立即清理。fresh 专项 44/44，0 失败 / 0 跳过；本树 Shell 和专项 Release 构建、空白 / LF 检查通过。未运行全 Core、GUI / probe、发布 / push，真实 UI 覆盖留给主代理串行整合。
