# 详情图片首帧门控与淡入回归

`ImageArrivalFade.cs`、`DetailPage.ArtworkArrival.cs` 是完整生产源码链接；`LoadArtworkAsync` 和 `Fresh` 在构建时从本树 `DetailViewModel.cs` 原样提取，缺失或歧义边界会使构建失败。网络、解码、页面周围状态和 WinUI 值/事件/Storyboard 时钟使用惰性边界，门控返回可控 Task。真实 AsyncLocal、Loaded、Rendering 与 Low 队列门控由浏览呈现专项测试负责。

覆盖文字先显示、门控前缓存图不解码、快速取消不请求、旧批次不请求、迟到响应不回填、Source 直接驱动淡入、同图/离树图不重播、清空/失败/离页/关闭动画落定、回收幻影、静止图无外部订阅、ImageBrush 专用宿主淡入及详情五图接线/重新进入。未启动窗口、联网、读凭据或播放；不能证明真实 WinUI 绘制、绑定时序与肉眼动效。

在当前工作树根运行：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.DetailImageArrivalTests/Momoka.DetailImageArrivalTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.DetailImageArrivalTests/Momoka.DetailImageArrivalTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.DetailImageArrivalTests/Momoka.DetailImageArrivalTests.csproj -c Release --no-build --no-restore
```
