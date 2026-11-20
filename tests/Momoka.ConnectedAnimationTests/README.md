# 海报到详情与演职人员到人物页的原生关联动画回归

2026-11-20 主页回收海报：准备来源与返回落点按当前 Frame 页面子树、同一 XamlRoot、
真实图片与已排版可见区域判断，不再单凭 IsLoaded=false 拒绝仍在屏上的卡片。
新增四项覆盖前进、返回、脱树残留与八种无效来源；原“返回未上树”用例改为实际清空
XamlRoot 后恢复，保留原布局等待。旧代码先复现三项失败，修复后本专项 **87/87**、
0 失败 0 跳过。目标页 Loaded 等待、圆角表面、身份／图片版本与超时不变。
这些受控边界不证明用户现场就是同一触发原因，也不替代原生中间帧与真实鼠标验收。

2026-11-16 悬停标题交接等待：原样链接 `PreviewHeadingTransition.cs`、整份 `DetailPage.PreviewHeading.cs` 和
`PreviewHeadingPlacement.cs`，只替换Frame／布局／Storyboard／位图与系统事件边界。新增19项，当前 **82/82**、
0失败0跳过：新目标未挂树时保留原700ms票据，生产Loaded先于后订观察者起跑；同页一次、布局重试、替票与迟到
Loaded／Timer／系统事件、Frame当前目标／身份／owner／来源资格、离页与动画关闭均清理。真实标题与徽标同步接同版图、
共用320ms时钟，高清不被源图覆盖，完成／取消还原两个元素；强制布局中同步换身份或Release不得遗留旧等待。
这些是生产控制流和终值测试，不能证明实际WinUI事件顺序、GPU补间、冻结中间帧或屏幕连续性；主树仍须运行原
`InspectPreviewHeadingMotion`，不延长探针、放宽断言或替换原失败结果。

2026-11-15 起跑延迟修复：`Attach` 先 `SeedConnectedLayoutWidth` + `ForceConnectedLayout`，目标已排过版时
`TryStartEarly` 在目标页 `Loaded` 之前直接启动；`LayoutUpdated` 在尚未 `Loaded` 时也先抢跑。新增「已排过版时
在新页 Loaded 前立即起跑」「第一次布局落定即可起跑」两项，原「目标未排过版不抢跑」「新页未 Loaded 时图片更正
不打断 Frame 创建」边界保留；当前专项 **63/63**、0 失败 0 跳过。

2026-11-15 S1：当前专项 58/58，0 失败 0 跳过。原样链接两个生产票据、`ConnectedMotionReceipt.cs` 与 `NavigationTiming.cs`；新增十项覆盖准备成功后的单调计时、前进/返回独立起点、Prepare 调用耗时排除、实际成功 TryStart 返回时点、固定匿名失败阶段、每次唯一终态、人物 Completed/完成超时、快速导航和观察回执时重入。诊断 sink 抛错不影响普通导航，Prepare 守卫失败不建立虚假票据或额外 Timer。已启动后的取消/完成超时继续落定，不发送 `started=false` 页面回退。

回执的 `PrepareToStartMs`：原生成功时从 `PrepareToAnimate` 返回到 `TryStart(true)` 返回；平台拒绝时记录到该次 TryStart 调用入口；完全没有尝试时为 null。`ElapsedMs` 从对应准备成功起点到该阶段，不混入点击耗时；700ms 是准备等待上限，1200ms 是生命周期保护。本专项只使用可控单调时钟与原生边界，不能证明实际 ≤250ms、GPU 补间或跨 DPI。

人物原生探针现在等待唯一 Completed 并保存 `person-transition.json`；海报探针收紧前进/返回唯一终态、计时与旧图/取消/缺源原因。须由本轮运行验证读取实际证据，源码接线不等于原生验收通过。原生运行由主任务串行执行。

2026-11-13：按 WinUI Gallery `6de91feccadfbb0a796b71caedead5a2284f730e` 的 `ItemsRepeaterCollectionPage.xaml.cs` → `DetailedInfoPage.xaml.cs` 保留原生准备／Suppress导航／目标图片启动顺序。前进默认 Gravity，返回 Direct；同 Primary 源图立即交接，不等高清请求，已有高清图不降级。当前 48 项，0 失败 0 跳过：海报→详情的身份、目标、超时、取消、减少动画、返回与平台事件异常继续覆盖；新增人物卡→人物页 8 项，覆盖可见／当前账号／Primary 前件、目标照片交接、人物身份与目标可见性回退、导航停止／失败、应用与系统动画关闭、快速重入和高清不降级。

原样链接 `Views/CardDetailTransition.cs` 与 `Views/PersonTransition.cs`，只把 XAML 树、实际图片、ConnectedAnimation API、Frame、源身份和系统事件替换为可控边界。人物票据只做前进：详情页没有缓存，返回时人物页和演职人员排都已重建，没有可靠来源；不为了动画缓存详情页或强制创建源卡片。原生退订抛错时仅确认本地资格撤销与普通回退，不能称 native 资源已释放。

真实匿名入口为 `--probe-shell inspect` 的 `InspectCardDetailConnected`，使用正式缓存 Page 导航和 Frame.GoBack，分别记录 returnTryStart/Completed/Timeoutfallback，覆盖真实详情前进、返回、图版本漂移前进回退、缺返回源、动画关闭和取消，保存 `card-detail-connected.json` 与实屏。人物入口为 `--probe-shell --probe-section person` 中的「人物页：演职人员卡到人物页关联动画」；历史启动/照片记录不替代本轮完整完成验收。该测试不证明 XAML 岛原生合成与视觉连续性。
