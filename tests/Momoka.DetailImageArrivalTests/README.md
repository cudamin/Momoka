# 详情图片首帧门控与淡入回归

详情封面关联动画回归会检查实际属性回调中的淡入落定、离树清图暂留、释放清图与换卡拒绝；ViewModel 检查网络门控前位图显示、尺寸先行、错项及离页拒绝。目标关联封面本次停留的高清替换保持不透明，下一次停留恢复普通淡入；这些替身不证明真实 WinUI 帧连续。

`ImageArrivalFade.cs`、`DetailPage.ArtworkArrival.cs` 是完整生产源码链接。图片专项用 SDK 自带 Roslyn 按语法成员从本树的 `DetailViewModel.cs` 与 `PosterCard.xaml.cs` 提取实际生产实现；缺失或含糊的类型、成员会让构建失败，不依赖注释文本边界。字段类型、方法返回类型、参数类型和参数名称也会核对。每次构建还用临时 C# 源文件验证缺失/重复类型与字段、缺失方法及受检签名变化都会被拒绝。网络、解码、页面周围状态和 WinUI 值/事件/Storyboard 时钟使用惰性边界，页面显式传入可控 Task。真实 Frame 归属、Loaded、Rendering 与 Low 队列门控由浏览呈现专项测试负责。

覆盖文字先显示、门控前缓存图不解码、快速取消不请求、旧批次不请求、迟到响应不回填、原生回调环境门控丢失、捕获本次门控、无门控页面隔离、取消重入和 Attach→捕获→Reload 接线顺序，以及 Source 直接驱动淡入、同图/离树图不重播、清空/失败/离页/关闭动画落定、回收幻影、静止图无外部订阅、ImageBrush 专用宿主淡入及详情五图接线/重新进入。未启动窗口、联网、读凭据或播放；不能证明真实 WinUI 绘制、绑定时序与肉眼动效。

在当前工作树根运行：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.DetailImageArrivalTests/Momoka.DetailImageArrivalTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.DetailImageArrivalTests/Momoka.DetailImageArrivalTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.DetailImageArrivalTests/Momoka.DetailImageArrivalTests.csproj -c Release --no-build --no-restore
```
