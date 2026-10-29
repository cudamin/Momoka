# MoviePilot 媒体管理与智能整理回归

## 范围与来源

官方前端 v3 `1da03170e54c8fbc36d3d4b7ddf33d074f1a21e2` 的 `TransferHistoryView.vue`、`TransferHistoryDeleteDialog.vue` 与对应测试；后端 v3 `be65e047b0e64b216420ba38bec7a087409552ff` 的 `app/api/endpoints/history.py`、`app/schemas/history.py` 和 `app/application/history/mutation.py`。
另核官方 v2 `1528176beacc0f9f8d8d9cbf1adf048b0da67b5c`：同样支持四档 DELETE 和 `ai-redo`，但删除成功信封没有分项回执，客户端只报告已接收、待核对，不伪造完成。

- Emby／Jellyfin 媒体更多菜单中“媒体管理”紧接“手动整理”；FnNas／Plex 保留原手动整理不可用的边界，媒体管理紧接版本搜索。四项短菜单为“仅删除记录／删记录和源文件／删记录和库文件／删除记录及两端”，具体范围在确认表完整说明。电影／单集显示“智能整理”，剧／季显示“智能批量整理”；媒体库、合集、演员没有这些入口。
- 使用当前 MP 用户 `user/current` 的管理权限和 `system/global/user` 中 `AI_AGENT_ENABLE`。权限／总开关不可确认时隐藏相关入口；订阅／搜索的原能力不借用管理权限。开关和连接配置不被清空。
- 先收齐当前媒体服务器下的所有文件、分页历史，按完整路径匹配唯一记录，核对类型和已知 TMDB 主身份。1000 文件硬上限明确拒绝，默认手动整理的 30 文件上限不变；不截断，不扩大为父目录，不按同名作品猜记录。
- 源／目标和记录编号在二次确认前展示；默认 Enter 关闭。确认后再次核对历史快照、权限、MP 连接、源账号与页面所有者；软链接依赖（包括本批次内引用）直接拒绝，不级联处理。
- 四档仅以 `DELETE history/transfer?deletesrc=...&deletedest=...` 和 `{ id }` 提交。智能单条为无正文 `POST history/transfer/{id}/ai-redo`；剧集／多个文件为 `POST history/transfer/ai-redo`、`{ history_ids }`，不拼聊天提示词、不回退普通手动整理。
- 本进程防重与手动整理共用历史／文件键。部分删除逐项保留并停止后续；响应不明、确认重复、换档或重开菜单不重发。批量中后续 401 不重放前面已执行 DELETE。任务进度键是接收证明，不是文件整理完成证明。
- 右侧设置页同款白色 MP 图标 保留左侧动作图标、真实菜单 Text、框架命名视觉状态、加速键和子菜单箭头；混合 MoviePilot／AniBT 来源只标记 MP 子项。MP 订阅菜单也使用同一品牌样式。菜单与设置页共用 `EgMoviePilotMarkTemplate` 的三个路径和正文颜色角色，不再复制紫色 SVG。

## 命令

在本次工作树根目录，使用固定用户级 SDK；当前 Release 编译成功后才 `--no-build`：

```powershell
& "$HOME/.dotnet/dotnet.exe" build ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:MSBuildNodeReuse=false
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- moviepilot-media-management-fixture
```

Core 套件真实服务／客户端／匿名假 HTTP，拒绝解密、登录、真实网络、播放、文件删除；覆盖菜单、全部标志、身份歧义、记录变更、权限／助手关闭、软链接依赖、单条／批量路由、预取消、回执丢失、部分成功、防重和 401 批次重放拒绝。

真实 WinUI 探针复用现有隔离入口：

```powershell
$exe = Join-Path (Get-Location) 'src/Momoka.Shell/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Momoka.exe'
& $exe --probe-shell --probe-section moviepilot --screen 1
```

新增 `MoviePilotMediaManagementAsync` 在 1280／760 宽度实际打开菜单、展开四项，检查 MP 图标在文字右侧并保存实屏；通过 WinUI UIA 调用同一真实确认／取消按钮，假 MP 记录调用次数、方法、正文和分项回执。普通电影取消不写；确认删除和剧集 batch ai-redo 只写匿名传输。验证界限：不证明真实 MP 权限、助手配置、文件处理最终完成、真实删除、服务器刷新或播放；也不声称读屏、高对比或所有服务器原生视图均已实测。

人工／外部 UIA 检查台沿用 `--probe-shell inspect --screen 1`，点击 `InspectMoviePilotMediaManagement` 后，在匿名媒体管理台可打开电影、剧、媒体库菜单以及确认对话框。普通 `inspect` 与 `--probe-section` 仍互斥；没有新增或放宽命令行开关。

部署只读核对（2026-10-28）：MP v3.2.1；管理权限及 AI_AGENT_ENABLE 均为 true，未发整理或删除请求。对应官方后端 tag `daa9212ba161d4573baf3744e2f13b99c2a90516`、前端 tag `4797eda16252fb6590e36f2b9ede5fb535d9df04` 已定点复核，本轮四档删除／ai-redo 相关源码与上述实时 v3 HEAD 相同。该读取不能证明真实文件处理成功。

## 2026-10-29 主树兼容核对

- 新增跨服务器入口／迟到命令拒绝、七字菜单与非动漫识别回归；媒体管理收集显式 `seriesFiles: true, maxFiles: 1000`，原手动整理仍只收整剧身份、默认30文件，AniBT逐文件展开不变。
- 本轮实时官方前端仍为 `1da03170e54c8fbc36d3d4b7ddf33d074f1a21e2`，后端 `87561a90d16e9bc087225c72bbfebe16865b3baa`；GitHub history接口、模型与mutation三个文件和已记录部署v3.2.1 tag逐字相同。前端TransferHistoryView／删除对话框／相关测试已重新读取；不依赖旧截图来证明当前主树验收。
- 当轮没有重新读取真实部署版本、权限或助手配置，v3.2.1仅为前次记录；当前UI会在真实使用时安全读取能力。匿名本地验证不能证明真实文件删除／智能助手最终完成。

- 主树实际菜单在1280／760窗口逐一检查全部五主题，保存 `mp-media-menu-<theme>-<width>.png`／`mp-media-submenu-<theme>-<width>.png`；删除确认窗另存五主题图。锚点置于顶部避免长菜单越出宿主，保持图标右侧、句柄和原确认／零写断言。
- 异步连接后订阅分区的Visible读数先过实际标题布局，再守原正高度／图标完整裁切检查；不把还没排版的0当产品失败，不改原失败阈值。首轮隐藏启动被实屏保护拒绝，保留原报告；后续用直接前台诊断启动，不改变像素保护。

- 发布件首轮moviepilot25/26，仅旧订阅悬停固定80ms进度读数失败；原红报告单独保留。探针先证明外部焦点、结束物理watch残留并确认归一化初态，再在原UntilAsync截止内等真实Storyboard进度；缩放 `(1,1.045]`、离开0.001容差与更多隐藏阈值不变。不修改订阅海报产品动画或用户偏好。
