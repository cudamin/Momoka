# danmu_api 自部署来源

返回 [弹幕技能](../SKILL.md)。本页描述 Momoka 实际调用的子集，以及核对上游时需要走的源码入口。

## 上游证据

2026-11-09 已读取 [huangxd-/danmu_api](https://github.com/huangxd-/danmu_api) 提交 `4c865798ddf8972fc80a6cc850d896f459d01d78`。用户提供的 [API 食用指南](https://github.com/huangxd-/danmu_api#api食用指南) 用于找入口，协议判断来自以下实现：

| 上游文件/符号 | 已核对行为 |
| --- | --- |
| [danmu_api/worker.js](https://github.com/huangxd-/danmu_api/blob/4c865798ddf8972fc80a6cc850d896f459d01d78/danmu_api/worker.js) | 路径令牌、默认令牌兼容、路由方法、comment 缓存命中与限流 |
| [danmu_api/apis/dandan-api.js](https://github.com/huangxd-/danmu_api/blob/4c865798ddf8972fc80a6cc850d896f459d01d78/danmu_api/apis/dandan-api.js) 的 `searchEpisodes` / `matchAnime` / `getComment` | 搜索、仅 `fileName` 的匹配输入、匹配输出和按 ID 读取 |
| [danmu_api/configs/envs.js](https://github.com/huangxd-/danmu_api/blob/4c865798ddf8972fc80a6cc850d896f459d01d78/danmu_api/configs/envs.js) | `TOKEN`、`ADMIN_TOKEN`、`RATE_LIMIT_MAX_REQUESTS`、`DANMU_OUTPUT_FORMAT` 默认配置 |
| [danmu_api/utils/danmu-util.js](https://github.com/huangxd-/danmu_api/blob/4c865798ddf8972fc80a6cc850d896f459d01d78/danmu_api/utils/danmu-util.js) 的 `formatDanmuResponse` | 查询参数优先，否则使用服务端输出格式 |

这是固定源码版本的结论，不是用户部署版本或真实服务的运行证明。升级时重新核对受影响入口与其 helper；本页没有复制上游实现，也不要求干净检出具备本次临时 clone。

## 地址与鉴权

[凭据规则](../../../../CLAUDE.md#凭据)禁止令牌进入 URL。Momoka 只接入服务原本允许的无令牌方式。要求路径令牌的部署保持未支持。

- 根地址允许 HTTP/HTTPS、端口和反向代理路径前缀，拒绝账号信息、查询字符串和片段；实际配置只填根地址。
- 请求路径是 `根路径/api/v2/...`。旧设置含令牌时停止该来源的请求，密文继续保留于本机直到用户清除，客户端不解密。清除保存失败时保持阻断。
- 不发送 `X-AppId`、`X-Timestamp`、`X-Signature`，不调用 Momoka 的弹弹play凭据端点。搜索、读取、match 均禁止重定向。
- 上游此版本默认 `TOKEN=87654321` 时允许已知 API 路径不带令牌；自定义 TOKEN 后，Momoka 调用的三类接口必须带有效路径令牌。不能泛化为“读取永远公开”。`ADMIN_TOKEN` 是上游管理用途，Momoka 不要求配置管理权限。

## Momoka 调用子集

| 操作 | 相对请求 | 本地读取 |
| --- | --- | --- |
| 手动搜索 | `GET api/v2/search/episodes?anime=<title>&v2=true[&episode=N]` | `animes[].animeTitle` 和 `episodes[].episodeId/episodeTitle`；最多 1000 条，截断标 `HasMore` |
| 自动匹配 | `POST api/v2/match`，JSON `{ "fileName": "示例作品 (2024) S01E02" }` | `isMatched=true` 且 `matches[0]` 有正数 `episodeId`、非空 `animeTitle` |
| 取弹幕 | `GET api/v2/comment/{episodeId}?withRelated=false&chConvert=0` | JSON `comments[].p/m`；沿用共同解析上限 |

- `MatchAsync` 接受去首尾空白后的 2–200 字符且无控制字符的名字；正文只有 `fileName`，没有 `fileHash`、`fileSize`、`videoDuration` 或伪造哈希。`DanmakuMatchRules.MatchFileName` 用媒体元数据构造名字；匹配前置规则和年份回退见主技能。
- 上游 `matchAnime` 从 `fileName` 提取标题、年份、季集和可选平台偏好，并可能应用服务端标题映射。Momoka 对第 1 季及以后的单集都会自动请求，季号由 `MatchFileName` 的 `SxxExx` 携带；读取首个结果后，还会拒绝结果标题明确写出的集号与当前视频冲突；没有明确集号不当作反证。上游的跨季映射不改变这条集号核对规则。
- `success=false`、非零/非法 `errorCode`、非法 JSON 都按协议失败处理。`isMatched=false` 或无有效首个结果是未匹配，不伪造候选。
- 搜索响应的 `HasMore` 和截断必须保留；上游支持平台选择、收藏和远程映射，不代表 Momoka 提供这些界面或调用。
- **输出格式须为 JSON。** 当前 Momoka 没有追加 `format=json`。上游 `formatDanmuResponse` 在缺少该参数时使用 `DANMU_OUTPUT_FORMAT`，默认是 `json`；若用户改为 XML 等格式，会触发本地解析失败。先核对部署输出格式，不把 XML 当作空弹幕吞掉。
- Momoka 没有调用按视频 URL 读取、XML、分片、时长、收藏、管理或本地弹幕上传接口；也没有向 danmu_api 发送评论的链路。

## 缓存、错误与限流

GET 复用两小时、最多 16 份 JSON 的客户端缓存。自部署按无令牌地址隔离，同 ID 不跨来源或服务复用。`MatchAsync` 不缓存。每次请求含排队和读取共用 30 秒预算，响应最多 32 MiB。

| 情况 | 核对顺序 |
| --- | --- |
| 401/403 | 核对服务的鉴权要求。若无令牌不可用，停止真实请求，记录未支持。 |
| 404 / ID 失效 | 根路径、实际部署版本与服务端 ID 缓存；重新搜索，不能用官方同号 ID 代替 |
| 429 | 用户部署的限流配置、服务端缓存命中和请求来源；等待恢复，不自动循环重试 |
| 无法识别弹幕数据 | 检查 JSON 输出格式、兼容字段和响应大小；不回显可能带令牌的正文 |
| Both 仅一源可用 | 按主技能的匹配/读取两阶段检查，保留成功源并核对实际发送目标 |

上游已核对版本的 `RATE_LIMIT_MAX_REQUESTS` 默认 3，0 表示不限流；`worker.js` 的 comment 路由先查弹幕缓存，未命中才按 IP 的一分钟窗口计数并可能返回 429。不能把这一段泛化成“所有接口固定每分钟三次”，也不能用该默认推断用户部署值。服务端缓存和 Momoka 本地缓存是两层。

## 验证边界

在 `DanmakuTests` 核对地址、旧令牌存在时搜索／匹配／读取均为零请求、清除后的无令牌请求、重定向拒绝及缓存隔离。设置清除失败与重试见 `Momoka.DanmakuSourceBoundaryTests`。在 Shell 的 settings/playback 探针核对来源切换与恢复。测试参数和命令见主技能。

真实部署测试限服务自身允许的无令牌模式。真实播放另按[播放授权](../../../../CLAUDE.md#播放相关改动的验证三档)执行。匿名夹具只覆盖本地构造和处理。
