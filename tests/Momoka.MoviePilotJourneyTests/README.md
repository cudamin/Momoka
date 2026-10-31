# MoviePilot 正常用户操作检查台

该专项加载当前工作树成功编译的 Shell，执行其中真实匿名 HTTP 检查台传输，验证登录、搜索、目录、订阅创建/修改/取消、业务拒绝、取消和未登记目标拒绝。不会创建窗口、使用真实账号或触发真实播放。

先编译当前 Shell 和本项目，再执行：

```powershell
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.MoviePilotJourneyTests/Momoka.MoviePilotJourneyTests.csproj -c Release --no-build --no-restore -- '<本树当前 Shell Momoka.dll 绝对路径>' '<本树根绝对路径>'
```

实际鼠标/键盘验收另外运行本树 `Momoka.exe --probe-shell inspect --screen 1`，点击 `InspectMoviePilotJourney`。检查台以真实 Shell/页面/返回栈承载匿名结果与订阅；保存 `moviepilot-journey-state.json` 记录读取、写入、页面及搜索状态。它仅允许匿名订阅操作，拒绝播放与真实媒体库写入。结束使用 `InspectDone`，只释放本轮窗口与夹具。

搜索结果使用空海报展示占位卡，避免 BitmapImage 绕过假 HTTP 传输发出真实请求；详情和选季图片经过假图片接口。这个边界不证明真实服务器登录、索引站搜索、下载或订阅任务执行。
