# 详情主体／版本删除后的观察回归

定向入口 `detail-subject-removal-fixture <本树当前x64 Release Shell DLL> <本树绝对根>` 执行真实 DetailViewModel 的 Attach／Apply／CaptureRemoval／CaptureReload／RemoveLibraryFiles／StartAsync／Reload 及生成命令。设置／秘密保护器／外部播放都为拒绝真实副作用的内存边界，scope匿名安装；当前33项覆盖主体确认失效、普通GET失败保持、取消、跨访问旧回调、非最后版本、同ID刷新替换DTO、迟到源列表和已选外源副本，以及 MoviePilot 精确库文件撤销与跨页面主体记忆。

MoviePilot 的 `RemoveLibraryFiles(item, removedPaths)` 只接受同来源同主体的已确认库文件路径，撤销相应媒体源及旧下拉行，并在当前访问的 `_deletedSourcePaths` 保留路径，过滤晚到详情和版本发现。它不把单个版本登记为主体删除；`Attach` 会重置此路径记忆，重新使用服务器返回的文件。整件删除由既有进程内 `RemovedMedia` 保留，`Attach` 从请求的 Services 读取；后续集、季和推荐清单继续过滤，返回已删主体详情在新 GET 前清空播放目标。普通成功删除与此入口共享清目标过程，原确认及来源／访问守卫不变。

`episode-removal-fixture` 单独运行 9 项纯函数用例：第五→第四、首集→下一集、仅一集、末集、跨季播出顺序、相同ID外源、其它作品、缺剧编号的季身份与未知落点。它只验证选集判断；真实 `Frame` 落地、多版本选择、关闭及取消由 `ShellNavigationProbe.EpisodeDelete` 在主任务整合后验证。

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
