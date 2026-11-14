# 页面源码控制流契约

这个离线控制台项目编译链接实际 Shell 页面的 code-behind，不复制其 `Attach`／导航／释放逻辑。`PageBoundaries.cs` 只替代未启动的 XAML／WinUI 控件边界及外部数据属主，使这些方法可以在没有窗口和真实服务器的测试进程中运行。

WinUI P3 多选栏直接链接 `LibraryPage.SelectionInput.cs`：有原生浮层或 CommandBar 溢出时 Escape 交给平台，关闭后才退出选择或取消剩余。六个控制流场景和一项 XAML 主次操作接线检查不证明真实键盘、焦点或溢出像素；`InspectLibraryBatchKeyboard` 使用实际菜单和外部键盘输入补齐该边界。

WinUI P4详情副行3项直接链接`PosterCard.Caption.cs`，真实CardItem驱动人员/媒体/库卡/Upcoming、OnScrim开关及同容器换型；解析实际Caption视觉状态和Dark/Light/HighContrast资源链，错误首页角色覆盖副行的反例必须拒绝。控件样式和VisualStateManager只记录值；实际状态优先级/像素由`InspectDetailResponsive`及原`RequireCardInk`验证，不把离线210项通过当作原生红项已消失。

- 当前覆盖：订阅列表导航 Tag／重入／释放；诊断 VM 快照与通知交接；真实 CardItem／PosterCache 的首图在飞换宽、旧下载和解码迟到、离屏与重复 Loaded、来源撤销及缓存宽度；R-014 同键坏缓存的有界恢复、无损错误分类、共享请求与迟到缓存保护。
- 订阅共享属主直接编译链接 `MoviePilotHomeSubscriptions`：受控时钟验证停留三分钟不再自动读取、重入首读与两个属主独立释放。VM 和时钟是边界替身，不代表真实网络或屏幕验证；实际主页停留 65 秒、手动刷新及连接恢复由 `HomeOrderAsync` 检查。
- P3 长剧测试编译链接真实 `CardShelf`、`CardCollection` 与 `CardItem`：2000 项计数与深处索引只创建被访问卡，删除未访问季、清理及 30 次重新填充不创建隐藏卡；非泛型 `IList` 增删改、移动、完整枚举和复制保留原语义。这里不证明 WinRT 的 `IBindableVector` 投影及 `ItemsRepeater` 布局访问数量，真实 Frame 检查由性能探针执行。
- 诊断页编译链接真实 `DiagnosticsViewModel` 与 `PageViewModel`，使用 Shell 已钉住的 `CommunityToolkit.Mvvm` 8.4.2 生成器，不升级依赖。`DIAGNOSTICS_VM_FIXTURE` 仅在本测试项目启用，主套件只登记 Core 日志测试。项目输出明确选 `Program` 为入口。
- 卡片测试以 `CARD_IMAGE_RESIZE_FIXTURE` 隔离，边界只替代 WinUI 值类型和位图解码；真实 Core 图片存储、SharedWork 和来源寿命仍执行。假图片 handler 只接受占位图片 GET，不运行登录或探测。R-014 继续使用真实图片存储与卡片源码，受控解码只用文本假字节，不证明实际 WinRT 的异常形态或画面。所有新夹具文件在本树测试输出下保留。
- 本项目的诊断 Core 部分与主套件重叠；同时报告两边结果时不能把重叠项累加成不同覆盖。
- 焦点测试编译链接实际 `FocusWatch`，用可控父子关系和事件边界验证海报／剧集行越过自身，选中外层 Button／ItemContainer、拒绝整张列表或滚动器、退订及重挂；设置名称测试解析真实模板与资源，验证行标签绑定不会被通用资源覆盖，并执行两种负向检查。真实 WinUI 焦点与 UIA 名称由 `--probe-shell --probe-section settings` 的「卡片焦点宿主与设置控件具体名称」用例补齐，不以这些边界测试代替。
- `library-anchor-page-fixture` 编译链接完整生产 `LibraryPage.Anchor.cs`，以真实 CardItem 和可控调度队列覆盖空库清理、读回内容后恢复、旧空库回调不清新锚点以及查询替换，并执行正式 ApplyPendingScroll 验证新请求零偏移会将旧滚动器归零。外层 VM、视觉坐标和 XAML 控件只作边界，不能证明真实 ItemsView 滚动或 Frame 布局；它与默认套件分别运行。用例数量以当次摘要为准（R16审查时默认137项），不能累加重叠覆盖。
- 使用与 Core 套件相同的互斥 passed／failed／skipped 运行器；不把预期修前失败放进正常产品套件。
- 主页顶栏运行判据直接链接 `HomeHeaderChromeProbeReadings`，覆盖三个状态以及残留按钮、系统三键、穿透区域、拖动区、非全宽背景与焦点回顶的失败输入；目标宽度还核对实际客户区像素、DPI/缩放和 Shell/Home 宽度，容差最多 1.5 DIP，拒绝控件一起停在旧宽。商店模式只检查全窗与 Shell，Home 内容区按侧栏布局收窄。清理失败测试直接执行 `HomeHeaderChromeProbeCleanup`，确保首项异常不阻止后续解绑与窗口/主题恢复，并保留失败状态；它不启动窗口。当前主树交付的实际滚动与原生区域由 `--probe-shell --probe-section shell --screen 1` 或 `InspectHomeHeaderChrome` 执行，回执 `home-header-chrome.json` 在断言前保留目标与实际宽度、偏移、焦点、原生高度及 Caption 区域；5 张实屏需交付会话目视。匿名播放与服务器写入均拒绝，不证明真实鼠标/物理 Tab、视频返回或关闭透明效果下的像素。
- 不读取用户设置、日志或凭据，不连接 MoviePilot、不创建窗口、不消费生产激活信号。
- **不能证明**真实 WinUI 布局、输入、滚动、Frame 返回／前进、服务可用性及渲染。此类验收仍使用适用的隔离 UI 探针，不能因为本项目通过而省略。
- 项目被加到方案，普通完整构建能编译它；运行须使用下面独立命令，普通 `Momoka.Tests` 运行不会自动运行此项目。

在当前工作树根使用用户级 SDK，先还原及编译当前代码：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.PageContractTests/Momoka.PageContractTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.PageContractTests/Momoka.PageContractTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.PageContractTests/Momoka.PageContractTests.csproj -c Release --no-build
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.PageContractTests/Momoka.PageContractTests.csproj -c Release --no-build --no-restore -- library-anchor-page-fixture
```

`CardPresentationTests` verifies the restored server titles, FnNas centered text and single-line card markup after the selective UI rollback.

The abandoned first-image case gates the actual shared download cancellation callback, rejects the
old immediate HTTP-token assertion while propagation is pending, and waits for the transport token
before delivering its late response. All original no-cache/no-decode/no-revival assertions remain;
card-waiter completion is not treated as completion of every linked cancellation callback.

## 2026-11-13实际卡片取图身份

CardArtworkIdentityTests原样执行CardItem.EnsurePosterAsync：冷下载记录Primary，释放图使引用不可用，热缓存/全新卡命中恢复真实引用；宽卡Thumb成功和Primary失败后Thumb回退准确记图种，不以首选候选猜图。新增4项后当前PageContract211/211，失败0、跳过0。该证据用于关联动画选源，仍不证明实际WinUI显示或动画帧。

实际原生研究在首次自定义Page Frame.Navigate崩溃，dump定位于关联Prepare前。改用已有LibraryPage正式导航与缓存返回，新增源契约核现有Page、真实realized源、原Page/Poster实例恢复、Started且Completed、匿名失败回退与禁止直接Content替换。当前212/212，真实运行由协调会话复验。
