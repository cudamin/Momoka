# 原生动效候选与诊断边界

先编译当前树 Release Core/Tests/Shell，再运行本次生成的 `Momoka.Tests.dll motion-probe-fixture`。
该入口仅运行启动参数和实际屏幕证据判定，共享桌面锁、窗口、播放和真实设置均不使用；完整生产 `MotionProbeEvidence.cs` 原样链接入测试。

当前开发件的原生入口为 `Momoka.exe --probe-shell --probe-section motion --screen 1`。
分区使用匿名假 HTTP、临时设置、现有正式页面，依次运行海报前进/正式返回/回退、人物前进、详情悬浮卡、主页历史及有限原生 Entrance 候选。
它持有当前用户桌面诊断锁，必须与其他自检/界面探针串行；命令不接受真实播放、普通 `--navigation-timing` 或自检数据参数。

候选按相同匿名库的 LibraryPage 和 MusicLibraryPage 比較 `Suppress + BrowseMotion.None` 与原生 Entrance，保留生产 BrowsePresentation 的首帧/图片门控。
96×28 DIP 绿色标记挂在实际页面根，随该页动画；取样限定到标记周围，ROI外绿色不参与。
Rendered 后同一屏幕帧读取 BGRA 与几何，检查动态中段、关闭应用动画且清除 ContentTransitions 后的前三个提交帧、正式返回后禁用动画的 GoForward，以及最终帧是否与对照一致。
为静止对照也产生实际提交帧，所有试验有同样的页面外 2px 红色心跳，间隔 16ms；这会增加采样负担，不是 CPU/GPU/帧率或真实点击速度基准。

每条试验都在 finally 保存 `motion-<route>-<trial>.json`；汇总为 `motion-page-transitions.json`，最多四张同帧截图。
结果只有 retained、inconclusive 和 candidate-only：retained 表示实测没有达到迁移门槛；inconclusive 在分区中明确失败；candidate-only 仅支持继续评估，不授权产品采用。
未修改 Windows 动画设置，未覆盖真实服务、其他影视服务界面、实际点击到首内容时间、跨 DPI、读屏或平台逐帧性能。
关闭系统动画的中断验证、首屏时序及最终整合版本验收仍需执行，不能把工具单测或当前候选报告写成原生迁移通过。

原生配置依据 Gallery 固定提交 `6de91feccadfbb0a796b71caedead5a2284f730e` 的 `WinUIGallery/Samples/PageTransition/PageTransitionPage.xaml` 和 `.xaml.cs`。
当前 WinUI 2.3.9 元数据没有 GoForward(transition) 重载，历史条目的 NavigationTransitionInfo 只有 getter；探针不修改条目或伪造历史。
