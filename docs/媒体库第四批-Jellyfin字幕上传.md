# 媒体库第四批：Jellyfin 本地字幕上传

2026-11-12，Task 2，独立树 `library-batch4-jellyfin`，基点 `52f8449`。本树完成实现与离线专项，主树整合、最终原生 UI 验收及发布由主控执行；本记录不宣称真实 Jellyfin 字幕写入已验证。

## 交互与权限

Jellyfin「修改字幕」对话框增加原生 `ComboBox` 媒体版本选择和本地字幕上传区。只有当前账号明确具有 `EnableSubtitleManagement=true` 才显示上传；只有下载权、明确拒绝或未知时隐藏整区。管理员身份不替代上传权限，管理员网页控制台入口保留。Emby 不出现本轮上传和版本选择。

选择字幕使用所属窗口初始化的系统 `Windows.Storage.Pickers.FileOpenPicker`；正文沿用现有 `ContentDialog`、滚动布局、主题资源及点外取消。可选 SUB、SRT、VTT、ASS、SSA、MKS，语言沿用中文/英语/日语/韩语目录，另有强制与听障标记，不增加默认字幕字段。六种扩展来自官方网页；**10 MiB 是 Momoka 本地资源限制**，不是 Jellyfin 协议上限。文件须大于 0，扩展统一为小写，原字节转 Base64，保留 BOM 和旧编码，不转换文本。

权限显隐变化保留文件草稿、语言和标记；切媒体版本清空草稿、候选和删除确认。各版本持有独立、固定 `ServerSubtitles` 委托；读写使用媒体源 `Id` 对应的版本条目，不从后来选中的下拉值猜目标。上传前再次读取版本并核唯一源 ID/路径、当前身份和权限代次。验证期间离页、注销或降权，包括 A→B→A 权限变化，拒绝旧提交。

真实 Page 的 `Unloaded` 撤销上传生命周期。卡片仍挂原根的回收 `Unloaded` 不撤销页面，正常对话框关闭不当作离页。系统选择返回、属性读取、打开流、加载与复制字节前后核对原页面/版本/权限归属；失效后丢弃结果并释放已打开资源。

## 固定上游

先读缓存的服务端与官方网页实现，证据在本树忽略目录 `artifacts/upstream`。

| 固定版本 | 文件与符号 | 已核对行为 |
| --- | --- | --- |
| Jellyfin 10.10.7，`aefb9b2cffca3ccb7a95d89af0d9fb2040de6ed3` | `Jellyfin.Api/Controllers/SubtitleController.cs`，`UploadSubtitle` | `POST Videos/{itemId}/Subtitles`，SubtitleManagement 策略；Base64 解码、等待上传完成、排队索引、返回 204。 |
| Jellyfin 12.4，`83d0d0e7f76e23e2e085c8ff67d28b772d35967d` | 同控制器 `UploadSubtitle` | 本轮路由、权限、五字段、Base64/204 合同相同，无 MediaSourceId 查询参数。 |
| Jellyfin 10.10.7 | `Jellyfin.Api/Models/SubtitleDtos/UploadSubtitleDto.cs` | JSON 只有 `Data`、`Language`、`Format`、`IsForced`、`IsHearingImpaired`，没有 `IsDefault`。 |
| Jellyfin 10.10.7 | `Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs`；`Jellyfin.Api/Auth/UserPermissionPolicy/UserPermissionHandler.cs` | SubtitleManagement 映射 `PermissionKind.EnableSubtitleManagement`；用户 handler 调 `HasPermission`，不借下载权或管理员判断。 |
| Jellyfin 10.10.7 | `MediaBrowser.Providers/Subtitles/SubtitleManager.cs`，`UploadSubtitle` / `TrySaveSubtitle` / `TrySaveToFiles` | 按库选项保存媒体目录或内部元数据目录；文件名带语言/forced/sdh，重名递增编号，等待复制完成，保存异常会抛出。 |
| Jellyfin Web `v10.10.7`，`f4b8aa0ed4c5b571a3a6cc3bb027bb8ecebe5b68` | `src/components/subtitleuploader/subtitleuploader.js` / template，`src/utils/file.ts` | 六扩展、语言/两标记；删除 data URL 前缀，提交原字节 Base64 五字段。 |

缓存 SHA256：10.10.7 控制器 `e437c0d1b7c2402544cc4bae691e310ef6c34ceaa2e01b2364c3645948a63183`；12.4 控制器 `f50e654fc1e0cc8518b979b05c66436c0f8fafbba838541b96431b8e4179cefa`；DTO `be1f5dc3d4ed73b82b136ffc92b39b40c23caf590cb10387b44bcf2d2588c8da`；manager `d5558fa1618bb2fbb665c48c48ac905dbb681120d97793dbe9332d2149362ad0`；网页上传器 `43cc5367c2b4fa28512a47216767cb35a9b0cd9ff0bce0116c8c570f644ea989`。

WinUI Gallery 固定 `6de91feccadfbb0a796b71caedead5a2284f730e`，已读 `Samples/ContentDialog/ContentDialogPage` XAML/code-behind、ContentDialogExample/ContentDialogContent、`Samples/ComboBox/ComboBoxPage` 和 `Samples/StoragePickers/StoragePickersPage`。沿用桌面 XamlRoot、原生控件和窗口归属；Gallery `2.6.6-experimental` 不引入，保持 Momoka 当前稳定包和 HWND 宿主。Gallery 新 picker 返回路径，本轮保持既有 WinRT picker 的 StorageFile/有界流读取，不改其他选择器。

## 回执与读取归属

上传只把完整 204 视为可靠接收回执。窄 `EmbyHttp.PostNoContentAsync` 在完整无正文 204 时报告确认，尾部离页/注销取消不抹掉成功；文件/语言/版本校验在标记已发出前完成，明确 4xx 可重试，不造未知写入。

204 后立即 `Touched=true`、`NeedsRefresh=true`、清上传草稿，再补读。失败或延迟索引保留“服务器已接收”，仅重读，不自动重发。补读失败锁版和写操作，成功 GET 后恢复；不保证索引已显示本次字幕。关闭时把父刷新义务交给仍当前的原页，离页/注销不刷新新父页。

丢失回执、非 204 或 5xx 上传进入只读恢复，不能切版、搜索或继续写；恢复 GET 不追认成功。Jellyfin 下载 204 无候选确认语义仍保留，网络未知下载/删除也不能通过 A→B→A 绕过保护。删除仍有完整字幕快照/同标签唯一性与取消零写；GET/DELETE 不是服务端原子事务。

多版本异步读取分为纯读取和接受快照：只有 Dialog 接纳当前目标/代次后才同步 `target.Source` 和服务删除快照。旧 A 读取在 B 时返回，或 A→B→A 的新读取先返回，不再单独修改服务 `_source/_snapshot` 导致当前合法删除被引用检查拒绝。公共 `ReloadAsync` 仍保留原有完整读/接受行为。

## 本树已执行验证

用户级 .NET SDK，Release/x64、单节点、禁共享编译。最终 Core/Tests、字幕源码夹具与 Shell 构建均 **0 警告 0 错误**；Shell 最后编译 3 分 11.93 秒，日志在 `artifacts/batch4-jellyfin/shell-build.log`。42 份改动文件的 UTF-8/LF/末换行/尾空白和 `git diff --check` 通过；自动化静态扫描 688 个交互元素、缺失句柄 0，不代替真实 UIA 定位。

| 入口 | 结果 | 日志 |
| --- | --- | --- |
| `Momoka.Tests.dll jellyfin-subtitle-upload-fixture` | 55/55，失败 0、跳过 0 | `jellyfin-subtitle-upload-fixture.log` |
| `Momoka.Tests.dll subtitle-target-fixture` | 207/207，失败 0、跳过 0 | `subtitle-target-fixture.log` |
| `Momoka.Tests.dll restore-identity-fixture` | 115/115，失败 0、跳过 0 | `restore-identity-fixture.log` |
| `Momoka.Tests.dll http-download-error-fixture` | 45/45，失败 0、跳过 0 | `http-download-error-fixture.log` |
| `Momoka.SubtitleDialogTests.dll` | 89/89，失败 0、跳过 0 | `subtitle-dialog-tests.log` |

下载/删除未知切版用例先红 **83/85、2 失败**（`subtitle-version-unknown-red.log`），修后 85/85；再补确认写回读失败锁版恢复，87/87。服务快照读取归属用例先红 **87/89、2 失败**（`subtitle-reload-owner-red.log`），修后 **89/89**。新上传专项包含 10.10.7/12.4 JSON、非首源、权限/身份/文件边界与令牌恢复。Dialog 夹具执行生产 Dialog/caller/父页和 picker，只替换 WinUI/WinRT 端点；专项与完整主测试重叠，不累加。

## 主控待执行

整合后串行运行 `--probe-shell --probe-section jellyfin-upload --screen 1`，或检查台 `InspectJellyfinUpload` / `InspectJellyfinUploadRecovery`。两场景正常 ShowAsync，真实 ContentDialog/ComboBox、实际 Core、假 picker 和匿名内存 HTTP，真实 picker/网络写入/播放零调用。整轮稳定 TopMost，截图不改层级。

1160/680：授权→隐藏→恢复草稿、原生版本下拉、切版清稿、B 版原字节五字段、写中关闭/切版阻止、204 回读失败只 GET、未知 GET 后仍只读。输出 `jellyfin-upload-*-screen.png`、`jellyfin-upload-*-receipt.json`。主控需看浮层与宽窄截图、核自动化定位/点外输入；本树不运行这些原生场景。

完整主测试、真实 Jellyfin 上传、系统 picker 手选、主树整合后验收及发布未执行。`Momoka.NaturalContinuationCallerTests/SourcePins.json` 的 Session/Scope 哈希交主控逐项审查更新，子树不整份接受。主控完成后追加最终提交、运行目录和结果。

## 2026-11-12 集成原生检查

父树整合后的 `jellyfin-upload` 两个场景已执行通过 2/2，1160/680 的上传稿、权限隐藏恢复、版本下拉、在飞保护、204 后只读刷新与未知回执恢复均完成。实际请求回执各为一次写入，未知场景未重复写入；真实 picker、真实网络和播放调用为零。实屏与回执在 `artifacts/library-batch4/jellyfin-native-final`，版本下拉截图针对其实际 Popup HWND，其余针对字幕滚动视口所在窗口；核对 PID 和窗口所有者，不放宽通用视频遮挡守卫。前几轮截图定位失败保留原日志。

当前集成专项上传 55/55、对话/caller/picker 89/89通过；来源 pin 四项逐份核对后自然接续 69/69与10个提取拒绝反例通过。主树最终完整验证与发布仍由本批收尾记录确认。
