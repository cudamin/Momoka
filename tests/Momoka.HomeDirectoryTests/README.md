# 媒体库目录到当前主页的快照交接回归

本项目在每次构建时按唯一标记原样提取真实 ShellPage 的 EnterShellAsync／LoadLibrariesAsync／GoTo／Open／ReleaseContent／RetryLibrariesAsync，并提取真实 HomeRequest；调用当前 Core 的 SourceLibraries／EmbySessionScope／EmbyClient／HomeLayout。Frame 只是无窗口导航与请求值边界，旁支外部服务拒绝执行，不复制目录或导航业务算法。

覆盖 Emby／Jellyfin 目录挂起→提前主页→合法结果后当前新请求持相同目录、未选页正常进入、已经转其它页不抢导航、访问／来源撤销拒绝旧结果、成功空目录、部分来源失败保提示、返回当前主页。FnNas／Plex 两项只使用真实 SourceLibraries 的已恢复内存目录＋另一来源挂起，覆盖聚合拓扑，不冒称它们实际 REST 协议验证。音乐库依旧过滤，目录与HomeRequest字段及HomeLayout的库排计划对应。

没有执行真实 HomePage.Attach／HomeViewModel 完整加载、页面渲染／Frame历史／滚动／焦点或UIA；快照到库／最近添加排的下游实际布局与HTTP加载属于运行覆盖缺口。

当前15个用例，先restore、编译再运行：

```powershell
$sdk = "$HOME/.dotnet/dotnet.exe"
& $sdk restore ./tests/Momoka.HomeDirectoryTests/Momoka.HomeDirectoryTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& $sdk build ./tests/Momoka.HomeDirectoryTests/Momoka.HomeDirectoryTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $sdk run --project ./tests/Momoka.HomeDirectoryTests/Momoka.HomeDirectoryTests.csproj -c Release --no-build --no-restore
```

所有scope／profile／目录仅匿名内存；保护器拒绝，SettingsStore使用无效禁止IO路径，handler只接目录GET。不登录、不读取真实配置／凭据DPAPI，不网络／后端／App／UI／播放。已在主页时沿用既有GoTo(force:true)作一次重建，代价是一次正常主页重载及滚动／选择状态可能重置，不抢其它页面导航。目录读取后手动重试的原路径未更改。
