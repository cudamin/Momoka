# R-016 卡片用户状态顺序专项

本项目独立运行，已纳入 Momoka.sln 与开发文档的离线验证命令；不修改 Momoka.Tests / PageContractTests 的公共 Program、csproj 或夹具。保留独立计数，普通 Core 或页面契约运行不会自动执行它；入口为 Momoka.Tests.ItemUserStateOrderTests.Main()。

## 验证的是实际生产代码

- 编译链接真实 ItemCommands.cs、CardItem.cs、PosterCache.cs、IShellActions.cs，引用本树当前 Core。
- 编译时从 ItemCommands.Server.cs 原样提取 AskAsync / GuardAsync，从 PosterCard.xaml.cs 原样提取 CardAction / CardActionEventArgs；找不到明确边界会使构建失败，不降级到复制实现。生成物仅在本项目 obj 内。
- 菜单用真实 Build / 点击委托，hover 用真实 Handle；状态应用、通知调用和来源守卫均由生产代码执行。只替代框架值/点击事件、原生图片解码以及无关对话框命令，无关命令一旦执行即失败。
- 受控单线程泵运行实际 ConfigureAwait(true) 回调，能扣住已完成 HTTP 的 UI 应用点再撤销来源。不用固定睡眠推测请求已经结束，完成异常/取消不能被泵计成成功。

## 严格离线边界

synthetic .invalid 主机只由注入 HttpMessageHandler 在内存回答；只允许原假账号的 PlayedItems / FavoriteItems POST 或 DELETE。测试通过反射为新的假 session 注册来源，不调用 SignIn、Restore、Adopt、SelectSource、保存配置、真实登录、凭据交换或服务器探测。ISecretProtector 读写均抛异常。独立目录仅写在本树测试 bin/Release 输出下，不递归删除。假 IShellActions 只记录通知、已看补报和收藏刷新，不创建 PlaybackService、不加载 libmpv 或起播。

## 精确运行命令

在工作树根目录 PowerShell 中执行（SDK 10.0.400，Release 单节点）：

~~~powershell
& "$HOME/.dotnet/dotnet.exe" restore 'tests/Momoka.ItemUserStateOrderTests/Momoka.ItemUserStateOrderTests.csproj' --source "$HOME/.nuget/packages" -p:NuGetAudit=false -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" build 'tests/Momoka.ItemUserStateOrderTests/Momoka.ItemUserStateOrderTests.csproj' -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
# 仅当前构建成功后运行，不能借其它树 DLL。
& "$HOME/.dotnet/dotnet.exe" run --project 'tests/Momoka.ItemUserStateOrderTests/Momoka.ItemUserStateOrderTests.csproj' -c Release --no-build --no-restore
~~~

本机已有固定包缓存可完全离线 restore；缓存含共享props已使用的分析器，未新增或升级依赖。NuGetAudit=false 仅用于这次固定缓存的离线还原，不永久修改仓库审计策略。缓存缺少依赖时明确失败，不升级或联网补齐。

## 覆盖和限制

57 个注册用例包含 6 组四请求交错，每组 24 种响应排列、整份/混合 204 两档，共 288 次假请求场景，不能把嵌套场景额外累计为注册用例。覆盖已看/收藏两种发起顺序、逆序/顺序/旧字段整份响应、全部进度/计数/日期字段、204 / JSON null、500、同字段反向/重复/失败、两入口、共享 DTO、不同条目、身份改绑、来源撤销与正常换默认。

状态按 EmbyItem 实例弱绑定，两个 CardItem 共享同一 DTO 时共用顺序；不新增跨独立 DTO 的全局条目缓存或跨页通知服务。两个字段重叠时只接受各自操作的字段组，已看组拥有进度/计数/日期；非重叠时仍保留整份响应刷新另一组的能力。失败不确认请求方向；较新成功屏蔽同字段旧成功的状态和副作用。

未验证真实 WinUI 输入/渲染/视觉、原生解码、真实服务器处理顺序、播放通知传输、其它客户端同时改状态；没有 DTO 服务端版本，无法证明任意服务端同字段逆序执行的一致性。本项目不重试已停止的 R001/003/005/031 链路，也不发布、推送或升级依赖。
