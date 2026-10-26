# 独立订阅分类页状态回归

`moviepilot-subscription-page-state-fixture` 显式加载本树当前 x64 Release Shell DLL，执行真实 `MoviePilotSubscriptionsViewModel.Attach／ReloadAsync`、页面分类投影和生成的属性通知，使用匿名缓存会话、拒绝保护器访问的假设置和仅列表读取的内存 HTTP handler。

- 本类空／另一类有内容：本类空态和刷新入口不被另一类吞掉。
- 保留另一类旧数据时挂起本类刷新：显示读取中，不先说暂无；成功后再显示空态。
- 本类最后条目在重新读取后消失：投影和通知更新；这里只模拟写确认之后的读取，不执行删除。
- 业务失败显示原提示和恢复操作，成功后恢复空态；同 VM 切分类不替换集合，不改变共享主页“任一类有内容”的语义。
- 普通页面源码契约另执行真实 Attach，核对分类在 AttachRow 之前交入；模板原 Loading／EmptyState／Refresh 接线仍保持。不启动 WinUI、不冒称完整导航／布局／滚动／输入／截图验收。

先编译本树当前 Shell／Core／Tests，再执行：

```powershell
$sdk = "$HOME/.dotnet/dotnet.exe"
& $sdk build ./src/Momoka.Shell/Momoka.Shell.csproj -c Release -p:Platform=x64 --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& $sdk build ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
# Shell输出路径以当前csproj及实际输出为准，不能借另一工作树DLL。
$shell = Join-Path (Get-Location).Path 'src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.dll'
& $sdk run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- moviepilot-subscription-page-state-fixture $shell (Get-Location).Path
```

新树需先restore；此入口不登录、不解密、不访问真实配置／服务／图片，不发写请求、起播或启动App。当前11个注册用例，源码断言不计额外UI测试，且不会自动进入普通Core全套。
