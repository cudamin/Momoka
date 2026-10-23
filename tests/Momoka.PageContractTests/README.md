# 页面源码控制流契约

这个离线控制台项目编译链接实际 Shell 页面的 code-behind，不复制其 `Attach`／导航／释放逻辑。`PageBoundaries.cs` 只替代未启动的 XAML／WinUI 控件边界及外部数据属主，使这些方法可以在没有窗口和真实服务器的测试进程中运行。

- 当前覆盖：MoviePilot 电影／电视剧订阅列表页的导航 `Tag`、实际 `OnNavigatedTo`、历史重入和释放契约。
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
