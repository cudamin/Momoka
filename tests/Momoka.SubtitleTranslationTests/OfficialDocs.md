# 官方正文核对证据

读取日期：2026-10-25（本机 Asia/Shanghai）。仅调用 Invoke-WebRequest 获取公开 primary docs，未调用任何翻译 endpoint、未送凭据。web.run 直开官方页面未返回正文，未重复失败搜索；以下是实际读取正文的证据，非搜索摘要。

原始完整页面保存在本项目 ignored 的 work/official-docs，只提交此短摘要与正文文件 SHA-256，不提交远程页面副本。

| 本地证据名 | 官方 URL | SHA-256 | 亲自核对的正文内容 |
|---|---|---|---|
| google-v2.html | https://cloud.google.com/translate/docs/reference/rest/v2/translate | F2D3B4543FDC5B85ED187EDA4A3650E986DA23319C6528F85D8C19D060215402 | 官方 POST v2 路径，q/source/target/format 与 translatedText。 |
| google-rest-json.txt | https://cloud.google.com/translate/docs/basic/translating-text | EA18019A66E4BD8BF7FFEC07D0755EC2701ACE6ECAC45813DE4F464714A7057B | 正文示例展示 q JSON 数组/字符串和请求 JSON body。 |
| google-header-best-practice.txt | https://cloud.google.com/docs/authentication/api-keys-best-practices | 6A9B2344345EC61DA764525228403ACCEB971EE8D8D3DE150E6858162139BD50 | API key 不放 query，改用 x-goog-api-key header。 |
| microsoft-v3.html | https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/reference/v3/translate | 28676BBCB7C1B36E93C99A1F1C90CF757B7B05ECC597AF6822ADB67F13A7E6E9 | POST /translate?api-version=3.0，from/to，数组 Text 与 translations。 |
| microsoft-auth.html | https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/reference/authentication | 0817E359EBA2C6CA8CF955245627735B77B27C6A5C9CCB224275248C8B93A1B3 | 订阅密钥头，global 可无 region，regional/multi-service 使用区域头。 |
| microsoft-endpoints.html | https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/reference/v3/reference | A2BD60E0B2CE3909019A733521E743AEDA2F58ED6CE86FA873D1E0999EAE5176 | 全局 URL 和单标签 Azure 自定义资源 /translator/text/v3.0/translate。 |
| microsoft-region-support.html | https://learn.microsoft.com/en-us/azure/ai-services/translator/region-support | 03E76DBE8DCF7B9962756527637DA93718390DA971897A5BAA289050633F5345 | 亲自核对 api-nam/api-apc/api-eur 三个 Geography 官方域。 |
| openai-chat.html | https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create | 6EB79CDAE4A3A5458203638E1767FDD6918B1474FC6813D2893876ABD144ADB8 | POST /chat/completions；model/messages/system/user、Bearer、store、max_completion_tokens。 |
| openai-model-markdown.txt | https://developers.openai.com/api/docs/models/gpt-4.1-mini.md | AC583E48898086366C346ECC76A2F46C489F7144EA4F2482688209238EB0BA3D | gpt-4.1-mini 支持 Chat Completions，标 Deprecated；当次 token 价格。 |
| openai-deprecations.txt | https://developers.openai.com/api/docs/deprecations.md | B5D1CAFEFC8A491F2C095D77359A6CA185498BAA5F65A66BAC1CB722E188855F | 2026-10-14 公告：GPT-4.1 mini 基础快照的停用日期 2027-01-14。 |

核对范围：只建立协议形状与文档事实，不代表账号授权、API 启用、Google header 实际线上成功、Microsoft 区域配置、模型调用、付费额度或翻译质量已验证。部分官方 URL 返回 404（Google 旧 using-api-keys、Microsoft 旧 regions、OpenAI Chat markdown），改读不同的现行官方正文，没有循环重试同一失败 URL。

主窗口最终默认模型核对：2026-10-25 官方 `https://developers.openai.com/api/docs/models/gpt-6-luna.md`，支持 Chat Completions 与 reasoning_effort=none（文档 SHA256 9e5a91be5e5a7f91d1a700907367ea7d261b57ce5b369aa8e3602b9b99239081）。不以历史 gpt-4.1-mini 价格或弃用计划作为当前默认服务可用性的证据。
