# Momoka.UiMotionTests

`HomeMotion`（页面与货架入场）、`SurfaceMotion`（常驻元素/浮层进出）、`GlyphPulse`（图标脉冲）与货架焦点重试的独立回归。

生产文件按原样链接进本工程，WinUI 类型与时钟由 `UiBoundaries.cs` 提供：

- `Storyboard.Advance(ms)` 推进假时钟；`AutoReverse` 与 `CubicEase` 的 EaseIn/EaseOut 都按生产的语义走；
- `FrameworkElement` 的 `XamlRoot` 可设为 `null`，用来模拟真正离树；
- 三个动效文件均原样链接；系统 `UISettings` 提供可控事件，后台通知通过假 DispatcherQueue 回到 UI 线程。
- `ShelfStrip` 的键盘、焦点、排队与离树方法由构建任务原样提取；只替换视口、容器实现与 Focus 返回值。

覆盖的是**时间轴与值**：起点、中点、落点、打断续接、重复显示不重播、关掉动画立即落定、
元素自己的 RenderTransform 不被抢占、离树复位、图标脉冲的峰值与回位、连点不越弹越大。
新增进行中关闭系统/应用动画、零时长、Begin 失败、完成回收、迟到回调、缺源目标与页面离树检查。货架持续拒绝聚焦时只能投递一次，后续布局恢复；换来源及离树的旧回调不再操作新状态。

2026-11-13 当前源码 34/34，失败0、跳过0。用同一测试及原始 `29e9188` 四个生产文件运行独立负例，34项中21失败，包括持续拒绝焦点产生无界队列；恢复当前源码再次34/34。负例复制项目在 ignored `work/`，不属于干净检出运行前提。

P8可靠性检查补充注册/退订前后异常12项，当前46/46。三动效仍原样链接；注入UISettings add/remove失败验证本地钟/Active记录、终值、回调一次、重复启动及迟到拒绝。remove前失败明确保留一个假原生handle，释放未知不算零泄漏；用例断言后只结束该fake事件边界，不偷偷清产品状态。实际本机并未观察到此WinRT故障，不把故障注入通过说成原生释放实测。

不覆盖真实渲染、合成器补间、材质像素、原生键盘与 Narrator —— 这些由主树最终运行验收。

2026-11-14 动效施工 S3：原样链接 `ShellPage.SidebarMotion.cs`，验证 Loaded 幂等、双开关、
离树静态回退、旧原生通知与旧队列、注册/退订失败。`ShellPage.Sidebar.cs` 的实际 Loaded/Unloaded
调用由 PageContract 补接线契约，不能把本专项称为真实 XAML 事件测试。
构建任务另从当前 `ShellPage.Music.cs` 原样提取 `PlaceMusicBar` 与 `PlaceMusicBarMargins`，
以真实 SurfaceMotion/假时钟验证退出中重开、尺寸变化、关闭系统/应用动画和音乐墙末行避让。
首页待 Loaded 票据补系统动画关闭撤销、恢复不重播与回收后旧通知拒绝。
待 Loaded 的注册前后、退订前后异常及音乐栏 Begin 拒绝均有当轮行为断言。
当前 **80/80、0 失败、0 跳过**；原生像素、真实系统设置通知与资源释放仍需最终整合验收。
音乐方法提取的缺起点、重复起点和缺终点均实际拒绝，合法源恢复成功；不删除失败读数。

2026-11-14 轮播鼠标柔光：原样链接 `BannerPointerGlow`，覆盖首次装饰层未布局、DIP光心同步、移动不重播、
移出重入与迟到回调、触摸/笔和重复退出、高对比度/透明效果判定边界、关动画、卸载和宽窄边界。
同时解析实际 `HomeBanner.xaml` 检查非命中装饰层、绝对圆形径向画刷及阅读遮罩顺序；假画刷不证明像素。
原生像素入口为匿名检查台 `--probe-shell inspect --screen 1` 的 `InspectHomeGlow`，显式调用生产状态，
核对1280/760 DIP增亮、移动、关闭恢复与零播放，仍不等于真实鼠标送达或真实系统设置回退。

2026-11-16 界面动效补全：原样链接 `VisibilityReveal`，覆盖首次可见不播、收起再显示淡入、收起打断、
关应用/系统动画、无 XamlRoot、元素自有 ScaleTransform 不抢占、关闭附加属性退订。
媒体库、订阅墙、空态与详情剧集列表的接线依赖实际 XAML 绑定，仍需最终整合探针与截图；
本专项只证明时间轴、值与订阅生命周期。当前 **87/87、0 失败、0 跳过**。

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.UiMotionTests/Momoka.UiMotionTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.UiMotionTests/Momoka.UiMotionTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.UiMotionTests/Momoka.UiMotionTests.csproj -c Release --no-build --no-restore
```
