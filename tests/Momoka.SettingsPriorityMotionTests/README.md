# 设置优先级回弹取消专项

独立树 `C:/Users/89400/.codex/worktrees/settings-rows-motion/Momoka`，在 Rows 动画提交
`c42d0653be34fdc7a3917fb56f013bd40c13c0b6` 之后单独修复。写集只有
`src/Momoka.Shell/Views/SettingsPriorityMotion.cs` 和本目录，不改 SettingTitleRulesRow、
Rules 持久化、SettingsPage、已有 probe 边界、主测试注册或解决方案。

## 问题与修复

真实设置探针的「三处关键词拖拽优先级与弹簧」在关闭动画后仍报告回弹：
`Rules.Move` 不论偏好都排队，`Stop` 只清活动 Storyboard，留下 `_queued` 和 `_before`。

- 行变化和 QueueSettle 在关闭动画 / 离树时直接 Stop，不制造新排队。
- Stop 同步清掉 `_queued` / `_before`，增加 generation，停止活动动画并将模板根 Y 清零。
- 延迟 delegate 在改任何状态之前检查 generation；旧 callback 不得消费新 queue 或快照。
  UpdateLayout 后也检查取消 / 偏好 / 生命周期，不再启动过时的回弹。
- HomeMotion.PreferenceChanged 关闭时直接 Stop，Detach 解除订阅并取消排队。
  旧 Completed 只有 generation 和当前根上的 Storyboard 实例都匹配时才能清理。
- 原 380ms、0 / 230 / 310 / 380ms 关键帧及回弹幅度完全不改。
  BeginReorder / EndReorder 的两阶段调用仍保留：Stop 只取消动画，不替业务结束拖拽；
  EndDrag / Detach 仍在原有结束点提交一次。

## 直接源码链接行为测试

本项目 compile-links 完整的实际 `SettingsPriorityMotion.cs`，不是复制的状态机。
FrameworkElement 布局、DispatcherQueue、Storyboard 时钟和规则交互端点是边界替身。
`_before` 反射仅用于验证执行 Stop 后的内部状态，不用源码搜索冒充行为测试。

22 个专项用例覆盖：

- 关闭偏好后同步 Rules.Move、关闭运行中 / 排队动画、Stop 清队列 / before、旧 callback / Completed；
- 关闭再开、替代队列、BeginDrag 失效旧队列、拖拽中关闭 / Stop 和 Remove + Add 两阶段调用；
- Detach / 重复 Detach、离树、Dispatcher 拒绝、布局期间关闭动画；
- 正常 Move、同轮连移、原 380ms 关键帧、379ms 未落定 / 380ms Completed 落定、无移动和非法拖拽边界。

先用未修的真实生产源运行：22 项中 13 项失败、9 项通过，复现队列 / 旧回调缺陷。
修复后 fresh Release 编译及运行：22 项通过、0 失败、0 跳过；编译 0 警告、0 错误。
规则端点只记录 Begin / End 调用次数，不声称覆盖真实持久化、WinUI 原生拖拽、鼠标或像素。

只运行本项目，不启动全 Core / SelfCheckRun mutex 套件，不生成窗口、不连服务器、不改真实设置、
不运行 probe / 发布 / push。在当前工作树根目录先还原、编译当前 Release，再运行：

```powershell
& 'C:\Users\89400\.dotnet\dotnet.exe' restore ./tests/Momoka.SettingsPriorityMotionTests/Momoka.SettingsPriorityMotionTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& 'C:\Users\89400\.dotnet\dotnet.exe' build ./tests/Momoka.SettingsPriorityMotionTests/Momoka.SettingsPriorityMotionTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& 'C:\Users\89400\.dotnet\dotnet.exe' run --project ./tests/Momoka.SettingsPriorityMotionTests/Momoka.SettingsPriorityMotionTests.csproj -c Release --no-build --no-restore
```

## probe / 集成

生产接口名不变，不需要弱化「关闭动画后仍在回弹」的断言：
`IsAnimating` 在 Stop / 关闭偏好后当场为 false，`HasSpring` 只读活动 Storyboard。
已有 SettingsProbeContractTests 若选择链接真实 Priority 文件，其 HomeMotion 边界需支持
PreferenceChanged。当前该项目的 Priority 是不可执行的边界替身，本专项才执行本次实际实现。
真实设置探针仍需由主代理串行重跑三处关键词表的宽 / 窄窗和原生拖拽。

## 供主代理整合的 PROGRESS 行

- 2026-10-27，独立树 `settings-rows-motion`，后续写集 `SettingsPriorityMotion.cs` + `Momoka.SettingsPriorityMotionTests`：修复关闭动画仍排队和 Stop 未清 `_queued` / `_before`，generation 拒绝过期 queue / Completed；保留 380ms 弹簧和原拖拽两阶段业务调用。实际源码链接专项旧源 13 红 / 9 绿，修复后 fresh 22/22、0 失败 / 0 跳过；本树 Shell / 专项 Release 0 警告 0 错误、空白 / LF 通过。未运行全 Core、GUI / probe、发布 / push；主代理保留原断言重跑真实三表拖拽探针。
