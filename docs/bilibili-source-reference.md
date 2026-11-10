# B站弹幕接入源码依据

本轮只读取公开源码，未登录真实账号、请求真实登录、播放或发送弹幕。源码证明实现依据，不证明当前服务可用。

同日匿名 GET `https://passport.bilibili.com/login` 返回 HTTP 200，无HTTP跳转，Content-Type 为 `text/html; charset=utf-8`，HTML 是948字节的 JavaScript 入口壳（标题 `Rsbuild App`）。这只证明入口能载入，不证明官方扫码页面渲染、成功回跳或真实会话。

核对于 2026-11-10，临时源码保存在调研工作树的 `artifacts/research/bilibili`：

- `xixial/bilibili-api`，`main`，提交 `4789508a93f3a39b7d3c896273309db892a9cc8b`。`bilibili_api/search.py` 的 `search_by_type` 与 `data/api/search.json` 调用 `/x/web-interface/wbi/search/type`，类型为 `media_bangumi`、`media_ft`，参数包含 `keyword`、`page`、`page_size`。`utils/network.py` 的 `_get_nav`、`_get_mixin_key`、`_enc_wbi` 从 nav 的 `data.wbi_img.img_url/sub_url` 生成置换密钥，用排序编码参数、`wts`、`web_location=1550101` 与 MD5 生成 `w_rid`。非零 `code` 抛业务错误，`data` 为空时提取 `result`。
- 同提交 `video.py` 的 `get_info/get_pages` 和 `data/api/video.json` 使用 `/x/web-interface/view` 与 `/x/player/pagelist`，分P有独立 `cid`；`get_danmaku_xml` 实际访问 `https://comment.bilibili.com/{cid}.xml` 并按 UTF-8 解码。`login_v2.py` 的登录请求 Referer 为 `https://passport.bilibili.com/login`；`network.py` 的 `_check_valid` 用 nav 的 `isLogin` 确认会话。Momoka 使用官方登录页与 WebView2 Cookie 管理器，读取白名单后再用 nav 核对。
- `nilaoda/BBDown`，`master`，提交 `259a5558cee0a349a7ebb60bd31e40c88e5bc1ed`。`BBDown.Core/Fetcher/NormalInfoFetcher.cs` 从 `/x/web-interface/view` 的 `data.pages[]` 读取 `cid/page/part/duration`。`BangumiInfoFetcher.cs` 用 `/pgc/view/web/season?ep_id=...` 读取 `result.episodes[]`，正片未包含目标 ep 时寻找 `result.section[].episodes[]`，取 `id/aid/cid/title/long_title`。`BBDown/BBDownUtil.cs` 的 `GetEpIdByBangumiSSIdAsync` 同路由以 `season_id` 查季度。
- 同 BBDown 提交 `BBDown/Program.cs` 的 `downloadDanmaku` 分支实际下载 `comment.bilibili.com/{cid}.xml`；`BBDown.Core/DanmakuUtil.cs` 从 `<i><d p="...">文本</d></i>` 解析八项字段，`p[0]` 是秒、`p[1]` 模式、`p[2]` 字号、`p[3]` 十进制颜色。`Util/HTTPUtil.cs` 配置自动 gzip/deflate 解压。Momoka 保持正常证书校验、拒绝重定向与Cookie日志，不复刻上游忽略证书和打印凭据行为。

Momoka 的最小读取范围：关键词只搜索番剧和影视第一页；分页或本地截断保留 `HasMore=true`。BV/av/ep/ss编号或官方完整链接可直接展开分P/剧集到 cid；不访问用户提供的任意地址，不解析短链、互动视频分支或海外代理。B站只读弹幕来源，不增加发送。

XML 是当前弹幕池的快照，不能承诺完整历史、每六分钟的全部 protobuf 分段、高级脚本或图片弹幕。模式只接现有渲染器支持的 1/4/5；非法数据丢弃，最多 40,000 条。XML 下载不携带账号Cookie。响应解压后限 32 MiB，DTD与外部实体禁止，30秒操作预算包括排队、读取和解析。访问串行，不自动重试风险限制错误；真实账号、风控、地区和平台变化仍需另行授权核对。
