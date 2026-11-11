# 花絮与特典回归

`extras-fixture` 执行实际 Core 规则与 `EmbyClient.GetSpecialFeaturesAsync`，覆盖 Emby 4.10.1 的视频前提、Jellyfin 的数量前提、各自路由与鉴权、裸数组、无效编号、取消、错误及可播放过滤。HTTP 仅到 `.invalid` 内存传输，不使用真实服务器。

先编译本树 Release/x64 Tests/Core/Shell，再运行：

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build tests/Momoka.Tests/Momoka.Tests.csproj -c Release -p:Platform=x64 -m:1
& "$env:USERPROFILE\.dotnet\dotnet.exe" build src/Momoka.Shell/Momoka.Shell.csproj -c Release -p:Platform=x64 -m:1
& "$env:USERPROFILE\.dotnet\dotnet.exe" tests/Momoka.Tests/bin/x64/Release/net10.0/Momoka.Tests.dll extras-fixture
& "$env:USERPROFILE\.dotnet\dotnet.exe" tests/Momoka.Tests/bin/x64/Release/net10.0/Momoka.Tests.dll detail-action-readiness-fixture "<本树Release Momoka.dll绝对路径>" "<本树绝对路径>"
```

详情专项加载正式 Shell DLL，执行真实 `DetailViewModel`，包含既有动作、预告片及花絮回归；不能把总数都算成新增花絮测试。花絮用例检查独立条目/文件、精确零起点、原片续播不变、空态隐藏与重读恢复、错误可重试、重复/不可播放/伪造来源过滤、菜单生成后的修改、取消/重附加/新菜单/注销后的旧操作及迟到结果。同身份客户端替换制造真实 401 重试，重新绑定的 Scope 可播放；相同账号键但不同在线所有者不能借菜单播放。播放止于捕获参数的 `IShellActions`，不启动播放器或窗口。

原生检查台 `--probe-shell inspect --screen 1` 的 `InspectDetailExtras`，以及 `--probe-shell --probe-section library --screen 1`，执行真实 `DetailPage` 与 `MenuFlyout`，覆盖 Emby/Jellyfin 各 1280/760 DIP、按钮及选项 UIA invoke、假播放参数、失败重试、隐藏恢复、关闭无播放、旧菜单和离页迟到。输出 `detail-extras-receipt.json` 与 `detail-extras-*-screen.png`。只有本次代码实际执行成功并检查实屏后，才算原生证据；编译探针不等于它已通过。

`Momoka.DetailImageArrivalTests` 和 `Momoka.SubtitleDialogTests` 逐字提取的详情生命周期现在包含 `ResetExtras()`；各自只补无作用的外部 partial 边界，不删改提取正文。运行这两项可确认新增接线没有切断既有图片/字幕调用覆盖。

多段正片 AdditionalParts 没有实现，评估见 [媒体库第四批-附加内容](../../docs/媒体库第四批-附加内容.md)。以上均不证明真实服务器或播放管线运行结果。
