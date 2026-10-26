# 手动已看補报的条目来源回归

`marked-watched-source-fixture` 执行实际 `PlaybackService.ReportMarkedWatchedAsync`、scope、EmbyClient 和JSON报文构造，不启动播放器。四种默认产品×四种条目来源×电影／集／季／剧的矩阵，逐项核是否发送、目标host、后代查询、相同PlaySessionId、位置0→片长；默认来源只决定未绑定旧条目的首次捕获，已绑定来源不受默认切换影响。

还覆盖后代读取中切默认、开始后切默认、旧来源取消、后代读取／开始后撤销、无在线来源／取消／开关关／无时长／无SeriesId／开始失败。匿名session来源只在内存安装，SettingsStore使用禁止IO的无效路径，保护器拒绝访问；handler仅回答目标Emby匿名列表与开始／停止，任何登录／真实HTTP／后端启动均失败。

当前82个注册用例，已纳入普通Core全套，定向结果与完整不重复累计。先编译当前Core／Tests再运行：

```powershell
$sdk = "$HOME/.dotnet/dotnet.exe"
& $sdk build ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $sdk run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- marked-watched-source-fixture
```

新树需先restore。Scope的作用是固定来源，退出不回退另一账号；补报仍仅支持原有Emby通知接管，非Emby条目不查后代、不发送这项补报。既有「正播放借用会话、阈值结账、整季最后已播出有时长单集」仍由完整PlaybackTests覆盖。

不读真实配置／DPAPI／凭据、不真实联网／服务器上报或观看历史修改、不播放／native／WinUI／发布。报文测试不是服务器已转发通知的证明。原补报会产生短暂的开始记录及通知，这个既有代价没有被本修复豁免；真实操作仍须当次授权。
