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

Momoka 使用 `DanmuApiEndpoint.TryCreate` 和 `DanmuApiToken`：

- 设置填服务根地址，不把 `api/v2` 或令牌预先拼入地址。允许 HTTP/HTTPS、端口和反向代理路径前缀；拒绝账号信息、查询字符串和片段。
- 令牌单独填写、去首尾空白，最长 512 字符且不能有控制字符；由 DPAPI 保存，不回填明文。无法解密时在联网前给出重新输入提示。
- 拼接顺序是 `根路径/转义令牌/api/v2/...`；无令牌时省略该段。例如根地址 `https://danmu.example/proxy/`、占位令牌 `fixture-token`，搜索路径为 `/proxy/fixture-token/api/v2/search/episodes`。反向代理须把前缀正确转交服务；不能把前缀存在当成服务端已支持的证据。
- 不发送 `X-AppId`、`X-Timestamp`、`X-Signature`，不调用 Momoka 的弹弹play凭据端点。搜索、读取、match 均禁止重定向。
- 上游此版本默认 `TOKEN=87654321` 时允许已知 API 路径不带令牌；自定义 TOKEN 后，Momoka 调用的三类接口必须带有效路径令牌。不能泛化为“读取永远公开”。`ADMIN_TOKEN` 是上游管理用途，Momoka 不要求配置管理权限。
- 令牌确实在请求 URL 路径里。诊断只输出脱敏路径和状态，不能把带令牌 URL 放入日志、聊天、命令行或播放器参数；客户端缓存文件名使用地址和令牌的摘要。服务端/代理日志是否隐藏路径需要分别核对。

## Momoka 调用子集

| 操作 | 相对请求 | 本地读取 |
| --- | --- | --- |
| 手动搜索 | `GET api/v2/search/episodes?anime=<title>&v2=true[&episode=N]` | `animes[].animeTitle` 和 `episodes[].episodeId/episodeTitle`；最多 1000 条，截断标 `HasMore` |
| 自动匹配 | `POST api/v2/match`，JSON `{ "fileName": "示例作品 (2024) S01E02" }` | `isMatched=true` 且 `matches[0]` 有正数 `episodeId`、非空 `animeTitle` |
| 取弹幕 | `GET api/v2/comment/{episodeId}?withRelated=false&chConvert=0` | JSON `comments[].p/m`；沿用共同解析上限 |

- `MatchAsync` 接受去首尾空白后的 2–200 字符且无控制字符的名字；正文只有 `fileName`，没有 `fileHash`、`fileSize`、`videoDuration` 或伪造哈希。`DanmakuMatchRules.MatchFileName` 用媒体元数据构造名字；匹配前置规则和年份回退见主技能。
- 上游 `matchAnime` 从 `fileName` 提取标题、年份、季集和可选平台偏好，并可能应用服务端标题映射。Momoka 读取首个结果后，还会拒绝结果标题明确写出的集号与当前视频冲突；没有明确集号不当作反证。不能因为上游支持跨季映射，就放宽 Momoka 的续季自动匹配边界。
- `success=false`、非零/非法 `errorCode`、非法 JSON 都按协议失败处理。`isMatched=false` 或无有效首个结果是未匹配，不伪造候选。
- 搜索响应的 `HasMore` 和截断必须保留；上游支持平台选择、收藏和远程映射，不代表 Momoka 提供这些界面或调用。
- **输出格式须为 JSON。** 当前 Momoka 没有追加 `format=json`。上游 `formatDanmuResponse` 在缺少该参数时使用 `DANMU_OUTPUT_FORMAT`，默认是 `json`；若用户改为 XML 等格式，会触发本地解析失败。先核对部署输出格式，不把 XML 当作空弹幕吞掉。
- Momoka 没有调用按视频 URL 读取、XML、分片、时长、收藏、管理或本地弹幕上传接口；也没有向 danmu_api 发送评论的链路。

## 缓存、错误与限流

GET 复用两小时、最多 16 份 JSON 的客户端缓存，身份包含服务地址与令牌摘要；同 ID 不得跨服务、跨令牌或跨 DandanPlay 复用。`MatchAsync` 不缓存。每次请求含排队和读取共用 30 秒预算，响应最多 32 MiB。

| 情况 | 核对顺序 |
| --- | --- |
| 401/403 | 令牌是否能解密、服务 TOKEN 是否已改、自部署根路径是否重复带令牌；不换弹弹play内置密钥 |
| 404 / ID 失效 | 根路径、实际部署版本与服务端 ID 缓存；重新搜索，不能用官方同号 ID 代替 |
| 429 | 用户部署的限流配置、服务端缓存命中和请求来源；等待恢复，不自动循环重试 |
| 无法识别弹幕数据 | 检查 JSON 输出格式、兼容字段和响应大小；不回显可能带令牌的正文 |
| Both 仅一源可用 | 按主技能的匹配/读取两阶段检查，保留成功源并核对实际发送目标 |

上游已核对版本的 `RATE_LIMIT_MAX_REQUESTS` 默认 3，0 表示不限流；`worker.js` 的 comment 路由先查弹幕缓存，未命中才按 IP 的一分钟窗口计数并可能返回 429。不能把这一段泛化成“所有接口固定每分钟三次”，也不能用该默认推断用户部署值。服务端缓存和 Momoka 本地缓存是两层。

## 验证边界

在 `DanmakuTests` 核对地址/令牌、两源请求身份、重定向拒绝、match 请求与返回集号、缓存隔离、同 ID 与聚合上限、单源发送隐藏；在 Shell 的 settings/playback 探针核对来源切换与恢复。测试参数和命令见主技能。

真实部署测试与真实媒体播放分别按 CLAUDE.md 范围执行。使用匿名夹具能够证明本地构造和处理，不能证明站点平台抓取、服务器缓存、代理前缀或当前限流参数。
