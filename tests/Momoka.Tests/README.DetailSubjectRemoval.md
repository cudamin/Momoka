# 详情主体／版本删除后的观察回归

定向入口 `detail-subject-removal-fixture <本树当前x64 Release Shell DLL> <本树绝对根>` 执行真实 DetailViewModel 的 Attach／Apply／CaptureRemoval／CaptureReload／StartAsync／Reload 及生成命令。设置／秘密保护器／外部播放都为拒绝真实副作用的内存边界，scope匿名安装；当前22项覆盖主体确认失效、普通GET失败保持、取消、跨访问旧回调、非最后版本、同ID刷新替换DTO、迟到源列表和已选外源副本。

这份专项在已确认成功删除回调边界进入，不执行实际删除；实际FnNasVersions/FnNasDelete/Delete两层确认、真实HTTP写及父详情回调由独立 [删除命令专项](../Momoka.DetailDeleteCommandTests/README.md) 覆盖，二者重叠不相加。

先编译当前Shell／Core／Tests，再运行：

```powershell
$sdk = "$HOME/.dotnet/dotnet.exe"
& $sdk build ./src/Momoka.Shell/Momoka.Shell.csproj -c Release -p:Platform=x64 --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $sdk build ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
$shell = Join-Path (Get-Location).Path 'src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll'
& $sdk run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- detail-subject-removal-fixture $shell (Get-Location).Path
```

新树先restore。未运行真实WinUI／完整菜单／服务器删除／真实文件／native／播放／DPAPI；外源版本以真实MediaVersion／SourceRow装配，不冒发现或在途版本HTTP覆盖。主体删除保留可读页面和返回入口，停止旧观察／清旧播放目标；非最后版本仅撤确认源并保留存活及其它来源副本，不把普通网络刷新失败当作删除。
