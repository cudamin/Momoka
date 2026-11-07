# 媒体库目录到当前主页的快照交接回归

本项目在每次构建时按唯一标记原样提取真实 ShellPage 的 EnterShellAsync／LoadLibrariesAsync／GoTo／Open／ReleaseContent／RetryLibrariesAsync，并提取真实 HomeRequest；调用当前 Core 的 SourceLibraries／EmbySessionScope／EmbyClient／HomeLayout。Frame 只是无窗口导航与请求值边界，不复制目录或导航业务算法。

覆盖 Emby／Jellyfin 目录挂起→提前主页→合法结果后当前新请求持相同目录、未选页正常进入、已经转其它页不抢导航、访问／来源撤销拒绝旧结果、成功空目录、部分来源失败保提示、返回当前主页。FnNas／Plex 两项只使用真实 SourceLibraries 的已恢复内存目录＋另一来源挂起，覆盖聚合拓扑，不冒称它们实际 REST 协议验证。音乐库依旧过滤，目录与HomeRequest字段及HomeLayout的库排计划对应。

没有执行真实 HomePage.Attach／HomeViewModel 完整加载、页面渲染／Frame历史／滚动／焦点或UIA；快照到库／最近添加排的下游实际布局与HTTP加载属于运行覆盖缺口。

原目录分组保留15个用例，关闭分组新增16个，共31项，先restore、编译再运行：

```powershell
$sdk = "$HOME/.dotnet/dotnet.exe"
& $sdk restore ./tests/Momoka.HomeDirectoryTests/Momoka.HomeDirectoryTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& $sdk build ./tests/Momoka.HomeDirectoryTests/Momoka.HomeDirectoryTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $sdk run --project ./tests/Momoka.HomeDirectoryTests/Momoka.HomeDirectoryTests.csproj -c Release --no-build --no-restore
```

目录分组的scope／profile／目录仅匿名内存；保护器拒绝，SettingsStore使用无效禁止IO路径，handler只接目录GET。该分组不登录、不读取真实配置／凭据DPAPI，不网络／后端／App／UI／播放。已在主页时沿用既有GoTo(force:true)作一次重建，代价是一次正常主页重载及滚动／选择状态可能重置，不抢其它页面导航。目录读取后手动重试的原路径未更改。

## R03-005 关闭期间账号生命周期

关闭分组链接完整真实 SignInViewModel／PageViewModel 和与 Shell 相同的 CommunityToolkit.Mvvm 8.4.2 生成器；另外原样提取当前 Shell 的 Shutdown、登录与来源通知、Start、Switch、聚合模式、ShowSignIn 及计数收尾。真实 Core Session／Gateway／SettingsStore 使用独立临时目录与合成占位凭据，HTTP handler 只接受 close.invalid；翻译、播放器、Frame 和 DispatcherQueue 是可控等待／排队边界，不重写登录采用或关闭业务。

16项覆盖翻译／播放器关闭等待中迟到成功及失败、再次提交、8个入口、关闭前已排队退出通知，以及已捕获来源的合法读取与计数／pending清理。UiQueue 驱动真实 VM continuation，所有关闭等待均在断言前释放并完成，重复关闭共用原Task。

本分组执行匿名登录及临时设置保存，不读取真实配置、DPAPI或服务器，不联网、不播放、不创建HostWindow或真实WinUI页面。可控停止端点不等于实际播放器／翻译收尾；真实主窗口关闭、页面像素及排队输入仍须由批次交付会话运行验证。
