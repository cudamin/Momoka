# 浏览页面首帧与图片放行回归

直接链接当前树 `Views/BrowsePresentation.cs`。Frame、Loaded/Unloaded、Rendering、DispatcherQueue、Storyboard 和系统偏好只模拟原生事件交付；导航代次、取消、图片等待与动画所有权执行生产代码。

覆盖首帧之后低优先级放行、快速换页的迟到回调、应用/系统关闭动画、未进入 UI 树的调用、动画异常、队列拒绝和异步请求自己的门控归属。补充清空 ExecutionContext 后按所属 Frame 捕获、尚无视觉父节点的 Page.Frame、同 UI 线程两窗口隔离与旧 Task 不被新导航替换。默认没有窗口、登录、真实网络、媒体播放或用户设置读取。

```powershell
& 'C:\Users\89400\.dotnet\dotnet.exe' restore tests/Momoka.BrowsePresentationTests/Momoka.BrowsePresentationTests.csproj
& 'C:\Users\89400\.dotnet\dotnet.exe' build tests/Momoka.BrowsePresentationTests/Momoka.BrowsePresentationTests.csproj -c Release --no-restore -p:Platform=x64 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false -m:1
& 'C:\Users\89400\.dotnet\dotnet.exe' run --project tests/Momoka.BrowsePresentationTests/Momoka.BrowsePresentationTests.csproj -c Release --no-build -p:Platform=x64
```

通过不证明 WinRT 事件之间的实际 ExecutionContext 传递、实屏第一帧、图片解码、动画观感或网页速度验收；这些由父交付会话的原生导航探针与实屏对照完成。
