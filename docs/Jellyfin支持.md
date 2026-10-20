# Jellyfin 连接与服务器管理

在登录页输入 Jellyfin 地址即可，包含反向代理子路径的地址也会保留。客户端通过匿名 `System/Info/Public` 识别产品，保存产品与版本；Jellyfin 使用服务根目录和 `Authorization: MediaBrowser` 请求头。**Jellyfin 10.10.7 的 `ProductName` 是 `Jellyfin Server`，不是简称 `Jellyfin`**；客户端按前缀识别，并在登录/恢复后用公开信息纠正误存为 Emby 的服务器类型与 API 基址。令牌不放入 URL、日志或播放器启动参数。

接口分别适配 Jellyfin 10.x 与 12.x：12.x 用户内容路由使用 `UserViews`、`Items`、`UserItems/Resume` 等顶层路径，旧版继续使用其用户路径。恢复保存的 Jellyfin 登录时先刷新公开版本信息，避免服务端升级后沿用旧路由。主页实时通道对 Jellyfin 使用网页端同样的 `/socket?api_key=…&deviceId=…`，Emby 才走 `/embywebsocket`。协议对照来自 Jellyfin 12.2.0 OpenAPI 快照与官方 10.11 源码；离线测试不等于对真实服务器的验证。

## 媒体库界面（Jellyfin Web 10.10）

Jellyfin 连接使用自己的库界面，Emby、飞牛影视与 Plex 继续走原来的库界面。分类栏照 Jellyfin Web 10.10：

| 库类型 | 分类 |
| --- | --- |
| 电影 | 电影、建议、预告片、我的最爱、合集、类型 |
| 电视剧 | 节目、建议、即将播出、类型、制片发行商、剧集 |
| 混合内容 | 电影和节目、类型、文件夹（沿用通用库三格） |

- **排序**：电影、电视剧、剧集、预告片、合集各有自己的菜单与默认排序，词条使用 Jellyfin 的「名称、随机、公众评分、影评人评分、加入日期、播放日期、家长分级、播放次数、发行日期、播放时长」等译名；电影默认 `SortName,ProductionYear`，剧集默认 `SeriesSortName,SortName`。Emby 的排序目录不受影响。
- **筛选**：电影/预告片/我的最爱、电视剧、剧集分别提供播放状态、状态/剧集、功能、类型、家长分级、标签、视频类型、年份等复选框组；查询参数使用 Jellyfin 的 `IsPlayed`、`IsUnplayed`、`IsResumable`、`IsFavorite`、`VideoTypes`、`Is4K`、`ParentIndexNumber`、`IsMissing`、`IsUnaired`、`SeriesStatus`。合集与索引页在网页端没有筛选按钮，这里同样收起。
- **布局**：Jellyfin 库用「布局」按钮替代 Emby 的设置齿轮；电影与电视剧六种（横幅图、列表、海报、海报卡片、缩略图、缩略图卡片），合集五种，剧集三种（列表、海报、海报卡片）。海报卡片与缩略图卡片会在图片和两行文字外面画卡片底；恢复旧显示设置时，Jellyfin 菜单认不出的形状（表格、光盘封面、徽标）退回海报。
- **库菜单与条目菜单**：分类栏标签、筛选按钮、布局菜单都只在 Jellyfin 来源下出现；Jellyfin 的「更多」菜单使用自己的中文词条（播放、标为已播放/未播放、加入收藏、加入集合、下载、编辑元数据、修改图片、修改字幕、刮削元数据、刷新元数据、删除媒体/系列/剧集），Emby 的菜单文案与次序不变。
- **建议页**：电影建议页是「继续观看、最新电影、为你推荐」三排，分别来自 Resume、`Items/Latest` 与 `Movies/Recommendations`；电视剧建议页是「继续观看、最新剧集、接下来看」，最后一排来自按库限定的 `Shows/NextUp`。混合内容库暂不提供建议页。
- **其它**：`GroupItemsIntoCollections` 是 Emby 参数，Jellyfin 请求不再发送；Jellyfin 的合集页通过 `IncludeItemTypes=BoxSet` 查询。

主页「最近添加」的每个剧集库不走 `/Items/Latest`：Jellyfin 的 Latest 只收可播放条目，一个只有剧集、还没扫出单集的库会回空表，整排会被当成空排隐藏。Jellyfin 电视剧库改为 `IncludeItemTypes=Series`、按 `DateLastContentAdded` 倒序的条目查询（`HomeLatest`）；电影库、混合库和其它服务器保持原查询。

管理入口随连接自动取舍：只有一台服务器在线时，Emby 在 Momoka 组里保留「通知」，服务器目录分「Emby Server／设备／高级」三组；Jellyfin 没有通知入口，管理员在 Momoka 组下面看到自己的五组目录。两家的「网页控制台」分类都已退役，统一落到服务器目录的第一项「控制台」；`--show-settings 网页控制台` 仅作兼容别名。Jellyfin 管理权限以用户 `Policy.IsAdministrator` 为准；普通账号、未连接和权限未确认时都不列出。切换服务器或账号后重新计算入口，失效页面会退出，切换产品时按新产品重建控制台。

同时连着多台服务器时，界面仍显示为通用界面，但设置左栏会在 Momoka 组之后为每台在线服务器各列一个可折叠组，多服务器时默认全部收起，展开后显示那一台自己的五段目录。组内入口绑定那一台 Jellyfin 的捕获会话：网页控制台、源路由和登录态都指向它，不会借用当前来源的令牌；没有管理员权限的 Jellyfin 组不占行。单服务器时的分组与入口次序不变。

Jellyfin 控制台的原生左栏照 Jellyfin Web 10.10 的管理抽屉分成五段，完整列表与 `JellyfinServerSettingsMenu` 一致：服务器（控制台、常规、用户、媒体库及显示／元数据／NFO 设置、播放相关的转码／继续观看／流媒体／特技播放）、设备（设备、活动）、电视直播（电视直播、数字录像机）、插件（我的插件、目录、服务器插件配置页）、高级（网络、API 密钥、日志、计划任务、媒体资料管理器）。条目把网页端两个可展开分组里的子页平铺出来，因为父项本身不是页面。选中条目会把内嵌网页带到 `/web/index.html#/dashboard/…`；网页自己那份抽屉与 React 顶栏由本地皮肤收起。插件配置页通过 Jellyfin 的 `web/ConfigurationPages?enableInMainMenu=true` 读取（与 Emby 的参数形状不同），CustomCssJS 页不列。飞牛影视作为唯一在线服务器时另有自己的「帐号／影视服务器」原生目录（见 [飞牛影视](飞牛影视.md)），与 Jellyfin 的五段目录不混用；Plex 没有原生目录。

| 设置入口 | Jellyfin 行为 |
| --- | --- |
| 控制台 | 使用 Jellyfin 的 `#/dashboard` 页面和 `jellyfin_credentials` 登录格式；浏览器数据按服务器与用户隔离 |

Jellyfin 皮肤与 Emby 分开注入：React／MUI 外壳（顶栏、抽屉）收起，旧版 `verticalSection`、`fieldset`、列表和设备卡改为 Momoka 面色、12px 圆角、无描边的紧凑卡片，输入框／下拉／复选框／按钮／滚动条换成 WinUI 样式，底色取当前主题的窗口色。主题只作用于这个 WebView，不写服务器主题或用户偏好。

2026-10-16 起 Emby 与 Jellyfin 都不再提供原生「控制台」「用户」「媒体库」三页，相关分类名一并退役（见 `PROGRESS.md`）；媒体库与用户改在服务器自己的网页端里管理。

远程候选图片没有受支持的代取接口时隐藏预览，保留文字选择、应用和本机图片上传。

MoviePilot 查找 Jellyfin 中已入库的对应条目时，分页读取当前用户可见的媒体并核对提供者标识，避免发送 Jellyfin 不支持的 Emby 筛选参数。大媒体库可能需要更多请求。

网页登录数据只写入受信服务器的 Web 路径下的本地存储，不注入其他站点；缺少服务器标识时使用网页自己的登录流程。Jellyfin 网页主题由其自身设置管理，客户端不会向它写入 Emby 专属主题键；嵌入控制台的配色只由本地皮肤覆盖。

验证使用 `JellyfinProtocolTests`、`JellyfinWebConsoleTests`、`ServerAccessTests`、`ServerSettingsTests`、`LibraryBrowseTests`、`LibraryRecommendedTests`、`ItemMenuTests`，并通过 `--probe-shell` 的「非管理员账号隐藏服务器管理入口」用例检查 Jellyfin 五组目录、插件页、媒体资料管理器、非管理员隐藏、宽窄导航和切回 Emby 的恢复。Jellyfin 媒体库另有「Jellyfin 媒体库标签、排序、筛选、布局与建议」用例，使用假 Jellyfin 传输逐项检查分类栏、排序菜单与默认值、复选框筛选、布局菜单、海报卡片、合集、建议货架和剧集页，并保存界面截图；它不连接真实服务器。真实 Jellyfin 写入与真实播放按主规则的授权档另行验证；本轮在 10.10.7 服务器上以只读方式核对了公开信息、管理页路由、同文档切页和皮肤，未修改服务器设置。**新增的媒体库界面只在假传输与假数据上验证过，尚未在真实 Jellyfin 库上核对选项结果与视觉密度。**
