# 页面源码控制流契约

这个离线控制台项目编译链接实际 Shell 页面的 code-behind，不复制其 `Attach`／导航／释放逻辑。`PageBoundaries.cs` 只替代未启动的 XAML／WinUI 控件边界及外部数据属主，使这些方法可以在没有窗口和真实服务器的测试进程中运行。

- 当前覆盖：订阅列表导航 Tag／重入／释放；诊断 VM 快照与通知交接；真实 CardItem／PosterCache 的首图在飞换宽、旧下载和解码迟到、离屏与重复 Loaded、来源撤销及缓存宽度；R-014 同键坏缓存的有界恢复、无损错误分类、共享请求与迟到缓存保护。
- P3 长剧测试编译链接真实 `CardShelf`、`CardCollection` 与 `CardItem`：2000 项计数与深处索引只创建被访问卡，删除未访问季、清理及 30 次重新填充不创建隐藏卡；非泛型 `IList` 增删改、移动、完整枚举和复制保留原语义。这里不证明 WinRT 的 `IBindableVector` 投影及 `ItemsRepeater` 布局访问数量，真实 Frame 检查由性能探针执行。
- 诊断页编译链接真实 `DiagnosticsViewModel` 与 `PageViewModel`，使用 Shell 已钉住的 `CommunityToolkit.Mvvm` 8.4.2 生成器，不升级依赖。`DIAGNOSTICS_VM_FIXTURE` 仅在本测试项目启用，主套件只登记 Core 日志测试。项目输出明确选 `Program` 为入口。
- 卡片测试以 `CARD_IMAGE_RESIZE_FIXTURE` 隔离，边界只替代 WinUI 值类型和位图解码；真实 Core 图片存储、SharedWork 和来源寿命仍执行。假图片 handler 只接受占位图片 GET，不运行登录或探测。R-014 继续使用真实图片存储与卡片源码，受控解码只用文本假字节，不证明实际 WinRT 的异常形态或画面。所有新夹具文件在本树测试输出下保留。
- 本项目的诊断 Core 部分与主套件重叠；同时报告两边结果时不能把重叠项累加成不同覆盖。
- 焦点测试编译链接实际 `FocusWatch`，用可控父子关系和事件边界验证海报／剧集行越过自身，选中外层 Button／ItemContainer、拒绝整张列表或滚动器、退订及重挂；设置名称测试解析真实模板与资源，验证行标签绑定不会被通用资源覆盖，并执行两种负向检查。真实 WinUI 焦点与 UIA 名称由 `--probe-shell --probe-section settings` 的「卡片焦点宿主与设置控件具体名称」用例补齐，不以这些边界测试代替。
- `library-anchor-page-fixture` 编译链接完整生产 `LibraryPage.Anchor.cs`，以真实 CardItem 和可控调度队列覆盖空库清理、读回内容后恢复、旧空库回调不清新锚点以及查询替换。外层 VM、视觉坐标和 XAML 控件只作边界，不能证明真实 ItemsView 滚动或 Frame 布局；它与默认套件分别运行。用例数量以当次摘要为准（R16审查时默认137项），不能累加重叠覆盖。
- 使用与 Core 套件相同的互斥 passed／failed／skipped 运行器；不把预期修前失败放进正常产品套件。
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
