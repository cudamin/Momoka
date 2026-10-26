# 整理提交前失败的离线回归

本项目编译链接真实 `MoviePilotReorganizeDialog.xaml.cs`，引用当前 Core 并执行实际 `TransferSubmitAsync`。XAML／WinUI 控件与 dispatcher 只是无窗口边界替身，不复制提交、预览或恢复逻辑。

- 以匿名内存会话初始化服务，保护器和登录端点均拒绝访问；不读设置文件、DPAPI 或真实凭据，不访问网络，不移动文件。
- 只读 history 的 404、JSON／schema 错误、断线、500、依赖核验错误必须零实际写，恢复编辑／新预览；已消费的旧预览不能再用。
- 写后丢回执、取消与格式错误继续锁定并保留跨预览防重；明确写拒绝不误归类为提交前失败。原记录改变、软链接依赖和连接取消仍有守卫。
- 内存处理器同步完成；测试只检查已完成任务，遇到未完成任务直接失败，不阻塞 UI 续体、不靠超时或重复运行洗绿。

在当前工作树先编译再执行：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.TransferSubmissionTests/Momoka.TransferSubmissionTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.TransferSubmissionTests/Momoka.TransferSubmissionTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.TransferSubmissionTests/Momoka.TransferSubmissionTests.csproj -c Release --no-build --no-restore
```

`MoviePilotTransferSubmissionTests` 的 Core 场景另注册在主测试中，定向入口为 `moviepilot-transfer-submission-fixture`，重叠覆盖不累加。当前程序不需要 Shell DLL，不启动 App／WinUI；布局、输入、完整弹框生命周期与真实 MoviePilot 整理均没有运行覆盖。
