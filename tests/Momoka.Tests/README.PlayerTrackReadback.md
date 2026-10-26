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

## 2026-10-25：第二轮纯合并基线测试兼容
本轮仍在 `C:/Users/89400/.codex/worktrees/merged-contract-tests/Momoka`，产品固定为
`4afca26672297aedd7725d675706d7f2b923519c`＋父 resolved/staged 的
`48c43fc20d5827fe5f5f147eb73e1d1ca7aa2795` 合并快照。629 份产品源均与上一轮清单一致；
未导入父新增 translation/runtime/UI/provider 文件或 `4a15f90` 原子写入修复。
本轮仅测试增量接在第一补丁 `968f2931e4d80346a649c19fd68e10ad91bd36e2` 后，
不提交生产快照，不动共享 `Momoka.Tests/Program.cs`、`Agreements.txt`、`Momoka.sln`。

使用本树实际 x64 Release Shell：
`src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll`，
2026-10-25 01:36:41 +08:00，SHA256
`b9125a7ff07a46e236603200c97dd0608fd548a84ba588d39431d99fbb5e67f5`。
本轮按固定用户 SDK 10.0.400 离线 restore，再成功 Release 编译当前 Core/相关项目后才 no-build。
Build flags：`--no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false`。
证据留本树 ignored `artifacts/verification/merged-contract-tests/batch02/`，不借父最终功能构建/桌面结果。

统一测试支撑 `Momoka.Tests/PlayerConstructorBoundary.cs` 精确核对实际唯一公开
PlayerViewModel 构造器的7个参数名、类型与顺序：playback/settings/session/images/shaders/ui/dandanPlay。
两处旧6参数反射构造改为按该签名调用，不把第7参数补null。

新匿名依赖使用真实 `DandanPlayClient`＋真实 `DandanPlayCredentials` 包装，偏好关闭、无密钥；
传输与保护器任何调用均抛错并计数，缓存路径含非法文件名，Dispose断言HTTP/凭据调用均0，
及时释放client。实际VM `_dandanPlay` 必须持有同一实例。原6个参数语义不变，images/session的
既有无用null边界不因此打开；没有运行弹幕加载、App、WinUI、服务器、配置或密钥。

新增4项构造边界反例：真实签名；旧6参数/新未审核必需参数拒绝；错名错序/错类型拒绝；
null或非真实第7依赖在调用VM前拒绝。原测试断言均保留，各runner的4项相同边界不能累计成额外12种覆盖。

当前 `player-track-readback-fixture` **15过／0失败／0跳过**＝原11轨道行为＋4构造边界。
当前 `player-volume-gesture-fixture` **35过／0失败／0跳过**＝原31音量行为＋同4构造边界。
两入口都在当前真实Shell/Core执行，原未确认量/迟到读回/切文件/取消/保存结果不改。

复跑沿用上文当前 Shell/MainTests build，再分别运行：

```powershell
$root = (Get-Location).Path
$shell = Join-Path $root 'src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll'
$dotnet = Join-Path $HOME '.dotnet/dotnet.exe'
& $dotnet run --project (Join-Path $root 'tests/Momoka.Tests/Momoka.Tests.csproj') -c Release --no-build --no-restore -- player-track-readback-fixture $shell $root
& $dotnet run --project (Join-Path $root 'tests/Momoka.Tests/Momoka.Tests.csproj') -c Release --no-build --no-restore -- player-volume-gesture-fixture $shell $root
```
