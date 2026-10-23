# 播放器轨道初读与通知时序回归

显式入口 `player-track-readback-fixture <本树当前x64 Release Shell DLL> <本树根>` 加载实际播放器VM，复用音量夹具的假设置／禁止网络与保护器／受控UI队列。为真实 `GetTracksAsync` 只构造已建立、未结束的内存PlayingAttempt所有权边界，调用真实VM初读与通知入口，不启动后端／监视／服务器上报。

覆盖同场音轨／字幕旧读在新通知后返回、服务回复后UI续体才排入、空通知、连续通知、正常无通知初读、同／不同句柄换文件、停止与取消、旧播放代次通知、排队旧通知。断言VM缓存的列表和Selected，菜单源码从该列表生成勾选；实际菜单／输入／画面未验。新通知只令旧初读退出，不发送切轨命令。

先本树当前Shell／Core／主Tests编译成功，再执行：

```powershell
$sdk = "$HOME/.dotnet/dotnet.exe"
& $sdk build ./src/Momoka.Shell/Momoka.Shell.csproj -c Release -p:Platform=x64 --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $sdk build ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
$shell = Join-Path (Get-Location).Path 'src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll'
& $sdk run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- player-track-readback-fixture $shell (Get-Location).Path
```

新树先restore，具体Shell路径以当前实际输出为准，不借其它树DLL或native。本套件11项，与复用边界下音量31项分别运行。无App／真实WinUI／IPC进程／libmpv／配置凭据DPAPI／网络／播放或写入；外部IPC实际调度和Standalone/uosc未动态运行，不能当产品运行验收。
