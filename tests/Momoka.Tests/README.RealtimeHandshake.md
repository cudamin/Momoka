# R-001：实时握手禁止自动重定向

## 当前行为与取舍

实际 `EmbyRealtimeConnection.ListenOnceAsync` 将 `HttpMessageInvoker` 传入 `ClientWebSocket.ConnectAsync`，其 `SocketsHttpHandler` 明确 `AllowAutoRedirect=false`。同源和跨源重定向一律当作握手失败，不把身份头带到下一站；原来源的指数退避重连与只读库轮询保持原样。只收紧跳转边界，默认代理、禁 Cookie 与不复用握手连接的行为保留。反向代理应直接接收既有 WebSocket 端点，而不是依赖客户端自动换端点。

没有恢复查询串令牌、没有改变 Emby／Jellyfin 端点拼法，也没有改其它 REST 客户端的跳转规则。

## 执行

在当前工作树根目录，先按 `CLAUDE.md` 的 Release 串行标志成功编译当前 `tests/Momoka.Tests/Momoka.Tests.csproj` 与 Core，再运行：

```powershell
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- realtime-handshake-fixture
```

当前 37 项：20 项实际重定向边界、1 项等待首响应取消、原有 16 项协议／101／重连／来源退出／退避回归。注册于完整 Core；分套读数不能与完整 Core 相加。

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
