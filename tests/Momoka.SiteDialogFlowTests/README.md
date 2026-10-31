# 站点选择对话流程离线回归

编译链接当前 `MoviePilotSitePicker.xaml.cs`、`MoviePilotSiteDialog.xaml.cs`、站点 VM、Page VM 和同版 MVVM 生成器，调用真实 Core 服务与匿名内存 HTTP。只替换 WinUI/XAML 创建、测量和对话完成边界，不复制对话业务流程。

覆盖搜索/设置确认、重复打开共享任务、X/Hide(None)取消零保存零搜索、显式清空、保存失败重试、切连接/离页/换模型、挂起目录取消和迟到完成、宽窄根尺寸与关闭后解除订阅。Esc/遮罩模拟框架终止端点 `Hide()`，不是实际按键或鼠标输入证据；真实遮罩订阅另由 DialogDismiss 专项和父代理运行探针验证。

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.SiteDialogFlowTests/Momoka.SiteDialogFlowTests.csproj --source "$HOME/.nuget/packages" --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.SiteDialogFlowTests/Momoka.SiteDialogFlowTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.SiteDialogFlowTests/Momoka.SiteDialogFlowTests.csproj -c Release --no-build --no-restore
```

不读真实配置、凭据或 DPAPI，不发真实 HTTP、不启动 GUI、播放器或发布。资源模板与草稿状态另由 `SiteGridContractTests.Register(sourceRoot)` 检查；该新 suite 的正式 Program 注册交由父代理。
