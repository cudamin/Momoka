# 资源浏览与下载适用性源码回归

编译链接真实 `MoviePilotResourceViewModel`、Browser／Group／SitePicker 和 `PageViewModel`，使用与 Shell 相同的固定 MVVM 生成器，断言实际生成命令／属性通知／稳定集合。只将 WinUI enum 与系统启动操作替换为无窗口边界；HTTP 是拒绝非白名单接口的匿名内存 handler，缓存会话手动注入，登录／保护器访问被拒绝。

站点专项同时链接 `MoviePilotVersionsViewModel`，覆盖默认／显式空选择、多选、服务与账号隔离、密码轮换与重连、目录失效恢复、失败不扩大搜索、保存失败反馈、实际临时设置文件跨服务重读，以及挂起目录期间重搜／离页取消。站点偏好只保存非敏感身份的哈希和编号，不保存连接 stamp、密码、站点 Cookie 或 URL；临时配置写入后删除，不访问生产配置。目录类型各自记忆，避免电影／剧集过滤目录改写关键词目录的选择。

覆盖精确目标集初筛、meta 缺失的标题兜底、重搜、重新附加另一集、清除与选回；没有正集号不筛。覆盖异来源／编号／类型／缺快照／明确否决／非 exact 的独立原因与下载入口显隐，以及合法缺标志／空 status、Working／Done／Failed／Uncertain 的通知和反馈保留。模板检查读取所传本树 XAML，验证绑定与已有按钮句柄，**不是渲染／UIA 证据**。

先 restore、编译当前代码再运行（最后参数必须是当前树绝对根目录）：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.ResourceBrowserTests/Momoka.ResourceBrowserTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.ResourceBrowserTests/Momoka.ResourceBrowserTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.ResourceBrowserTests/Momoka.ResourceBrowserTests.csproj -c Release --no-build --no-restore -- (Get-Location).Path
```

仅执行站点持久化专项时，在本树根目录参数后加 `search-sites`；这是完整套件的子集，不与全套数量相加。源码与状态测试不代替实际 WinUI 弹层、焦点、输入和截图验收。

不会启动 App、native、浏览器或下载器，不读真实配置／凭据／DPAPI、不联网／播放／提交下载。实际 WinUI 模板布局、宽窄窗口、键盘／鼠标输入、UIA 和截图仍需适用的运行验收，不拿本项目属性与源码断言冒充。

AniBT 资源行回归编译链接实际 `AniBtResourceViewModel.cs`，检查头像有值／缺失／加载失败、页签切换与展开时保留组和行身份、下载状态的图标／悬停说明／辅助名称通知，以及两个按钮独立位于行尾的模板契约。只运行这部分可在上述命令末尾追加 `anibt-layout`。这些检查不产生图片下载或窗口；实际头像解码、36 DIP 方钮尺寸、宽窄行尾位置与 UIA 句柄由 `--probe-shell --probe-section anibt` 使用本地图片夹具验证，并分别保留渲染帧和真实屏幕截图。

2026-10-28 紧凑界面回归增加搜索标题／外卡移除、RSS 复制与二次文本筛选退役、四项资源筛选退役、完整列表最新排序与页签切换、MP 资源角色色与行间距契约。真实 AniBT 页面探针验证搜索到结果的 8 DIP 间距、收起状态行零残留、宽窗紧凑行、宽窄窗口与全部存活主题的真实截图；Core RSS 资源读取协议仍保留。

排序菜单按网页拆为方向与字段两组，覆盖五字段双向、当前名称通知、稳定集合／下载反馈、清除筛选保留排序和新搜索恢复默认；模板契约检查透明文本按钮及两组菜单绑定。运行探针另外检查宽窄窗口、实际菜单输入、选中状态、关闭与每套存活主题截图。

当前 AniBT 资源区已移除四项筛选。回归覆盖完整资源最新排序、页签切换保持行对象与下载状态、空进度组、无框下划线页签与“打开 AniBT”位置；实际网页命令以记录型 launcher 验证当前编号／页签及返回、断线守卫，不启动真实浏览器。

搜索分支整合契约读取本树 XAML 与真实页面/探针源码，检查同行站点入口保留中性焦点、来源收起不改变请求分流、订阅卡片点击与滚动条车道，以及紧凑探针和来源退役检查共存。两条反例分别把焦点刷改回强调色、重新插入旧来源筛选，确认契约会拒绝错误合并；这是合并回归，不证明实际 WinUI 焦点、间距或命中行为。

剧资源清单专项编译链接实际 `MoviePilotSeriesReorganizeViewModel`、资源行和标题分组，使用匿名内存传输；只在图片解码边界使用空对象。覆盖自定义搜索、状态、方向／默认顺序、稳定行与集合、过滤后撤销隐藏选择、折叠全选、刷新撤销旧选择、空／错态、迟到读取与连接更换。模板契约核对原生 AutoSuggestBox、独立单选菜单、CollectionViewSource 分组和方向显隐。此项目不证明物理键盘／鼠标、真实像素或服务器整理；运行证据由 Shell MoviePilot 分区提供。
搜索头胶囊回归检查无标题/无外卡、原生搜索框40 DIP与20圆角、占位与输入垂直居中且保留原生文本模板、来源栏最大480 DIP宽/32 DIP高与16圆角、每项24 DIP高/12圆角和项目12 DIP字号、主题角色资源、四个稳定来源、窄窗标签与宽度扣除，以及恢复方形/外卡的拒绝反例。真实运行探针另执行原生UIA单选、绘制模板圆角、选中主题色、收起/恢复和普通媒体库标题恢复，源码契约不替代实屏。
