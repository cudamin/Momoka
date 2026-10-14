# Jellyfin 连接与服务器管理

在登录页输入 Jellyfin 地址即可，包含反向代理子路径的地址也会保留。客户端通过匿名 `System/Info/Public` 识别产品，保存产品与版本；Jellyfin 使用服务根目录和 `Authorization: MediaBrowser` 请求头。令牌不放入 URL、日志或播放器启动参数。

接口分别适配 Jellyfin 10.x 与 12.x：12.x 用户内容路由使用 `UserViews`、`Items`、`UserItems/Resume` 等顶层路径，旧版继续使用其用户路径。恢复保存的 Jellyfin 登录时先刷新公开版本信息，避免服务端升级后沿用旧路由。协议对照来自 Jellyfin 12.2.0 OpenAPI 快照与官方 10.11 源码；离线测试不等于对真实服务器的验证。

设置中的管理分组随连接自动显示为 **EmbyServer** 或 **JellyfinServer**。Jellyfin 管理权限以用户 `Policy.IsAdministrator` 为准；普通账号、未连接和权限未确认时隐藏管理分组。切换服务器或账号后重新计算入口，失效页面会退出，返回 Emby 后恢复原来的分类顺序。

| 设置入口 | Jellyfin 行为 |
| --- | --- |
| 控制台 | 读取服务器、会话和活动；仅显示已适配的设置和操作 |
| 用户 | 适配列表、创建、权限、密码、头像和删除；修改自身密码时提供当前密码输入；隐藏 Emby Connect、复制用户数据、个人 PIN 等无对应接口的功能；12.x 隐藏既有用户改名 |
| 媒体库 | 适配库列表、创建、重命名、目录、保存、扫描与移除；只显示 Jellyfin 支持的选项、提供者和内容类型 |
| 通知 | Jellyfin 没有对应的通知服务管理 API，整项隐藏；保留本机的 Emby 通知偏好供切回时使用 |
| 网页控制台 | 使用 Jellyfin 的 `#/dashboard` 页面和 `jellyfin_credentials` 登录格式；浏览器数据按服务器与用户隔离 |

无法适配的按钮不以置灰替代隐藏；命令入口和请求层也拒绝不支持的操作。修改库、用户和配置仍遵守服务器权限及原有确认、并发合并和部分失败重试规则。

图片上传按 Jellyfin 的 Base64 正文处理；目录“上一级”解析 Jellyfin 返回的 JSON 字符串，不把 JSON 引号写入路径。远程候选图片没有受支持的代取接口时隐藏预览，保留文字选择、应用和本机图片上传。

MoviePilot 查找 Jellyfin 中已入库的对应条目时，分页读取当前用户可见的媒体并核对提供者标识，避免发送 Jellyfin 不支持的 Emby 筛选参数。大媒体库可能需要更多请求。

网页登录数据只写入受信服务器的 Web 路径下的本地存储，不注入其他站点；缺少服务器标识时使用网页自己的登录流程。Jellyfin 网页主题由其自身设置管理，客户端不会向它写入 Emby 专属主题键。

验证使用 `JellyfinProtocolTests`、管理接口测试与 `JellyfinWebConsoleTests`，并通过 `--probe-shell` 检查设置分组的产品切换、非管理员隐藏、宽窄导航和恢复。真实 Jellyfin 写入、真实播放及服务器自带 Web 客户端登录，需要对应服务器上的另行验证。
