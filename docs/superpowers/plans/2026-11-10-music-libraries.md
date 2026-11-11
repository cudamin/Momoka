# 音乐媒体库实施计划

> 执行方式：本会话持续实现，独立 Core 子任务使用隔离工作树，根代理负责 Shell 整合与最终审查。

**目标：** 连接现有音乐库与飞牛音乐，提供底部亚克力播放栏。

**架构：** 扩展媒体服务器适配，复用 PlaybackService；AudioOnly 隔离视频初始化；音乐页面与跨页面播放 VM 分工。

**技术：** 当前仓库 .NET 10 / WinUI 3 / libmpv，不升级依赖。

**规格：** ../specs/2026-11-10-music-design.md

## 约束与重点

遵守 CLAUDE.md。所有异步结果受来源/加载代际约束；停止取消预备起播；播放栏不覆盖页面末尾内容；凭据不进入 URL/日志/子进程参数；未授权不播放真实媒体。

## 任务

- [x] 音频播放：PlaybackTicket/Request.AudioOnly、Planner 音频选项与音频 URL、LibMpvBackend/Handle 无窗运行、PlaybackService.IsAudioOnly 和内置音频后端。先测试参数、生命周期/来源撤销，再实现并编译测试。
- [x] 飞牛音乐：Core/FnMusic 协议客户端/适配，MediaServerKind.FnMusic、服务器探测/登录/恢复、媒体查询与能力、图像与音频入口。先用官方匿名夹具测试签名、身份、分页与映射，再实现并测试。对不支持的影视接口明确返回空或拒绝。
- [x] 音乐浏览：Core 音乐查询/队列规则与离线测试；MusicLibraryViewModel/Page 的稳定集合、分页、搜索、容器导航；音乐库不再被目录过滤，Shell 路由音乐条目。
- [x] 播放栏：MusicPlaybackViewModel/Bar 与 Shell 连接；上一曲/暂停/停止/下一曲/进度/模式/队列/收藏/音量；旧 PlayerViewModel 不接管音频事件；宽窄布局与亚克力角色色。
- [ ] 收尾：审查所有差异，当前 Release 测试、自动化句柄扫描与离线运行验证/主题截图；相关文档与 PROGRESS；提交并依照主树规则整合、重验、发布。未完成项明确记录，不能宣称交付。
