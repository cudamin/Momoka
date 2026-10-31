# 订阅集图片探针的行实现与视口前件

`CompactImagesProbeTests` 原样提取当前 `PrepareSubscriptionImagesAsync` 及真实的树遍历、布局等待、条件等待与断言；缺失或重复提取边界使构建失败，注册前核对两份输入源码 SHA-256，建后修改须先重新编译。

框架端点只替换视觉树、`ItemsRepeater.GetOrCreateElement`、滚入请求和控件尺寸。回归检查目标零／一行均需真正实现、复用的屏外首行仍需滚回、同名但不同集合不能被接管，以及视口未到达时等待／严格超时。它不加载图片、创建窗口、发送 HTTP、读取配置或执行播放；实际图片失败／恢复和截图仍由 Shell `moviepilot` 分区验证。

当前 Release 的主测试与 Core 编译成功后，可运行原有定向入口：

```powershell
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- composition-readiness-fixture
```

完整主测试也登记这些用例，二者覆盖重叠。用例数量以本次运行输出为准，不把局部通过说成全量 Shell 或实际图片加载通过。
