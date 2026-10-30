# 弹窗点外关闭实际行为回归

本项目 compile-link 当前 `Views/DialogDismiss.cs`，只替换 WinUI／WinRT 边界。
覆盖独立遮罩输入、内部及边界点、已处理事件、写入保护、不穿透、Loaded／Unloaded、关闭重开、
内部菜单与跨 XamlRoot 隔离、订阅清理、反复启用和未排版。
不复制业务方法，不模拟真实 WinUI 路由、弹窗几何或鼠标；后者必须使用离线检查台和实鼠验证。

当前工作树根目录：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.DialogDismissTests/Momoka.DialogDismissTests.csproj --disable-parallel
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.DialogDismissTests/Momoka.DialogDismissTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.DialogDismissTests/Momoka.DialogDismissTests.csproj -c Release --no-build --no-restore
```

样式与新增弹窗完整性另由 `Momoka.Tests/DialogChromeTests.cs` 检查。
运行及交付规则仍以 [CLAUDE.md](../../CLAUDE.md) 为准。
