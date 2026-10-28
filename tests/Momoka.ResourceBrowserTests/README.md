# 资源浏览与下载适用性源码回归

编译链接真实 `MoviePilotResourceViewModel`、Browser／Group／SitePicker 和 `PageViewModel`，使用与 Shell 相同的固定 MVVM 生成器，断言实际生成命令／属性通知／稳定集合。只将 WinUI enum 与系统启动操作替换为无窗口边界；HTTP 是拒绝非白名单接口的匿名内存 handler，缓存会话手动注入，登录／保护器访问被拒绝。

覆盖精确目标集初筛、meta 缺失的标题兜底、重搜、重新附加另一集、清除与选回；没有正集号不筛。覆盖异来源／编号／类型／缺快照／明确否决／非 exact 的独立原因与下载入口显隐，以及合法缺标志／空 status、Working／Done／Failed／Uncertain 的通知和反馈保留。模板检查读取所传本树 XAML，验证绑定与已有按钮句柄，**不是渲染／UIA 证据**。

先 restore、编译当前代码再运行（最后参数必须是当前树绝对根目录）：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.ResourceBrowserTests/Momoka.ResourceBrowserTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.ResourceBrowserTests/Momoka.ResourceBrowserTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.ResourceBrowserTests/Momoka.ResourceBrowserTests.csproj -c Release --no-build --no-restore -- (Get-Location).Path
```

不会启动 App、native、浏览器或下载器，不读真实配置／凭据／DPAPI、不联网／播放／提交下载。实际 WinUI 模板布局、宽窄窗口、键盘／鼠标输入、UIA 和截图仍需适用的运行验收，不拿本项目属性与源码断言冒充。

AniBT 资源行回归编译链接实际 `AniBtResourceViewModel.cs`，检查头像有值／缺失／加载失败、筛选恢复时组和行身份、下载状态的图标／悬停说明／辅助名称通知，以及两个按钮独立位于行尾的模板契约。只运行这部分可在上述命令末尾追加 `anibt-layout`。这些检查不产生图片下载或窗口；实际头像解码、36 DIP 方钮尺寸、宽窄行尾位置与 UIA 句柄由 `--probe-shell --probe-section anibt` 使用本地图片夹具验证，并分别保留渲染帧和真实屏幕截图。

2026-10-28 紧凑界面回归增加搜索标题／外卡移除、RSS 复制与二次文本筛选退役、单选分面恢复、MP 资源角色色与行间距契约。真实 AniBT 页面探针验证搜索到结果的 8 DIP 间距、收起状态行零残留、宽窗紧凑行、宽窄窗口与全部存活主题的真实截图；Core RSS 资源读取协议仍保留。

排序菜单按网页拆为方向与字段两组，覆盖五字段双向、当前名称通知、稳定集合／下载反馈、清除筛选保留排序和新搜索恢复默认；模板契约检查透明文本按钮及两组菜单绑定。运行探针另外检查宽窄窗口、实际菜单输入、选中状态、关闭与每套存活主题截图。
