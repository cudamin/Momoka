# 图片选择筛选离线回归

本项目编译链接本树完整的 `CoverPickerDialog.xaml.cs`。来源、语言、选择、重试和上传事件由最小值/事件边界触发；筛选、取消、换代、空态、按钮状态和预览控制流全部来自实际产品文件。Core 的 `RemoteImageSearch`、图片类型与比例规则也使用实际实现。

测试覆盖默认值和初始化事件、服务端筛选参数传递、稳定集合、快速切换的迟到结果/错误、关闭取消、首批慢预览释放、客观空态、请求失败重试、本机文件取消/失败/成功、无安全代理的文字候选和不同图片类型的预览比例。

外层图片弹窗仅替代为读取与选文件的委托，位图解码仅替代为非空返回边界。不会创建 WinUI 窗口、加载真实账号、连接网络、上传媒体图片或播放；通过数不能证明实际 WinUI 渲染、原生按钮同步、鼠标/键盘输入、真实服务器筛选或上传成功。

在工作树根目录运行：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.ArtworkSearchDialogTests/Momoka.ArtworkSearchDialogTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.ArtworkSearchDialogTests/Momoka.ArtworkSearchDialogTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.ArtworkSearchDialogTests/Momoka.ArtworkSearchDialogTests.csproj -c Release --no-build
```

统一回归入口：`tools/run-regressions.ps1 -Suite Momoka.ArtworkSearchDialogTests`。可用 `ArtworkPickerSource` MSBuild 属性验证另一个明确的 Git 文件版本，默认始终链接当前树内产品文件。
