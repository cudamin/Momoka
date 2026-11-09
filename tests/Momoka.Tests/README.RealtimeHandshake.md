# R-001：实时握手禁止自动重定向

## 当前行为与取舍

实际 `EmbyRealtimeConnection.ListenOnceAsync` 将 `HttpMessageInvoker` 传入 `ClientWebSocket.ConnectAsync`，其 `SocketsHttpHandler` 明确 `AllowAutoRedirect=false`。同源和跨源重定向一律当作握手失败，不把身份头带到下一站；原来源的指数退避重连与只读库轮询保持原样。只收紧跳转边界，默认代理、禁 Cookie 与不复用握手连接的行为保留。反向代理应直接接收既有 WebSocket 端点，而不是依赖客户端自动换端点。

没有恢复查询串令牌、没有改变 Emby／Jellyfin 端点拼法，也没有改其它 REST 客户端的跳转规则。

## 执行

在当前工作树根目录，先按 `CLAUDE.md` 的 Release 串行标志成功编译当前 `tests/Momoka.Tests/Momoka.Tests.csproj` 与 Core，再运行：

```powershell
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- realtime-handshake-fixture
```

当前 53 项：30 项三产品实际重定向边界、1 项等待首响应取消、原有协议／101／重连／来源退出／退避回归，以及 Plex 原生通知和 Jellyfin 应用心跳。注册于完整 Core；分套读数不能与完整 Core 相加。

2026-11-09 新增范围与固定上游证据见 [媒体库实时更新](../../docs/媒体库实时更新.md)。Plex Watch 实际接收并回调来源；有效与无效样例也逐条直接断言解析结果，避免混合计数提前满足。Jellyfin 在回环上验证立即／周期／更新／重连心跳，阻塞发送替身另验证周期更新不取消网络写入、无并行发送；替身不代替真实 socket 回环。

## 真实执行的隔离边界

- 实际 Core、session、实时连接、`ClientWebSocket` 与默认网络 handler；REST 恢复为严格限制回环主机的匿名假传输。
- `EmbyRealtimeTests.Redirect.cs` 直接调用真正的一次 `ListenOnceAsync`，不启动自动重连，避免把返回原端点的重连误当同源跳转。
- 301／302／303／307／308 × Emby／Jellyfin × 同源相对位置／跨端口绝对位置；原站确实收到占位身份，第二站异步监听实际 TCP／HTTP。已接收但读取未完成时被取消，仍计一次请求，不当作零。
- 原站 3xx 回复后一次握手必须抛 `WebSocketException`，第二站请求及身份字段为零、没有库变更通知或待接受的下一跳。等待首响应的取消不能等满 15 秒握手截止。
- 既有 101 连接、令牌替换后的重连、source-key 回调、收消息或退避中的来源退出仍执行。

## 原红与绿色证据

本树 `artifacts/staged-repair/batch07/r001/` 保留构建、读数、退出码、输入与产物 SHA256。修前产品源码对应 base `8865cc4dda46da100c06f5cd7cea413a6719604e`、新增边界测试共 37 项 **17 过／20 败**：Emby 第二站实收两类自定义身份字段；Jellyfin 标准 `Authorization` 被清除，但仍发下一跳，所以“请求必须为零”失败。没有真实凭据或真实服务器泄漏证据。

修后 37 项全过；观察器的取消计数随后收紧，因此修前与最终测试源码哈希不同，两次输入分别保存，不能称为完全相同的测试字节。后续补充证据以实际日志和 hash 为准，不擦掉原红。`.NET` 官方 v10.0.0／v10.0.11 源码与本机 10.0.11 reference XML 用于核对 overload、handler 默认代理／Cookie／连接生命周期；回环 IO 才是本轮运行证据，官方源码不替代本机运行结果。

## 未覆盖

未建立 TLS 回环或模拟 HTTPS→HTTP 降级，未运行用户配置代理、真实 Emby／Jellyfin 握手、真实令牌／DPAPI 保存链、WinUI 页面、native 或播放。本地全拒跳转策略对 TLS 同样配置为不跳转，但此处不宣称真实 TLS 链已验。原有 libmpv 缺失的真实 runtime 门禁红项和旧快捷方式状态不变，专项通过不是正式交付。

## 飞牛官方任务快照

实时专项同时登记 FnNasTaskWatchTests：生产每次成功/失败请求结束后等3秒再读task/running，每来源一条循环。首次成功只建基线，精确TaskItemScrap类型的unique ancestor旧减当前触发目录回调；无status过滤。失败保留基线，最新scope权限/代次、释放、退出和重复附加拒绝旧结果，并异步等待实际读取结束。首页调用者决定Attach/Release，生命周期有意比官方顶层TaskStore更窄，不新增全局后台。

十四项匿名内存HTTP测试使用实际Core、Session和FnNas接口；读取替身能挂起并忽略取消来证明晚结果拒绝和Dispose等待，延迟替身只用于决定性周期步进，生产固定3秒。没有真实服务器、凭据、扫描或媒体。原生飞牛入库推送仍未找到；task快照轮询不是WebSocket推送，也不直接读取媒体货架。
