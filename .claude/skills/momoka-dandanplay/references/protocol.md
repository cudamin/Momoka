# 弹弹play v2 协议参考

依据：`src/Momoka.Core/Danmaku/DandanPlayClient*.cs`、`DanmakuComment.cs`、`DandanPlayMatching.cs` 与官方 [开放弹幕网络](https://doc.dandanplay.com/open/)、[Swagger](https://api.dandanplay.net/swagger)。额度与审核状态来自 2026-10-25 开发者中心观测，属于会变化的事实，使用前重新核对。

## 基址与签名

- 基址：`https://api.dandanplay.net`。所有请求都带 `Accept: application/json`。
- 每次请求三个头：

```text
X-AppId:     <应用 ID>
X-Timestamp: <Unix 秒>
X-Signature: base64(SHA256(appId + timestamp + path + appSecret))
```

- `path` 是 URL 的 `AbsolutePath`，**不含查询串**。把查询串或完整 URL 拿去签名会得到 `Invalid Signature`。
- 时间戳来自本机时钟；偏差会导致 `Invalid Timestamp`。客户端没有额外的时间同步逻辑，提示用户校准 Windows 时间。
- 签名是纯函数：`DandanPlayClient.Signature(appId, secret, timestamp, path)`。测试固定了串联顺序与“不含查询参数”这两点。

## 接口

| 用途 | 请求 | 是否签名 | 重定向 | 缓存 |
| --- | --- | --- | --- | --- |
| 搜索剧集 | `GET /api/v2/search/episodes?anime=<title>&v2=true[&episode=N]` | 是 | 不跟随 | 2 小时 |
| 获取弹幕 | `GET /api/v2/comment/{episodeId}?withRelated=<true|false>&chConvert=0` | 是 | 允许最多 3 次，仅限官方 HTTPS 子域 | 2 小时 |
| 发送应用弹幕 | `POST /api/v2/comment/{episodeId}/app` | 是 | 不跟随 | 成功后删缓存 |

### 搜索

- `title` 去空白后要求 2–120 字符；`episode` 留空表示全部，否则必须是 1–6 位正整数。
- 响应结构：

```json
{
  "success": true,
  "errorCode": 0,
  "animes": [
    {
      "animeId": 23,
      "animeTitle": "作品名",
      "episodes": [
        { "episodeId": 230001, "episodeTitle": "第1话" }
      ]
    }
  ],
  "hasMore": false
}
```

- 客户端只读 `animes[].animeTitle` 与 `episodes[].episodeId` / `episodeTitle`，平展成最多 1000 条；达到上限时把结果标成 `hasMore = true`。`hasMore = true` 时不允许自动匹配，因为当前页的一项不能证明唯一。
- 去重按 `episodeId`；标题为空或结构不符的条目跳过。

### 获取弹幕

- `withRelated` 决定是否包含关联第三方站点弹幕；修改后重新加载当前库。
- `chConvert=0` 表示不做繁简转换。
- 响应：

```json
{
  "count": 2,
  "comments": [
    { "p": "12.5,1,16777215,user", "m": "滚动弹幕" },
    { "p": "1,5,255,user", "m": "顶部弹幕" }
  ]
}
```

- `p` 至少三段：`time,mode,color[,uid,...]`。`time` 为 0–86400 秒的有限数；`mode` 只接受 1（滚动）、4（底部）、5（顶部）；`color` 为 0–0xFFFFFF。
- `m` 去掉控制字符后截到 300 字符；空文本丢弃。客户端最多保留 40,000 条，并按时间排序。
- 高级脚本弹幕不支持；非法模式直接丢弃，不进入渲染器。

### 发送

- 请求体：

```json
{ "time": 12.5, "mode": 1, "color": 16777215, "comment": "文本", "userName": "Momoka" }
```

- `time` = 点击时视频时间 − 当前弹幕延迟，夹在 0–86400；文本 1–100 个 UTF-16 字符、无控制字符。
- 成功必须同时满足：`success === true`、`errorCode === 0`、`cid` 为正整数。只判断 HTTP 200 会把“结果未确认”误报成成功。
- **只写入本应用私有弹幕库。** 公开文档没有提供基础层用户登录并写主弹幕库的流程；当前客户端不登录、不发主库。不要把 `/app` 的 `cid` 解释为主库评论 ID。
- 不跟随重定向、不自动重试。超时或读回执失败时提示“结果未确认，可能已送达”，避免用户立即重复发送。
- 一旦请求发出，成功或结果不明都要删掉该 `episodeId` 的 `withRelated=true/false` 两份 GET 缓存；成功后再把评论加入本地列表，不再多取一次。
- 内置凭据 401/403：清缓存后让用户重发；自定义凭据只映射白名单错误。

## 重定向与凭据边界

- 取弹幕的 302 可能指向弹幕加速节点。只接受：HTTPS、443、无 `UserInfo`、主机为 `api.dandanplay.net` 或以 `.dandanplay.net` 结尾。
- 跳到同源 `api.dandanplay.net` 时，按新路径、新时间重新签名；跳到 CDN 子域时**不带任何鉴权头**。
- 搜索请求不跟随跳转；发送请求不跟随跳转。
- 最多 3 次跳转，超过即失败。

## 错误与缓存

| 情况 | 客户端行为 |
| --- | --- |
| 401/403 | 只映射 `X-Error-Message` 的 `Invalid Timestamp`、`Invalid AppId`、`Invalid Signature`、`Invalid AppSecret`、`Missing Authentication Headers`；未知值给通用提示 |
| 401/403 + 内置凭据 | GET 丢掉凭据缓存后换一份重试一次；POST 不重试 |
| 429 | 提示额度或频率受限 |
| 业务 `success=false` / `errorCode≠0` | 通用失败提示，不回显服务端正文 |
| 非法 JSON、超大响应、超时 | 归类为协议/网络错误，不缓存 |
| 成功结果 | 按「AppId + 凭据身份 + 路径」写缓存，2 小时、最多 16 份 |

- 每请求共用 30 秒排队+读取预算；响应上限 32 MiB；限长读取在超大 `Content-Length` 声明前就停止。
- 缓存文件只含响应 JSON，不含明文 AppSecret。内置凭据缓存是单独的 DPAPI 文件。
- 失败不自动重试，避免重复消耗共享额度；只有内置凭据的 GET 401/403 有上述一次换钥重试。
- 不提供额度查询、定时探测或瞬时速率猜测；官方没有公开剩余额度接口。

## 额度与主库

- 2026-10-25 基础层观测：搜索 100/日、获取弹幕 100/日、发送 10/日（各约 2,400/月）；番剧详情/文件识别 200/日（约 4,800/月）。所有内置用户共享同一份额度，以开发者中心实时值为准。
- 公开页的接口说明不等于应用后台额度；实际受限时返回 429。
- 当前内置凭据只面向 Momoka 自己的应用层级；扩大分发前先核对官方授权与额度。
- 主弹幕库写入：**未实现、未验证、不要承诺**。用户登录、OAuth、关注/历史同步都不在当前范围；接入前先取得官方明确文档与授权。

## 本地限制速查

| 项 | 值 |
| --- | --- |
| 标题长度 | 2–120 字符 |
| 集数 | 1–999999，留空搜全部 |
| 搜索结果 | 最多 1000 条；截断即 `hasMore` |
| 弹幕条数 | 最多 40,000 |
| 弹幕文本 | 解析后最多 300 字符 |
| 发送文本 | 1–100 个 UTF-16 字符 |
| 发送时间 | 0–86400 秒，可含小数 |
| GET 超时 | 30 秒 |
| GET 响应 | 32 MiB |
| 缓存 | 2 小时 / 16 份 |
| 跳转 | 最多 3 次，仅官方 HTTPS 子域 |

## 未覆盖

- 没有真实服务的长稳监控、额度耗尽演练或主库写入验证。
- 没有视频文件哈希识别；匹配是按媒体元数据标题搜索。
- 没有关注、观看历史、用户 OAuth 或跨设备同步。
- 没有高级脚本弹幕、弹幕举报、删除或编辑。
- 公开文档与 Swagger 可能滞后；新增接口前先以官方文档和开发者中心核对，再写测试固定本地行为。
