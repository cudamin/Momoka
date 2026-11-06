# R08 官方源码与界面对照

本轮重新通过 `git ls-remote --symref <官方仓库> HEAD` 确认两个默认分支均为 v3，再从 GitHub codeload 按完整 SHA 取得源码。它们是本次公开分支快照，不代表用户部署版本或稳定版支持承诺。未读取真实设置、凭据或 `dashboard/system`；适用服务器 tag 未确认，不声称本地已同步全部上游或实服兼容。

| 仓库 | 固定提交 | ZIP SHA256 |
| --- | --- | --- |
| [jxxghp/MoviePilot](https://github.com/jxxghp/MoviePilot/tree/4cd64d684b0cd7e198728ffb26c9eb3321db8672) | `4cd64d684b0cd7e198728ffb26c9eb3321db8672` | `d19a82844ff961e79612a8cac6242b541c5f7520ce24c6236a299fb4ac51cc1a` |
| [jxxghp/MoviePilot-Frontend](https://github.com/jxxghp/MoviePilot-Frontend/tree/c78efcf442e00cd2ba1387a188c3900408c3875d) | `c78efcf442e00cd2ba1387a188c3900408c3875d` | `6231517e49aea283de46eccf57db8f4dc195731469306fe792554a5e0d7d1656` |

原始获取信息与日期见证据根 `upstream.json`，只读源码放在本树证据目录 `upstream/`。没有安装上游依赖或运行上游测试；下表“测试”均指源码阅读。源码引用均相对对应固定仓库。

## 控件、状态与行为

| 表面 | 实际读取依据 | 本地对照与处置 |
| --- | --- | --- |
| 登录/资源凭据 | 后端 `api/endpoints/login.py:151–244`、`chain/user.py:38–95,198–217`、`adapters/web/security/access.py:207–362`；前端 `pages/login.vue:244–397,650–681,697–764` 和 `pages/__tests__/login.spec.ts:300–399` | 本地用户名/密码、连接反馈、JWT更新具备；MFA挑战被误报密码错误见R08-003。资源Cookie跨来源见R08-001。前端的MFA/PassKey/插件登录不能据外观假定本地支持。 |
| 下载确认 | 前端 `components/dialog/AddDownloadDialog.vue` 全文及同名spec全文；`misc/MediaIdSelector.vue`、spec、`composables/useMediaSources.ts`、`utils/mediaId.ts`、`api/storage.ts`全文 | 原标题、站点、做种/下载人数、描述、大小、默认下载器、自动/自填目录、高级来源/类型/编号、片名搜索/查找和失败重试均有对应。本地原生WinUI布局与完整媒体快照校验为既有差异；不把外层media_id当作替代media_in。音乐/未识别合集继续下载不纳入当前范围。 |
| 资源结果 | 前端 `cards/TorrentCard.vue`、spec、`utils/torrent.ts`全文；后端 `chain/search/execution.py:141–185`、`result.py:264–332`、`title.py`全文、`scan.py:560–612` | 资源信息、详情网页独立入口、下载反馈对应；本地平铺/筛选与上游更多来源不同，不自动要求新增功能。精确候选显式否决、类型/来源身份与原始正文保留已核。关键词上游不直接绑定目标media_info，未将虚构响应当实际缺陷。 |
| 搜索站点 | 前端 `dialog/SearchSiteDialog.vue:1–235`、`views/MediaDetailView.vue` 中站点选择成员及其spec集成用例576–625；后端 `api/endpoints/search.py` 两类搜索 | 本地默认/全选/清空/刷新、计数、错误、确认即搜索/只保存两种宿主具备。明确清空不退回服务器默认；默认标签准确。失败后取消却改变偏好见R08-005。未找到同名站点组件独立spec，不冒充已读。 |
| 季选择 | 前端 `dialog/SubscribeSeasonDialog.vue` 全文1–976及spec全文1–552；`composables/useMediaSubscribe.ts:60–110,244–288,306–498,545–589` | 未选季不自动预选；已有订阅、三模式、未知入库、亲自选模式与重试有对应。本地保留分组草稿、原生开关，属既有约定。上游notexists与本地exists_remote端点不同；已核双方请求/返回成员，全部下游分组换算未展开，实服等价未确认。 |
| 编辑订阅 | 前端 `dialog/SubscribeEditDialog.vue:1–748`及spec `86–250,348–429,596–721`；后端subscribe更新、schema及mutation | 基础/进阶、关键词、季集范围、质量/分辨率/特效、站点、周期、下载器/目录、洗版、IMDb、包含排除、规则/组/季、分类/自定义词均有对应。本地差量提交、未知值保留、在飞拦关闭更严格；默认系统规则及音乐表单不纳入当前编辑范围。 |
| 订阅封面/详情 | 前端 `cards/SubscribeCard.vue:186–289,391–490,529–610,746–1098`及spec `796–846,879–946,968–1030`；`views/MediaDetailView.vue:60–69,165–202,611–755,850–960,1070–1155`及spec `326–380,517–625` | 本地封面开详情、封面进度、电影不另列集统计依既有用户范围。官方新增执行详情/日志/分享/批量/排序未作为本轮新增功能；细分权限显隐缺少部署/账号支持证据，保留缺口。AniBT同页开关错误是本地自身规则缺陷R08-006。 |
| 文件统计/播放映射 | 后端 `api/endpoints/subscribe.py:348–458,762–783`、`application/subscription/mutation.py:44–67,311–389`、`chain/subscribe/query.py:291–520`、`tests/test_subscription_review_regressions.py:331–343` | 同ID可改媒体/季/组得到固定源码和测试证明；两读快照混用见R08-004。正常跨库itemid仍核路径，默认组数字唯一回退是明确保留的语义，不按风格删除。 |

上表 `views/MediaDetailView.vue` 的实际文件为 `src/views/discover/MediaDetailView.vue`；其spec在对应 `__tests__/`。其他组件前缀为 `src/components/`。精确路径和字节哈希由验证记录的 `upstreamFiles` 列出，避免相似组件名误指。

## 后端专项阅读范围

除上述外，完成以下成员/全文核对：

- `app/api/endpoints/download.py:1–95,232–269,336–423,509–552`：可信媒体与无媒体正文、字幕验签、目录和回执。
- `app/api/endpoints/search.py:1–111,159–190,222–413,442–567,575–655,727–765,830–871`：SSE批次、完整结果、Cookie认证、重复与逗号参数的区别、关键词/精确季和字幕。
- `app/api/endpoints/media.py:430–520,813–846`：来源目录、开放来源、重复参数、分页与详情type_name。
- `app/schemas/context.py:415–461`、`app/chain/download/subtitle.py:545–633`、`app/application/security/url.py:551–620`：字幕原对象、下载、稳定HMAC签名。
- `app/api/endpoints/subscribe.py:288–458,933–975`、`app/api/endpoints/mediaserver.py:146–208`、`app/schemas/subscribe.py:211–216,260`：新增/更新/季范围和入库状态字段。
- 全文阅读 `tests/test_download_paths_endpoint.py`、`test_download_media_source.py`、`test_search_stream.py`。`test_subscription_review_retarget.py`全文为音乐测试，未用于证明电视剧问题。

这些源码足以支持报告中的具体协议结论；没有展开全部官方服务实现，不声明全量上游审计。审查计划明确禁止自动产品修复，因此本轮将适用差异登记为缺陷或缺口，没有借技能“同步上游”要求擅自新增功能。
