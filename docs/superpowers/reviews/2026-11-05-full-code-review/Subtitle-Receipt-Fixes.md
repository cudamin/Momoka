# R04-004 字幕下载回执修复

2026-11-07，Asia/Shanghai。隔离树 `C:/Users/89400/.codex/worktrees/review-subtitle-receipt/Momoka`，基点 `08324e33b355a118112d849ae3e30c8f549b0770`。范围仅为 [R04](R04.md) 的 Jellyfin 下载204误报成功；多版本目标沿用已修的原文件绑定，不重新扩展协议或真实服务器操作。

Jellyfin 10.10.7 固定提交 `aefb9b2cffca3ccb7a95d89af0d9fb2040de6ed3` 与12.4固定提交 `83d0d0e7f76e23e2e085c8ff67d28b772d35967d` 的 `Jellyfin.Api/Controllers/SubtitleController.cs:148–170` 都在捕获下载异常并仅记录日志后返回204；成功下载也只是排队更新元数据。固定10.10.7 `SubtitleManager.cs:139–168,178–219` 显示保存名称由媒体文件、下载响应语言、forced/SDH与格式构造，搜索候选的名称不是保存轨道标识。缓存源码位于 `C:/Users/89400/.codex/worktrees/def8/Momoka/artifacts/review/6ba7a7f/R04/20261106-first/upstream/`，本轮重读并核对 `manifest.json`；两控制器SHA256分别为 `e437c0d1b7c2402544cc4bae691e310ef6c34ceaa2e01b2364c3645948a63183` 与 `f50e654fc1e0cc8518b979b05c66436c0f8fafbba838541b96431b8e4179cefa`。本轮在线重取未取得可用正文，结论依据固定缓存，不宣称核对最新服务器版本。

此前 `ServerSubtitles.DownloadAsync` 将请求完成交给无结果委托；`SubtitleDialog.OnDownload` 无条件设置 `Touched=true`，补读只填列表，不管是否新增都显示“已挂上”。修复让服务明确提供产品回执能力，调用者交给对话框；Jellyfin请求返回后先记 `NeedsRefresh`，保留“请求已返回，但服务器未确认所选字幕已添加”的文案，再执行原固定文件的只读补读。未变、新增、延迟可见、读失败均不能解除本次未确认状态，也不能重新开放下载和删除；刷新与关闭保持可达。搜索不会覆盖未确认反馈。Emby明确回执、删除、响应丢失、关闭、互斥和身份守卫保持既有路径。

Jellyfin回读轨道没有远程候选ID。数量、语言、格式或显示名相似不能严格证明这次候选已安装，因此不伪造目标成功，也不新增下载内容核对。真正读到的新轨道仍填入对话框，关闭时靠 `NeedsRefresh` 和原 scope 回读原详情；`Touched` 继续仅代表可靠写回执。代价是即使列表已新增，Jellyfin本张表仍显示未确认，只能刷新或关闭后重新打开核对；不会自动重发写入或持久化新状态。

验证使用当前完整生产对话框、实际 Core/ServerSubtitles、原样提取的调用者与父页加载方法；仅WinUI值/事件和HTTP端点是匿名替身。11项新增用例覆盖两版204未新增/新增、延迟可见、自动和手动恢复读失败、补读在飞关闭保护、手动读尚挂起关闭、来源切换、明确403及Emby成功对照。没有真实设置、令牌、DPAPI、账号、媒体、服务器写入或播放。

| 阶段 | 结果与证据 |
| --- | --- |
| 初次RED | `artifacts/r04-receipt/red-build.log`：0警告0错误；`red-tests.log`：48项39过9败0跳过，退出1。8项击中回执/欠项，1项在手动GET挂起时过早观察父页，不作为额外产品缺陷。 |
| 初次GREEN（保留红） | `green-build.log`：0警告0错误；`green-tests.log`：48项44过4败0跳过，退出1。新增Jellyfin夹具漏掉真实父详情的 `MediaSegments/{itemId}` 附加GET，未知端点导致父页保留旧值；补上仅该匿名路径的空Items结果，未改2轨预期或产品。 |
| 基点编译边界初次失败 | `baseline-red-build.log`：0警告1错误（CS2001）。固定源archive起初漏掉 `Directory.Build.props` 的 `tools/BannedSymbols.txt` 输入；未运行测试，不算RED。从同一08324e3原样补提该文件并核对Git blob，没有改政策或忽略分析器。 |
| 最终原样基点RED | `baseline-red-build-02.log`：0警告0错误；`baseline-red-tests-02.log`：48项39过9败0跳过，退出1。9项均为回执行为反例，含修正后的手动恢复关闭；旧Core、旧Caller、旧构造签名可直接编译，没有拿缺成员当红。`baseline-source-proof.json` 核对完整Dialog、Caller和ServerSubtitles的Git blob；`baseline-test-proof.json` 核对当前测试快照。 |
| 最终当前GREEN/Shell | `final-green-build.log`：0警告0错误；`final-green-tests.log`：48项全过、0失败0跳过，退出0。`shell-restore.log` 成功；`shell-build.log`：当前Release/x64 Shell 0警告0错误，退出0，用时2:03.09。完整编译结束后才记录结果；没有旧DLL充当当前证据。 |
| 实际WinUI | 现有metadata区域已登记“字幕无确认回执、只读恢复与关闭（匿名UIA）”，用真实搜索/下载/刷新/关闭控件和惰性委托，1100/760宽保存 `subtitle-receipt-*-screen.png`。尚未在本树执行，批次整合后由交付会话在最终主树运行并审屏。 |

基点审计副本只住ignored artifacts，保留完整08324e3 Core、完整Dialog和Caller与当前测试；每轮重新还原/编译对应项目，没有覆盖工作区源文件或用旧产物。SDK以本树 `global.json` 的10.0.400为准，Release、单节点、不共享编译；CLI home、临时文件及包缓存均在 `artifacts/r04-receipt/`。完整批次验证、主树合入及交付仍由根会话串行完成。本树没有发布、推送或刷新桌面快捷方式。
