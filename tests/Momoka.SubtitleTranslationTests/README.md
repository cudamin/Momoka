# 实时字幕翻译 Core 独立离线测试

本项目独立引用 Core，不注册到 Momoka.sln，不修改原测试 Program.cs。所有请求均由 FakeHandler 接管，不加载用户设置、不读真实密钥、不请求翻译服务、不启动 UI 或 mpv。

## 构建与运行

在当前 Git worktree 根运行（使用项目 global.json 指定的 .NET SDK）：

```powershell
dotnet restore tests/Momoka.SubtitleTranslationTests/Momoka.SubtitleTranslationTests.csproj --disable-parallel
dotnet build tests/Momoka.SubtitleTranslationTests/Momoka.SubtitleTranslationTests.csproj -c Release --no-restore -m:1
dotnet run --project tests/Momoka.SubtitleTranslationTests/Momoka.SubtitleTranslationTests.csproj -c Release --no-build --no-restore
```

本机 SDK 在用户级 .dotnet，不应使用 PATH 上的 .NET 8。先成功构建当前源码，再运行 --no-build；失败输出仅给用例名/异常类型，永不打印请求、响应或密钥。

## 父任务公开契约

- Momoka.Configuration：SubtitleTranslationProvider { Google, Microsoft, OpenAi }；SubtitleTranslationSettings 保持全部约定默认值，额外 FontSize=32（20–64）、Position=80（10–90），Normalize() 钳制字号/位置、补空值、保留非空 endpoint/model/密文；HasGoogleApiKey / HasMicrosoftApiKey / HasAiApiKey 均 JsonIgnore。
- SubtitleTranslationCredentials(ISecretProtector)：SetGoogleApiKey/GetGoogleApiKey、SetMicrosoftApiKey/GetMicrosoftApiKey、SetAiApiKey/GetAiApiKey；通用 SetApiKey(settings, provider, apiKey)/GetApiKey(settings, provider)。Set 方法的 key 传空表示清除。加密失败只抛固定中文错误并保留旧密文，解密失败返回空并保留密文。父注入 DPAPI；测试 AES-GCM 替身不证明真实 DPAPI。
- Momoka.Translation：SubtitleTranslationClient 默认构造（持有安全 HttpClient）、注入 HttpClient 构造（不接管其 Dispose），TranslateAsync(settings, text, apiKey, cancellationToken) 返回 Task<string>。默认 ctor 或 CreateHttpClient() 禁重定向/禁 cookie；注入 HttpClient 必须同样禁重定向、不得带默认认证或记录敏感头。HttpClient 公共 API 无法在构造后读取或修改内部 handler 的 AllowAutoRedirect；该边界不是一次 3xx 单测可证明的。
- 测试超时重载 SubtitleTranslationClient(HttpClient, TimeSpan)：大于零且不超过 30 秒。默认总预算 15 秒，覆盖请求和响应读取；调用方取消返回带原 token 的 OperationCanceledException。真实 handler 应遵守取消 token。
- SubtitleTranslationException.Error 是固定 SubtitleTranslationError 分类，不携带 inner/body/header/key/URL。关闭或空白句直接返回空；超长句、语言/地址/密钥非法在外发前拒绝。无重试，401/403/429/3xx 均固定错误，错误响应不读 body。
- 输入最多 4000 UTF-16 字符，响应最多 128 KiB，译文最多 16000 UTF-16 字符。Google v2 使用 q 数组、可选 source、target、format=text 和 X-Goog-Api-Key 头；Microsoft v3 使用 /translate、api-version=3.0、from/to 及订阅/区域头。AI 使用 Chat Completions，字幕只在 user JSON 的 subtitle 字段，system 独立可信提示，未启用工具，stream/store=false、输出最多 2048 tokens。此结构不保证模型永远不会受提示注入影响，也不保证第三方兼容实现的数据留存策略。
- TranslationCue(FileGeneration, Revision, Text)、LatestCue.Reset/Update/IsCurrent 是纯规则；文件代次递增，空句/换片/Reset/A→B→A 使旧结果失效。SubtitleTranslationCache(capacity=128) 最大容量 512，Reset() 返回新 scope；TryGet(scope,text,out translated)/Store(scope,text,translated) 只接受当前 scope。父须在换片、换凭据、provider/语言/endpoint/model 后 Reset，缓存不持有密钥、不写磁盘。没有后台循环，未写父的 SubtitleTranslationRuntime.cs。

## 使用代价与未覆盖

- 选择启用才外发字幕原文。Google Cloud Translation / Microsoft Azure Translator 需要用户开通对应官方 API 与密钥，不使用免费网页私有接口；额度和费用由服务账户决定。
- 自定义 AI 仅 HTTPS 或 IP/localhost loopback HTTP，拒绝 URL userinfo（包括空 userinfo）、query、fragment；凭据和字幕会交给用户选中的服务。Google/Microsoft 则限定官方域（含 Microsoft 单标签 Azure 资源域），不向任意自定义站点发它们的密钥。
- 初始适配约定为 gpt-4.1-mini；主窗口在 2026-10-25 核对官方弃用状态后改用 gpt-6-luna 并以 reasoning_effort=none 降低逐句延迟。模型仍可填，第三方服务不会收到该特定模型选项。价格与可用性由实际服务决定，不承诺免费。
- 未验证真实服务账号、Google/Azure 启用/地域/额度、真实 DPAPI、第三方模型、翻译质量、在线延迟与限流恢复。AI 注入测试只证明请求角色与 JSON 数据边界，不能证明模型输出可靠。
- 父负责 AppSettings 根字段、Normalize 接入、备份/重置、Settings 卡片、DI、文件生命周期、mpv/Lua 独立译文 OSD 与迟到响应判定。始终独立译文+原文双语，不写 display mode，不占主/次 sid；本 worker 没有运行覆盖此链路。

## 官方正文证据

只读取 primary 官方站点。原始正文保存在本测试目录的 ignored work/official-docs，公开摘要及 SHA-256 见 OfficialDocs.md；没有真实服务请求或凭据。
