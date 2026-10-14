# 接口清单

Momoka **尚未调用任何一条**——下表是接入时的起点。来源两处，逐行标注：

- 【抓包】= 社区抓包文档 `jimboo7339/fntv_danmu_all` 的 `doc/飞牛影视API文档.md`，基于前端 `0.9.7` / 服务端 `0.8.35`。
- 【上游】= MoviePilot `app/modules/trimemedia/api.py`，长期维护的生产实现。

路径一律相对**入口**（`{入口}` 恒以 `/v` 结尾）。多数是 `{入口}/api/v1/...`；登录是唯一的 `/api/v2` 例外。

> 版本号是观测值不是常量。用之前 `GET {入口}/api/v1/sys/version` 重取并记下；对不上就登记缺口，而不是改成"猜的写法"。

## 系统

| 方法 | 路径 | 用途 | 来源 |
| --- | --- | --- | --- |
| GET | `/api/v1/sys/config` | 服务器名、`server_guid`、`initialized`、`nas_oauth`、支持语言 | 抓包 |
| GET | `/api/v1/sys/version` | `{version, mediasrvVersion}`；**接入前的版本自证** | 两者 |
| GET | `/api/v1/server/info` | GPU/解码/直链能力、`meta_dir` 等 | 抓包 |
| GET | `/api/v1/server/oauthStatus` | OAuth 可用性 | 抓包 |
| GET | `/api/v1/server/getAppAuthorizedDir` | App 授权目录 | 抓包 |

## 用户与登录

| 方法 | 路径 | 用途 | 来源 |
| --- | --- | --- | --- |
| POST | `/api/v2/user/loginByPassword` | **登录（现行）**，密码 `sha256` 十六进制小写 → `data.token` | 两者 |
| POST | `/api/v1/login` | 旧版明文登录，**新服务端已废弃**（一律 `-15`） | 上游 |
| POST | `/api/v1/user/logout` | 登出 | 上游 |
| GET | `/api/v1/user/info` | 当前用户信息 | 两者 |
| GET | `/api/v1/manager/user/list` | 用户列表（**仅管理员**） | 上游 |
| POST | `/api/v1/user/getData` | 用户偏好 KV（`key` 如 `mdb:list:setting`） | 抓包 |

登录细节与 `-15` 的陷阱见 [传输与鉴权](http-and-auth.md#登录)。

## 媒体库

| 方法 | 路径 | 用途 | 来源 |
| --- | --- | --- | --- |
| GET | `/api/v1/mediadb/list` | 媒体库列表（普通用户） | 两者 |
| GET | `/api/v1/mediadb/sum` | 各库数量统计 | 两者 |
| GET | `/api/v1/mdb/list` | 媒体库列表（**管理员**，多 `dir_list`） | 上游 |
| POST | `/api/v1/mdb/scanall` | 扫描全部媒体库 | 上游 |
| POST | `/api/v1/mdb/scan/{guid}` | 扫描指定库 | 上游 |

## 条目（媒体）

| 方法 | 路径 | 用途 | 来源 |
| --- | --- | --- | --- |
| POST | `/api/v1/item/list` | **核心查询**：按库/类型/标签筛选 + 排序 + 分页 | 两者 |
| GET | `/api/v1/item/{guid}` | 条目详情（含 `posters`/`backdrops`） | 两者 |
| DELETE | `/api/v1/item/{guid}` | 删除媒体；正文 `{delete_file, media_guids}` | 上游 |
| GET | `/api/v1/search/list?q=` | 搜索影片、演员 | 上游 |

`item/list` 正文（上游形态）：`{tags:{type:[…]}, sort_type, sort_column, page, page_size, ancestor_guid?, exclude_grouped_video?}`；响应 `data.{total, list[]}`。`ancestor_guid` 省略时是跨库查询。

`type` 取值：`MediaDB` / `Movie` / `TV` / `Season` / `Episode` / `Video` / `Directory`。

## 季 / 集

| 方法 | 路径 | 用途 | 来源 |
| --- | --- | --- | --- |
| GET | `/api/v1/season/list/{tv_guid}` | 某剧的季列表 | 两者 |
| GET | `/api/v1/episode/list/{season_guid}` | 某季的集列表 | 两者 |

## 播放与进度

| 方法 | 路径 | 用途 | 来源 |
| --- | --- | --- | --- |
| POST | `/api/v1/play/info` | 取播放信息：`media_guid`、`video_guid`、`audio_guid`、`subtitle_guid`、续播 `ts`、`item` | 抓包 |
| POST | `/api/v1/play/record` | **上报进度**：`{item_guid, media_guid, video_guid, audio_guid, subtitle_guid, resolution, bitrate, ts, duration}` | 抓包 |
| GET | `/api/v1/play/list` | 继续观看列表 | 两者 |
| POST | `/api/v1/stream` | 取播放流：`{media_guid, ip, header, level}` | 抓包 |
| GET | `/api/v1/stream/list/{media_guid}` | 音视频字幕流列表（strm 可能返回空） | 抓包 |

`POST /stream` 响应里的两条来源要分清：

- `qualities[]`：服务端转码/渐进流（`bitrate`、`resolution`、`progressive`）。
- `direct_link_qualities[].url`：**云盘直链**（外部网盘地址）；`cloud_storage_info.cloud_storage_type == 9001` 表示 strm 文件（网盘串流）。
- 直链属**外部来源**，播放前按 CLAUDE.md 的来源边界处理。

## 标签与人员

| 方法 | 路径 | 用途 | 来源 |
| --- | --- | --- | --- |
| GET | `/api/v1/tag/list?ancestor_guid=&is_favorite=` | 筛选可选值（类型/分辨率/地区/年代等） | 抓包 |
| GET | `/api/v1/tag/genres` · `/tag/iso3166` · `/tag/iso6391` · `/tag/iso6392` | 类型 / 地区 / 语言标签映射（带 `lan`） | 抓包 |
| POST | `/api/v1/person/list/{item_guid}` | 演员/导演（正文 `{page, page_size}`） | 抓包 |

## 图片

| 方法 | 路径 | 用途 | 来源 |
| --- | --- | --- | --- |
| GET | `/api/v1/sys/img{poster_path}` | 图片代理；`poster_path` 来自其他响应的 `poster`/`still_path`/`profile_path`；可加 `?w=256` 等尺寸参数 | 两者 |

**需要鉴权**，不能当公共静态图直接交给外部进程，见 [传输与鉴权](http-and-auth.md#凭据边界)。

## 任务

| 方法 | 路径 | 用途 | 来源 |
| --- | --- | --- | --- |
| GET | `/api/v1/task/running` | 是否有任务在跑（上游只判真假） | 上游 |

## 规格缺口与矛盾

接入时**必须核对**，不要照抄：

| 项 | 冲突/缺口 | 处理 |
| --- | --- | --- |
| `authx` 的 `sign` 算法 | 社区文档 `MD5(nonce+timestamp+token)` vs 上游 `MD5(APP_SECRET_apiPath_nonce_ts_dataHash_APP_KEY)`，互相矛盾 | 抓官方前端 JS 或对目标服务器实测定案，见 [传输与鉴权](http-and-auth.md#authx-签名未决项) |
| v1 登录 | 新服务端已废弃，一律 `-15`；`-15` 无法区分账号/密码/接口版本 | 一律走 v2，见 [传输与鉴权](http-and-auth.md#登录) |
| 访问码 | 校验在 `/c/{code}`（不在 `/v` 下），失败返回 404，靠会话 Cookie | 做成可选配置；请求需支持 Cookie |
| `search/list` | 只在上游实现里出现，社区抓包文档未登记 | 标记为待核实，抓前端 JS 确认 |
| `mdb/*`、`task/running` | 只在上游实现里出现（管理员接口） | 同上 |
| 前端/服务端版本 | 接口随版本变，社区文档基于 0.9.7 / 0.8.35 | 每次接入前 `sys/version` 重取并记录 |

## 容易猜错的

| 你可能会猜 | 实际 |
| --- | --- |
| 业务路径是 `{host}/api/v1/...` | 入口自带 `/v`，合成后是 `{host}/v/api/v1/...` |
| 登录也在 `/api/v1` 下 | 登录是 `/api/v2/user/loginByPassword`，其余仍在 v1 |
| 密码直接传明文 | 现行 v2 传 `sha256(明文)` 小写十六进制；明文是已废弃的 v1 |
| 拿到 `-15` 就是账号密码错 | 新服务端上打 v1 也会 `-15`，先确认接口版本 |
| 按 HTTP 401/403 判错 | 错误码在正文 `code` 里，HTTP 常常还是 200 |
| 图片 URL 可以直接给 mpv/浏览器 | 需要鉴权头，必须经进程内/已验证通道 |
| `sign` 用 token 参与 | 两套公式里只有社区文档版用 token；上游版用的是应用级常量 |
| 访问码校验在 `/v` 下 | 在设备根路径 `/c/{code}`，且失败是 404 |
