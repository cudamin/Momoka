# 元数据日期原样保存回归（R-017）

[ItemMetadataDateTests.cs](ItemMetadataDateTests.cs) 直接执行生产 [ItemMetadataEdit](../../src/Momoka.Core/Emby/ItemMetadataEdit.cs) 的 `Read` / `Problem` / `ApplyTo`，另通过真实 [EmbyClient](../../src/Momoka.Core/Emby/EmbyClient.cs) 和 `EmbyHttp` 在内存 HTTP 边界读取与提交 JSON。用例纳入全 Core，不能把专项与全套数量相加。

## 保留与修改的契约

日期框展示服务器时间戳自身的日历日，不是转换后的 UTC 日。用户未改这个日期，或者只换了同一天的输入格式时，保存保留原时间、偏移、精度及整份 JSON；只改名称也不重写日期。主动改到不同显示日时仍生效，即使那个日恰好是旧时间戳的 UTC 日。真改日期沿用现有 `Moment` 解析与 UTC 序列化策略，常规日期输入写为 UTC 午夜，不新增时区设置。

当前 96 项覆盖正负偏移跨 UTC 日及不跨日、Z 与零偏移、极端偏移、闰日/跨年、五种同日输入格式、名称编辑、匿名 GET/POST 正文、真改/清空/缺失/null、坏输入原子拒绝与未知字段保留；另有两种文化下同日／异日显式时刻偏移的宽松解析输入；每次恢复原文化。未解析的服务器坏日期等既有行为不在此小修中改变。

## 定向命令

在当前工作树根目录执行；SDK 以 `global.json` 为准。当前成功编译后才运行 `--no-build`。

```powershell
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- metadata-date-fixture
```

## 证据边界

- 此夹具用匿名连接、假 HTTP 响应和内存对象；不提供令牌，不执行登录/设置/保护器/真实服务器、媒体写入或播放。
- 读与写使用真实产品方法；`if (ApplyTo) UpdateItemAsync` 驱动仅复刻调用边界，不执行真实 `MetadataDialog` 或 WinUI 的保存按钮。生产 [保存调用者](../../src/Momoka.Shell/Views/ItemCommands.Server.cs) 仍有相同的无改动早退，相关源码另作限定一致性检查。
- 基线 `cfde3c746bbdb4664122bfe3b4cbbe9096c8b13e` 在同一夹具下为 46 通过 / 42 失败 / 0 跳过；最小日历日比较修改后原88全过；独立复核建议补8个显式偏移输入回归后96全过，解析策略未改。原红、构建与源码哈希留在 `artifacts/staged-repair/batch06/r017`，不以历史宿主解析观察代替这轮产品方法执行。
- R-017 的跨日输入在编辑器支持契约下已证实有内部错误并作防误写修正；目标部署是否返回该偏移格式仍未观测，不声称真实服务器触发、WinUI输入或共享元数据存库验收。
