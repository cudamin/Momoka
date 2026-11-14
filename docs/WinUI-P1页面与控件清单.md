# WinUI P1 页面、控件与布局源码清单

日期：2026-11-12。源码基线：`4e0394acbb8ee5b03a39e2078736d40254e8cb15`。以下路径均相对本仓库，行号对应这个固定提交；以后源码变化时重新核对行号、符号和调用链。

本文件是 P1 的历史只读源码盘点；原分阶段计划已于2026-11-14按用户要求删除，本报告不作为待执行清单。当前任务以[项目规则](../CLAUDE.md)、[Shell 技能](../.claude/skills/momoka-winui-shell/SKILL.md)和当前源码为准。本次没有通过修改产品来消除发现的问题，没有构建、启动 UI、读取真实设置或访问凭据。源码存在、源链接测试覆盖与真实 WinUI 运行是三种不同证据；运行结果由本阶段基线报告另行登记，本文件不填历史通过数。

## 1. 库存与证据范围

在 `src/Momoka.Shell/Views` 按 XAML 根元素枚举，实际有 **19 个 Page、31 个 UserControl**。`ShellPage`、`SignInPage`、`PlayerPage` 都是 UserControl。代码中的文件名带 `Page` 不代表继承 Page；纯代码控件、对话框和资源字典也不计入这两个数字。

| 清单 | 本文件覆盖 | 后续验证边界 |
| --- | --- | --- |
| Page | 19 个完整类型、主要内容与滚动所有者、导航与释放入口 | 逐页实际布局、绑定通知、焦点、返回与身份切换 |
| UserControl | 31 个完整类型、组合职责；重点控件进一步追踪 | 模板回收、输入语义、UIA、图像与事件释放 |
| 纯代码控件 | `EgPicker : ComboBox`、`WrapRow : Panel` 及相关布局/状态辅助 | 不把辅助类算作新增视觉控件；替换前证明等价行为 |
| 主题资源 | App 合并顺序、主题角色、字体几何、输入/操作/菜单局部覆盖 | 逐表面资源解析、首帧、主题变化、高对比度与回退 |
| Gallery | 固定提交中的 XAML、code-behind 及实际调用 helper | 样例不替代当前包/API 与生产生命周期验证 |

### 1.1 证据分类

- **已读实现事实**：本仓库上述固定提交；Gallery 固定 `6de91feccadfbb0a796b71caedead5a2284f730e`。
- **已有覆盖入口**：本文件只确认测试或探针的代码和覆盖方向，不声称本次已运行。
- **待运行候选**：由完整调用链发现的缺口；需匿名数据、真实控件及图片/请求/布局读数确认，不能仅凭静态结构宣布性能故障。
- **项目选择**：仅深色、侧栏模式、点外取消、影视按服务区分、音乐统一等沿用现有约定。改变范围按 CLAUDE 的确认流程处理。
- **官方建议待在线核对**：本次尝试读取 Microsoft Learn 的 ItemsRepeater、CommandBar、列表优化和焦点导航入口时遇到 SSL 失败，未取得页面内容。下文 Gallery 对照只作为已读实现证据，不包装为本次已核实的官方推荐或硬限制。

Gallery 的 `standalone.props` 使用 `WindowsAppSdkPackageVersion=2.6.6-experimental`。Momoka 的包版本以[Shell 项目](../src/Momoka.Shell/Momoka.Shell.csproj)为准，本次为 WinUI 子包 2.3.9、Runtime 2.5.1。不得把样例版本、特定 workaround 或新增 API 自动搬进项目。

## 2. 19 个 Page 完整清单

所有这些 Page 都实现 `IShellContent`。框架导航离页通过 `OnNavigatedFrom` 清理；外壳切账号/退出直接清 `ContentFrame.Content` 时，由[IShellContent.Release](../src/Momoka.Shell/Views/IShellContent.cs)和外壳 `ReleaseContent` 明确完成清理，不能认为赋值会自动走导航事件。

| 序号 | 类型与源码 | 主要内容和滚动所有者 | 导航、生命周期与后续验证 |
| --- | --- | --- | --- |
| 1 | [HomePage](../src/Momoka.Shell/Views/HomePage.xaml) | 根 `ScrollView Scroller` 竖滚；`ShelfRepeater` 的纵向 StackLayout 组织分区；每个 ShelfStrip 独立横滚 | code-behind 的 `OnNavigatedTo:1355`、`Release:1451`、`OnNavigatedFrom:1517`；Enabled 缓存，尝试同服务 reactivation/snapshot，后台刷新；返回保留 offset，释放取消读取、订阅和 fold 动画 |
| 2 | [LibraryPage](../src/Momoka.Shell/Views/LibraryPage.xaml) | 普通墙 `ItemsView CardWall` 使用内部 ScrollView；TableScroll 默认关闭滚动，表格路径另行配置；分组搜索、推荐、收藏、即将播出分别使用独立竖向 ScrollViewer | `OnNavigatedTo:520`、`Release:665`；Enabled 缓存，Attach 通知合并、锚点与来源守卫；影视按 Emby/Jellyfin/飞牛/Plex 分别验证，不能因共享类就合成一种服务体验 |
| 3 | [DetailPage](../src/Momoka.Shell/Views/DetailPage.xaml) | 根 ScrollView；横向 ShelfStrip；纵向集列表 ItemsRepeater + StackLayout | `OnNavigatedTo:1658`、`Release:1878`；首访/类型更正、PageWidth、图片迟到分别更新布局；电影/剧/季/集与简洁/沉浸分型；菜单、图片、请求归访问期 |
| 4 | [SettingsPage](../src/Momoka.Shell/Views/SettingsPage.xaml) | ≥860 DIP：左分类 ListView + 右 SettingsScroll；较窄时分类 ComboBox 在正文上方；右侧 HostedFrame 承载管理页 | `OnNavigatedTo:417`、`ReleaseHosted:789`；Required 缓存保留草稿和分类；每次打开新 activity，重读保存状态/设备/权限；Hide 清理不能依赖 Frame 离页 |
| 5 | [MusicLibraryPage](../src/Momoka.Shell/Views/MusicLibraryPage.xaml) | 曲目/收藏 ListView 自滚；专辑/艺术家/歌单为外层 WallScroll + MusicWall 内部 ScrollView | `OnNavigatedTo:31`、`Release:56`；Rows 稳定，LoadGeneration/scope/activity 拒绝旧结果；页面订阅 CurrentKey 并释放。分页、图片加载与两层 viewport 是本阶段重点候选 |
| 6 | [LocalLibraryPage](../src/Momoka.Shell/Views/LocalLibraryPage.xaml) | 有限 Grid 正文格中的 LocalLibraryEntries ListView | code-behind `OnNavigatedTo:26`、`Release:58`；按请求附加，离页 Cancel，封面容器 Unloaded 释放；长路径/目录、空态和返回焦点 |
| 7 | [LocalMediaPage](../src/Momoka.Shell/Views/LocalMediaPage.xaml) | LocalMediaScroll + 文件 ItemsRepeater/StackLayout | 独立请求与取消路径；大量文件时 realized、图片和焦点待运行；本地文件读取不等于可随意触发播放 |
| 8 | [MoviePilotSubscriptionsPage](../src/Momoka.Shell/Views/MoviePilotSubscriptionsPage.xaml) | 外层 ScrollViewer；共享 MoviePilotSubscriptionsView/WallView | `OnNavigatedTo:41`、`Release:88`；subscriptions 与列表 VM 各有属主；墙内部列表关闭竖滚，跟宿主滚动，需量 viewport |
| 9 | [MoviePilotSubscriptionPage](../src/Momoka.Shell/Views/MoviePilotSubscriptionPage.xaml) | DetailScroll；来源/明星/信息 repeater 与媒体季/版本子视图；错误态另有滚动面 | 订阅 VM 与媒体 VM 分开，访问期与释放不可合并猜测；简洁/沉浸、长标题、季列表、返回及控制台能力分别验证 |
| 10 | [ServersPage](../src/Momoka.Shell/Views/ServersPage.xaml) | 服务器/账号 ListView 与局部选项，部分列表显式关闭内部滚动 | 当前确认与登录/退出动作通过真实 VM；匿名夹具覆盖来源、长账号名、焦点、重新排序，不能读真实密钥来盘点 |
| 11 | [DiagnosticsPage](../src/Momoka.Shell/Views/DiagnosticsPage.xaml) | 原生 CommandBar、日志 ListView；诊断详情局部 ScrollView MaxHeight=440 | VM 已有日志唤醒合并与水位/队列属主；离页停止监视；输入与日志自动滚动、首标题、暂停/恢复待运行 |
| 12 | [DashboardPage](../src/Momoka.Shell/Views/DashboardPage.xaml) | 原生 CommandBar + WebView2，网页内容自滚 | ISessionScopedPage；NavigationId、请求代次和队列合并有专门契约；释放 WebView，不把隐藏窗口等同已释放浏览器 |
| 13 | [NotificationsPage](../src/Momoka.Shell/Views/NotificationsPage.xaml) | 原生 CommandBar；主 ScrollView 内含通知 ListView | ISessionScopedPage；`OnNavigatedTo:88`、`Release:109`。这是服务器通知管理，不是 Windows 桌面通知；匿名验证不能真正发送服务通知 |
| 14 | [FnNasLibrariesPage](../src/Momoka.Shell/Views/FnNasLibrariesPage.xaml) | 库 ListView、编辑器 ScrollView、目录选择 ListView Height=220 | ISessionScopedPage；`OnNavigatedTo:32`、`Release:87`，Unloaded 也释放；来源/权限撤销、对话框和晚响应核查 |
| 15 | [FnNasUsersPage](../src/Momoka.Shell/Views/FnNasUsersPage.xaml) | 用户 ListView、编辑器 ScrollView | ISessionScopedPage；来源与管理权限变化、草稿、离页/隐藏恢复；不会因重构自动授权真实用户写入 |
| 16 | [FnNasSettingsPage](../src/Momoka.Shell/Views/FnNasSettingsPage.xaml) | FnNasSettingsScroll | ISessionScopedPage；同服务/权限 lifetime，配置回执与错误态，不用隐藏失败代替反馈 |
| 17 | [FnNasTasksPage](../src/Momoka.Shell/Views/FnNasTasksPage.xaml) | FnNasTasksScroll | ISessionScopedPage；任务读取、能力显隐、来源变化及 Cancel；真实执行/终止不属于本只读基线 |
| 18 | [FnNasPreferencesPage](../src/Momoka.Shell/Views/FnNasPreferencesPage.xaml) | 主 ScrollView | ISessionScopedPage；`OnNavigatedTo:28`、`Release:37`；用户偏好/草稿、来源晚响应、重入 |
| 19 | [FnNasPasswordPage](../src/Momoka.Shell/Views/FnNasPasswordPage.xaml) | 主 ScrollView | ISessionScopedPage；密码身份与管理员权限是不同 lifetime，不能把所有飞牛页按一种权限退订；不访问真实密码 |

[ISessionScopedPage](../src/Momoka.Shell/Views/ISessionScopedPage.cs) 提供 `IsCurrentSession` 与 `SourceKey`，用于设置 HostedFrame 重核身份。管理页存在这个接口不证明每项晚响应都正确，仍需对应 VM/源链接测试和运行覆盖。

## 3. 31 个 UserControl 完整清单

表中“职责”来自类型/XAML 与调用点盘点。每项进一步改造仍需读取完整 code-behind、VM、服务及资源路径；本表不是把所有控件都批准重写。

| 序号 | 类型与源码 | 当前组合职责 |
| --- | --- | --- |
| 1 | [AlphaBar](../src/Momoka.Shell/Views/AlphaBar.xaml) | 名称排序的字母跳转与当前位置指示；是否显示由库/服务能力决定 |
| 2 | [AniBtResourceBrowser](../src/Momoka.Shell/Views/AniBtResourceBrowser.xaml) | AniBT 资源列表、过滤与预览操作 |
| 3 | [AniBtSearchPanel](../src/Momoka.Shell/Views/AniBtSearchPanel.xaml) | AniBT 搜索与资源承载、来源可用性/恢复 |
| 4 | [DanmakuPanel](../src/Momoka.Shell/Views/DanmakuPanel.xaml) | 播放层弹幕面板与当前播放 VM 交接 |
| 5 | [EpisodeRow](../src/Momoka.Shell/Views/EpisodeRow.xaml) | 单集行，图像、文字、当前项和可达的操作；容器/图片/焦点生命周期 |
| 6 | [FilterPanel](../src/Momoka.Shell/Views/FilterPanel.xaml) | 媒体库筛选组合；来源/分类/权限由调用者决定 |
| 7 | [HomeBanner](../src/Momoka.Shell/Views/HomeBanner.xaml) | 首页轮播、驻留时钟、图像与用户操作 |
| 8 | [HtmlColorPicker](../src/Momoka.Shell/Views/HtmlColorPicker.xaml) | 颜色输入与预览，解析/提交/焦点分离 |
| 9 | [LibrarySettingsPanel](../src/Momoka.Shell/Views/LibrarySettingsPanel.xaml) | 服务相关库显示偏好；改变立即回调，页面负责重查和持久化 |
| 10 | [MetadataListEditor](../src/Momoka.Shell/Views/MetadataListEditor.xaml) | 元数据列表字段编辑组合 |
| 11 | [MoviePilotMediaSeasonsView](../src/Momoka.Shell/Views/MoviePilotMediaSeasonsView.xaml) | MoviePilot 媒体季/集信息与来源可播放状态 |
| 12 | [MoviePilotResourceBrowser](../src/Momoka.Shell/Views/MoviePilotResourceBrowser.xaml) | MoviePilot 资源列表和选择操作 |
| 13 | [MoviePilotResourceFacetPicker](../src/Momoka.Shell/Views/MoviePilotResourceFacetPicker.xaml) | 单一资源 facet 选择，DependencyProperty 与属性订阅 |
| 14 | [MoviePilotResourcePanel](../src/Momoka.Shell/Views/MoviePilotResourcePanel.xaml) | 覆盖式资源面板、返回和确认对话框交接，避免两个 ContentDialog 抢同一根 |
| 15 | [MoviePilotSearchPanel](../src/Momoka.Shell/Views/MoviePilotSearchPanel.xaml) | 搜索输入、结果与资源覆盖面板 |
| 16 | [MoviePilotSearchProgressView](../src/Momoka.Shell/Views/MoviePilotSearchProgressView.xaml) | 搜索进度与过程反馈 |
| 17 | [MoviePilotSitePicker](../src/Momoka.Shell/Views/MoviePilotSitePicker.xaml) | 站点选择与对话框打开状态 |
| 18 | [MoviePilotSubscriptionEditor](../src/Momoka.Shell/Views/MoviePilotSubscriptionEditor.xaml) | 订阅草稿和规则编辑组合 |
| 19 | [MoviePilotSubscriptionPoster](../src/Momoka.Shell/Views/MoviePilotSubscriptionPoster.xaml) | 订阅图像、徽标、状态与操作 |
| 20 | [MoviePilotSubscriptionsView](../src/Momoka.Shell/Views/MoviePilotSubscriptionsView.xaml) | 订阅目录/货架共享视图 |
| 21 | [MoviePilotSubscriptionWallCard](../src/Momoka.Shell/Views/MoviePilotSubscriptionWallCard.xaml) | 管理墙中的单张订阅卡、状态与操作 |
| 22 | [MoviePilotSubscriptionWallView](../src/Momoka.Shell/Views/MoviePilotSubscriptionWallView.xaml) | 管理墙、筛选/排序/批量操作、ListView + ItemsWrapGrid；内部滚动关闭 |
| 23 | [MoviePilotVersionsView](../src/Momoka.Shell/Views/MoviePilotVersionsView.xaml) | 媒体版本信息与可用操作 |
| 24 | [MusicPlayerBar](../src/Momoka.Shell/Views/MusicPlayerBar.xaml) | 全来源共用底部音乐栏、传输键、进度、队列和音量 |
| 25 | [PlayerPage](../src/Momoka.Shell/Views/PlayerPage.xaml) | 集成播放 UI、面板与输入；独占 uosc 是另一路线 |
| 26 | [PosterCard](../src/Momoka.Shell/Views/PosterCard.xaml) | 海报/图像/角标/选择/悬停与焦点操作组合 |
| 27 | [ShelfHead](../src/Momoka.Shell/Views/ShelfHead.xaml) | 货架标题、读数、来源与跳转 |
| 28 | [ShelfStrip](../src/Momoka.Shell/Views/ShelfStrip.xaml) | 横向虚拟化、翻页、焦点/离屏目标和图像属主交接 |
| 29 | [ShellPage](../src/Momoka.Shell/Views/ShellPage.xaml) | 标题栏、侧栏、ContentFrame、登录/浏览/播放/音乐层与窗口交接 |
| 30 | [ShortcutCaptureBox](../src/Momoka.Shell/Views/ShortcutCaptureBox.xaml) | 快捷键输入与明确提交 |
| 31 | [SignInPage](../src/Momoka.Shell/Views/SignInPage.xaml) | 两步登录、来源/账号选择与输入反馈 |

### 3.1 重点控件已经存在的能力

| 控件或属主 | 精确实现位置 | 后续改造必须保留的行为 | 已有验证入口与新增验证 |
| --- | --- | --- | --- |
| ShelfStrip | [code-behind](../src/Momoka.Shell/Views/ShelfStrip.xaml.cs)：`OnElementPrepared:483`、`OnNoFocusCandidateFound:504`、`OnKeyDown:521`、`FocusItem:601`、`QueueFocusRetry:669` | 横向 ScrollView/StackLayout；PositionInSet/SizeOfSet；左右虚拟化目标 GetOrCreateElement；Home/End/PageUp/PageDown；定位后检查 Focus 回执；离树清重试/hover/动画 | [CardStripTests](../tests/Momoka.Tests/CardStripTests.cs)、[FocusWatchTests](../tests/Momoka.PageContractTests/FocusWatchTests.cs)、[LongSeries 探针](../src/Momoka.Shell/ShellNavigationProbe.LongSeries.cs)；补真实键盘跨边缘/返回/离页重入，区分逻辑调用与实际输入 |
| CardShelf/CardCollection | [CardShelf](../src/Momoka.Shell/ViewModels/CardShelf.cs)：`FillDeferred`、`Apply`、`Clear`；[CardCollection](../src/Momoka.Shell/ViewModels/CardCollection.cs)：`MaterializedCount:34`、`ResetDeferred:71` | 稳定集合、同身份/指纹复用；只访问才创建 CardItem；弃用图片与工厂；CardShelf 是数据状态，视觉虚拟化在 ShelfStrip | [DeferredCardsTests](../tests/Momoka.PageContractTests/DeferredCardsTests.cs)、LongSeries；新增运行读数分别记录 materialized 与 realized，不能混成一个“虚拟化率” |
| PosterCard/EpisodeRow | [PosterCard](../src/Momoka.Shell/Views/PosterCard.xaml.cs)：FocusWatch、ImageArrivalFade、Loaded/Unloaded；[EpisodeRow](../src/Momoka.Shell/Views/EpisodeRow.xaml.cs)：`OnCardChanged:111`、`OnLoaded:133`、`OnUnloaded:140` | 回收/换卡/短暂双容器时的 Claim/Release；晚图不能回填弃用身份；悬停操作有焦点入口；选择与媒体状态不串身份 | [PageContractTests](../tests/Momoka.PageContractTests/README.md)、[CardArtworkRecoveryTests](../tests/Momoka.PageContractTests/CardArtworkRecoveryTests.cs)；补坏图/恢复、快速回收与真实焦点/实屏 |
| EgPicker | [EgPicker.cs](../src/Momoka.Shell/Views/EgPicker.cs)：ComboBox 子类，`WidenToDropDown:124`、`ClampDropDown:139`、`RestoreDropDownWidths:153` | 长轨道名按内容扩展、父栏封顶、弹层后续 SizeChanged 修正、收起清宽度/订阅；原生键盘/选值语义 | [AppearanceOpacityTests](../tests/Momoka.AppearanceOpacityTests/README.md)与详情/settings 探针；补菜单开着缩放/换主题、长文本、Escape 与返回焦点 |
| WrapRow | [WrapRow.cs](../src/Momoka.Shell/Views/WrapRow.cs)：`MeasureOverride:62`、`ArrangeOverride:76`、`Layout:94` | 复用 Core WrapLayout；收起子项仍 Measure 以保留测量关系；有限宽换行；Arrange 返回整格宽，防意外居中偏移 | [WrapLayoutTests](../tests/Momoka.Tests/WrapLayoutTests.cs)与真实头部/菜单探针；替换前同输入同布局，不能仅凭行数变少认定改进 |
| MusicPlayerBar | [code-behind](../src/Momoka.Shell/Views/MusicPlayerBar.xaml.cs)：`Attach:16`、`OnSeekReleased:45`、`OnBarSizeChanged:59`；[ShellPage.Music](../src/Momoka.Shell/Views/ShellPage.Music.cs)：`PlaceMusicBarMargins:79` | ValueChanged 只观察；pointer release/KeyUp 才提交 seek；scrub owner 防串曲；760/480 两断点；页面滚动避让，停播归还；来源能力显隐 | [MusicViewModelTests](../tests/Momoka.MusicViewModelTests/Program.cs)、[Music 原生探针](../src/Momoka.Shell/ShellNavigationProbe.Music.cs)；补真实键盘 seek/队列取消/窄窗菜单与末行可达，不真起播 |
| MoviePilotSubscriptionWallView | [XAML](../src/Momoka.Shell/Views/MoviePilotSubscriptionWallView.xaml)：`WallGrid:219`、ItemsWrapGrid 352×162、内部竖滚关闭 | 宿主统一竖滚，稳定订阅/选择/拖拽语义；新卡不能改变权限与执行属主 | [SubscriptionWallWiringTests](../tests/Momoka.PageContractTests/SubscriptionWallWiringTests.cs)及 moviepilot 分区；补大集合有效 viewport/realized，不能因关闭滚动条就认定已虚拟化 |

## 4. 主题与资源大类

材质本身的逐表面规则见[详细材料指南](../.claude/skills/momoka-winui-shell/references/materials.md)。本节盘点资源的所有者和解析范围，不把资源名字当作材料已生效的证明。

| 类别 | 源码与符号 | 当前事实 | 后续检查 |
| --- | --- | --- | --- |
| 框架与应用合并 | [App.xaml](../src/Momoka.Shell/App.xaml) | RequestedTheme=Dark；依次 XamlControlsResources → Palette → Styles → MoviePilotMenus | 首帧资源存在/PRI 合并与应用作用域读取；恢复浅色/跟随系统另按 D01，不能借 P1 扩产品范围 |
| 颜色角色/层色/材料 | [Palette.xaml](../src/Momoka.Shell/Theme/Palette.xaml)：Dark/Light/HighContrast；[ThemeHost.cs](../src/Momoka.Shell/Theme/ThemeHost.cs)：`Brushes:63`、`Apply:203`、`Register:256` | Dark/Light 的画刷就地重涂，Acrylic 更新 Tint/Fallback；Color 结构值替换供后续解析；roots 弱引用；HighContrast 使用系统角色 | 角色同义重叠、局部覆盖、首帧与切主题一致性、HC 与关闭透明；历史“永不 Default”注释不作为当前平台硬限制 |
| 窗口画布与侧栏 | ThemeHost `CanvasAlpha:47`、`SidebarLayerAlpha:53`；ShellPage.ContentHost/SidebarHost | 全树公共画布一次 EgCanvasBrush；侧栏 LayerFillColorAltBrush；数值是现有项目参数 | [MicaMaterialTests](../tests/Momoka.Tests/MicaMaterialTests.cs)与材质基线；透明度数值不是像素对比度或节能保证 |
| 字体/字号/几何 | [Styles.xaml](../src/Momoka.Shell/Theme/Styles.xaml)：字体族 36/47/55、字号 58 起、EgDialogSurfaceStyle:202、EgFrameStyle:522、EgIconButtonStyle:584 | 字体族、角色字号、间距/圆角/滚动车道与通用模板集中；有默认模板继承，也有局部完整模板 | 字体缩放、焦点厚度、命中与 HC；自定义模板需逐状态验证；不以统一缩小字号修布局 |
| 输入颜色计算 | [InputControlTheme.cs](../src/Momoka.Shell/Theme/InputControlTheme.cs)：`Glass:14`、`Brushes:17` | 这是 C# 角色计算，不是 InputControlTheme.xaml；ComboBox、文本框静止/聚焦/边线键分别映射 | [InputFocusThemeTests](../tests/Momoka.Tests/InputFocusThemeTests.cs)、settings/详情真实输入状态；中性边线是项目外观选择 |
| 设置输入样式 | [SettingsInputs.xaml](../src/Momoka.Shell/Theme/SettingsInputs.xaml)、SettingsPage 局部资源 | 基于原生 TextBox/PasswordBox/AutoSuggestBox/ComboBox 模板；分类/设置行另有局部覆盖 | 具体可访问名称、草稿提交时机、已选项/聚焦/禁用状态与透明回退 |
| 操作按钮角色 | [ActionButtons.xaml](../src/Momoka.Shell/Theme/ActionButtons.xaml) | Dark/Light/HighContrast 的 Button/Toggle 状态资源接 EgActionGlass 系列 | 局部资源范围、图上/普通面上的墨、Hover/Pressed/Disabled/Focused，不仅检查静止态 |
| MoviePilot 菜单/站点/资源筛选 | [MoviePilotMenus.xaml](../src/Momoka.Shell/Theme/MoviePilotMenus.xaml)、[MoviePilotSiteControls.xaml](../src/Momoka.Shell/Theme/MoviePilotSiteControls.xaml)、[MoviePilotResourceFilterStyles.xaml](../src/Momoka.Shell/Views/MoviePilotResourceFilterStyles.xaml) | 前者应用合并，后两者按调用者局部解析；只作用对应业务表面 | 与详情 MenuFlyout/设置输入区分；权限变化、长选项/窄窗和点外关闭 |
| 存活配色与深浅 | [UiThemes.cs](../src/Momoka.Core/Theming/UiThemes.cs)：`All:38`、`Resolve:68` | 当前 emby-dark、oled-black、midnight、graphite、plum 五套均深色；未知 ID 回默认 | 本阶段运行按真实存活列表取样；“纯黑”说明不是本次 OLED 功耗量测，浅色派生留存不是浅色产品已支持 |

## 5. 绑定、导航、尺寸与生命周期

### 5.1 状态所有者

| 路径 | 已有真实实现 | 缺少的证据与后续要求 |
| --- | --- | --- |
| Shell 导航 | [ShellPage.xaml.cs](../src/Momoka.Shell/Views/ShellPage.xaml.cs)：ContentFrame.CacheSize=2（179）、Navigated（187/658）、BrowseTransition（1953）、GoBack（2214）、GoForward（2231）、ReleaseContent（2764） | NavigationView 已存在，不从零引入；Frame 与面包屑/前进 trail 的一致性、切身份清理、焦点恢复、动画抑制/已有动效的关系要在实际导航验证 |
| 侧栏与标题栏 | [ShellPage.Sidebar.cs](../src/Momoka.Shell/Views/ShellPage.Sidebar.cs)：`SidebarWideThreshold:13`、`SyncSidebar:72`、`ApplySidebarLayout:125` | 页面仍在原 ContentFrame，侧栏只导航；模式是产品选择；窄启动后拉宽的 Header 文字恢复是候选，历史注释不能当本次运行结论 |
| 首页缓存 | [HomePage.xaml.cs](../src/Momoka.Shell/Views/HomePage.xaml.cs)：1355–1523 的访问/释放；[HomePage.Rows.cs](../src/Momoka.Shell/Views/HomePage.Rows.cs) | 同服务重入、snapshot 后后台刷新、稳定行与排序改变有不同路径；宽/中/窄、暖/冷分别取基线，不将网络延迟混入纯布局时间 |
| 媒体库绑定与分页 | [LibraryPage.xaml.cs](../src/Momoka.Shell/Views/LibraryPage.xaml.cs)：Attach 合并通知（67 起）、resize timer（134–139）、OnViewChanged（957）、PumpIfShort（1018）；[LibraryViewModel.Window.cs](../src/Momoka.Shell/ViewModels/LibraryViewModel.Window.cs)：`WindowEligible:31` | 当前有条件窗口读取只用于适合的普通影视根库。聚合/游标/随机排序/集列表等不机械套用；追加页、删除、权限变化、返回锚点保留单独覆盖 |
| 详情绑定 | [DetailViewModel.cs](../src/Momoka.Shell/ViewModels/DetailViewModel.cs)：PageWidth（422）及其派生通知属性、OnPageWidthChanged（990）、AnnounceShape（2659）；[DetailPage.xaml.cs](../src/Momoka.Shell/Views/DetailPage.xaml.cs)：OnBodySizeChanged（2094） | PageWidth 已有成批派生通知；AnnounceShape 先 AnnounceAffordances，再同类型早退，首访/类型更正更新整批。应测试通知是否完整，不能描述为尚无绑定更新机制 |
| 设置访问与草稿 | [SettingsPage.xaml.cs](../src/Momoka.Shell/Views/SettingsPage.xaml.cs)：Required 缓存（100）、新活动/Attach（417 起）、SyncSaveState（重入路径）、ReleaseHosted（789） | Half-typed 值/分类/集合身份保留；Hide 与离页共用释放，重开新 ticket；队列/权限/源变化不能借用新访问身份；不要用重建整页同步保存旗标 |
| 音乐集合与来源 | [MusicLibraryViewModel.cs](../src/Momoka.Shell/ViewModels/MusicLibraryViewModel.cs)：Rows（25）、Attach（52）、SelectAsync（110）、LoadAsync（128）、Live（183）、Release（186） | 稳定 Rows，加载代次与来源守卫；搜索草稿和 `_loadedSearch/_loadedSection` 分开；收藏/搜索显隐按来源能力。完整播放队列另有属主，不能当页面分页 |

页面上的 `x:Bind` 模式按真实属性语义决定：动态数据与显隐普遍 OneWay，编辑字段 TwoWay，模板静态值允许 OneTime。不能用全仓替换 Mode 来“统一”；需要同时检查生成通知、Attach 时机、Bindings.Update、复用对象与实际编辑提交。

### 5.2 已有断点与尺寸基线

| 表面 | 实际阈值/计算 | P4 运行矩阵 |
| --- | --- | --- |
| SettingsPage | [XAML](../src/Momoka.Shell/Views/SettingsPage.xaml)：CompactSettings/WideSettings，`AdaptiveTrigger MinWindowWidth=860`（1418–1446）；左列 200（1484） | 859/860/861；独立设置 Window 与 Shell fallback 分开；短高、文本放大、分类搜索跳转、草稿/焦点 |
| Shell Sidebar | ShellPage.Sidebar.cs:13 阈值 1008；XAML:519 OpenPaneLength=220、CompactPaneLength=48 | 1007/1008/1009；手动开关、窄启动再拉宽、标题栏洞与页面实际可用宽 |
| Detail | [Core/Emby/DetailHero.cs](../src/Momoka.Core/Emby/DetailHero.cs)：CompactFloor=819.2（560）、IsCompact（544）读取 pageWidth | 实际页面宽在 819.2 两侧及等值，不以 native 窗口 820 冒充；简洁与沉浸、异步类型更正、迟到图片和切模式 |
| MusicPlayerBar | code-behind:62/63 在 760、480 分档 | 759/760/761、479/480/481；底栏实测高度、菜单与最后一行可达；无空间时双行，不整体缩字号 |
| Music 搜索框 | [MusicLibraryPage.xaml.cs](../src/Momoka.Shell/Views/MusicLibraryPage.xaml.cs)：PlaceSearchBox（70），按分区按钮占完后的余宽计算 | 1280/760/600 与宿主实际最小宽；进入/切分区/显隐恢复；宽度初始 NaN 和首次 0 宽处理 |
| Library 头部/网格/弹层 | OnHeaderSizeChanged/ApplyHeaderLayout，ResizeGrid/120ms 重排，LibraryPanelSize.For | 代表宽度 + 真断点前/后；动态工具栏、表格列、Popover 边缘；记录 DIP、RasterizationScale 与 native 像素分别是谁 |

本文件只定位尺寸责任。不同显示器缩放、文字缩放、100/125/150/200% 与跨屏仍由 P1/P4 实测账本记录；没有设备的组合应明确留缺口。

## 6. 音乐墙三项重点候选

### 6.1 第 81 项之后的入口

完整链路：

1. [MusicCatalog.cs](../src/Momoka.Core/Music/MusicCatalog.cs)：PageSize=80（10）；Query（22–46）按 Tracks/Albums/Artists/Playlists/Favorites 设置类型、Limit=80、StartIndex。
2. [MusicLibraryViewModel.cs](../src/Momoka.Shell/ViewModels/MusicLibraryViewModel.cs)：SelectAsync（110）重载；LoadAsync（128）首批 offset=0；155–168 消费返回项、追加 Rows，并用 `result.Items.Count>0 && _offset<_total` 更新 MoreAvailable。
3. [MusicLibraryPage.xaml](../src/Momoka.Shell/Views/MusicLibraryPage.xaml)：曲目父 Grid（306）受 TrackViewVisibility 控制；唯一 MoreButton 在 MusicRows.Footer（343–361）；墙独立为 WallScroll → MusicWall（366–390）。
4. [code-behind](../src/Momoka.Shell/Views/MusicLibraryPage.xaml.cs)：OnMore（88）调用 LoadMoreAsync；未发现墙滚动分页事件、其它加载按钮或自动补页调用。

| 匿名总数 | 首批预期 | 有效第二页预期 | 当前源码提示 |
| --- | --- | --- | --- |
| 81 | Rows=80，MoreAvailable=true，StartIndex=0 | StartIndex=80，取得 1 项，Rows=81，MoreAvailable=false | 墙模式把曲目父容器收起，唯一加载按钮不可由正常布局到达 |
| 160 | Rows=80，MoreAvailable=true，StartIndex=0 | StartIndex=80，取得 80 项，Rows=160，MoreAvailable=false | 同上；完整队列加载不能回填墙 Rows |

`CaptureQueueLoader:82–107` 捕获已提交分区/搜索，为歌曲播放准备音频队列，过滤非音频且不回填页面 Rows。现有 [MusicViewModelTests](../tests/Momoka.MusicViewModelTests/Program.cs) 的 80+20 覆盖完整播放队列、离页/停止/旧搜索，不能证明用户能进入墙第二页。

现有[Music 原生探针](../src/Momoka.Shell/ShellNavigationProbe.Music.cs)是 12 曲目、9 专辑（28、39），只覆盖小集合形状/底栏/宽窄/主题；没有 81/160 或 Artists/Playlists 后页入口。

**最小 P1 诊断**：每种墙 ×81/160，实际选择并等待首批；记录 MoreButton 自身与祖先 Visibility、有效屏上矩形/UIA 可达性；滚至实际页尾；记录请求只含 offset=0 还是出现80、Rows/MoreAvailable 与截图。故障可作为基线事实保存，不用“检测到故障的诊断通过”冒充产品分页验收通过。

**P4 方案准备**：显式“加载更多”最接近现有曲目语义；自动滚动加载减少手动操作，但增加自动请求、重复触发/取消/失败重试责任。先比较真实缺口与最小方案；涉及既有产品选择或新的实质代价时按 D07 说明并确认，再改产品。

### 6.2 曲目/封面墙图片是否真正加载

模板只绑定 RowPoster（105、168）；VM 构造 CardItem（161–166），未调用 EnsurePosterAsync；页面没有 Loaded/Unloaded、ElementPrepared 或 ContainerContentChanging 为这些模板交接图片。

[CardItem.cs](../src/Momoka.Shell/Views/CardItem.cs) 构造（307 起）只记录对象/图片来源，不发图片；RowPoster（731）返回 Poster 或 FolderTile0；实际读取入口 EnsurePosterAsync（858）。因此当前调用链提示新卡可能始终只有占位图标，需实际图片请求与 Image.Source 证实。

与此相反，[MusicPlaybackViewModel.cs](../src/Momoka.Shell/ViewModels/MusicPlaybackViewModel.cs) 的 SetArtwork（310–315）明确 ClaimCard + EnsurePosterAsync。底栏的封面已经读到，不证明曲目表/墙封面读到。

**最小 P1 诊断**：独立 item ID/图片标签，不启动播放；记录图像 GET 属于列表还是底栏、真实 Image.Source、首图完成、滚出/返回/离页状态。先不手工给列表 CardItem 调 EnsurePosterAsync，否则会绕过缺失的生产路径。

**P5 最小实施与覆盖**：若运行证实缺口，为实际回收容器建立 claim/load/release，复用现有 CardItem 生命周期；分别测首图、离屏再回、同容器换项、旧下载/解码迟到、来源撤销和无图/坏图恢复。不能在 VM 首批给全部卡发图片来掩盖容器缺口。

### 6.3 两层竖滚与虚拟化

MusicWall 是带内部 ScrollView 的 ItemsView，外层 WallScroll 同时开启竖滚。结构确有两层所有者，但结构本身不证明 viewport 无界或所有项实现。

**最小 P1 诊断**：记录 WallScroll 与 MusicWall.ScrollView 各自 ActualHeight、ViewportHeight、Extent/ScrollableHeight、VerticalOffset；两者分别滚到底一次，并记录 realized ItemContainer/Image 数、Rows 数和图片 GET。1280/760/600 宽度使用相同数据和图片，图片冷/热缓存分开。不要用 Rows=80 解释虚拟化，也不要把隐藏滚动条当作有限 viewport。

这项与后页入口、图片加载是三个独立问题。修一个不能自动证明另两个解决，合成同一未经测量的“嵌套滚动性能问题”会掩盖可达性缺陷。

## 7. P3–P6 最小交付单元与验证

| 阶段/单元 | 真实文件与问题 | 已有覆盖 | 必须新增或补齐的证据 | 决策边界 |
| --- | --- | --- | --- | --- |
| P3 导航责任 | ShellPage/Sidebar/ContentFrame；侧栏已是 NavigationView，重点历史/选中/标题责任重复 | shell 分区、PageContract 与来源/目录夹具 | 侧栏模式开关、窄启动拉宽、返回/前进/切身份与焦点实测 | 不恢复已删除导航模式；符合建议的等价小调整正常执行，推翻产品选择按 D07 |
| P3 成组命令 | LibraryBatchSurface（LibraryPage.xaml:925）当前 WrapRow+Button/MenuFlyout；评估 CommandBar 主/次操作与溢出 | [LibraryBatch 探针](../src/Momoka.Shell/ShellNavigationProbe.LibraryBatch.cs)、主批量规则/权限测试 | 鼠标/键盘打开与取消、宽窄溢出、失权后菜单、选中快照、失败/取消反馈 | 真实媒体操作仍按匿名假服务/授权边界；不机械替换全部按钮 |
| P3 输入/模板范围 | EgPicker、SettingsInputs、局部 Button/Toggle/Popup | AppearanceOpacity、PageContract、settings/详情分区 | 打开中缩放/主题、长文本、Escape/点外、焦点返回与UIA状态 | 官方冲突和点外语义按 D02；纯模板改型也不能丢草稿 |
| P4 音乐墙后页 | 第6.1节生产入口缺口 | MusicCatalog 核心查询、MusicViewModelTests | 81/160 ×三墙分区，实际入口触发 offset80、最后项可见、失败/重试/切分区取消、底栏避让 | 显式按钮/增量滚动的具体取舍按必要 D07，不能一项方案顺带改播放 |
| P4 精确布局/绑定 | Settings 860、Sidebar1008、Detail819.2、MusicBar760/480、Library按内容宽 | Core布局、PageContract、AppearanceOpacity、SettingsLifetime | 阈值两侧、短高、文字放大、异步类型更正/图片迟到、来源切换、native命中/DPI | 不通过缩小字号、删掉必要操作过关；浅/跟随系统另按 D01 |
| P5 音乐图像/滚动 | 第6.2/6.3节，音乐模板缺图片属主与两层viewport候选 | CardItem图片源链接/恢复、Music VM、现有小数据Music探针 | 真实请求/解码/回收、有限viewport、realized、同条件冷/热/往返、资源释放 | 不把直接预加载全集合当修复；先测再决定滚动所有者 |
| P5 库墙/长集/货架 | CardWall窗口读、CardShelf/CardCollection、ShelfStrip | [PerfListDiagnostics](../tests/Momoka.PerfListDiagnosticsTests/README.md)、LongSeries、DeepReturn、LibraryCache、WarmTiming | 100/1000/10000同路线量测；DTO/CardItem/materialized/realized/图片/帧/内存分项；只选基线中的真实瓶颈 | 已有懒卡/焦点不能当空白重写；无GPU/功耗数据不声称改善 |
| P6 音乐与列表语义 | MusicTile ItemContainer（156）未显式Name；焦点/选择/状态要看实际peer | FocusWatchTests、settings具体名称、主输入规则 | UIA实际Name/Role/状态；只用键盘完成分区、页尾、菜单取消/返回；Narrator读出另记 | 子TextBlock可能为默认peer供名称，未写属性不直接等于无名称；真实播放按钮不invoke |
| P6 关联动画最小研究 | 当前 BrowseTransition抑制；Gallery同项卡→详情可参考 | [UiMotionTests](../tests/Momoka.UiMotionTests/README.md)、HomeFoldCompletion、SettingsRowsMotion与已有原生动效探针 | 单一稳定同项准备/目标布局/返回，虚拟化或缺目标普通导航，快速打断/离页/零动画/焦点 | 不为API名覆盖现有用户动效；整体可见语义变化按具体D07，宿主切换另按D04 |

每个实现批次都要先证明缺口，再包含生产改动、对应测试、受影响运行/实屏和独立提交。P1 的列表不是 P3–P6 已批准实现或完成声明。

## 8. 本次已读 Gallery 实现对照

以下全部为 Gallery 固定 `6de91feccadfbb0a796b71caedead5a2284f730e` 下仓库相对路径。本机只读克隆为 `C:/Users/89400/AppData/Local/Temp/momoka-winui-gallery-20261112`，不作为干净检出必须存在的工具依赖。

| 样例文件/符号 | 已读行为 | 对 Momoka 的用途与限制 |
| --- | --- | --- |
| `WinUIGallery/Samples/ItemsView/ItemsViewPage.xaml`：ItemContainer（22/38/70/105）、BasicItemsView（143）、SwappableLayoutsItemsView（161） | ItemContainer显式Name绑定Title；示例给400高视口；StackLayout/UniformGridLayout选择；ItemsView自身ScrollView | 对照音乐墙语义、viewport和布局；400不是Momoka高度建议，不能照搬；没有显式Name需运行peer判断 |
| 同页 `.xaml.cs`：Loaded（32）、布局切换（108起）、滚动归零（170起） | 稳定ObservableCollection；读取内部ScrollView；缓存布局与模板；布局切换先归零 | 异步ItemsSource路径是样例LinedFlowLayout特定workaround，不自动用于Momoka或当前包 |
| `WinUIGallery/Samples/ListView/ListViewPage.xaml/.cs`：过滤（262/275）、播报（290）、相对滚动（354–375）、ScrollIntoView（382–394） | 原地增删过滤；UIHelper可访问状态播报；ListViewPersistenceHelper按项恢复；ScrollIntoView按真实索引 | 与现有锚点/列表状态对比；样例helper不替代Momoka源身份/删除/分页语义 |
| `WinUIGallery/Samples/CommandBar/CommandBarPage.xaml/.cs`：PrimaryCommandBar、SecondaryCommands、Add/RemoveSecondaryCommands、KeyboardAccelerators | 标签位置、主/次操作、可开/关、动态增删次命令和快捷键 | 用于库批量操作候选；本次未取得Learn动态溢出文档，不凭样例把内部集合/性能机制当官方保证 |
| `WinUIGallery/Samples/NavigationView/NavigationViewPage.xaml/.cs`，`WinUIGallery/MainWindow.xaml/.cs`：Navigate（194）、Frame与选中同步、返回/前进、pane模式 | 原生NavigationView组织目的地；Frame导航同步选中，模式/窗口边缘处理 | 证明现有Momoka选型有可参考实现；Gallery结构不是媒体产品规格，不套全部分组/设置 |
| `WinUIGallery/Samples/ConnectedAnimation/ConnectedAnimationPage.xaml/.cs`，`SampleSupport/SamplePages/SamplePage1.xaml.cs`、`SamplePage2.xaml.cs` | PrepareToAnimate，抑制页面过渡，TryStart；辅助内容EntranceThemeTransition | 一次单项研究，不能因TryStart有调用就声称动画成功；生产需缺目标/打断/偏好/关闭处理 |
| `WinUIGallery/SampleSupport/SamplePages/ItemsRepeaterCollectionPage.xaml.cs`：ElementPrepared（29）、键盘（42）、准备（66）、导航（72）、返回（75） | 模板Enter/Space、记录滚动、通过ConnectedAnimationService准备；返回查TryGetElement、恢复焦点，缺容器回到repeater | 与ShelfStrip已有离屏目标/焦点比较；样例的单次缓存/对象引用不替代Momoka跨来源身份 |
| `WinUIGallery/SampleSupport/SamplePages/DetailedInfoPage.xaml.cs`：GoBackButton_Loaded、OnNavigatedTo、OnNavigatingFrom | 落点焦点放返回键并BringIntoView，详情目标TryStart与返回准备 | P6只键盘/焦点不遮挡对照；不顺带改变产品导航行为 |

## 9. 后续比较要记录的量

| 量 | 记录方式 | 不能替代的证据 |
| --- | --- | --- |
| 网络分页 | 匿名handler请求的类型、StartIndex/Limit、返回项与总数、读/写/播放意图 | Rows.Count不证明用户能发第二页；零真实播放不等于所有命令可乱invoke |
| 数据状态 | DTO数、Rows/CardItem数、CardCollection.MaterializedCount与身份 | 不等同realized视觉元素、GPU/图片成本 |
| 视觉实现 | 实际容器/图像数、每层viewport/extent/offset、末项与有效矩形 | XAML结构和隐藏滚动条不证明有限测量或可达性 |
| 图片 | 标签/尺寸固定的本地图或匿名图像GET、解码完成、实际Source、弃用/回收 | handler提供了图不证明UI请求；底栏图不证明墙图 |
| 时间/资源 | 固定尺寸/缩放/数据/冷暖的首屏、滚动/返回、帧/内存与重复往返 | 计数算法更少不直接等于帧率提高；没有显存/功耗trace不承诺改善 |
| 输入/无障碍 | 实际键盘/鼠标目标回执、UIA Name/Role/状态、焦点与Narrator记录 | 内部handler调用、AutomationId属性或坐标返回不证明实际输入/读出 |

本清单的文档验证只负责类型枚举准确、源码链接存在、固定证据与候选措辞一致，以及UTF-8/LF/末尾换行。它不证明 P1 运行基线、后续产品修复或官方资料自动更新已完成。

## 10. P1 期间的主树增量与运行核实

### 10.1 主树增量固定在 07e991b

P1 调查期间，主树进入 `07e991b4cb79fa1ca3e9a0373d817c9772d087a1`，合入同期媒体库第四批的花絮/特典、Jellyfin 本地字幕上传与飞牛字幕管理。这是其它批次的产品变更，不是本次 P1 改造。通过只读 `git diff 4e0394a..07e991b` 及两次提交的 XAML 根元素重新枚举核对：**Page 仍为19、UserControl仍为31**，本文件第2/3节类型集合不变；XAML ContentDialog 从23增至24，新增 FnNasSubtitleDialog。这个对话框计数只包含 XAML 根，不包括纯代码对话框，也不代替全部弹层清单。

第1–9节的原始行号和结论仍固定在4e0394a，不用新源码漂移重写旧证据。增量的三个界面文件为 `DetailPage.xaml`、`SubtitleDialog.xaml`、`FnNasSubtitleDialog.xaml`，另有相应 partial、服务/协议、调用者、测试和探针；本節新增符号与行号全部固定在07e991b。

| 新增或变化表面 | 07e991b 的真实文件/符号与界面事实 | 状态与生命周期事实 | 已有测试/运行入口及本轮未覆盖 |
| --- | --- | --- | --- |
| 详情花絮与特典 | [DetailPage.xaml](../src/Momoka.Shell/Views/DetailPage.xaml)：ColumnExtrasButton（875）、CompactExtrasButton（1297）；[DetailPage.Extras.cs](../src/Momoka.Shell/Views/DetailPage.Extras.cs)：ShowExtrasAsync（18）、OnExtraStateChanged（85）。两个现有布局区域增加 Button，打开原生 MenuFlyout；加载/错误/重试与条目按真实响应填入 | [DetailViewModel.Extras.cs](../src/Momoka.Shell/ViewModels/DetailViewModel.Extras.cs)：ResetExtras（19）、OwnsExtras（50）、CanRetryExtras（56）、CloseExtras（62）、GetExtrasAsync（71）、OpenExtraAsync（106）。选择捕获原subject/source/epoch/token；关闭撤销该菜单读/选项但保留入口；旧关闭不撤销新菜单；重试等Closed后入队并重核属主。DetailPage.Release明确Hide菜单，VM加载/取消重置Extras | [DetailExtraTests](../tests/Momoka.Tests/DetailExtraTests.cs)、[ItemExtraTests](../tests/Momoka.Tests/ItemExtraTests.cs)、[README.Extras](../tests/Momoka.Tests/README.Extras.md)；原生 `InspectDetailExtras` 与 library 分区调用 [DetailExtrasAsync](../src/Momoka.Shell/ShellNavigationProbe.Extras.cs)。两服务×1280/760、真控件/UIA、匿名捕获播放意图、错误/空态/关闭/迟到结果；这是覆盖入口，**本轮没有执行这些新增场景**，旧4e的详情布局基线不能替代 |
| Jellyfin字幕版本/本地上传 | [SubtitleDialog.xaml](../src/Momoka.Shell/Views/SubtitleDialog.xaml)：VersionBox（48）、UploadSection（54）、UploadDraft（58）、语言/强制/听障（60–65）；仍是标题在外、SubtitleScroll（41）内的可滚内容。Emby继续默认文件目标，Jellyfin由caller枚举完整MediaSources | [SubtitleDialogTarget.cs](../src/Momoka.Shell/Views/SubtitleDialogTarget.cs)固定每个媒体版本的委托；[SubtitleDialog.Upload.cs](../src/Momoka.Shell/Views/SubtitleDialog.Upload.cs)：ReadIsCurrent（21）、OnVersionChanged（45）、UploadSelectedAsync（109）。切版撤销旧读/删除确认/草稿并重绑；写入在飞/未知回执/陈旧列表拒绝切版；权限失去隐藏上传，恢复保留同版草稿；未知写入保留只读恢复，不用刷新或A→B→A洗掉义务；Closing阻止写中关闭 | [JellyfinUploadDialogTests](../tests/Momoka.SubtitleDialogTests/JellyfinUploadDialogTests.cs)、[SubtitlePickerTests](../tests/Momoka.SubtitleDialogTests/SubtitlePickerTests.cs)、[JellyfinSubtitleUploadTests](../tests/Momoka.Tests/JellyfinSubtitleUploadTests.cs)。原生 [JellyfinUploadAsync/RecoveryAsync](../src/Momoka.Shell/ShellNavigationProbe.JellyfinUpload.cs) 为1160/680，新增 `jellyfin-upload`分区与Inspect入口。假文件选择/HTTP，不证明系统picker和真实服务器；**本轮尚未运行** |
| 飞牛字幕管理对话框 | [FnNasSubtitleDialog.xaml](../src/Momoka.Shell/Views/FnNasSubtitleDialog.xaml)：ContentDialog根（3），版本ComboBox（29），SubtitleScroll（26）/ItemsControl列表/内联删除确认；搜索、下载、本地上传及能力允许的服务器路径区分显示。沿用EgLibraryDialogSurfaceStyle，Styles.xaml新增隐式派生样式，不新增Page/UserControl | [FnNasSubtitleDialog.xaml.cs](../src/Momoka.Shell/Views/FnNasSubtitleDialog.xaml.cs)：Closing（74）拒绝写中关闭，否则closed/generation/cancel，Closed（83）释放token；Current（103）核服务/页面/generation；FitLayout（159）按root宽高缩；版本切换（173）取消读、清列表/确认再重读。每版本独立service快照与回执，跨版Touched/NeedsRefresh归整个visit；权限/未知回执控制操作，不能把恢复读当写成功 | [Momoka.FnNasSubtitleTests](../tests/Momoka.FnNasSubtitleTests/README.md)源链接真实dialog/caller/Core；原生 [FnNasSubtitlesAsync](../src/Momoka.Shell/ShellNavigationProbe.FnNasSubtitles.cs) 为1280/760，新增 `fnnas-subtitles`分区。匿名假HTTP/假picker，真控件与截图；**本轮未运行**，同期批次报告不是P1本轮证据 |
| 字幕caller与文件选择的窗口/访问归属 | [ItemCommands.Server.cs](../src/Momoka.Shell/Views/ItemCommands.Server.cs)：SubtitlesAsync（471）捕获owner/root/最近Page；[ItemCommands.FnNasSubtitles.cs](../src/Momoka.Shell/Views/ItemCommands.FnNasSubtitles.cs)：FnNasSubtitlesAsync（10）；[ServerSubtitlePicker.cs](../src/Momoka.Shell/Views/ServerSubtitlePicker.cs)、[FnNasSubtitleFile.cs](../src/Momoka.Shell/Views/FnNasSubtitleFile.cs) | Page/owner离树取消访问；Jellyfin每版服务分别观察可用性，ShowAsync结束finally退订，再核访问后父刷新。飞牛后台Current保持纯托管，XamlRoot采样只在UI事件/predicate，dispatcher提前捕获；visit/root/source/permission观察finally退订。picker从当前root对应AppWindow取得HWND，在异步每步后重核访问，按各自文件边界读取 | 字幕源链接/线程归属/picker专项和原生caller覆盖；系统文件选择器、真实点外投递、实际服务器写入仍须明确授权与适用检查，不能借P1自动运行 |

对后续阶段的影响：P3命令/原生菜单盘点需包含新Extras MenuFlyout、字幕版本选择与动作；P4详情头部多一项按钮、字幕长路径/窄窗/root变化要补矩阵；P5字幕列表使用ItemsControl，大数量/滚动成本是否成为瓶颈待实际量测；P6加入新入口可访问名称、操作状态、关闭/重试焦点与版切换反馈。现有19/31表不需要扩类型，验证范围需要按最新实际行为扩展。P1只登记增量，**不自动授权修改同期产品或执行字幕上传/删除/花絮播放**。

### 10.2 本轮音乐墙观察已经核实的部分

主代理在4e0394a产品基线上的隔离匿名 MusicSurvey 完成了实际 Tab/滚动路径观察：1280/760/600 DIP ×81/160项×专辑/艺术家/歌单共 **18个墙场景**。每场景均观察到Rows=80、MoreAvailable=true，第二页请求0、列表图片GET0、Image.Source非空数0、CardItem.Poster非空数0。第6.1节入口缺口与第6.2节封面未发起加载，现已由真实控件/请求/图片读数支持；这仍是故障基线，不能登记为产品分页或封面验收通过。

1280 DIP宽下滚动到底后，视觉树计数曾出现80个容器，而按Repeater.TryGetElement取得的当前布局容器约31–33个；760宽为16→18、600宽为11→13。两种计数口径不同，不能把它们混算成“80个全被实现/虚拟化失效”，也不能把31–33直接当GPU/图片内存结论。第6.3节继续分别记录活动realized、树内回收/保留对象与各层viewport，再决定是否改滚动所有者。

本段数据由主控本轮运行回执提供，详细条件、产物来源、各场景值及运行覆盖边界以[WinUI P1 基线](WinUI-P1基线.md)为准。它覆盖当前匿名音乐调查，**没有覆盖07e991b新增字幕/Extras场景，也不是全量Shell、真实服务或播放管线验收**。
