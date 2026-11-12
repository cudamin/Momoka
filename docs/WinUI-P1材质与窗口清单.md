# WinUI P1：材质与窗口清单

## 1. 本页范围与证据版本

本页记录 2026-11-12 的只读源码盘点，冻结 Momoka 提交为 `4e0394acbb8ee5b03a39e2078736d40254e8cb15`。盘点覆盖宿主、窗口、页面底材、卡片、菜单、对话框及播放叠层的实际 API、覆盖层和清理责任。源码中的配置、支持检查和对象存在不等于实屏材质生效，也不证明性能、功耗、高对比度或资源释放已经通过。

应用运行、匿名数据、布局及性能采样由本阶段基线记录另行汇总。本页不把历史探针通过数当作本轮通过数，没有启动界面、修改 Windows 设置、读取真实账号配置或执行媒体播放。

规则入口为 [CLAUDE「WinUI 官方优先与偏离确认」](../CLAUDE.md#winui-官方优先与偏离确认)，方法见 [材质指南](../.claude/skills/momoka-winui-shell/references/materials.md)、[上游源码地图](../.claude/skills/momoka-winui-shell/references/gallery-source-map.md)及 [WinUI 验证矩阵](../.claude/skills/momoka-verification/references/winui-validation.md)。本页登记事实和缺口，不改变既有产品选择，也不批准 P2–P8 的待决定方案。

### 1.1 证据分类

| 标记 | 含义 | 在本页如何使用 |
| --- | --- | --- |
| 源码事实 | 冻结提交中的实际分支、资源、目标和调用 | 给出文件、符号、行号及适用条件；只描述实现，不代替运行结果 |
| 硬限制 | 当前 API、版本或合成目标确实不支持 | 须有当前包合同、实现或实测，不把历史故障注释写成全平台禁令 |
| 官方建议 | Microsoft 对用途、层次或生命周期的建议 | 与技术支持分开；冲突按 CLAUDE 说明取舍后确认 |
| 支持选项 | 官方 API 支持的不同配置 | Mica Alt、局部 SystemBackdropElement、controller 参数、非打包均不因非默认而自动违规 |
| 产品选择 | Momoka 已有外观、交互或维护决定 | 保留确认范围；要改变现有选择，按涉及的实际差异处理 |
| 待核实 | 缺少运行读数、平台覆盖或版本证据 | 不写成已发生缺陷，不作为放宽基线或批量重写的理由 |

### 1.2 当前依赖与上游版本

[Shell 项目文件](../src/Momoka.Shell/Momoka.Shell.csproj) 的事实为：WinUI 子包 `2.3.9`、InteractiveExperiences `2.1.9`、Runtime `[2.5.1]`，目标 `net10.0-windows10.0.19041.0`、非打包、`win-x64`。各子包版本不能互相替代，也不能把子包号直接当 Windows App SDK 元包号。[global.json](../global.json) 要求 .NET SDK `10.0.400`，以其滚动策略决定实际 SDK。

本轮读取 WinUI Gallery 固定提交 `6de91feccadfbb0a796b71caedead5a2284f730e`。其 `standalone.props` 第 5 行请求 `2.6.6-experimental`；只参考具体实现，不据此升级 Momoka 或照搬实验 API。

当前 InteractiveExperiences `2.1.9` 包的 `lib/net6.0-windows10.0.18362.0/Microsoft.InteractiveExperiences.Projection.xml` 第 7109、7254 行分别说明 DesktopAcrylicController、MicaController 的 `AddSystemBackdropTarget`：能建立材质返回 true，否则返回 false。本页两个忽略回执的发现有当前包合同依据，不依赖另一个 SDK 版本推断。

### 1.3 P0 之后已经改变的现状

| 同期变更 | 冻结源码现状 | 对后续计划的影响 |
| --- | --- | --- |
| 设置窗口首次 Mica 改造 `0b24d0e` 及后续统一画布 | 目前整窗 Mica Base＋一支 `0x99` 画布；标题与分类栏不再单独铺局部 Mica Alt | 不按旧截图再新增标题卡、类别卡或整栏 Mica Alt；以当前 [SettingsWindow](../src/Momoka.Shell/Views/SettingsWindow.cs) 与 [SettingsPage](../src/Momoka.Shell/Views/SettingsPage.xaml) 为基线 |
| 主窗口云母采样修复 `8cdda6d`，合并/交付记录 `0a29e9e`、`4e0394a` | 主岛改用 `IslandMicaBackdrop`，默认框架配置仍复用，激活状态由宿主 HWND 提供 | 不把主岛当尚未使用 Mica；P2 核验新 controller 的失败、恢复、主题和清理 |
| 简洁详情页面底统一 | 媒体详情及 MoviePilot 订阅详情已去掉整页局部 Mica Alt，页面底回到 Shell 画布；头部有界卡面 Mica 保留 | 区分整页底与局部卡面，不能因移除整页材质而删除所有 SystemBackdropElement |

## 2. 窗口、宿主与 XamlRoot

下文 `R-main` 表示主窗口 XAML 岛的 XamlRoot，`R-settings`、`R-versions` 分别表示两个 framework Window 的独立 XamlRoot。菜单从某页面打开，只能说明所属根；它的系统材质合成 target 仍须按实际 Popup/presenter 单独登记。不能认为主窗口的 backdrop 会给另一 HWND 提供底材。

| 窗口/宿主 | 实际实现、材质与范围 | 状态和释放责任 | 分类及验证边界 |
| --- | --- | --- | --- |
| 主窗口 | [HostWindow.cs](../src/Momoka.Shell/Windowing/HostWindow.cs)，类第 73 行；Win32 HWND＋DesktopWindowXamlSource，岛创建第 2928 行。`ApplyBackdrop` 第 3059 行挂 `IslandMicaBackdrop`；controller 为 Mica Base。`ApplyWindowsChrome` 第 2541 行另请求原生框架 DWM Mica | `WM_ACTIVATE` 第 3257 行发送 FocusChanged；`OnBackdropFocusChanged` 第 3097 行更新配置。`Dispose` 第 3484 行释放 source，再销毁 HWND；controller 由目标断开移除并释放 | 长期 Mica 是官方建议；自定义激活桥接是当前实现选择。原生 frame 和 client target 不同，不能只凭两个 API 就断言双重模糊或零成本 |
| 设置窗口 | [SettingsWindow.cs](../src/Momoka.Shell/Views/SettingsWindow.cs)，`TryCreate` 第 141 行；framework Window，`ApplyBackdrop` 第 341 行设 MicaBackdrop Base。根 Grid 使用唯一 `_canvas`，支持时 alpha `0x99` | 默认非模态、无 owner、缓存重用；X 的 `OnClosing` 第 290 行取消关闭并隐藏，释放当前页活动；最终 `Close` 第 269 行退订 ThemeHost 并真正关窗 | 长期 Mica符合用途；0x99、空白标题、缓存隐藏是产品选择。`_mica` 是初始化标记，不能代表当前 controller Active/Fallback |
| MoviePilot/AniBT 搜索版本窗口 | [MoviePilotWindow.cs](../src/Momoka.Shell/Views/MoviePilotWindow.cs)，`TryCreate` 第 85 行；framework Window，没有设置 SystemBackdrop。根和版本视图用 WindowFill，标题栏为不透明主题窗口色 | `Show` 重新发起当前搜索；`Hide` 第 165 行及 OnClosing 第 196 行取消请求并隐藏。`Close` 第 178 行退订主题、释放内容、关窗 | 当前实色长期窗是产品现状，Mica 是可选一致性方案；不存在必须先修复的 API 违规 |
| 播放尺寸、起停及退场遮罩 | [VideoFrameOverlay.cs](../src/Momoka.Shell/Windowing/VideoFrameOverlay.cs)，构造第 56 行；临时 layered/no-activate/tool Win32 HWND，无 XamlRoot，画缓存帧或纯色，整体 alpha 可渐隐 | PlayerPage 的 ClientRect/Transition/Exit 持有各自遮罩；Dispose 第 299 行销毁目标及资源 | 这是应用帧承接效果，不是系统材质。不得将它的截图或 opacity 变化当 Mica/Acrylic 验收 |
| 独占 mpv/uosc 窗口 | [播放技能](../.claude/skills/momoka-playback/SKILL.md)及 Core/播放后端界定：mpv 自建自管顶层窗口，不由上面几个 XAML 根提供材质 | 播放后端、独占换片和 Lua/uosc 各自管理 | WinUI 主岛或集成本地探针不覆盖此窗口；本轮没有启动它 |

设置窗口类注释第 18 行仍称“应用唯一的 framework Window”，但 MoviePilotWindow 同样创建 Window。SettingsWindow、MoviePilotWindow 和 HostWindow 部分注释仍沿用“主窗口下有 mpv 子窗口”的旧模型；当前 HostWindow 第 3100 行明确集成视频位于 CompositionVideoTarget 的视觉树，独占视频由 mpv 顶层窗口管理。这些是相关批次应修正的注释债，不能拿旧注释作为更换宿主的依据。

## 3. 逐表面材质与清理责任

本表描述当前产品。详细选材原则与不能做的组合仍集中在材质指南；这里的“待核实”不是许可要求，正常授权且保持行为的故障修复可直接按规则执行。

| 表面 | 根与存活时间 | 底材、覆盖资源及实际范围 | 更新/释放所有者 | 分类和 P1 核查点 |
| --- | --- | --- | --- | --- |
| 主岛浏览基底 | R-main，长期 | `HostWindow.ApplyBackdrop → IslandMicaBackdrop → MicaController(Base)`；无材质时下方是固定 BaseBrush `#16181C` | HostWindow 管浏览/播放意图与激活；SystemBackdrop 连接/断开管理 controller | 长期 Mica 官方建议；失败回执及诊断字段见第 4 节 |
| Shell 主画布 | R-main，长期 | [ShellPage.xaml](../src/Momoka.Shell/Views/ShellPage.xaml) 第 446 行 ContentHost.Background＝EgCanvasBrush；[Palette](../src/Momoka.Shell/Theme/Palette.xaml) 第 269 行 Window 色＋0x99 | [ThemeHost](../src/Momoka.Shell/Theme/ThemeHost.cs) 原地改同一画刷；HighContrast 有 EgCanvasBrush 系统色映射 | 半透明 SolidColorBrush 本身不是 Mica。0x99 是项目取舍，不是微软标准 |
| 标题与面包屑 | R-main，长期 | Shell 相关根/条透明，露出页面或主画布；caption colors及命中区域由 HostWindow 管 | HostWindow ConfigureTitleBar、PaintCaption、ThemeHost.Changed | 实际显示/拖动/按钮/DPI须运行确认；不能让装饰层覆盖命中区 |
| 主侧栏 | R-main，长期且可折叠 | ShellPage 第 516 行 SidebarHost 用 LayerFillColorAltBrush＝Surface＋0x80；里面是 NavigationView | ThemeHost 刷新色；ShellPage 调尺寸、显示模式和选中项 | 色层，不是 Desktop Acrylic；高对比实际解析待核实 |
| 主页面根 | R-main，按页面导航 | [HomePage](../src/Momoka.Shell/Views/HomePage.xaml)、[LibraryPage](../src/Momoka.Shell/Views/LibraryPage.xaml)、[MusicLibraryPage](../src/Momoka.Shell/Views/MusicLibraryPage.xaml)、[SettingsPage](../src/Momoka.Shell/Views/SettingsPage.xaml)、[SignInPage](../src/Momoka.Shell/Views/SignInPage.xaml) 等根透明，统一露 ContentHost | 页面按各自 Attach/Release/导航所有权管理；底色由 Shell 一次画 | 已有画布政策；再给页面铺同一 alpha 层会改变最终颜色，不机械加层 |
| 首页轮播与图上命令 | R-main，首页存活 | [HomeBanner.xaml](../src/Momoka.Shell/Views/HomeBanner.xaml) 图像＋scrim/渐变；第 37/43/49 行翻页键为三态 AcrylicBrush。主详情键还有自身局部色层 | HomeBanner Loaded/Unloaded 对称订阅主题、停计时并释放图片；按当前图重算前景 | 图像、阅读罩及应用内 Acrylic必须分开；不同按钮背景不自动统一 |
| 海报、人物与集卡 | R-main，按容器回收 | [PosterCard](../src/Momoka.Shell/Views/PosterCard.xaml) 用图、EgSurfaceBrush、scrim及选择层；播放键引用 EgPlayGlass。EpisodeRow以图和局部色层呈现 | CardItem与实际控件共同管理图片/回收；Loaded/Unloaded和item身份必须一起核对 | 未见逐海报 Mica controller；图像保真、亮/暗/缺图可读性待实屏 |
| MoviePilot订阅墙卡 | R-main，按墙容器回收 | [MoviePilotSubscriptionWallCard.xaml](../src/Momoka.Shell/Views/MoviePilotSubscriptionWallCard.xaml) 第 15 行 EgSurfaceAlt底，背景图＋EgScrim，第 26 行选择描边 | WallCard与ViewModel持有图片/绑定；墙视图管理回收与布局 | 图像＋色层，不是系统材质；近期图标/年份布局按冻结源码 |
| 媒体库工具区和过滤卡 | R-main，页面存活 | LibraryPage第339/785/831行等使用 CardBackgroundFillColorDefaultBrush；FnNasLibrariesPage也引用这个资源 | Palette/ThemeHost全局资源更新，页面负责实际显示 | **该键被项目覆盖成应用内 AcrylicBrush**；不能称原生默认实色或直接批量替换 |
| 媒体库树内筛选/设置浮层 | R-main，短暂叠层 | [FilterPanel](../src/Momoka.Shell/Views/FilterPanel.xaml) 第248行和 [LibrarySettingsPanel](../src/Momoka.Shell/Views/LibrarySettingsPanel.xaml) 第18行 LibraryDialogSurface＝不透明 LayerFillDefault；LibraryPage第1173/1200行外部scrim按钮 | LibraryPage持有面板/草稿、动效及退出；面板外按下取消，不能点穿 | 浮层表面实色是既有产品选择；内部卡片/按钮材质不因此同类 |
| 简洁媒体详情页面底 | R-main，页面存活 | [DetailPage.xaml](../src/Momoka.Shell/Views/DetailPage.xaml) 第198行起说明整页SystemBackdropElement已移除；底回Shell画布，正文/信息区用MicaLayerSource主题层 | DetailPage的PlainMode、SettleShowcase和PaintBackdropReading按模式更新 | 旧整页Mica Alt方案已失效；改回需说明可见差异 |
| 简洁媒体详情头部卡面 | R-main，有界材质target | DetailPage第337/1069行HeroMicaTop/Bottom；[Showcase.cs](../src/Momoka.Shell/Views/DetailPage.Showcase.cs) 第29行ApplyPlainMica，仅Plain＋appearance active＋HeroCardShown才挂Mica Base | [DetailMica](../src/Momoka.Shell/Views/DetailMica.cs)仅配置；StopAppearance/ReleasePlainMica在模式/离页清空SystemBackdrop | SystemBackdropElement是支持选项；对象挂上不证明目标当前采样和高对比正确 |
| 沉浸媒体详情图片底 | R-main，页面存活 | [DetailPage.Backdrop.cs](../src/Momoka.Shell/Views/DetailPage.Backdrop.cs) 第31行PaintBackdropFade按类型/宽度规则用AcrylicBrush；窄窗使用既有40%阅读罩；高对比给系统实色 | DetailPage订阅模式、主题与图变化，并以系统watch更新；离页StopAppearance | 宽窗应用内Acrylic是既有选择；不恢复高斯采样链，不给窄窗静默加回材质 |
| 媒体详情正文卡 | R-main，页面存活 | `_panelFill`从CardMaterialSource复制Tint/Fallback/Luminosity；沉浸时Opacity＝0；BodyPaper根据模式透出或铺纸面 | PrepareShowcase/RefreshPanelBrushes/SettleShowcase；ThemeResource替换及主题变化重算 | 同一Acrylic对象在Opacity＝0时不等于正在模糊；层次和成本按实际状态量测 |
| 详情按钮和闭合选择框 | R-main，页面存活 | [ControlBackgroundBrushes](../src/Momoka.Shell/Views/ControlBackgroundBrushes.cs) 每控件复制四态brush；沉浸按UI设置选Acrylic/色层；简洁ApplyFixedSurface强制fallback固定面 | DetailPage.Appearance按偏好、主题、阅读采样及系统状态更新 | AlwaysUseFallback下虽然类型是AcrylicBrush，视觉实为色层；非默认参数不自动触发确认 |
| 详情下拉展开面 | R-main来源Popup，短暂 | [EgPicker.cs](../src/Momoka.Shell/Views/EgPicker.cs) 第99行OnApplyTemplate后设ShouldConstrainToRootBounds=true；展开面由Palette应用内Acrylic绘制；第124行撑宽/封顶 | DropDownClosed第83行恢复宽度并解除展开内容订阅；使用框架模板部件 | 支持配置＋已有例外；受根边界约束，需短高/边缘/键盘验证，不扩大到全应用下拉 |
| 详情更多及媒体子菜单 | R-main所属根，各菜单target，短暂 | [DetailMenuBackdrop](../src/Momoka.Shell/Views/DetailMenuBackdrop.cs) 第15行创建DesktopAcrylicController Thin；Tint=.65，Luminosity=.45，Tint/Fallback＝SurfaceAlt；[Styles](../src/Momoka.Shell/Theme/Styles.xaml) 第21行用透明底及原生基样式 | 各target进入dictionary，断开第54行Remove＋Dispose；打开期间主题同步有缺口 | 短暂系统Acrylic符合用途；参数为支持选项。false回执、初始化异常和换色见第5节 |
| 普通菜单和普通Flyout | 各所属根，短暂 | Palette第172–175行覆写MenuFlyoutPresenterBackground/FlyoutPresenterBackground＝EgSurfaceBrush；部分场景有局部presenter样式 | WinUI Popup/presenter及页面命令生命周期 | 实色底是产品选择；原生模板仍有何Backdrop、是否被实色盖住需运行检查，不能据资源名断言无controller |
| 模态ContentDialog及遮罩 | 各所属根，短暂 | Styles第202行EgDialogSurfaceStyle＝不透明EgSurfaceBrush；库管理第213行用LayerFillDefault；ContentDialogTopOverlay是独立表面，SmokeLayer由框架移入另一Popup | [DialogDismiss](../src/Momoka.Shell/Views/DialogDismiss.cs) Opened寻找真实遮罩，Closed退订；Closing保护仍生效 | 点外取消是项目明确要求；对话框、遮罩、内部弹层分开验证，不让取消执行保存或点穿 |
| 设置整体、标题与类别栏 | R-settings，缓存长期窗 | SettingsWindow第197行根Grid唯一canvas；Frame/32DIP标题行透明；最新类别栏没有SystemBackdropElement | SettingsWindow ThemeHost.Changed刷新canvas/caption；页面活动票、草稿和缓存由原生命周期管理 | 固定0x99、空白标题和隐藏保活为产品选择；手造canvas高对比待核实 |
| 设置卡片及输入 | R-settings；创建失败时R-main | SettingsPage第1730行card资源实际为应用内Acrylic；输入控件由 [SettingsInputs](../src/Momoka.Shell/Theme/SettingsInputs.xaml)及相关主题体系覆盖状态 | 页面行集合、控件资源和ThemeHost更新；隐藏需保留恢复入口及偏好 | 整窗Mica不替代输入、卡片、焦点、高对比验证；不把样例0.8写成推荐默认 |
| 音乐页面及Shell音乐栏 | R-main，页面＋Shell长期栏 | MusicLibraryPage根透明，辅助卡用card资源；[MusicPlayerBar.xaml](../src/Momoka.Shell/Views/MusicPlayerBar.xaml)第19行Surface用EgFrostBrush | ThemeResource跟随，不持独立材质controller；音乐VM与Shell管理栏状态 | 音乐栏是应用内Acrylic，不是Mica或桌面实时透视；分页/性能见阶段基线 |
| 集成视频图像与黑边 | R-main，播放存活 | [CompositionVideoTarget](../src/Momoka.Shell/Windowing/CompositionVideoTarget.cs)在Player视觉树合成mpv交换链；HostWindow播放时摘浏览材质 | 播放后端与Player管理交换链/视觉；窗口只控制浏览材质意图 | 当前没有岛下mpv子HWND；没有根据VideoVisible读值而真起播的源码链 |
| 播放控制条、OSD、时间线 | R-main，播放存活 | [PlayerPage.xaml](../src/Momoka.Shell/Views/PlayerPage.xaml)第119行起PlayerGlass等主要是SolidColorBrush；[TimelineMaterial](../src/Momoka.Shell/Views/PlayerPage.TimelineMaterial.cs)按高对比/AdvancedEffects选系统实色或低alpha背景 | PlayerPalette/PlayerPage.PaintPalette；时间线按现有tick更新系统状态；旧视频取样模糊链退役 | Glass/Acrylic旧名字不代表实际API；XAML Acrylic不可承诺采mpv视频 |
| 播放菜单及倍速浮层 | 播放所属根，各Popuptarget，短暂 | PlayerMenuPresenterStyle基于原生DefaultMenuFlyoutPresenterStyle且背景透明；第157行PlayerAcrylicBackdrop供倍速Flyout显式使用 | 原生Backdrop/presenter及Player菜单生命周期 | 系统Acrylic与视频上的普通色层不同；不得以集成菜单证明独占uosc同样呈现 |
| WebView管理控制台外围 | R-main外围，WebView独立合成内容 | [DashboardPage.xaml](../src/Momoka.Shell/Views/DashboardPage.xaml)外围为EgSurfaceBrush，网页内容由WebView管理 | Dashboard导航与trusted-origin/凭据边界 | CSS模糊、XAML材质和WebView像素不是同一证据；不承诺Acrylic可采网页 |
| InfoBar、TeachingTip、tooltip | 各所属根，按语义存活 | 复用对应原生控件和主题资源，错误恢复不能因材质或显隐消失 | 页面/VM管理通知与恢复操作；原生控件管理自身表面 | 不为了小浮层新增controller；本页没有逐tooltip实屏覆盖 |

## 4. HostWindow 状态链与原候选纠正

### 4.1 实际调用链

1. HostWindow第128行 `_useBackdrop` 默认true；第942行UseBackdrop setter先记录新值，再ApplyBackdrop，相同值直接返回。
2. 第964行VideoVisible getter返回 `!_useBackdrop`，setter反向写UseBackdrop。
3. [PlayerPage.Transition](../src/Momoka.Shell/Views/PlayerPage.Transition.cs) 第90/103/153行进入播放写VideoVisible=true；[PlayerPage.xaml.cs](../src/Momoka.Shell/Views/PlayerPage.xaml.cs) 第874/1085行退出/返回写false。
4. HostWindow第3071行按 `_useBackdrop` 挂同一IslandMicaBackdrop或将source.SystemBackdrop清空。
5. 外层设置SystemBackdrop异常，第3089行仍把 `_useBackdrop=false`；这里没有显式画当前主题的不透明底，露出的是类背景刷＋Shell画布。
6. [IslandMicaBackdrop](../src/Momoka.Shell/Windowing/IslandMicaBackdrop.cs) 第65行不支持时只记日志return，内部第89行catch也只释放新controller并记日志；这两条不会把HostWindow `_useBackdrop`改false。

所以 `_useBackdrop` 同时装有浏览/播放意图和一部分异常结果，却又不是成功挂材质或controller当前状态。`UseBackdrop=true`不代表Active；`VideoVisible=true`也不是实际视频像素存在的可靠读数。

全仓读者核对后，VideoVisible读值用于诊断/自检，未发现据它启动后端、跳转播放器或显藏视频的生产分支。P0候选应改为：

> 材质赋值失败会污染浏览/播放诊断及重试意图；尚未发现误起播链。需要失败注入核验残留target、重试和browse→video→browse。

不能写“已确认材质失败触发播放”。源码能确认字段耦合，不能确认某机正常初始化会走失败分支。

### 4.2 新岛controller的连接和主题

IslandMicaBackdrop第40行SetInputActive修改每个Entry.Configuration；第52行ApplyTheme按ThemeHost当前深浅更新configuration.Theme；第111行默认配置更新回调重新钉宿主输入状态。框架默认configuration负责其余主题/高对比机制，当前实现没有定制controller的Tint/Fallback/Luminosity默认值。

第84行AddSystemBackdropTarget的false只被写进日志，第86行仍保存Entry，第87行仍说“云母已挂上”。controller构造与GetDefaultSystemBackdropConfiguration处于局部try之前。局部catch仅Dispose，正常断开才Remove＋Dispose；初始化各阶段失败和重复连接的资源结果尚无本轮运行证据。

宿主起始输入状态按true处理，是第一帧产品策略；随后用WM_ACTIVATE。失焦、隐藏、minimize/restore及source重新挂接时的最终状态应真实验证，不能把这一初始值说成永久强制前台，也不能据源码注释保证所有XAML岛永远没有激活能力。

## 5. DetailMenuBackdrop 生命周期与主题缺口

当前详情菜单通过EgDetailMenuPresenterStyle用透明背景，Thin DesktopAcrylicController覆盖四项属性。TintOpacity=.65、LuminosityOpacity=.45是受支持参数，也是目前项目选择；不同于默认值不自动触发许可流程。

| 分支 | 当前源码事实 | 缺少的验证或处理 |
| --- | --- | --- |
| 连接 | 第18行仅连接时从ThemeHost读SurfaceAlt，19行构造controller，29行add target，30–33行存默认配置及dictionary | 无IsSupported守卫；false回执未分支；构造/属性初始化位于try之前 |
| 打开前换主题 | 下次连接会读取新主题色 | 现有探针能覆盖各主题重新开菜单，不证明同一个target在打开期间更新 |
| 菜单开着换主题 | 无ThemeHost.Changed订阅，无默认配置变化回调更新覆盖色 | 配置Theme变化不自动重涂已自定Tint/Fallback；相同深色模式不同Momoka色板尤其需要显式更新 |
| 部分初始化异常 | catch仅Dispose并重新throw | 对已add但尚未登记、dictionary部分写入、重复target的所有结果缺少真实测试；不能声称必然泄漏或已经安全 |
| 正常断开 | 第54行移除controller/config，RemoveSystemBackdropTarget后Dispose | 正常路径有释放责任；反复打开、子菜单、多target和失败阶段需要独立计数证据 |

[ShellNavigationProbe.DetailCustomization.cs](../src/Momoka.Shell/ShellNavigationProbe.DetailCustomization.cs) 第992行CheckDetailMenuAcrylicAsync检查主/子菜单样式、透明presenter背景、UsesCardColor、MaterialState及实屏；第1002行每次重新打开。它没有菜单保持打开时换主题、false回执或初始化阶段异常注入。UsesCardColor检验颜色/参数，不能独立证明桌面模糊Active；原探针也明确Fallback不能算透明成功。

## 6. 回退与高对比的实际边界

| 场景 | 当前实现 | 可得结论与缺口 |
| --- | --- | --- |
| 主岛不支持Mica | IslandMicaBackdrop日志后不建controller；HostWindow仍可能UseBackdrop=true；底为BaseBrush＋0x99画布 | 支持标记、意图、实际状态要分开。底与当前主题窗口色不完全相同，需实屏读数 |
| 主岛source赋值异常 | HostWindow将 `_useBackdrop=false`，不改画布为不透明主题色 | 会影响诊断/重试意图；没有发现启动视频链。是否残留旧target必须注入测 |
| 设置初始化不支持/赋值抛异常 | 初始化mica=false选择不透明Window色；catch将 `_mica=false`，canvas改不透明Window色 | 源码证明这两条主动回退路径，尚未证明真实环境走过 |
| 系统关闭透明、RDP或其他自动fallback | controller/框架有系统策略；SettingsWindow没有因此主动重写canvas的分支 | `_mica`及BackdropState是初始化标记，不是系统当前状态。不能把“系统变实色”注释当应用canvas确实更新 |
| 设置高对比 | 子页面角色有HighContrast资源；根canvas为手造SolidColorBrush，PaintCaption只读取ThemeHost当前颜色，没有AccessibilitySettings观察 | 整窗canvas/caption是否符合系统角色待实测；子卡正确不证明窗口正确 |
| 主侧栏高对比 | Dark/Light覆写LayerFillColorAltBrush为半透明色；HighContrast字典未映射此平台键 | 需读SidebarHost的实际brush、颜色、焦点及文字；不能仅凭缺键就判必然失败 |
| 主画布高对比 | Palette HighContrast明确映射EgCanvasBrush为SystemColorWindowColor | 有正确资源候选，但是否解析、生效及覆盖仍要实际运行 |
| 详情按钮、图层高对比 | Appearance/Backdrop代码读系统背景/前景并选实色，系统watch轮询变化 | 属性分支不是视觉覆盖；所有文字、焦点、菜单及读取时序仍需验收 |
| 省电Mica策略 | 较新材料页说省电不影响Mica，旧设计资料列回退条件 | 分别记录OS/API和实际结果；没有回退不能按旧描述直接认定bug，不承诺已节能 |

本轮没有改变用户Windows全局高对比、透明效果、壁纸或省电设置。真实系统矩阵使用已同意的环境或单独请求具体系统状态变更；假configuration只验证应用逻辑，不能替代真实系统策略。

## 7. 已有覆盖、优先缺口与 P2 单元

| 编号 | 真实文件/缺口 | 已有覆盖的边界 | P2优先验证与最小单元 |
| --- | --- | --- | --- |
| P1-M01 | HostWindow.UseBackdrop/VideoVisible及Island初始化结果耦合 | [MicaMaterialTests](../tests/Momoka.Tests/MicaMaterialTests.cs)检查资源、源码接线/默认值；SelfCheck读诊断 | 先对生产状态路径做false、构造、配置、add、set-configuration异常注入；明确意图/attach/系统State，不改变播放行为。验browse→video→browse、重试及0残留 |
| P1-M02 | IslandMicaBackdrop的add false仍存Entry、清理边界 | 当前包XML确认bool合同；源码有正常断开Remove＋Dispose | 验重复连接/断开、每阶段异常、支持性及重新挂source。只修已有目标配置，不迁移宿主 |
| P1-M03 | DetailMenuBackdrop开着换主题缺少覆盖色更新 | AcrylicControlsTests校验样式；DetailCustomization探针每主题重新打开 | 同一主/子target连续换五个存活色板，核Tint/Fallback和实屏；反复关闭重开计数；保留Thin/.65/.45现有值 |
| P1-M04 | Settings手造canvas/caption及主侧栏HighContrast | [ThemeTests](../tests/Momoka.Tests/ThemeTests.cs)、[DialogChromeTests](../tests/Momoka.Tests/DialogChromeTests.cs)、[SettingsLifetimeTests](../tests/Momoka.SettingsLifetimeTests/README.md)各有离线边界 | 先做实际系统矩阵，读取所有相关色、target状态和截图；根据证据补最小资源/观察修复，不能先恢复浅色主题 |
| P1-M05 | 根画布、卡片Acrylic、局部Mica层次 | MicaMaterialTests及 [DetailMica探针](../src/Momoka.Shell/ShellNavigationProbe.DetailMica.cs)有局部对象/释放/各主题截图入口 | 代表页面量目标与覆盖层，先核最新整页底移除；卡片层数、文字可读性和开销有实际基线后再决定调整 |
| P1-M06 | 设置/菜单状态标记被当视觉成功 | SettingsBackdrop、UsesCardColor和MaterialState提供部分读数 | 日志明确支持、赋值、add成功、controller状态及真正实屏各自含义；不把Fallback当透明成功 |
| P1-M07 | 注释残留旧宿主、约等于透明度公式和统一省电保证 | [材料指南](../.claude/skills/momoka-winui-shell/references/materials.md)已区分参数和合成证据 | 在对应代码批次定点改注释，保留历史适用条件；不是仅用改断言消除错误 |
| P1-M08 | MoviePilot/AniBT版本窗仍为实色长期窗 | [MoviePilotWindow](../src/Momoka.Shell/Views/MoviePilotWindow.cs)与相关搜索/窗口探针 | 后续可评估Mica一致性；当前不是违规。先明确是否有产品收益，不能顺带切owner、缓存行为或搜索语义 |

其他已有入口包括 [AppearanceOpacityTests](../tests/Momoka.AppearanceOpacityTests/README.md)、[SettingsProbeContractTests](../tests/Momoka.SettingsProbeContractTests/README.md)、[ShellNavigationProbe.Materials](../src/Momoka.Shell/ShellNavigationProbe.Materials.cs)、[ShellNavigationProbe.Music](../src/Momoka.Shell/ShellNavigationProbe.Music.cs)及输入主题专项。测试项目或源码存在只说明可以复用的边界；本页没有填写未来通过数。

建议顺序为：P2a初始化/状态与释放 → 菜单保持打开换色 → 真实系统回退/高对比 → 代表页面层次整理。普通故障修复、同范围已确认参数和等价原生资源调整不增加笼统审批。恢复浅色/系统主题、扩大材质范围、改变既有对话框/详情默认、恢复视频取样链、切宿主或新增通知，仍只针对真正涉及的D01–D07事项说明取舍；P1不一次要求用户回答全部。

## 8. 固定 Gallery 与官方读取记录

### 8.1 本轮实际读取的 Gallery 文件

以下路径相对固定Gallery根，SHA均为 `6de91feccadfbb0a796b71caedead5a2284f730e`。读取了XAML、code-behind及两个实际支撑窗口，不只读取显示片段。

| 文件/符号 | 实际模式 | 对Momoka的适用边界 |
| --- | --- | --- |
| `WinUIGallery/Samples/SystemBackdrops/SystemBackdropsPage.xaml(.cs)` | 入口创建BuiltIn、MicaController或DesktopAcrylicController样例窗，并分别说明Kind | 样例窗不是Momoka手造HWND；文案不作为包兼容证明 |
| `WinUIGallery/SampleSupport/SamplePages/SampleBuiltInSystemBackdropsWindow.xaml.cs:94`，TrySetMicaBackdrop | IsSupported后设Window.SystemBackdrop；切换先null；样例按Mica→Acrylic→无材质回退 | 可参考framework设置窗；回退顺序是样例选择，不是所有产品必需 |
| `WinUIGallery/SampleSupport/SamplePages/SampleSystemBackdropsWindow.xaml.cs:124/151`，TrySetMica/TrySetAcrylic | 检查支持、创建configuration与controller、add target；177/183行激活更新和关窗释放，主题显式重算 | 需要补生产错误与重复订阅边界；Gallery也没有在add返回值上做完整处理，不能照抄其疏漏 |
| `WinUIGallery/Samples/Acrylic/AcrylicPage.xaml:74`及`.xaml.cs` | 真实XAML底色图形之上用AcrylicInAppFillColorDefaultBrush；Tint、Fallback及Luminosity分别变化 | 适用于应用内采样；0.8展示值不是Momoka推荐默认，也不能移植UWP的BackgroundSource |
| `WinUIGallery/Samples/SystemBackdropElement/SystemBackdropElementPage.xaml:17`及`.xaml.cs` | 有界元素置内容后面，运行切Mica/Alt/Acrylic和圆角；58行说明app/OS主题更新机制 | 局部API受支持，但任意父RequestedTheme不是可靠替代；Momoka色板、岛激活、剪裁和释放需自己的验证 |

### 8.2 本轮官方网页与当前包合同

| 成功读取的官方来源 | 本轮核实内容 | 使用限制 |
| --- | --- | --- |
| [System backdrop implementation](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/system-backdrop-controller) | 多数app优先Window.SystemBackdrop；WindowsAppSDK2.0+有界SystemBackdropElement可与窗口材质共存；controller覆盖四项属性后主题对应值需应用手动更新 | HTTP只读成功；不能据当前网页保证旧包或手造宿主行为。旧地址本轮返回200，不写成不可达 |
| [Material API overview](https://learn.microsoft.com/en-us/windows/apps/develop/ui/materials) | AcrylicBrush仅应用内；关闭透明和高对比抑制材质；较新省电Mica说明与旧设计资料不同 | 系统实际策略按OS/API矩阵观察，不写全版本统一保证 |
| [MicaController.AddSystemBackdropTarget](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.systembackdrops.micacontroller.addsystembackdroptarget?view=windows-app-sdk-1.8) | false表示不能建立材质，调用线程需要DispatcherQueue | 网页选1.8；bool合同另由实际InteractiveExperiences2.1.9 XML核对 |
| 当前包DesktopAcrylic/Mica XML，第7109/7254行 | 两个AddSystemBackdropTarget都有true/false合同 | 这是当前包合同，尚未证明本机运行中发生false |

Mica/Acrylic设计页及DesktopAcrylic AddSystemBackdropTarget网页本轮HTTP遇到SSL失败，未声称成功重读；有关长期/短暂表面、缝隙与性能的设计建议沿用P0的固定来源和材料指南，新的冲突仍需正式核实。未读取页不能被列成新运行结论，也没有虚构web工具引用编号。

## 9. 同期主树增量：字幕与附加内容

本节单独审查主树提交 `07e991b4cb79fa1ca3e9a0373d817c9772d087a1`（2026-11-12），读取 `git diff 4e0394a..07e991b` 和该提交中的相关完整文件。第1–8节仍绑定 `4e0394a`，其中固定行号不改写成新提交的行号。本节是同期源码增量，新增字幕/花絮功能没有由本轮冻结 `4e0394a` 的原生探针执行，不能并入它的运行通过数或截图。

新增文件尚未合入本清单工作树时以仓库相对文字路径登记，避免建立当时不可达的文件链接。下表所有新增行号均属于 `07e991b`。

| 增量表面/路径与符号 | 根、材质及状态事实 | 清理责任与后续验证 |
| --- | --- | --- |
| 飞牛字幕管理 `src/Momoka.Shell/Views/FnNasSubtitleDialog.xaml`，第2行ContentDialog；`Theme/Styles.xaml`新增类型样式 | 归调用者页面XamlRoot。Styles只新增 `FnNasSubtitleDialog → EgLibraryDialogSurfaceStyle`，继承不透明LayerFillDefault及既有点外取消；第8行ContentDialogTopOverlay也用LayerFillDefault。没有新SystemBackdrop、AcrylicBrush或controller。标题关闭键透明，主体ScrollViewer含版本、轨道、搜索、上传及权限相关区域 | `FnNasSubtitleDialog.xaml.cs`第145–164行Loaded缓存根、订阅Changed并FitLayout，Unloaded退订；宽上限660且留根宽96，最大高根高减220。第74–83行Closing在写入中拒绝关闭，否则终止读/lifetime及删除草稿，Closed释放CTS；还需真实短高、点外、焦点、高对比与滚动验收 |
| 飞牛字幕确认区及结果/进度 | 同一dialog树内，第62行DeleteConfirmation是边框区域，没有额外弹窗或材质。第105行ServerFileArea按管理员能力显隐；忙/未知回执状态在原dialog显示恢复说明，避免新浮层。构造第58行RequestedTheme仅按当前深浅设置 | 对话框`SyncControls`按CanManage/CanWrite/IsUnconfirmed管理恢复入口，写入完成finally解锁；构造设主题不证明打开期间换色、系统高对比及辅助技术通过 |
| 飞牛字幕正式调用者 `Views/ItemCommands.FnNasSubtitles.cs`，FnNasSubtitlesAsync | 第74行给dialog显式设捕获的XamlRoot。Core使用的Current谓词只读受管状态，UI谓词另采样根/页面，防止后台线程碰XAML。没有独立应用窗口或材质target | 第55–58行订阅页面Unloaded、root.Changed、SourcesChanged、PermissionsChanged；第80–86行finally逐项退订。dialog.Touched或NeedsRefresh在有效访问范围内交父页面刷新；旧版本/账号/离页与写入未知债务需要该批自己的验证 |
| 飞牛本地字幕文件选择器 `Views/FnNasSubtitleFile.cs`，PickAsync第10行 | 系统FileOpenPicker是dialog之外的系统UI，不受Momoka画布材质提供。第17–21行由root.ContentIslandEnvironment.AppWindowId定位所属HWND并InitializeWithWindow。没有新WinUI Window或SystemBackdrop | 每个await后核isCurrent，读取流与DataReader按using释放；取消不提交，32MiB是本地资源限制。系统文件选择器是否正确归属、可取消及返回焦点不由源码证明 |
| Jellyfin字幕版本及上传区 `Views/SubtitleDialog.xaml`，第46行VersionRow、第54行UploadSection | 延用现有ContentDialog、EgLibraryDialogSurfaceStyle及第17行实色ContentDialogTopOverlay；版本多于一份才显示，第58行UploadDraft含语言/强制/听障输入。没有新增系统材质，原生输入的主题状态按现有资源解析 | `SubtitleDialog.xaml.cs`第106行Closing写入中拒绝关闭，其他关闭递增read generation；第189–212行保留Loaded/Unloaded根Changed及尺寸限制。`SubtitleDialog.Upload.cs`第47–72行换版本清上传草稿、重设输入、替换目标委托和可用性；读/选择回执用generation＋target守卫 |
| Jellyfin上传状态与正式调用者 `Views/SubtitleDialog.Upload.cs`、`Views/ItemCommands.Server.cs` | 上传区按CanUpload显隐，未知上传/修改回执保留ReadOnlyRecovery，原表展示恢复操作；服务器版本封装为SubtitleDialogTarget，没有新增材质层。ItemCommands第541行给dialog设root | 正式caller捕获owner/page的访问存活及session状态，订阅各服务AvailabilityChanged，ShowAsync的finally停止观察并退订，外层finally解除owner/page Unloaded；上传成功/未知回执保留父刷新义务。对应新增功能需独立测试和原生探针，不能用旧字幕管理结果替代 |
| Jellyfin本地选择器 `Views/ServerSubtitlePicker.cs`，PickAsync第12行 | FileOpenPicker以当前dialog根的AppWindowId初始化HWND，与飞牛选择器分别处理文件限制及服务语义；系统选择器自身不是EgDialogSurfaceStyle表面 | isCurrent在选择、读取各await后验证；流/reader按using释放，大小及读取期间文件变化分别拒绝。真实picker归属与返回输入仍待对应批次核验 |
| 花絮与特典按钮 `Views/DetailPage.xaml`，ColumnExtrasButton第875行、CompactExtrasButton第1297行 | 宽/窄动作区新增第八颗键，使用已有EgPillButtonStyle；PrepareAppearance原来枚举Button的路径因此包含它，不新增局部材质资源或controller。根画布、HeroMica、阅读罩和简洁/沉浸规则没有改 | 绑定ExtraVisibility，能力/异步状态变化后重算；新增按钮对密度、窄布局、焦点和可读性需要增量批次验证，固定4e基线中的七键不能冒称覆盖八键 |
| 花絮与特典选择/失败重试 `Views/DetailPage.Extras.cs`，ShowExtrasAsync第18行 | 第34行普通 `new MenuFlyout()`，未指定EgDetailMenuPresenterStyle或DetailMenuBackdrop；使用本页“普通菜单”路径，不能当详情Thin Acrylic菜单新增覆盖。加载、选项、失败提示及重试在同一菜单更新，真实选择可触发播放 | 第39–49行Closed清 `_extraMenu`、CloseExtras终止选择/读任务；重复打开等待旧菜单Closed；状态不再可用时第88行Hide；DetailPage.Release新增 `_extraMenu?.Hide()`。VM的CloseExtras/ResetExtras取消并Dispose当前读CTS；重试、离页、失权、快速打开及真实播放授权按该功能分批验证 |

本次增量没有修改 `Windowing/HostWindow.cs`、`Windowing/IslandMicaBackdrop.cs`、`Views/SettingsWindow.cs`、`Views/DetailMenuBackdrop.cs`、`Theme/Palette.xaml`、`Views/ControlBackgroundBrushes.cs` 或 `DetailPage.Appearance/Backdrop/Showcase`。因此主岛Mica Base、设置唯一画布、现有详情菜单controller的主题/false回执缺口及高对比候选判断不变。Styles只为新dialog接既有样式，未改变全局材质参数。

新增字幕dialog采用与既有库管理一致的不透明表面；新花絮MenuFlyout沿普通菜单而非局部Thin材质。新增surface必须登记自己的target、Root、输入、取消和清理，不能因为它们继承了样式就跳过原生验收。后续合入较新主树时，章节基线仍按上述版本分别阅读，不把源码增量审查当已完成当前程序验证。

## 10. 收尾与未覆盖项

本页只把冻结源码转换成可执行清单。真实高对比、关闭透明、省电、RDP、各缩放/显示器切换、GPU成本、反复打开的实际资源保留及独占uosc均没有由本页验收。P2先从现有缺口的最小生产路径补证据；“源码配置正确”“单测通过”“屏幕呈现”“性能改善”四种结果必须分别记录。
