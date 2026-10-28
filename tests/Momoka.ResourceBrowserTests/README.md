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

排序菜单按网页拆为方向与字段两组，覆盖五字段双向、当前名称通知、稳定集合／下载反馈、清除筛选保留排序和新搜索恢复默认；模板契约检查透明文本按钮及两组菜单绑定。运行探针另外检查宽窄窗口、实际菜单输入、选中状态、关闭与每套存活主题截图。

搜索分支整合契约读取本树 XAML 与真实页面/探针源码，检查同行站点入口保留中性焦点、来源收起不改变请求分流、订阅卡片点击与滚动条车道，以及紧凑探针和来源退役检查共存。两条反例分别把焦点刷改回强调色、重新插入旧来源筛选，确认契约会拒绝错误合并；这是合并回归，不证明实际 WinUI 焦点、间距或命中行为。

搜索头胶囊回归检查无标题/无外卡、原生搜索框40 DIP与20圆角、来源栏18/选中项14圆角、主题角色资源、四个稳定来源、窄窗标签与宽度扣除，以及恢复方形/外卡的拒绝反例。真实运行探针另执行原生UIA单选、绘制模板圆角、选中主题色、收起/恢复和普通媒体库标题恢复，源码契约不替代实屏。
