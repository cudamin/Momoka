# MoviePilot 订阅编辑回归

本项目还编译实际 `MoviePilotSubscriptionActionsViewModel`，验证活动分页去重、完整详情读取、分享失败和防重、失效归属拒绝迟到成功、关闭后的读取拒绝。UI 弹窗显示、菜单、材质仍由 MP 运行探针及实屏覆盖。

本项目编译本树完整 `MoviePilotSubscriptionEditorViewModel`、`PageViewModel`、Toolkit 源生成器及实际 Core 服务；只替换 `Visibility` / `InfoBarSeverity` 和匿名内存 HTTP。它不创建 WinUI 窗口，不使用保存账号、真实网络、播放或下载。

覆盖基础/进阶表单的增量请求、系统/自定义搜索周期与整数范围、洗版显隐和偏好恢复、电影/非 TMDB 的适用性、选项未返回或读取失败时保留旧值、关闭后迟到读取、站点/规则/路径清空、别名到 `download_path` 的映射、实际 PUT 的未知字段保护和结果不明后的防重。XAML 接线和保存中关闭保护另以源契约断言，不能代替实际像素和点击。

在当前工作树根目录执行：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.SubscriptionEditorTests/Momoka.SubscriptionEditorTests.csproj --disable-parallel -m:1 -p:BuildInParallel=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.SubscriptionEditorTests/Momoka.SubscriptionEditorTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.SubscriptionEditorTests/Momoka.SubscriptionEditorTests.csproj -c Release --no-build --no-restore
```

上游核对：MoviePilot-Frontend `v3` 提交 `1f574a4fa8a254e3308b440f1e265965bfb11f83` 的 `SubscribeEditDialog.vue` / 同名 spec、`api/constants.ts`、`api/rule.ts`、`api/storage.ts` 与 `utils/downloadDirectory.ts`；MoviePilot `v3` 提交 `f5bd3458a6d699600c7496b22a523412d4ef9762` 的 `app/api/endpoints/subscribe.py` 和 `app/schemas/subscribe.py`。音乐/系统默认订阅规则不属 Momoka 的本次编辑入口。实际部署版本及真实服务写入由主交付报告列明。
