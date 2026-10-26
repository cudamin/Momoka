# MoviePilot 搜索订阅设置离线专项

## 边界与执行来源

- 主项目编译链接**当前完整生产** `PageViewModel`、`MoviePilotSearchViewModel`、`MoviePilotMediaViewModel`（含 Related）、组行、`MoviePilotSeasonPicker` 与 `MoviePilotDialog`；使用真实 CommunityToolkit.Mvvm 8.4.2 源生成属性和命令。
- 引用当前 Core，执行真实 `MoviePilotService` / `MoviePilotClient` / 差量计划；只有内存 `HttpMessageHandler`、WinUI 值和对话/位图解码边界为匿名替身。所有请求限定到 `.invalid`，没有 socket、真实凭据读取、配置持久化、DPAPI、真实订阅/取消、桌面窗口或播放。
- 主夹具程序集复用 Core 现有 `Momoka.Tests` friend 名称，输出在本项目独立 `bin/obj`；没有扩大 Core 内部访问范围，也不覆盖主测试输出。
- `Regression/Momoka.Tests.csproj` 编译链接现有匿名协议回归，独立输出和注册，不改主测试 `Program.cs`。登录重定向回归运行本机双回环 HTTP，只有占位用户名/密码，不连接外部或真实服务。

## 可见行为

- 搜索电视剧打开「请选择订阅季」；默认和可选剧集组、每季开关、三种模式、海报/首播日期/集数/实际订阅与入库状态，下方提交与取消。
- 首次无订阅优先选搜索结果指定季；没有明确季号时选服务端首个普通季，不猜 1。切组保留各组草稿，提交只处理当前可见组/季。
- 搜索电影直接新增；明确已有订阅时按钮为「取消订阅」，再次点击直接取消。状态未知时第一次点击只读，不猜未订阅；不明回执有明确的「已在 MoviePilot 核对 · 重新读取」恢复键。
- 身份由来源 + 类型 + 原生编号 + 季 + 剧集组组成。取消和修改仅对明确订阅 ID，写前核对，写后核实；重复身份不选第一条。每季防重，不明回执保守阻止换组绕过，其他季独立。
- 失败重读服务器实际状态，已成功季不重发；不明回执不会被刷新自动解除。断线、切服、离页和迟到请求不能恢复旧按钮或旧组统计。

## 官方协议证据

本轮读取本地官方 `jxxghp/MoviePilot` 固定提交 **ba2f69b939b90839ec446b1f8b22dd87f5d3fb11**，未调用真实服务器：

- `app/api/endpoints/subscribe.py`：GET 全列表、POST 返回 `success` / `data.id`、PUT 仅 ID 差量、DELETE 明确 ID；新增 `exist_ok=True` 不能证明模式已更新。
- `app/schemas/subscribe.py`：`media_source/media_id/type/season/episode_group`；三模式 `best_version,best_version_full` 分别为 `0,0` / `1,0` / `1,1`。
- `app/api/endpoints/media.py`：默认季与 TMDB 组季路径。
- `app/api/endpoints/mediaserver.py`：`POST mediaserver/exists_remote` 是只读，返回**裸季→集对象**；新增 `ReadPostAsync` 区别于必须有 `success` 的写操作。
- 同机编译前端 `mp-web/assets/SubscribeSeasonDialog-B9KG_PVi.js` 用于核对截图结构和三模式，不当作可编译源码或版本身份的替代证据。

## 运行

在当前工作树根目录，使用 `global.json` 指定的用户级 .NET 10 SDK：

```powershell
& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.MoviePilotSubscribeSettingsTests/Momoka.MoviePilotSubscribeSettingsTests.csproj --disable-parallel -p:NuGetAudit=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.MoviePilotSubscribeSettingsTests/Momoka.MoviePilotSubscribeSettingsTests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.MoviePilotSubscribeSettingsTests/Momoka.MoviePilotSubscribeSettingsTests.csproj -c Release --no-build --no-restore

& "$HOME/.dotnet/dotnet.exe" restore ./tests/Momoka.MoviePilotSubscribeSettingsTests/Regression/Momoka.Tests.csproj --disable-parallel -p:NuGetAudit=false
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.MoviePilotSubscribeSettingsTests/Regression/Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.MoviePilotSubscribeSettingsTests/Regression/Momoka.Tests.csproj -c Release --no-build --no-restore
```

主专项 **62 项**：三模式新增/更新 payload、完整身份防误取消、多季/组、电影实际命令无对话、取消、错误与重入、组季草稿、统计和对话迟到、实际源生成通知、不明恢复。

相关既有匿名回归 **160 项**，与主 Core 回归重叠，不能与全 Core 数相加。原有两个按季新增夹具仅补上新增写前 GET 的回话序列，主测试 Program 未改。

## 集成与未覆盖

- 父窗口在最终整合树将主专项 csproj 纳入 `Momoka.sln` 和适用验证入口；`Regression` 可保持手动专项（与主 Core 重叠），不需新增 DI 注册。
- 未启动桌面，**未验证真实 WinUI 像素、宽窄窗、主题、高对比/读屏、鼠标/键盘和实际弹窗提交按钮布局**；由父窗口预约后的截图与 UIA 验收补齐。本专项 XAML 合同及 Release XAML 编译不冒充截图证据。
- 未验证真实 MoviePilot 的下载、跨来源自动转 TMDB、账号权限或 POST 延迟副作用。固定官方 `app/chain/subscribe/create.py` 在 `RECOGNIZE_SOURCE=themoviedb` 时可能转换非 TMDB 主身份；当前实现对不同身份回读保守报待核对，**不把同编号的 TMDB 记录当作豆瓣/其他来源的可取消目标**。自动转换场景需父决定是否另做有明确转换证据的产品流程。
- 无 push、发布、快捷方式变更；父整合重验并发布前，桌面快捷方式仍是旧版。
