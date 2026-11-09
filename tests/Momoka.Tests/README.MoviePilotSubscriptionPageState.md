# 独立订阅分类页状态回归

`moviepilot-subscription-page-state-fixture` 显式加载本树当前 x64 Release Shell DLL，执行真实 `MoviePilotSubscriptionsViewModel.Attach／ReloadAsync`、页面分类投影和生成的属性通知，使用匿名缓存会话、拒绝保护器访问的假设置和仅列表读取的内存 HTTP handler。

- 本类空／另一类有内容：本类空态和刷新入口不被另一类吞掉。
- 保留另一类旧数据时挂起本类刷新：显示读取中，不先说暂无；成功后再显示空态。
- 本类最后条目在重新读取后消失：投影和通知更新；这里只模拟写确认之后的读取，不执行删除。
- 业务失败显示原提示和恢复操作，成功后恢复空态；同 VM 切分类不替换集合，不改变共享主页“任一类有内容”的语义。
- 普通页面源码契约另执行真实 Attach，核对分类在 AttachRow 之前交入；模板原 Loading／EmptyState／Refresh 接线仍保持。不启动 WinUI、不冒称完整导航／布局／滚动／输入／截图验收。
- 电影与电视剧详情执行真实 Reload，挂起两张头图、季图、演职员和推荐图片；数据读取结束后刷新、切组与离页仍取消对应后台图片。图片委托只观察取消令牌，不访问图片或解码。

先编译本树当前 Shell／Core／Tests，再执行：

```powershell
$sdk = "$HOME/.dotnet/dotnet.exe"
& $sdk build ./src/Momoka.Shell/Momoka.Shell.csproj -c Release -p:Platform=x64 --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $sdk build ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
# Shell输出路径以当前csproj及实际输出为准，不能借另一工作树DLL。
$shell = Join-Path (Get-Location).Path 'src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll'
& $sdk run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- moviepilot-subscription-page-state-fixture $shell (Get-Location).Path
```

新树需先restore；此入口不访问真实配置／服务／图片，不发有副作用的写请求、起播或启动App。详情图片取消用例仅使用占位登录和匿名内存媒体读取，包括官方只读 POST 入库统计。其余用例继续拒绝解密和登录。用例数以当前输出为准；分集行展示覆盖空白／默认／真实标题与电影标记、简介显隐、未取得图片时收起，以及季心形的动态绑定。源码断言不计额外UI测试，且不会自动进入普通Core全套。

`--probe-shell --probe-section moviepilot --screen 1` 的「MoviePilot 无图季列表、筛选空态与图片恢复」另检查真实模板、1240／760／600 DIP 八集高度、故意增高反例、隐藏季筛选隔离、图片失败与刷新恢复；截图为 `mp-compact-*-screen.png`。订阅恢复用例还检查首步读取返回404时撤销旧统计与播放入口。全部使用匿名假传输，不构成真实订阅／下载／播放证据。
