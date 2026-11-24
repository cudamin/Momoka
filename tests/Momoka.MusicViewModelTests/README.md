# 音乐视图模型离线回归

2026-11-14 补充长标题有限列与原生 Ctrl+F 接线契约，并完整链接 `MusicLibraryPage.Search.cs` 验证未附加、离页、隐藏、禁用、未加载及聚焦失败时放行。2026-11-24 同文件再链接 `SearchInputGuard`，验证音乐搜索框漏送退格按下时抬起补删单个可见字符或选区、开头不越界、原生退格／组字／Ctrl 组合不重复补删、离页解除旧编辑框；实际截断、焦点、快捷键与输入法按键路由仍由 WinUI 运行时确认。

此控制台链接当前树的真实 `MusicPlaybackViewModel`、`MusicLibraryViewModel` 和 `PageViewModel`，使用与 Shell 相同的 CommunityToolkit.Mvvm 8.4.2 生成器。Core 的 `PlaybackService`、`PlaybackPlanner`、`EmbySession`、`EmbyClient`、`MusicCatalog` 与 `MusicQueue` 是当前项目引用。

45 项覆盖停止后 metadata 与队列迟到、队列第二页 HTTP 取消、加载队列时切槽、未提交搜索与已加载查询隔离、旧视频/音频事件排队、自动下一曲后收藏迟到、确认音量保存与下一曲起播、80+20 分页完整队列、离页仍播放、旧曲目拖动释放及页面释放后的迟到列表；也覆盖曲目/墙分型、正在播放标记与副标题，以及音乐搜索框的退格补偿与去重。

P4 新增18项：实际墙分页按钮独立于曲目 Footer、实时显隐与自动化名称；正式 `OnMore`、`Show`、`SetPlayerClearance` 在编译时严格提取，缺失或重复边界使构建失败。三种墙各用81/100/160条验证第81项、末页、稳定集合/首项与原地追加；再检查曲目/收藏入口、重复点击、HTTP失败重试、分区切换迟到响应、释放并重新附加另一身份及空余页。`PageBoundaries.cs`只提供页面所需的控件值和入口调用，分页/避让控制流来自生产方法；XAML入口关系从本树实际页面读取。

原生分页探针另新增5项，直接编译链接 `ShellNavigationProbe.MusicPagingFixture.cs`。核对81/160、三类型、StartIndex/Limit、HTTP500一次后原偏移重试、真实域名/POST/非法参数拒绝、图片404与取消不执行请求；正式 `ValidateLoaded` 对漏项、乱序、混合类型和末页仍显示分页的伪读数必须拒绝。这些测试验证探针的成功/失败边界，不代替实际WinUI运行。实际入口是music分区的“音乐墙分页、原生入口与重试”，或检查台 `InspectMusicPaging`；`InspectMusicPagingFailure`主动使自动化ID错误，预期记录失败，用于验证实际失败路径。

假 HTTP 只接受 `music-vm.invalid`，测试拒绝凭据读写和服务器账号配置；假后端只记录启动请求、运行可手动结束的控制句柄，不打开媒体或原生内核。未读取生产设置，临时图片目录在所属工作树 `artifacts/music-vm-tests/`。

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
