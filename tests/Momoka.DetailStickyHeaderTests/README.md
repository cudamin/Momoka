# 详情悬浮卡片回归

此独立控制台项目将当前工作树完整的 `DetailPage.StickyHeader.cs` 与 `DetailPage.StickyPlayMotion.cs` 作为编译输入，实际执行生产代码的显隐、按钮关联移动、布局测量、焦点、事件、队列与偏好管理，不复制一份判断算法。额外核对同树 XAML 的同级浮层、原有资料与播放绑定、可访问名称以及页面初始化、释放、滚动接入。

测试覆盖三分之一裁切阈值、多种按钮高度、非整数布局位置、不滚动的布局越线、4 DIP/小按钮滞回、宽窄主播放按钮选择、底部未出现按钮不误触、重复及反向滚动、播放能力失效/恢复、附属读取继续可用、断树/平台异常、LayoutUpdated 合并、Dispatcher 拒绝恢复、重复 Loaded、Release/Unloaded、旧代回调、焦点交接、系统/应用动画关闭、360/520 DIP 与迟到/空海报。

滚动响应回归另验证：显示120ms/隐藏80ms且位移与透明度同步落定、入场和退场都能从当前帧反转、布局更新仍在排队时滚动越线当场显隐，不等待队列或动画完成。

播放按钮移动验证两个方向的原生Direct配置与实际源/目标、TryStart受理后未完成时不提前清源透明度/父层、完成后占位与操作恢复、500ms兜底、拒绝及异常、快速反向取消与迟到回调、两方向的偏好/离页/能力/尺寸取消、回顶仍返回、换行/迟到落点变化和小数坐标容差。关联动画边界只记录平台调用与完成时序，不承诺或模拟原生中途像素连续反向。

无障碍视图边界验证移走端设为Raw、移回及取消/离页恢复原值，避免透明占位留在一般读屏列表；不将模拟属性值当作真实UIA树、Narrator或缓存peer调用证明。

Composition 边界要求 Translation 属性先注册再 StopAnimation，并像 Toolkit 实现一样在非空隐式动画 hook 时注册；首次导航 Reset 早于 Loaded/hook、启动前关闭应用动画、未上树平台延后注册三条路径另有回归。没有注册时的 ArgumentException 保持为失败，不能让宽松边界掩盖真实平台初始化顺序。

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.DetailStickyHeaderTests/Momoka.DetailStickyHeaderTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.DetailStickyHeaderTests/Momoka.DetailStickyHeaderTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.DetailStickyHeaderTests/Momoka.DetailStickyHeaderTests.csproj -c Release --no-build --no-restore
```

WinUI、XAML、Composition、Toolkit 和系统偏好在此使用可观察边界；动画边界只记录生产代码向平台提交的组、起点、目标、取消与显隐触发，不模拟 GPU 逐帧动画，也不证明亚克力像素、原生焦点、物理键盘或指针、滚动平滑度。XAML 契约核对不等于实际排版。运行只读本树源码，不启动窗口、不读取真实设置或媒体库、不起播、不向服务器发请求。实际 WinUI 视觉及运行证据另由匿名详情探针取得。
