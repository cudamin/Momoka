# 音乐视图模型离线回归

此控制台链接当前树的真实 `MusicPlaybackViewModel`、`MusicLibraryViewModel` 和 `PageViewModel`，使用与 Shell 相同的 CommunityToolkit.Mvvm 8.4.2 生成器。Core 的 `PlaybackService`、`PlaybackPlanner`、`EmbySession`、`EmbyClient`、`MusicCatalog` 与 `MusicQueue` 是当前项目引用。

10 项覆盖停止后 metadata 与队列迟到、队列第二页 HTTP 取消、旧视频/音频事件排队、自动下一曲后收藏迟到、确认音量保存与下一曲起播、80+20 分页完整队列、离页仍播放、旧曲目拖动释放及页面释放后的迟到列表。假 HTTP 只接受 `music-vm.invalid`，测试拒绝凭据读写和服务器账号配置；假后端只记录启动请求、运行可手动结束的控制句柄，不打开媒体或原生内核。未读取生产设置，临时图片目录在所属工作树 `artifacts/music-vm-tests/`。

`UiBoundaries.cs` 只替换 Visibility/InfoBar 值、图片卡片 IO、调度器与导航数据；VM 控制流和属性通知不复制。`Fixtures.cs` 通过反射安装真实会话的匿名内存身份，不进行真实登录；反射构造边界变化会导致测试失败。延迟回包用明确的 TaskCompletionSource 信号控制，模拟对取消不敏感的外部完成后，仍验证生产代码拒绝迟到结果。

在当前工作树运行统一入口：

```powershell
./tools/run-regressions.ps1 -Suite Momoka.MusicViewModelTests
```

或执行当前项目：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.MusicViewModelTests/Momoka.MusicViewModelTests.csproj --source "$HOME/.nuget/packages" --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.MusicViewModelTests/Momoka.MusicViewModelTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" ./tests/Momoka.MusicViewModelTests/bin/Release/net10.0/Momoka.MusicViewModelTests.dll
```

本树实测反例：仅在临时产品输入移除每曲收藏代次守卫与确认音量保存，10 项变为 8 通过 / 2 失败，分别是 A 收藏污染 B 和确认音量未保存；恢复原字节后 10/10。该反例不提交产品修改。测试不证明 XAML 编译、WinUI 布局/指针/视觉、真实 mpv 声音、真实服务器协议兼容或持久化磁盘失败重试；这些由音乐批次最终构建、原生探针与服务器授权范围另行验证。
