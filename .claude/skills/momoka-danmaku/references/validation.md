# 弹幕技能复核场景

返回 [主技能](../SKILL.md)。维护技能时使用以下只读场景；读取技能和指向的源码即可，不启动应用、不请求真实服务、不发送弹幕。记录结论和缺口，不把走读当作自动触发率或产品运行测试。

| 场景 | 应能得到的决定 |
| --- | --- |
| 部署要求访问令牌，自动匹配没有视频哈希 | 确认旧 `DanmuApiEndpoint` 会将非空令牌写入路径而停止接入；用匿名夹具核对仅以元数据构造 `fileName` 的请求。 |
| Both 两源返回同一个 episodeId，其中一源失败或返回空列表 | 身份带来源；区分匹配和读取失败；成功空列表计为加载，读取失败允许另一源继续；合并排序后受上限约束，过滤另做去重 |
| Both 下手动只加载 danmu_api，随后发送 | 手动加载替换原结果；没有已加载弹弹play库就不能发送；纯 danmu_api 不显示输入栏 |
| 自部署读取 200 却解析失败 | 找到 `DANMU_OUTPUT_FORMAT` 与 Momoka 未传 `format=json` 的差异，不把 HTTP 200 当作兼容证明 |
| 弹弹play内置端点改 key 或返回 401/403 | 区分 key 与应用密钥轮换；客户端与 Worker 向量同步；GET 一次换钥重试，发送不自动重试 |

## 触发范围

应使用：

- “接入/排查弹弹play或 danmu_api 弹幕，两个来源合并后串集了。”
- “修改 DanmakuSourceRules、DanmakuMatchRules 或自部署令牌。”
- “调整弹幕四张设置卡、uosc 弹幕输入栏或 momoka_danmaku.lua。”
- “核对内置弹幕凭据端点和 Cloudflare Secret 轮换。”

相邻但不应单独触发：普通 ASS/PGS 字幕样式、HDR 输出、无弹幕改动的 mpv 管线生命周期、媒体服务器登录；分别交给已有领域技能。

## 结构与引用

运行 `py tools/check-docs.py` 检查相对链接和 CLAUDE 技能索引，用 `tools/check-diff.ps1` 检查编码及行尾，再按当前源码核对方法、路径和行为。`tools/check-review-docs.py` 另查指定文档的跨页约定。
