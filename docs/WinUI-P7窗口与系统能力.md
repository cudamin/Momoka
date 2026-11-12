# P7 窗口、激活与系统能力研究

日期：2026-11-13。研究代码基于 `29e9188` 及 P6 `b11174f`，独立树 `C:\Users\89400\.codex\worktrees\winui-p6-input-20261113\Momoka`。本阶段保持当前 HWND/XAML 岛主宿主、非打包x64和稳定拆分SDK。D04主宿主迁移、D05桌面通知与D06部署变化尚未决定，不因完成研究自动采用。

## 当前责任与官方源码

主窗口 `HostWindow.ConfigureTitleBar` 已通过 AppWindow.GetFromWindowId 获取官方窗口和 TitleBar，同时用 InputNonClientPointerSource 声明命中区域。该能力不是待接入项目。主宿主仍负责 CreateWindowEx、DesktopWindowXamlSource、原生WM消息、播放全屏/任务栏、客户区冻结/恢复、键盘兜底与释放；集成视频在页面Composition树，独占视频由mpv顶层窗口负责。`WM_DPICHANGED`采用系统suggested rect并通知几何变化。

`SettingsWindow` 已是 framework Window。原生标题栏使用SetTitleBar，关闭按钮取消并隐藏，缓存页面与草稿重开保留；真正Close只在Shell shutdown。主树P6/现有SettingsLifetime覆盖该生命周期的一部分，不能用研究窗的成功代替实际设置或播放窗口。

Shell csproj 顶部仍有早期「framework Window试过并排除、岛永久不透明」注释，比当前HostWindow注释更强；没有对应当前Composition视频证据，不能把它当平台硬限制。主树整合时应同步成当前事实与研究边界。

固定官方参考：WindowsAppSDK-Samples `128d55b5df1c0de188314a95db69244073ad3245`，已读 `Samples/Windowing/cs/cs-winui/WindowBasicsPage.xaml.cs`、`MainWindow.xaml.cs`，以及 `Samples/AppLifecycle/Instancing/cs/cs-winforms-unpackaged/CsWinFormsInstancing/Program.cs`。窗口示例分别用Resize/ResizeClient，样例MainWindow也将DIP乘GetDpiForWindow缩放后设置物理窗口大小。Instancing示例读取AppActivationArguments，FindOrRegisterForKey并以RedirectActivationToAsync重定向，Activated处理额外启动；示例文件关联注册是其产品流程，不为Momoka新增该功能。

Momoka目前 `Program` 使用版本化命名mutex/event维护单实例，`App` 启动ActivationListener后将激活投递UI队列；关闭先标记停止，排队旧回调不再激活，工作线程负责释放句柄。已读其完整实现及测试入口。当前只需普通启动唤醒，没有文件/协议激活产品入口；引入AppLifecycle不能仅以API数量算收益。本批保持当前路径，实际重复启动与关闭/重开仍须最终运行核验。

`Platform/StartupRegistration` 在非打包身份使用当前用户Run与StartupApproved，读回真实状态并确认写入结果，失败不冒充成功；打包身份隐藏该入口。接口便于匿名测试，研究不读取或改用户注册表。服务器通知页与InfoBar仍不算Windows桌面通知；无D05场景时不新增注册、后台或通知。

## 隔离Window/AppWindow兼容性样例

新增 `Windowing/FrameworkWindowResearch.cs` 仅用于匿名研究，产品正常启动不调用。原生Window包含一个TextBox和Button，关闭取消并隐藏，研究Dispose才真关闭并解除所有事件。隐藏anchor延长framework应用寿命，整轮probe结束统一关闭anchor；不会切主宿主。

`ShellNavigationProbe.WindowResearch.cs` 对现HostWindow和framework Window分别请求600/760/1100 DIP客户区，记录实际DIP、物理像素、RasterizationScale与AppWindow变化，读取原生TextBox peer名称/角色和两控件键盘Focus，验证Hide/Show/WmClose取消/草稿保留/最终Closed与内容释放。正常路径保存 `window-host-research-760-screen.png` 和 `window-host-research.json`；注入异常路径保存 `window-host-research-failure.json`，必须观察预定异常与finally清理后才通过。

检查台按钮为 `InspectWindowHostResearch` 和 `InspectWindowHostFailure`，以 `--probe-shell inspect --screen 1` 开启，运行两项后用InspectDone结束；该入口只存在于原有离线探针，不新增CLI开关，不访问服务/凭据或加载媒体。

## 验证与结论边界

独立 `Momoka.WindowResearchTests` 原样链接研究生命周期，6/6通过、0失败、0跳过；SDK10.0.400 Release编译0警告0错误。首次边界错用了EventHandler导致2错误/2警告，修成对应强类型事件后通过。首次Shell研究编译的集合类型错误修正为framework Window存留集合，空peer改为明确拒绝；随后Shell编译0警告0错误，最终微调再验证。新增专项已登记统一regression清单，runner通过当前新专项6/6；相关工具清单自测22/22通过。未用旧编译产物冒充失败后成功。

真实正常与注入失败两项仍由主协调会话同轮串行运行，本子任务未操作桌面。即使二者通过，也只证明匿名基础控件窗口尺寸/焦点/关闭缓存行为。mpv交换链合成、resize/fullscreen视频像素、HDR、集成/独占以及内置/外部后端、跨不同缩放显示器、Narrator和OS激活路径仍未覆盖。D04收益需由这些证据支持并明确选择后才实施，不能写成已迁移。

部署维持现有PRI合并、native载荷/许可和x64发布路线；最后交付件验证及快捷方式由P8主树会话完成。没有新通知、MSIX迁移、运行库升级、远端推送或真实注册表写入。
