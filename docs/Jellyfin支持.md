# Jellyfin 连接与服务器管理

在登录页输入 Jellyfin 地址即可，包含反向代理子路径的地址也会保留。客户端通过匿名 `System/Info/Public` 识别产品，保存产品与版本；Jellyfin 使用服务根目录和 `Authorization: MediaBrowser` 请求头。令牌不放入 URL、日志或播放器启动参数。

接口分别适配 Jellyfin 10.x 与 12.x：12.x 用户内容路由使用 `UserViews`、`Items`、`UserItems/Resume` 等顶层路径，旧版继续使用其用户路径。恢复保存的 Jellyfin 登录时先刷新公开版本信息，避免服务端升级后沿用旧路由。协议对照来自 Jellyfin 12.2.0 OpenAPI 快照与官方 10.11 源码；离线测试不等于对真实服务器的验证。

管理入口随连接自动取舍：Emby 在左栏列出「通知」与「网页控制台」，Jellyfin 只列「网页控制台」（Jellyfin 没有对应的通知服务管理 API，整项隐藏；保留本机的 Emby 通知偏好供切回时使用）。两者都排在 诊断 与 关于 中间。Jellyfin 管理权限以用户 `Policy.IsAdministrator` 为准；普通账号、未连接和权限未确认时都不列出。切换服务器或账号后重新计算入口，失效页面会退出，返回 Emby 后恢复原来的分类次序。

| 设置入口 | Jellyfin 行为 |
| --- | --- |
| 网页控制台 | 使用 Jellyfin 的 `#/dashboard` 页面和 `jellyfin_credentials` 登录格式；浏览器数据按服务器与用户隔离 |

2026-10-16 起 Emby 与 Jellyfin 都不再提供原生「控制台」「用户」「媒体库」三页，相关分类名一并退役（见 `PROGRESS.md`）；媒体库与用户改在服务器自己的网页端里管理。

远程候选图片没有受支持的代取接口时隐藏预览，保留文字选择、应用和本机图片上传。

MoviePilot 查找 Jellyfin 中已入库的对应条目时，分页读取当前用户可见的媒体并核对提供者标识，避免发送 Jellyfin 不支持的 Emby 筛选参数。大媒体库可能需要更多请求。

网页登录数据只写入受信服务器的 Web 路径下的本地存储，不注入其他站点；缺少服务器标识时使用网页自己的登录流程。Jellyfin 网页主题由其自身设置管理，客户端不会向它写入 Emby 专属主题键。

验证使用 `JellyfinProtocolTests` 与 `JellyfinWebConsoleTests`，并通过 `--probe-shell` 的「非管理员账号隐藏服务器管理入口」用例检查设置分组的产品切换、非管理员隐藏、宽窄导航和恢复。真实 Jellyfin 写入、真实播放及服务器自带 Web 客户端登录，需要对应服务器上的另行验证。
