# 详情页预告片

用户要求新增观看预告片，并明确先使用媒体服务器提供的预告片。沿用直接实现、不先做 Demo 的偏好。

## 范围与方案

Emby / Jellyfin 详情页提供“预告片”按钮，有资源时显示，无资源时隐藏。打开后列出服务器内预告片与远程链接，标明“在 Momoka 播放”或“在浏览器打开”；多条可选，点外关闭。读取失败显示原因并允许重试。

服务内预告片用其自身条目 ID、原来源、从零开始播放，不借正片 ID、版本、播放队列或续播位置。远程链接交给现有系统浏览器启动器，仅接受无用户信息的 HTTP(S) URL，不传递媒体服务器凭据。暂不增加网页解析器或独立 URL 播放会话。

可选方案是另建无上报网络播放链路，或自动在线搜索；前者显著扩大播放状态与凭据边界，后者超出用户选定范围。Plex 的 Extras/primaryExtraKey 尚未接入；飞牛官方前端未发现预告能力，本轮两者隐藏。

## 约束与验证

异步结果只属于当前详情条目、来源和页面访问；离页、重载、切换账号后旧菜单及旧响应失效。空列表、错误、取消不影响正片操作。保持现有宽窄布局及主题控件风格。

覆盖真实 DTO/API、URL 过滤去重、实际详情 VM 的获取/选择/失败重试/失效、原生宽窄入口及菜单截图。使用匿名假 HTTP 与录制播放委托，不真实起播、不改变服务器记录。遵循 CLAUDE.md 编译、测试、提交、主树整合重验与发布要求。

## 上游依据

- Emby 官方 SDK `f8499bd9d0e356b8a204a27b142765494dc6f450`：BaseItemDto、MediaUrl、UserLibraryServiceApi；官方前端 4.10.1.0 的 apiclient.getLocalTrailers。
- Jellyfin v10.10.7：UserLibraryController.GetLocalTrailers / legacy route、DtoService、DtoOptions；12 使用既有 JellyfinQuery 路由转换。
- 两者单条详情默认包含完整 DTO，无需更改 Fields。LocalTrailers 返回裸条目数组。
