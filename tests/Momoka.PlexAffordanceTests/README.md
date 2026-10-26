# R-029 / R-030 Plex 入口适用性专项（2026-10-23～24）

## 范围与实现取舍

- 收藏独立判断条目所属 `Scope.Connection.SupportsFavorites`，不合并到 UserState 显隐。Plex 的电影/集卡片及宽窄详情只收起收藏，已看保留；聚合 Emby/Jellyfin 仍可收藏，管理默认来源不参与判定。
- 旧海报/集行快捷、直接 ToggleFavorite/SetFavorite 和详情生成命令/直接方法均在 HTTP、状态操作账、DTO 更新和通知之前拒绝。详情保留活动期、Busy 和条目来源/ID 一致性，不拿换来源前的旧条目借新 scope 写入；支持来源原有成功/失败语义不变。
- R-030 不实现或伪装 Plex 跨库题材查询，只按当前详情 scope 把题材画成普通 Run；原文字/分隔符/配色保留。OpenGenre 守卫旧点击；不填某库 ParentId，不改 Emby/Jellyfin 的 ForGenre 跨库路由。Attach、条目应用、Cancel 和页面重新订阅后同步能力/重画；播放媒体版本来源不冒充详情 scope。
- 局部展示判断留在 CardItem/DetailViewModel，已有 ItemCommands 只加收藏路径守卫，不新增服务/DI/通用能力层，不做 R-025 或已停止的 R-001/003/005/031 登录凭据链路。

## 执行真实生产代码的边界

本项目独立入口、自注册用例；子代理未改共享 Program、方案或文档，主树整合时已最小加入 Momoka.sln 并同步开发命令。下文红绿证据均明确属于来源独立树，不代替主树整合后的当前重验。历史 JSON／报告和基线未改。

- Compile-link 本树真实 CardItem.cs、ItemCommands.cs、PosterCache.cs、IShellActions.cs；引用本树当前 Core。
- 构建时原样提取 ItemCommands.Server.cs 的 GuardAsync/AskAsync、PosterCard.xaml.cs 的事件 DTO、DetailPage.xaml.cs 的 PaintGenres、OnViewModelChanged、GenreRead 和 OnNavigatedTo 中真实 Attach→重订阅→重画→Reload 尾段；找不到唯一边界就构建失败，不改成复制业务模型。
- 详情 Attach、Reload、收藏/已看命令、CanExecute、Cancel、异源版本 SelectedSource 和来源切换均来自显式提供的本树当前 x64 Release Shell DLL。拒绝其他树、非 Release 或非 Momoka.dll 路径。ReflectedDetail 只是调用/通知转发；替代主题画刷、WinUI 值/内联元素/无关视觉边界，不创建 App、窗口或 native/WinRT 对象。
- 复用既有 ItemUserStateOrderTests.Boundaries.cs 的框架/无关命令拒绝边界，不运行其测试用例作为本项目的计数。VisibilityContract 读取真实 XAML 的 OneWay 绑定并消费真实 PropertyChanged；是源码/绑定契约，不是实际 WinUI 渲染/UIA 证据。
- 仅在新建假 session 内安装占位来源，假 handler 只回答本项读写，缺失端点即失败。NoSecrets/假设置/容器/外部动作均拒绝真实凭据/配置/窗口/播放。状态写异步；不相关的元数据/图片上级/版本假读取同步，避免真实后台任务在测试泵释放后继续投递。假数据写入前后 DTO 不与服务端副本共享。
- 临时目录仅在本树 artifacts/r029-r030/fixture/run-随机，不递归删除。日志和构建产物也仅本树；不借其他树 DLL/native，不访问真实服务器、DPAPI、用户 mutex、播放、发布、推送或升级。

## 已执行结果与证据

时间均为 Asia/Shanghai。

| 阶段 | 结果 | 本树日志（artifacts/r029-r030） |
| --- | --- | --- |
| 未改产品的有效红测（2026-10-23 23:51） | 59 项，33 过、26 败、0 跳过，退出 1 | red-regression-final.log |
| 修复中首次汇总 | 59/59 汇总但含未处理队列释放异常，**无效，不能认绿** | green-regression-initial.log |
| 校准有界同步假读后，追加实际页面取消订阅重入回归（2026-10-24 00:06） | 60 项，59 过、1 败、0 跳过，退出 1 | reentry-red-regression.log |
| 最终实际代码专项（2026-10-24 00:11） | 67 过、0 败、0 跳过，退出 0，无未处理异常 | green-regression-final.log |
| 当前 Shell / 当前专项 Release 编译 | 各 0 警告、0 错误 | green-build-shell-final.log / green-build-fixture-final.log |
| 既有用户状态顺序兼容专项 | 66 过、0 败、0 跳过，退出 0 | user-state-order-regression.log |
| 真实约定/分析器哈希检查 | 6 过、0 败、0 跳过，退出 0 | agreements.log |
| 定向空白 | Shell 0 文件修改/退出 0；专项 0 文件修改/退出 0 | format-shell-diagnostic.log / format-fixture.log |
| 自动化扫描 | 432 交互控件；检查范围 312 已可定位、0 无句柄；模板/复用控件不冒充实际 UIA 树覆盖 | automation-ids.log |

有效红测 SHA256：E2EA26D9554B3AD93D7A4D4E910388E0394C994FFAAD87E2FA383797919B6589。
最终绿测 SHA256：4DE4ABD21D92A1801D79D56DC28410527DAAE9426653B36176FFF22EC1A4C0B5。
最终 Shell DLL SHA256：67D1DC72CE917F740AC84BB60AAF58481D4D84F9D3F7A861F1F62775CDB32078。

67 项与 66 项兼容专项分开报告，不把重复能力覆盖或嵌套假请求排列相加。没有运行全 Core、真实 SelfCheck 或 UI 探针；改动未发布，桌面快捷方式仍旧版。只有离线协议/实际源码执行证据，不声称真实 Plex 服务器通过。

## 精确重验命令

以下是本次使用的路径、SDK、Release 和串行标志。主树整合时仅替换 $tree 并先核 Git 根；先 restore，先当前编译成功，随后才能 no-build。缓存缺失就报错，不联网升级/补 native；NuGetAudit=false 仅当次固定缓存 restore，不改审计政策。

~~~powershell
$tree = 'C:\Users\89400\.codex\worktrees\r029-r030-plex-affordances\Momoka'
$sdk = 'C:\Users\89400\.dotnet\dotnet.exe'
$cache = 'C:\Users\89400\.nuget\packages'
Set-Location -LiteralPath $tree
if ((git rev-parse --show-toplevel).Trim() -ne $tree.Replace('\', '/')) { throw '工作树不符' }
$shell = Join-Path $tree 'src\Momoka.Shell\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Momoka.dll'

& $sdk restore 'src\Momoka.Shell\Momoka.Shell.csproj' --source $cache --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& $sdk restore 'tests\Momoka.PlexAffordanceTests\Momoka.PlexAffordanceTests.csproj' --source $cache --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& $sdk build 'src\Momoka.Shell\Momoka.Shell.csproj' -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw 'Shell 编译失败' }
& $sdk build 'tests\Momoka.PlexAffordanceTests\Momoka.PlexAffordanceTests.csproj' -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '专项编译失败' }
& $sdk run --project 'tests\Momoka.PlexAffordanceTests\Momoka.PlexAffordanceTests.csproj' -c Release --no-build --no-restore -- $shell $tree
& $sdk run --project 'tests\Momoka.PlexAffordanceTests\Momoka.PlexAffordanceTests.csproj' -c Release --no-build --no-restore -- agreements

& $sdk restore 'tests\Momoka.ItemUserStateOrderTests\Momoka.ItemUserStateOrderTests.csproj' --source $cache --disable-parallel -p:NuGetAudit=false -m:1 -p:BuildInParallel=false
& $sdk build 'tests\Momoka.ItemUserStateOrderTests\Momoka.ItemUserStateOrderTests.csproj' -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
if ($LASTEXITCODE -ne 0) { throw '状态兼容专项编译失败' }
& $sdk run --project 'tests\Momoka.ItemUserStateOrderTests\Momoka.ItemUserStateOrderTests.csproj' -c Release --no-build --no-restore

$env:Configuration = 'Release'
& $sdk format whitespace 'src\Momoka.Shell\Momoka.Shell.csproj' --verify-no-changes --no-restore --include "$tree\src\Momoka.Shell\Views\CardItem.cs" "$tree\src\Momoka.Shell\Views\ItemCommands.cs" "$tree\src\Momoka.Shell\Views\DetailPage.xaml.cs" "$tree\src\Momoka.Shell\ViewModels\DetailViewModel.cs" --verbosity diagnostic
& $sdk format whitespace 'tests\Momoka.PlexAffordanceTests\Momoka.PlexAffordanceTests.csproj' --verify-no-changes --no-restore --include "$tree\tests\Momoka.PlexAffordanceTests\PlexAffordanceTests.cs" "$tree\tests\Momoka.PlexAffordanceTests\PlexAffordanceFixture.cs" "$tree\tests\Momoka.PlexAffordanceTests\GenrePageContract.cs" "$tree\tests\Momoka.PlexAffordanceTests\InlineBoundaries.cs" --verbosity detailed
node 'tools\scan-automation-ids.js'
git diff --check
~~~

未覆盖：真实 WinUI 像素/布局、宽窄窗口截图、焦点/键盘/UIA 树、Frame 历史、物理输入、真实服务器协议响应和副作用、独立 DTO 跨页同步。用户要求禁止启动 App/native/UI 桌面，因此这些缺口保留给主线程适用批次验证，不把它们标通过。主线程只限定整合六个题内产品文件和本独立专项，统一 PROGRESS/共享文档后对整合 HEAD 重编译重验；本树结果不是完整产品交付。
