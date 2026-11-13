# 详情悬浮卡片回归

此独立控制台项目将当前工作树完整的 `DetailPage.StickyHeader.cs` 作为编译输入，实际执行生产代码的显隐、布局测量、焦点、事件、队列与偏好管理，不复制一份判断算法。额外核对同树 XAML 的同级浮层、原有资料与播放绑定、可访问名称以及页面初始化、释放、滚动接入。

测试覆盖半切阈值、4 DIP/小按钮滞回、宽窄主播放按钮选择、底部未出现按钮不误触、重复及反向滚动、播放能力失效/恢复、附属读取继续可用、断树/平台异常、LayoutUpdated 合并、Dispatcher 拒绝恢复、重复 Loaded、Release/Unloaded、旧代回调、焦点交接、系统/应用动画关闭、360/520 DIP 与迟到/空海报。

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.DetailStickyHeaderTests/Momoka.DetailStickyHeaderTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.DetailStickyHeaderTests/Momoka.DetailStickyHeaderTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.DetailStickyHeaderTests/Momoka.DetailStickyHeaderTests.csproj -c Release --no-build --no-restore
```

WinUI、XAML、Composition、Toolkit 和系统偏好在此使用可观察边界；动画边界只记录生产代码向平台提交的组、起点、目标、取消与显隐触发，不模拟 GPU 逐帧动画，也不证明亚克力像素、原生焦点、物理键盘或指针、滚动平滑度。XAML 契约核对不等于实际排版。运行只读本树源码，不启动窗口、不读取真实设置或媒体库、不起播、不向服务器发请求。实际 WinUI 视觉及运行证据另由匿名详情探针取得。
