# R10 上游核对边界

本轮公网只读源码。源码和SHA256登记在`artifacts/review/aa631ff/R10/20261106-first/upstream.json`，逐行范围见[R10验证](R10-verification.json)。无真实播放/服务写入。

| 来源 | 本轮实际阅读 | 与本地关系 |
| --- | --- | --- |
| dyphire/mpv-config `e235480f28674ee507ac87d2d0c8e79ac1fa0682` | scripts/chapterskip.lua 1–185（默认/类别/模块）、609–666（matches完整）、819–895（chapterskip完整） | 标题匹配、80–100秒、前后两章节、200/300秒窗口、第二开头候选、下一章或duration为终点 |
| Jellyfin `v10.10.7` | Jellyfin.Api/Controllers/MediaSegmentsController.cs全文 | 按用户可访问itemId查询，返回QueryResult Items，缺条目404；本地补读只在支持媒体段且请求Chapters时执行 |
| Jellyfin `v10.10.7` | MediaBrowser.Model/MediaSegments/MediaSegmentDto.cs全文 | Type、StartTicks、EndTicks；本地仅Intro/Outro转ServerSkipMarker，过滤非法区间 |

dyphire本轮GitHub commits API返回403限流，未把空响应当提交。随后`git ls-remote`取得上述master提交，用固定codeload ZIP仅取指定Lua源码；没有下载/执行脚本或更改本地依赖。Jellyfin使用固定版本原文件；用户部署版本未核。

本地采用其中的章节识别规则，未实现上游静音快进、历史记录或完整按钮。存在有意范围差异：本地标题命中在时长未知时仍可使用有下一章的区间，上游chapterskip入口对duration≤200整体返回；本地还有中文片尾/大小写等兼容。没有据此要求复制全部上游功能。R10-008是本地VM提前返回跳过本地Merge的缺陷，现有本地回归已经定义非重叠章节须保留，不依赖上游界面行为。

Emby服务端实现不可得；本轮开始/停止顺序结论来自本地真实服务发送的匿名报文，未声称验证真实服务器最终状态。Plex/飞牛映射沿R05已读边界，不重申上游兼容。native ABI/窗口/IPC/uosc完整上游源码与运行核对属于R11，本轮只联读相关本地控制流，不把托管端点称为实际libmpv。
