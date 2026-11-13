# 音乐图片需求与回收回归

本项目编译链接当前完整 `CardArtwork.cs`、`MusicLibraryPage.Artwork.cs`、`CardItem.cs`、`PosterCache.cs` 与真实 Core。
`CardImageResizeTests.Boundaries.cs` 提供既有匿名图片 HTTP、控件线程队列和假 BitmapImage；
新增边界只模拟 Image 的附加属性、Loaded/Unloaded 和淡入对象收放，不复制生产领取/释放算法。
内部Repeater边界保留同owner池的Tree/XamlRoot与x:Bind项，实际生产prepared/clearing决定图片需求；
ListView边界提供InRecycleQueue事件输入。该池模型不是实际WinUI布局或自动化证据。

在当前工作树根先 restore/build，再 run；无需参数：

```powershell
& 'C:/Users/89400/.dotnet/dotnet.exe' restore tests/Momoka.MusicArtworkTests/Momoka.MusicArtworkTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& 'C:/Users/89400/.dotnet/dotnet.exe' build tests/Momoka.MusicArtworkTests/Momoka.MusicArtworkTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& 'C:/Users/89400/.dotnet/dotnet.exe' run --project tests/Momoka.MusicArtworkTests/Momoka.MusicArtworkTests.csproj -c Release --no-build --no-restore
```

覆盖曲目与三类方墙的模板入口、44/170 DIP请求/解码、所属首帧门控、首次离树无请求、
同项/换项、回收幻影、真离树的晚到缓存、旧持有者释放、移除行为清订阅、来源撤销、404终态与坏解码重试。
100/1,000/10,000匿名元数据各使用20个模拟容器，两次换项只保留20张卡片首图，全部卸载回0；
这是生产图片需求生命周期证据，不是 WinUI 实际 viewport/虚拟化/帧测量。
新增预算纯断言使用实际native基线反例：Realized21而四口径25、557宿主而50576内viewport必须拒绝；
当前独立入口32项，0失败/跳过。实际有限viewport与首末项聚焦仍由运行probe验收。
音乐两Image只绑定CardArtwork.Item，Source由该行为唯一拥有；回收时取消RowPoster监听并清Source，
返回同项同步恢复缓存。新增Source不残留/已退订通知拒绝/旧holder不清新Source回归。
新增精确ListView顺序：先CCC旧绑定，后phase0新绑定，再原生下一phase callback；
无缓存旧tag不能产生GET，代次/身份/回收/离页守卫全部执行生产方法，框架callback边界只负责输入时序。

HTTP只允许 `card-image.invalid` 图片GET，数据在本测试输出的随机 `fixtures/r012` 目录，
无网络、真实账号、设置、播放或媒体写入。程序集名沿用Core已有友元以构造匿名来源，不新增公共测试API。

运行 UI 入口是 `--probe-shell --probe-section music --screen 1` 新增的「音乐图片读取、回收与列表量测」，
或检查台的 `InspectMusicArtwork`。实际运行和主树交付由协调会话串行完成。
