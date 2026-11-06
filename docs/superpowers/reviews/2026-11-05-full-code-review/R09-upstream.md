# R09 官方源码与界面对照

本轮重新解析官方默认HEAD，前后端仍为v3，提交已经不同于R08。按SHA下载codeload ZIP并校验哈希，未访问用户配置或部署服务器。当前公开源码只用于判定本轮协议，不声称部署版本同步。ZIP大小、SHA256、核对时间见[R09验证](R09-verification.json)和本地`upstream.json`。

- 后端：[jxxghp/MoviePilot @ 48874bc](https://github.com/jxxghp/MoviePilot/tree/48874bc08a3c1cb5247f17c8845a6918976c0d22)。
- 前端：[jxxghp/MoviePilot-Frontend @ fad61c3](https://github.com/jxxghp/MoviePilot-Frontend/tree/fad61c3ed951d09504694d21e462f8ef99a9f342)。
- 证据根：`artifacts/review/861f48b/R09/20261106-first/upstream`。R08旧快照保留，没有覆盖。

## 同输入行为与界面状态

| 场景/控件 | 官方实现与本地结论 |
| --- | --- |
| 原历史文件定位 | 成功move从dest恢复，其余从src恢复；本地逻辑相同，但AniBT确认仍显示旧src文件名，R09-005 |
| 重整及旧目标清理 | 预览跳过清理；实际重整按当前src/storage再取最新历史，可能从所选H1变H2，R09-001 |
| 目标存储、路径、方式、刮削、类型/分类目录 | 官方目标匹配返回整组配置；本地可空覆盖和显式使用/清空保持相同语义。目标允许rclone等，软链依赖不能只比字符串，R09-007 |
| 来源/媒体号、类型与历史识别 | 编号为空时不发送孤立来源；from_history优先旧识别，本地风险文案已有，不自动新增抽象 |
| 季/集、偏移、剧集组 | 官方显式episode_detail优先文件名；本地批量丢叶条目编号会发送错误集号，R09-004 |
| 预览、确认、取消、同步/后台 | 本地要求完整预览与一次明确确认；这是项目约束。已发防重、未知结果及队列accepted不等于完成仍保留 |
| 删除四档、选择、分项回执 | 官方前端有仅记录/记录与源/记录与目标/两端四档，后端按logid和FileItem存储执行；本地仅删源的匹配端漏存储，R09-002 |
| 部分成功、重试、列表更新 | 官方逐步骤回执与失败记录保留；本地记录页保守停批。AniBT尾扫异常丢outcomes为R09-003；媒体库旧索引重新造卡为R09-008 |
| 动画资格和AniBT开关 | 本地专有功能，不要求MP网页存在相同UI；缺集行混淆资格与能力为R09-006 |
| 音乐/CUE、目录推荐与额外管理功能 | Momoka当前仅影视。官方格式推荐依赖显式FileItem，历史ID模式没有该输入；未把不适用功能缺失报为缺陷 |

## 可追溯阅读范围

以下为实际全文或成员段阅读；并非整仓源码全部审查。各文件SHA256及总行数在验证JSON中。

| 仓库 | 文件 | 阅读行 |
| --- | --- | --- |
| jxxghp/MoviePilot | `app/api/endpoints/transfer.py` | 190-226,576-746,774-1092 |
| jxxghp/MoviePilot | `app/api/endpoints/transferhistory.py` | 1-118 |
| jxxghp/MoviePilot | `app/schemas/transfer.py` | 337-447,519-594 |
| jxxghp/MoviePilot | `app/chain/transfer/history.py` | 74-99,193-327 |
| jxxghp/MoviePilot | `app/chain/transfer/workflow.py` | 328-355,566-614,671-710,756-886 |
| jxxghp/MoviePilot | `app/chain/transfer/records.py` | 433-533,539-613 |
| jxxghp/MoviePilot | `app/chain/transfer/execution.py` | 465-493,596-628,674-712 |
| jxxghp/MoviePilot | `app/chain/transfer/plan.py` | 442-457,504-532,701-726 |
| jxxghp/MoviePilot | `app/application/history/retry.py` | 291-303 |
| jxxghp/MoviePilot | `app/db/adapters/history/transfer.py` | 215-228 |
| jxxghp/MoviePilot | `app/db/oper/transferhistory.py` | 271-283 |
| jxxghp/MoviePilot | `app/db/models/transferhistory.py` | 273-289 |
| jxxghp/MoviePilot | `app/schemas/file.py` | 23-31 |
| jxxghp/MoviePilot | `app/schemas/types.py` | 758-772 |
| jxxghp/MoviePilot | `app/chain/storage.py` | 108-114,191-242 |
| jxxghp/MoviePilot | `app/api/endpoints/history.py` | 389-413 |
| jxxghp/MoviePilot | `app/api/dependencies/history.py` | 134-163 |
| jxxghp/MoviePilot | `app/application/history/mutation.py` | 315-348 |
| jxxghp/MoviePilot | `app/application/formatting.py` | 103-234 |
| jxxghp/MoviePilot | `tests/test_transfer_history_gate.py` | 533-583 |
| jxxghp/MoviePilot | `tests/test_manual_transfer_history.py` | 104-150,969-1014,1155-1196 |
| jxxghp/MoviePilot | `tests/test_transfer_history_retransfer.py` | 290-400,814-865 |
| jxxghp/MoviePilot | `tests/test_history_mutation_command.py` | 274-349 |
| jxxghp/MoviePilot-Frontend | `src/components/dialog/ReorganizeDialog.vue` | 1-224,583-641,664-850,1275-1319,1351-1398,1414-1756,1812-2006,2025-2460,2638-2668 |
| jxxghp/MoviePilot-Frontend | `src/components/dialog/TransferHistoryDeleteDialog.vue` | 1-56 |
| jxxghp/MoviePilot-Frontend | `src/views/reorganize/TransferHistoryView.vue` | 182-289,425-448,709-740,754-848,1045-1164,1182-1203,1509-1517,1761-1787 |
| jxxghp/MoviePilot-Frontend | `src/components/dialog/__tests__/ReorganizeDialog.spec.ts` | 828-857,970-1045,1287-1319,1360-1447 |
| jxxghp/MoviePilot-Frontend | `src/views/reorganize/__tests__/TransferHistoryView.spec.ts` | 874-901,1065-1092,1128-1154,1855-1899 |

## 原样上游函数反例与边界

`upstream-repro.py`用AST唯一定位并原样编译`get_by_src`、`resolve_history`、`_is_successful_move_history`、`_get_manual_transfer_history`、`_delete_manual_transfer_history`，只替换数据库查询对象与记录型存储。相同源、较新H2实际被选中，记录删除H2目标和H2历史；源码行/哈希保存在`upstream-repro.json`。没有真实文件删除、完整后端启动、服务器原子性或部署版本证明。端点把logid转成源、workflow在非预览时调用清理，由另两名审查者独立连读核实。

## AniBT公开资料缺口

读取了[公开主页](https://anibt.net/)、[Wiki](https://wiki.anibt.net/)与[公开OpenAPI](https://wiki.anibt.net/openapi.yaml)，原件与哈希留在本轮证据根。Wiki链接仅指文档仓库AniBT-Net/wiki，未取得AniBT后端可执行实现。OpenAPI此次185,583字节；窄段读取570–633匹配决定、791–855 RSS入口/重定向/集号参数。它只提供规格，不能替代服务实现。

本地目录使用`api/animes/search`、详情/分组等内部读入口，公开规格没有完整覆盖；未发实际搜索或matcher请求，也不把主页渲染的内容当接口测试。无法证明当前后端兼容，也不据不完整文档宣称确定缺陷。主责匿名客户端的请求边界与响应解析仍由当前源码及定向测试审查。
