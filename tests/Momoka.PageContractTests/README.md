# 页面源码控制流契约

这个离线控制台项目编译链接实际 Shell 页面的 code-behind，不复制其 `Attach`／导航／释放逻辑。`PageBoundaries.cs` 只替代未启动的 XAML／WinUI 控件边界及外部数据属主，使这些方法可以在没有窗口和真实服务器的测试进程中运行。

- 当前覆盖：订阅列表导航 Tag／重入／释放；诊断 VM 快照与通知交接；真实 CardItem／PosterCache 的首图在飞换宽、旧下载和解码迟到、离屏与重复 Loaded、来源撤销及缓存宽度。
- 诊断页编译链接真实 `DiagnosticsViewModel` 与 `PageViewModel`，使用 Shell 已钉住的 `CommunityToolkit.Mvvm` 8.4.2 生成器，不升级依赖。`DIAGNOSTICS_VM_FIXTURE` 仅在本测试项目启用，主套件只登记 Core 日志测试。项目输出明确选 `Program` 为入口。
- 卡片测试以 `CARD_IMAGE_RESIZE_FIXTURE` 隔离，边界只替代 WinUI 值类型和位图解码；真实 Core 图片存储、SharedWork 和来源寿命仍执行。假图片 handler 只接受占位图片 GET，不运行登录或探测。所有新夹具文件在本树测试输出下保留。
- 本项目的诊断 Core 部分与主套件重叠；同时报告两边结果时不能把重叠项累加成不同覆盖。
- 使用与 Core 套件相同的互斥 passed／failed／skipped 运行器；不把预期修前失败放进正常产品套件。
- 不读取用户设置、日志或凭据，不连接 MoviePilot、不创建窗口、不消费生产激活信号。
- **不能证明**真实 WinUI 布局、输入、滚动、Frame 返回／前进、服务可用性及渲染。此类验收仍使用适用的隔离 UI 探针，不能因为本项目通过而省略。
- 项目被加到方案，普通完整构建能编译它；运行须使用下面独立命令，普通 `Momoka.Tests` 运行不会自动运行此项目。

在当前工作树根使用用户级 SDK，先还原及编译当前代码：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.PageContractTests/Momoka.PageContractTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.PageContractTests/Momoka.PageContractTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.PageContractTests/Momoka.PageContractTests.csproj -c Release --no-build
```
