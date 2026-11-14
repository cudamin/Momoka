# Momoka WinUI 动效施工文档

编写日期：2026-11-14；执行记录更新：2026-11-15。状态：S0～S3 已实施，S2 原生候选按门槛评估后保留现状；S4 正在收尾。用户已要求开始施工，本批不发布、不上传。实际结果见第八节和 [本次施工验收记录](WinUI动效施工验收-2026-11-15.md)。

目标：在现有动效上补齐可观测性、导航一致性和中断处理，让后续执行者可以按批次实施、测试、合并和验收。默认施工范围是 S0～S4；AnimatedVisualPlayer 和 Composition Effects 暂缓，原因与重新评估条件见第七节。

本文只覆盖本次五类动效，不恢复已撤销的其他 WinUI 优化计划。项目规则以 [CLAUDE.md](../CLAUDE.md) 为准。2026-11-14 的只读审查及文档编写已结束，随后用户“开始做吧”授权执行默认 S0～S4；原计划步骤保留，执行结论单独记录。

## 一、施工结论与范围

| 能力 | 当前结论 | 后续默认施工动作 |
| --- | --- | --- |
| Connected Animations，关联动画 | 已用于封面、详情和人物之间的导航，适合保留 | 补准备到启动的计时、明确回退原因、人物完成事件验收；保留图片和身份守卫 |
| Implicit Animations，隐式动画 | 原生属性过渡与 Toolkit 显隐动画都已存在 | 检查快速反向、关动画、命中、焦点和离页清理；按目标属性选实现，不全量替换 |
| Page Transitions，页面过渡 | 现有浏览淡入与关联动画有协调逻辑 | 在有限普通前进路由验证原生 Entrance；满足门槛才采用，保留首帧门控与已有回退 |
| Animated Visuals，动画图形 | 已有 AnimatedIcon；没有发现业务页面使用 AnimatedVisualPlayer | 本批保留设置齿轮及静态回退，不新增循环插画或动画资产依赖 |
| Composition Effects，合成效果 | 未发现应用自写的 CreateEffectFactory 效果链 | 暂缓新增。现有材质和播放视觉保持各自职责 |

施工完成后的预期体验：封面和人物动画能迅速接上目标，失败时页面正常出现；普通页面前进的动效有明确归属；连续操作、中途关动画和页面回收后不会留下半透明控件、错误位置或不可见的点击层。

范围约束：不改变媒体的点击/播放语义，不重做播放器管线，不改变已确认的材质和主题范围，不为展示 API 新增开关，不自动升级或新增包。后续发现具体产品要求与官方推荐冲突时，按 CLAUDE 的现行流程处理。

## 二、事实基线与证据边界

### 2.1 文档编写时的核对基线

| 项目 | 核对结果 |
| --- | --- |
| Momoka 源码基线 | `e70f55b397845e92355c1ff22672424faa1f47bd` |
| 前次只读审查基线 | `69de7081d05106811a62024618232d30bb553e59`；本文涉及的核心动效实现未变化，详情页接线以新基线为准 |
| 本地应用形式 | WinUI 3、.NET 10、非打包 x64；主宿主为 HWND 与 XAML 岛 |
| 本地 WinUI / Runtime 声明 | `Microsoft.WindowsAppSDK.WinUI` 为 `2.3.9`，Runtime 为 `[2.5.1]` |
| 本地动画 Toolkit 声明 | `CommunityToolkit.WinUI.Animations` 为 `8.2.251219` |
| Gallery 固定提交 | `6de91feccadfbb0a796b71caedead5a2284f730e` |
| Gallery 对照版本 | `standalone.props` 的 Windows App SDK 为 `2.6.6-experimental`，Win2D 为 `1.4.0`，Toolkit 为 `8.2.251219` |

本地版本来自 [Shell 项目文件](../src/Momoka.Shell/Momoka.Shell.csproj)。这些是项目声明，不是本次还原、构建或分发测试结果；Gallery 整仓使用实验 SDK 不代表所有基础动画 API 都要求实验包。施工前重新读取当前项目文件和包元数据，保持项目的稳定依赖与拆包策略。

2026-11-14 文档编写阶段只核对源码、现有测试编译输入和断言，以及官方文档和固定 Gallery 样例，没有运行客户端、产品测试、探针、真实服务器或性能采样。下面清单保留该阶段的事实；之后施工的当前基线、真实通过/失败和覆盖限制见第八节及验收记录，不能混用。

### 2.2 历史记录如何使用

[封面关联动画记录](封面关联动画.md) 保存过往海报前进/返回以及人物启动的原生证据；[P6 动效与输入记录](WinUI-P6动效与输入.md) 保存历史生命周期修复。它们可以帮助定位，不能作为当前最终代码的验收结果。

人物“原生启动、接住照片”的历史记录，不能扩写成“完整 Completed、所有中间帧和跨 DPI 已验证”。施工记录不得复制旧测试数量冒充本次通过数。

## 三、现有实现清单

下列行号用于定位本次基线；施工时以文件中的符号与真实调用链为准。表中的时长是源码参数，不是本次测得的实际播放时长或响应耗时。

### 3.1 关联动画

| 入口/职责 | 当前行为 | 文件与符号 |
| --- | --- | --- |
| 首页封面、预览“详情” | 先准备实际封面，再导航 | [HomePage.xaml.cs](../src/Momoka.Shell/Views/HomePage.xaml.cs)，约 1541、1600 行 |
| 媒体库封面、详情入口 | 从实际容器找 PosterCard，尝试准备 | [LibraryPage.xaml.cs](../src/Momoka.Shell/Views/LibraryPage.xaml.cs)，约 1053、1100、1134 行 |
| 人物页作品封面 | 复用同一封面到详情实现 | [PersonPage.xaml.cs](../src/Momoka.Shell/Views/PersonPage.xaml.cs)，约 168 行 |
| 详情的剧季、类似作品 | 为新卡片准备，旧页退出不能撤销新票据 | [DetailPage.xaml.cs](../src/Momoka.Shell/Views/DetailPage.xaml.cs)，约 2547、2571 行 |
| 演职人员到人物页 | PersonTransition 只做前进；目标先接来源照片 | [PersonTransition.cs](../src/Momoka.Shell/Views/PersonTransition.cs)，Prepare、Attach、TryStart |
| 详情返回 | 源页和源卡仍有效、同项同图时尝试 Direct 返回 | [CardDetailTransition.cs](../src/Momoka.Shell/Views/CardDetailTransition.cs)，BeginReturn、CompleteReturn |
| 导航协调 | 关联成功不叠普通页面淡入，失败恢复普通呈现 | [BrowsePresentation.cs](../src/Momoka.Shell/Views/BrowsePresentation.cs)，ResolveConnectedMotion；[ShellPage.xaml.cs](../src/Momoka.Shell/Views/ShellPage.xaml.cs)，构造函数的 Frame 事件 |

当前前进保留平台默认配置，没有自设固定播放时长。前进等待目标就绪的 700ms、启动后的完成观察 1200ms，以及返回路径的 1200ms 观察/取消期限，都是生命周期保护参数，不能写成动画播放时长。

已有守卫包括当前账号/来源、条目身份、Primary 图片身份、源/目标可见性和容器回收。电影、剧、季的自身封面可以关联；单集剧照与剧封面不同、音乐、缺图或窄窗没有可见目标等情况走普通导航。目标已有高清图时不能被来源小图覆盖降级。

详情页当前先发起不等待的 ReloadAsync，再 Attach 来源图；因此可表述为“不等待网络结果”，不能表述为“准备到启动之间完全没有网络操作”。是否调整请求发起时序，由 S1 的测量决定。

### 3.2 隐式动画与局部动效

| 表面 | 当前实现与参数 | 源码 |
| --- | --- | --- |
| 集成播放器标题条、操作行、音量条 | 原生 OpacityTransition / ScalarTransition，220ms；应用或系统关闭动画时设为 0 | [PlayerPage.xaml](../src/Momoka.Shell/Views/PlayerPage.xaml)，约 563、969、1048 行；[PlayerPage.Chrome.cs](../src/Momoka.Shell/Views/PlayerPage.Chrome.cs)，Render、FadeRail、FadeStrip |
| 详情顶部悬浮播放卡 | Toolkit 隐式显隐：显示位移 400ms、透明度 200ms；隐藏位移 200ms、透明度 100ms；位移终点 0 / −24 DIP | [DetailPage.StickyHeader.cs](../src/Momoka.Shell/Views/DetailPage.StickyHeader.cs)，SyncHeaderMotion、ClearHeaderMotion |
| 普通浮层与音乐播放栏 | SurfaceMotion：默认显示 180ms / 12 DIP，退出 140ms / 8 DIP；反向从当前值接续 | [SurfaceMotion.cs](../src/Momoka.Shell/Views/SurfaceMotion.cs)；[ShellPage.Music.cs](../src/Momoka.Shell/Views/ShellPage.Music.cs)，PlaceMusicBar |
| 媒体库筛选与视图设置 | 遮罩显示/退出 150/120ms；面板显示/退出 200/140ms，位移 12/8 DIP | LibraryPage.xaml.cs，相关 SurfaceMotion 调用 |
| 首页货架 | 默认 440ms / 20 DIP；货架错峰 65ms，序号上限 5；仅动已实现模板根 | [HomeMotion.cs](../src/Momoka.Shell/Views/HomeMotion.cs)，Enter、Reveal、TargetOf |
| 设置分类 | 180ms / 12 DIP，当前可见分组与排队代次协调 | [SettingsPage.Motion.cs](../src/Momoka.Shell/Views/SettingsPage.Motion.cs) |
| 设置从属行 | 160ms Storyboard；关闭原生 ItemContainerTransitions，避免重叠 | [SettingsRowsMotion.cs](../src/Momoka.Shell/Views/SettingsRowsMotion.cs) |
| 设置优先项排序 | 380ms 的现有排序动效 | [SettingsPriorityMotion.cs](../src/Momoka.Shell/Views/SettingsPriorityMotion.cs) |

原生属性过渡在属性变更时补间；Toolkit Show/Hide 在显隐切换时启动动画；SurfaceMotion 还承接退出回调。三者职责不同，不能仅凭“隐式”名称替换。尤其不能丢掉退出后收起容器、关闭命中和归还焦点的现有逻辑。

StickyHeader 注释中的原始参考来源是 Gallery 2.10.0 / `7b4314a`；本文另外核对的 `6de91fe` 是比较依据，不改写其复制来源。

### 3.3 页面呈现与图标

浏览 Frame 当前显式 Suppress，随后由 BrowsePresentation 播放 180ms、透明度 0.8→1 的轻淡入。该类同时负责首帧后放行图片，动效只是其中一项职责。首页自己还有货架入场，媒体库 SearchSurface 自带 220ms / 8 DIP 局部入场；新增整页位移必须逐路排除叠加。

文档编写基线的侧栏设置齿轮使用 [ShellPage.xaml](../src/Momoka.Shell/Views/ShellPage.xaml) 中的 AnimatedIcon 和内置 AnimatedSettingsVisualSource，静态回退为 FontIconSource。[ShellPage.Sidebar.cs](../src/Momoka.Shell/Views/ShellPage.Sidebar.cs) 的 SyncSidebarSettingsIcon 在不允许动画时清空 Source。

本次对应用源码的检索没有发现业务使用 AnimatedVisualPlayer、Lottie 动画文件或自写 CreateEffectFactory。这里指应用显式使用，不推断框架或系统材质内部没有合成效果。

## 四、施工顺序与并行边界

| 批次 | 产出 | 依赖 | 状态 |
| --- | --- | --- | --- |
| S0 | 刷新源码、路由、测试和测量基线 | 无 | 已完成，重复性能/资源采样留到交付 |
| S1 | 关联动画计时、回退原因和完成验收 | S0 | 已实施，原生收尾结果见第八节 |
| S2 | 有限普通前进路由的原生过渡与单一动效归属 | S1；先有可靠计时与回退 | 已评估，候选不采用 |
| S3 | 隐式/局部动效的中断、命中、焦点和资源回归 | S0；涉及外壳时等待 S2 | 已实施/核对保留，真实输入和资源测量待交付 |
| S4 | 合并记录与最终统一验收 | S1～S3 | 收尾中；交付另行授权 |

S1、S2 共用 Frame、BrowsePresentation 和关联票据，顺序实施。S3 的只读审查或独立测试分析可以并行；修改同一文件、共享桌面验证和最终发布必须按项目规则协调。每批留下单独提交与可回滚边界，不堆成一次大规模动画替换。

## 五、逐批施工说明

### S0：刷新基线，确定测量场景

**输入文件**：CLAUDE.md、本文、Shell 项目文件、第三节全部生产文件、[开发与验证](开发与验证.md)、[WinUI 验证矩阵](../.claude/skills/momoka-verification/references/winui-validation.md)。

**执行步骤**：

1. 记录当前工作树、HEAD、未提交改动和包声明。和本文基线有差异时只追受影响调用链，不照旧行号直接编辑。
2. 从真实调用点列出前进、返回、历史前进、面包屑跳回、账号切换及导航失败的路径；同时记录该页面自己的入场动画。
3. 建立匿名本地图片/假 HTTP 场景：热缓存、目标延迟 Loaded、图片晚到、图片失败、容器回收、快速 A→B→C。不能用真实观看记录当测试夹具。
4. 先成功编译当前受影响测试，再运行现有专项获得基线；记录失败、跳过和覆盖限制。所需命令以当前开发文档为准，不运行旧二进制冒充当前通过。
5. 对首帧、Prepare→TryStart、重复导航后的资源、滚动和响应时间建立同机对照。没有测量能力的项目标注待验，不先承诺帧率、功耗或百分比提升。

**完成条件**：有当前源码与依赖清单、路由表、测试结果和测量配置；明确原生验收要在用户要求交付时补齐哪些项目。S0 不是要求为文档编写启动这些检查。

### S1：关联动画的计时、回退原因与完成验收

**主要文件**：CardDetailTransition.cs、PersonTransition.cs、BrowsePresentation.cs、DetailPage.xaml.cs、PersonPage.xaml.cs；[NavigationTiming.cs](../src/Momoka.Core/Diagnostics/NavigationTiming.cs) 作为已有诊断入口，优先复用其能力，不另建通用动画管理器。

**需要改变的行为与诊断**：

1. 在每张关联票据内保存实际准备成功的单调时间戳，记录 TryStart 结果、Completed、取消及超时。前进和返回分别记录，不能用点击耗时替代 Prepare→TryStart。
2. 扩展现有 receipt / Observe，使前进准备超时与普通不满足条件能区分。文档编写基线的 CardDetailTransitionReceipt 只有 Direction、Started、Completed、TimedOut；准备超时可能与普通失败给出相同读数。文档编写基线的 PersonTransition.Observe 是 Action<bool>，Completed 路径只清理而不发布完成结果：需改为结构化生命周期回执，在清空观察者之前结算终态，并同步人物探针的 List<bool> 与相关测试。
3. 用固定原因集合表达回退：例如 Disabled、SourceUnavailable、TargetUnavailable、IdentityChanged、PrepareFailed、StartRejected、PrepareTimeout、CompletionTimeout、NavigationCancelled。最终名称可贴近现有类型；诊断不记录账号、地址、令牌或媒体名称。Prepare 因守卫或平台异常直接返回 false 时尚无票据，需通过轻量 Prepare 结果/直接诊断出口报告，不为观察失败而伪造活动票据；不把所有 false 都误记为平台拒绝。
4. 既有 NavigationTiming 有严格阶段名称和长度限制。扩展时保持该限制，诊断写入失败不能破坏导航；没有开启采样时不创建持续定时任务。
5. 保留来源小图立即交接、高清不降级及图片身份更正后取消。若测得元数据请求的同步前段影响启动，先检查能否安全把已有图交接提前；不能为了动画阻塞业务数据、跳过身份核对或永久冻结低清图。
6. 正常场景以准备后约 250ms 内启动作为设计与测量目标。700ms 仍只是等待目标的容错上限；不把它改称允许等待时间。先记录实际数据，再判断是否收紧超时，不能仅为达标提前失败。
7. 补人物动画的原生 Completed 验收。文档编写基线的人物探针只证明 Started 和目标有照片；保持人物返回的普通导航，不为补功能强行重建/定位旧源卡。

**测试工程**：Momoka.ConnectedAnimationTests、Momoka.BrowsePresentationTests、Momoka.DetailImageArrivalTests、Momoka.PageContractTests。探针修改位于 [ShellNavigationProbe.ConnectedAnimation.cs](../src/Momoka.Shell/ShellNavigationProbe.ConnectedAnimation.cs) 与 [ShellNavigationProbe.Person.cs](../src/Momoka.Shell/ShellNavigationProbe.Person.cs)。

**必须增加或收紧的用例**：

- 准备超时、启动拒绝、完成事件缺失、导航取消产生不同原因；每次只结算一次。
- 快速导航使旧事件、旧计时和旧完成回调失效；来源图保留释放一次，不影响新票据。
- 中途关系统/应用动画立即恢复可读界面；失败回退不重新导航、不重复请求数据。
- 已有高清不降级、错误 Primary 更正、单集不同图与回收换项继续正常回退。
- 计时字段非负、阶段顺序正确；用受控时钟验证逻辑，真实 250ms 目标留给运行测量。

**原生验收**：海报前进、正式返回和人物前进均读取 Started/Completed；同时收集准备耗时与回退原因。延迟夹具必须正常回退。运行时记录操作开始、首内容出现和动画启动三个时点，不能只看最终截图。

**完成条件**：新增诊断可区分失败原因，源码测试通过；声明过的原生结果有当次证据。因正常守卫回退不视为功能错误，不能以动画成功率为由放宽守卫。

### S2：有限普通导航的原生页面过渡候选

**主要文件**：ShellPage.xaml、ShellPage.xaml.cs、ShellPage.Music.cs、BrowsePresentation.cs；必要时在 Shell/Views 下增加小型导航动效选择类型。Frame 导航是 UI 职责，不把 WinUI 过渡对象或页面类型搬进 Core。

**已核对的 API 边界**：当前 `Microsoft.WindowsAppSDK.WinUI/2.3.9` 的 `Microsoft.UI.Xaml.winmd` 中，NavigatingCancelEventArgs.NavigationTransitionInfo 与 PageStackEntry.NavigationTransitionInfo 只有 getter；Frame.GoForward 只有无参方法。不能在 Navigating 里替换过渡对象、直接改历史条目的该属性，或调用不存在的 GoForward(transition) 重载。包版本变化后重新核对。

**默认路由矩阵**：

| 路由/条件 | 计划动作 |
| --- | --- |
| 系统或应用不允许动画 | Suppress，所有当前动效落定，不跑普通淡入 |
| 具备有效封面/人物关联票据的前进 | Suppress，关联动画接管；启动失败才用现有轻淡入 |
| 普通顶层 LibraryPage / MusicLibraryPage 前进，且不是搜索 | 原生 Entrance 的首批候选；满足下述门槛后采用 |
| HomePage、搜索页、设置分类、订阅详情等已有独立入场 | 本批保持当前动效归属；不自动扩大原生过渡范围 |
| OpenChild、OpenDetail、OpenPerson、OpenMusicContainer 下钻 | 本批保留现状；未采用关联时仍正常呈现。DrillIn 留待单独需要时评估 |
| 返回与面包屑跳回 | 显式 Suppress；已有 BeginReturn / CompleteReturn 继续处理关联返回，失败用现有轻淡入 |
| 历史前进 GoForward | 明确核对历史条目携带的过渡和当前开关，不遗漏；不能假设已有可用重载或能在事件后覆盖 |
| 首次启动、账号切换、失败恢复 | 保持快速可用与当前呈现，不为补动画延迟首屏 |

**必须按顺序处理的实现细节**：

1. 将当前无参数 BrowseTransition 的“总是 Suppress”选择，改为能看见目的页面、请求类型、导航方向与动画偏好的显式策略；先只开放矩阵中的候选路由。
2. 在发起导航前确定本次动效计划，并在对应 Navigating/Attach 流程中消费一次；取消、失败和下一次导航必须撤销旧计划。至少分清普通原生、关联待决、关联成功、关联失败回退，并保留普通自定义淡入和无动画分支。不要用一个跨导航残留的 bool 控制所有页面。
3. BrowseTransition 的参数求值早于 Frame.Navigating。前进 Prepare 已在调用方做完，返回 BeginReturn 却在 Navigating 才执行。本批保留所有返回 Suppress，不能提前误选 Entrance 再指望返回事件消除叠加。
4. 分离 BrowsePresentation 的“首帧/图片门控”和“普通透明度动效选择”。原生候选成功接管时不播放其 180ms fade；门控、代次、释放、低优先级图片放行全部保留。不能删除整个类。
5. 关联动画未能启动时，当前这次导航已经选择 Suppress，不应二次 Navigate 或再叠一套原生 Entrance；直接走现有 180ms 普通回退。已经启动之后的中断、完成超时或关闭动画按 S1 清理并落定，不因缺少 Completed 再把已显示页面淡入一遍。
6. 原生 Entrance 路由不得再让相同内容同时跑 HomeMotion、SearchSurface 或其他整页入场位移。保留局部加载反馈，但明确每个属性由谁控制。
7. 当前 GoForward 依赖历史条目，不迁移为新 Navigate。若普通前进写入了原生过渡条目，在运行中关闭动画后“返回→历史前进”必须测试。若无法通过公开支持路径避免重放旧 Entrance，受影响候选继续保留当前方案，不用清历史栈、重建页面、伪造 setter 或私有 API 绕过。
8. 保留滚动位置、焦点、面包屑、选择项和账号归属。动效选择不能改变导航缓存/数据刷新语义，也不能通过多发 HTTP 或强制实现屏外元素准备动画。
9. 当前 ContentFrame 没有显式 NavigationThemeTransition。结合 Gallery 的 Frame XAML 和当前框架模板/默认行为核对公开的 ContentTransitions 配置，按需增加 NavigationThemeTransition 并验证原生效果。传入 Entrance 对象本身不能证明动画已生效。
10. [ShellNavigationProbe.Foundation.cs](../src/Momoka.Shell/ShellNavigationProbe.Foundation.cs) 约 178、215 行目前要求导航历史内为 Suppress。采用候选时把断言收紧为“该路由应有的具体过渡”，保留历史栈和页面参数检查；不删除断言或把任意过渡都算通过。[ShellNavigationProbe.HomeReturn.cs](../src/Momoka.Shell/ShellNavigationProbe.HomeReturn.cs) 中普通返回、播放返回的入场抑制断言继续保留。

**采用原生候选的门槛**：

- 先比较同一匿名场景的导航首内容时间，原生过渡不能引入等待旧页退场后才显示新内容的可见停顿。
- 快速切页、点击返回、动画中关闭应用/系统开关都能立即得到正确可操作页面。
- 没有父页面与子内容双重移动、重复淡入、残留透明度或丢失焦点。
- 历史前进、切账号和导航失败后计划没有串到下一次导航。

若门槛不满足，保留当前 Suppress + 轻淡入，记录具体路由、API/运行证据和不迁移原因；保留原方案可以是该候选的完成结论，不能记为“原生迁移通过”。不降低原有输入、动画偏好或首帧要求。

**测试工程**：Momoka.BrowsePresentationTests、Momoka.NavigationStateTests、Momoka.PageContractTests、Momoka.HomeFoldCompletionTests；若需要新策略测试，在 Shell 可测试边界链接真实选择逻辑，不写与生产平行的模拟实现。

**关键断言**：每条矩阵路由的选择正确；原生路由不启动自定义 fade；关联成功不叠层、失败只回退一次；Loaded 前后顺序正确；页面门控仍放行；主页返回不重播货架；新导航不能继承旧计划。

**原生验收**：LibraryPage、MusicLibraryPage 热/冷进入、搜索切入、返回/前进、面包屑、快速来回、动画开关中途变化；按服务分别记录影视媒体库，共用音乐页按实际来源能力记录。只确认了某一种服务器的界面，不能宣称其他服务都覆盖。

### S3：隐式、浮层和图标的状态收尾

**主要文件**：DetailPage.StickyHeader.cs、SurfaceMotion.cs、HomeMotion.cs、SettingsRowsMotion.cs、SettingsPriorityMotion.cs、SettingsPage.Motion.cs、ShellPage.Music.cs、ShellPage.Sidebar.cs。播放器仅核对 PlayerPage.xaml / PlayerPage.Chrome.cs 的现有原生透明度路径；发现需要改播放相关行为时按对应验证边界处理。

**执行步骤**：

1. 为每个表面写清目标属性与唯一控制者。已有表现正确、测试充分的部分保持实现，不为了使用同一 API 重写。
2. StickyHeader 保留显隐组和当前参数；快速显示→隐藏→显示从当前帧接续，清理顺序保持 Translation 注册后停钟再写终值。隐藏时立即取消命中，必要时归还原播放按钮焦点。
3. 筛选、视图设置与音乐栏保留 SurfaceMotion 的退出回调和代次。关闭过程中再次打开，旧退出不能把新内容折叠；音乐栏的内容避让必须与最终显示状态一致。
4. HomeMotion 只作用已实现的模板根，不触碰 Repeater 自己安排的布局属性；回收、换项和离页恢复透明度/位移。历史属性占用注释不能推广为“Composition 一律不支持”，但迁移前必须核对当前属性所有权。
5. 设置行仍由现有单一实现处理显隐/排序；不额外开启 AddDelete、Reposition 或全局隐式位置动画。快速变更不重建整个 ItemsSource，不丢滚动和键盘焦点。
6. 应用与 Windows 动画偏好任一关闭都立即落定；重新开启不重播离页动作。只在需要的生命周期内观察设置，关闭窗口、卸载和源替换后检查订阅及排队回调。
7. 设置齿轮继续使用 AnimatedIcon 的状态与 FontIconSource 静态回退；键盘与指针状态都可读。动画不承担按钮名称、确认状态或命令可用性的唯一表达。
8. 播放器现有 220ms 原生透明度在零时长时必须有正确终值与命中，不延伸为视频帧滤镜或独占 uosc 改造。

**测试工程**：Momoka.UiMotionTests、Momoka.DetailStickyHeaderTests、Momoka.SettingsRowsMotionTests、Momoka.SettingsPriorityMotionTests、Momoka.SettingsProbeContractTests、Momoka.PageContractTests。音乐栏测试在当前音乐/页面夹具内扩展；播放器修改时重新选择实际适用测试，不把浏览测试当播放验证。

**验收动作**：同一浮层连续开关、滚动跨悬浮阈值往返、显示/退出中关闭动画、Tab 到按钮再隐藏、页面离开并重入、可见容器回收换项、窄宽窗口反复切换。即时命中状态、最终值、焦点、订阅与动画画面分别核验。

**完成条件**：问题有真实生产路径上的回归测试；已有正确行为不退化。无差异的表面记录“核对保留”，不为形成提交而改参数。未能完成原生验证的部分在交付台账保留待验，不能以假时钟证明 GPU 补间。

### S4：合并与最终统一验收

1. 每批受影响代码与测试编译成功、相应测试通过后提交。独立树完成后合入 master，登记批次，清理已合并的本任务临时分支与工作树。
2. 默认只合并，不刷新发布目录和快捷方式；用户明确要求交付时，在主树最终整合版本对待交付范围做一次统一验证。
3. 原生验证使用当前探针真实注册的分区或 inspect 项，不凭文件名猜命令。新增或修改探针的成功与失败路径须当轮实际运行，不能一律留到交付再验。
4. 若改依赖、版本或分析器，按 CLAUDE 完整四道闸门执行；这不是本施工范围的默认动作。
5. 记录最终提交、实际通过/失败/跳过、运行配置、截图/帧证据、资源采样和未覆盖项。保存失败证据，不删检查或放宽条件来取得绿色结果。

## 六、验收矩阵与证据要求

### 6.1 必须覆盖的情况

| 编号 | 场景 | 必须观察到的结果 |
| --- | --- | --- |
| A01 | 海报前进与有效源返回 | 图像对应正确，Started 与 Completed 分别成立，没有重复整页运动 |
| A02 | 演职人员到人物页 | 接住原照片、启动和完成均有证据；后到高清正常替换 |
| A03 | 目标慢、缺图、图更正、容器回收 | 原因明确，普通导航可用，来源/目标不残留冻结图 |
| A04 | 连续 A→B→C、动画中返回 | 旧回调不污染新页，历史栈、焦点和滚动正确 |
| A05 | 中途关应用动画/Windows 动画 | 当场落定，透明内容不截获输入，重新开启不重放旧动作 |
| A06 | LibraryPage / MusicLibraryPage 普通前进 | 按矩阵选择；最多一个主要页面过渡，首屏没有可见退场等待 |
| A07 | Home、搜索、返回、历史前进、面包屑 | 独立入场与外壳过渡不冲突，缓存/滚动/选择不变 |
| A08 | 悬浮卡、筛选面板、音乐栏反复开关 | 反向连续，旧退出不折叠新内容，命中与焦点符合可见状态 |
| A09 | 列表追加、筛选、排序、回收 | 保持项身份和焦点，不为动画创建屏外控件，不保留半透明复用项 |
| A10 | 窄窗、短窗、缩放和文字放大 | 目标可见时才关联，不遮关键操作，焦点和弹层边界可达 |
| A11 | 离页、窗口关闭、重复导航 | 动画对象、图片保留、事件和队列无持续累计 |
| A12 | 失败注入 | Prepare/TryStart/调度/源创建失败仍有静态内容或普通呈现；业务不被动画阻塞 |

建议先用 1240、760、600 DIP 的代表窗口，按实际屏幕能容纳的大小调整并记录；360 DIP 是探针压力尺寸，不当作当前主窗口最低尺寸承诺。跨 DPI、200% 文字、高对比度、键盘和 Narrator 分别记录；硬件或环境不足时列为覆盖缺口。

用户的图片限制继续适用：输入图片累计达到 40 张时自动暂停，不继续增加图片输入。优先使用状态读数、日志和少量代表帧解释差异，但截图不能替代中间帧、计时或事件证据。

### 6.2 测试与运行证据不能互相替代

| 现有工程/证据 | 可以证明 | 不能证明 |
| --- | --- | --- |
| [ConnectedAnimationTests](../tests/Momoka.ConnectedAnimationTests/Momoka.ConnectedAnimationTests.csproj) | 原样生产票据、身份/图片、取消、超时与回退控制流 | 原生合成、实际启动速度和完整中间帧 |
| [BrowsePresentationTests](../tests/Momoka.BrowsePresentationTests/Momoka.BrowsePresentationTests.csproj) | 首帧门控、低优先级放行、关动画和关联互斥控制流 | 实际首内容可见时点、WinRT 执行上下文和解码速度 |
| [UiMotionTests](../tests/Momoka.UiMotionTests/Momoka.UiMotionTests.csproj) | 生产动效的生命周期、终值、代次和回收规则 | GPU 帧率、真实输入和读屏结果 |
| [DetailStickyHeaderTests](../tests/Momoka.DetailStickyHeaderTests/Momoka.DetailStickyHeaderTests.csproj) | 原样悬浮卡逻辑、注册顺序、阈值、参数、焦点/命中请求 | Toolkit/Composition 实际逐帧结果和材质像素 |
| 设置各动效专项 | 当前生产逻辑配可控时钟/队列的反向、离页和旧回调 | 原生排版、真实滚动/拖拽和功耗 |
| 实屏/帧序列 + 原生日志 | 该次配置下的可见效果与实际事件 | 其他机器、未跑的服务、后端或管线 |
| 同场景性能/资源采样 | 被测配置下的前后对比 | 无测量配置的笼统“更流畅”“零开销” |

性能采样建议对同一场景各记录至少 20 次暖导航与 5 次冷启动导航，保留原始值、p50/p95/最大值和失败/回退原因。此数量是本计划的采样建议，不是微软规范；不将不可控桌面耗时直接写成易抖动的单元测试阈值。

在 S0 的同机重复测量后确定首内容时间与资源的可接受波动带，S2 采用前记录阈值与理由。观察到持续劣化或肉眼可见等待时不采用该候选。不能先捏造一个百分比，再调整样本或回退条件凑达标。

### 6.3 运行安全与归属

默认使用匿名假 HTTP、临时设置和本地素材，不登录真实媒体库，不执行播放、进度上报、收藏/已看写入。只读导航也可能联网，必须按实际数据来源标注。

集成播放器改动按 [播放技能](../.claude/skills/momoka-playback/SKILL.md) 与 CLAUDE 选择隔离本地探针；当前集成探针不证明独占窗口、uosc 或外部 mpv 覆盖。真实服务器播放和有副作用的验证仍需已有的明确授权范围。

同桌面检查串行；资源被占用时按项目规则轻量确认、等待释放后继续，不结束其他会话进程。屏幕和系统偏好不为测试而静默改动。

## 七、暂缓项与重新进入施工的条件

### 7.1 AnimatedVisualPlayer

当前没有需要逐帧播放、反向、定位或循环的大型动画业务。本批继续使用原生控件状态和已有 AnimatedIcon。

以后只有出现明确的一次性状态反馈或业务插画需求时再新增任务。任务必须写清触发事件、资产来源/许可、静态回退、关闭动画行为、离页停止、源替换和资源释放。按钮图标的悬停/按下优先评估 AnimatedIcon，不把它们改造成独立播放器。

Gallery 的具体 LottieLogo1 生成源码使用 CanvasPathBuilder / CanvasGeometry，项目引用真实包名为 Microsoft.Graphics.Win2D；直接复制该资源会产生对应依赖。AnimatedVisualPlayer 原生控件本身不等于所有使用方式都必须新增 Win2D 或运行时 Lottie 包。要按选定资产和生成路径核对，不能直接套包名或估计体积。

官方文档说明 Pause 不会结束 PlayAsync；离页只暂停并等待该 Task 可能无法收尾。未来必须区分暂停、停止、异步完成与资源释放。Gallery AVP 示例文字提到图片回退，但本次固定 XAML 未见显式 FallbackContent；不能假定复制它就已有可靠回退。

### 7.2 Composition Effects

它用于图像效果与可动画的效果参数，不等同于位置/透明度动画。现有 Acrylic/Mica 的存在，也不等于应用已经有自定义效果链。

以后只有原生材质、普通画刷和已有图片处理无法满足明确表面需求时再评估。任务应限定输入纹理/表面、效果类型、面积、变化频率、GPU/内存成本、设备/窗口生命周期与静态回退。

不能从 Gallery 的弹簧缩放/表达式动画推导出滤镜能力；不能由效果支持推导出跨 Composition tree 取样、对 mpv 视频实时模糊或私有渲染注入可用。此固定 Gallery 路由没有独立 Composition Effects 页面，不推广为所有版本都没有。

当前没有实际新增发布件，不给包体积、GPU 或耗电的数字承诺。将来若新增 Win2D、Lottie 或其他包，另批核对依赖图与发布差异，遵守稳定包与四道闸门要求。

## 八、执行记录与交接格式

每批结束时更新下表，并在 [PROGRESS.md](../PROGRESS.md) 与 [待交付批次](待交付批次.md) 记录实际状态。只填执行过的结果；文档写完不勾选产品施工完成。

| 批次 | 产品提交/合并提交 | 受影响测试实际结果 | 原生/性能证据 | 未覆盖与后续动作 | 当前状态 |
| --- | --- | --- | --- | --- | --- |
| S0 | 基点 `63ef01a`，整合 `d711cbe` / master `ee3d23c` | 当前专项基线见验收记录 | 匿名图片/假 HTTP、实际 XAML 岛与同机对照配置已建立 | 暖/冷重复导航及资源采样待交付 | 已完成 |
| S1 | `cad4f2e`、`3bc3cde` | Connected 61/61；Browse 32/32；图片26/26 | 海报前进/返回、人物唯一 Completed 已实测，原生motion5/5 | 海报首次600.9283ms超过250ms目标；返回209.1449ms，人物45.6615ms；重复性能/系统中断/跨DPI待验 | 已实施 |
| S2 | `b03668b`、`5669199` 与工具收尾 | Browse 32/32；Navigation 10/10；HomeFold 71/71 | Entrance 仅探针候选；中途关闭实帧仍移动 | 保留 Suppress + 原轻淡入，未采用原生迁移；详细门槛/历史见验收记录 | 有依据保留原方案 |
| S3 | `4b9ed43` | UiMotion80/80；Priority35/35；Sticky58/58；Rows46/46；SettingsProbe36/36 | motion5/5；悬浮卡真实客户区/页宽1000、760、360一致，静态截图已目视 | 其他表面核对保留；物理输入/Windows事件/资源测量待交付 | 已实施/核对保留 |
| S4 | 原生工具 `5669199`、`bbb9509`、`49714bc`，最终合并待填 | PageContract276/276；工具29/29；Shell0警告0错误 | 首轮3/5与隐藏启动失败原档保留；可见motion5/5，PID33304 | 合入后登记待交付、清本任务辅助树；发布另行授权 | 收尾中 |

原始施工任务模板：“按 WinUI动效施工文档.md 从 S0 开始，依次完成默认批次，暂缓项不做；分支完成后按项目规则合并，交付另行要求。”用户已开始本次施工；模板保留供复核范围，不能代替下面记录的执行事实。

## 九、官方文档与固定源码索引

官方文档于 2026-11-14 核对。实现时如页面或包版本变化，重新核对受影响部分。

| 来源 | 本文使用的结论 |
| --- | --- |
| [Microsoft Learn：Connected animation](https://learn.microsoft.com/en-us/windows/apps/develop/motion/connected-animation) | 相同元素保持导航联系；前进 Gravity、返回 Direct；尽快启动、不等待网络结果；关联时压制默认导航过渡 |
| [Microsoft Learn：Page transitions](https://learn.microsoft.com/en-us/windows/apps/develop/motion/page-transitions) | Entrance 对应顶层切换，DrillIn 对应深入，Slide 对应横向相邻；Suppress 可用于自有过渡 |
| [Microsoft Learn：AnimatedVisualPlayer](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/animated-visual-player) | 可控播放与状态图标的分工；Pause 与 Stop/PlayAsync 的区别 |
| [Microsoft Learn：Composition effects](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-effects) | 效果描述、效果画刷、动态属性和表面/树边界；具体支持按当前 API 核对 |

下列 Gallery 链接全部固定到同一提交，读取的是可执行 XAML/code-behind/依赖文件，不能只拿页面展示片段代替：

| 范围 | 源码 |
| --- | --- |
| 版本与真实包名 | [standalone.props](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/standalone.props)、[WinUIGallery.csproj](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/WinUIGallery.csproj) |
| 关联动画页面 | [ConnectedAnimationPage.xaml](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Samples/ConnectedAnimation/ConnectedAnimationPage.xaml)、[code-behind](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Samples/ConnectedAnimation/ConnectedAnimationPage.xaml.cs) |
| 关联源/目标 | [SamplePage1.xaml.cs](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/SampleSupport/SamplePages/SamplePage1.xaml.cs)、[SamplePage2.xaml.cs](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/SampleSupport/SamplePages/SamplePage2.xaml.cs) |
| 原生隐式属性过渡 | [ImplicitTransitionPage.xaml](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Samples/ImplicitTransition/ImplicitTransitionPage.xaml)、[code-behind](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Samples/ImplicitTransition/ImplicitTransitionPage.xaml.cs)、[UIHelper.cs](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Helpers/UIHelper.cs) |
| 页面过渡 | [PageTransitionPage.xaml](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Samples/PageTransition/PageTransitionPage.xaml)、[code-behind](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Samples/PageTransition/PageTransitionPage.xaml.cs) |
| 可控动画图形 | [AnimatedVisualPlayerPage.xaml](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Samples/AnimatedVisualPlayer/AnimatedVisualPlayerPage.xaml)、[code-behind](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Samples/AnimatedVisualPlayer/AnimatedVisualPlayerPage.xaml.cs)、[LottieLogo1.cs](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Assets/AnimatedVisuals/LottieLogo1.cs) |
| 状态图标 | [AnimatedIconPage.xaml](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Samples/AnimatedIcon/AnimatedIconPage.xaml)、[code-behind](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Samples/AnimatedIcon/AnimatedIconPage.xaml.cs) |
| 动画互操作与路由 | [XamlCompInteropPage.xaml.cs](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/Samples/XamlCompInterop/XamlCompInteropPage.xaml.cs)、[ControlInfoData.json](https://github.com/microsoft/WinUI-Gallery/blob/6de91feccadfbb0a796b71caedead5a2284f730e/WinUIGallery/SampleSupport/Data/ControlInfoData.json) |

不同目标的协调入场可以同时存在；Gallery 的 SamplePage2 就有协调内容。本文要求避免的是同一属性被多方驱动，以及同一页面不必要的重复整体运动，不能简化成“整个页面只能有一个动画”。
