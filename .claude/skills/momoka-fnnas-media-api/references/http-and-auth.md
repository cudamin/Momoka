# 传输与鉴权

本文件记的是**飞牛影视媒体服务**的传输层事实与两处未决项。因为 Momoka 尚未接入，这里没有"已经做过的决定"，只有"接之前必须核对的输入"。凡标 `【实测】` 的才是在本机服务器上验过的；其余为 `【抓包·未实测】`（社区文档）或 `【上游实现】`（MoviePilot）。

## 入口与基址归一

- 业务接口形如 `{入口}/api/v1/...`，**入口自带 `/v`**。合成后：`http://host:5666/v/api/v1/mediadb/list`。
- 端口/路径随部署变：社区文档记媒体服务直连 `http://{NAS_IP}:8056`；上游示例用 fnOS 网关 `http://127.0.0.1:5666/v`。**别把 8056 或 5666 当常量**，以用户实际能打开的入口为准。
- 上游的兼容策略【上游实现】：`standardize_base_url(host)` 后，先试 `host + "/v"`（能访问 `sys/version` 就用），否则再试原样 `host`。
- **归一化产出恒以 `/v` 结尾的基址**，业务路径相对它拼。把 `/v` 当入口的一部分（类比 Emby 的 `/emby/`），否则会拼出 `/v/v/api/...`。

## 登录

`app_name` 固定 `"trimemedia-web"`。密码字段两版不同：

| | v1（旧，已废弃） | v2（现行） |
| --- | --- | --- |
| 方法/路径 | `POST {base}/api/v1/login` | `POST {base}/api/v2/user/loginByPassword` |
| 请求体 | `{username, password, app_name}` | `{username, password, app_name}` |
| `password` 值 | **明文** | **`sha256(明文)` 的小写十六进制** |
| 成功响应 | `{code: 0, data: {token}}` | `{code: 0, data: {token}}` |

判定顺序【上游实现】：

1. 若开了访问码，先过访问码（下节），否则连登录接口都够不着。
2. 打 v2。`code == 0` → 取 `data.token`。
3. v2 返回了**业务错误码**（如 `-15`）→ 直接失败，**不回退**。
4. v2 **HTTP 层失败**（接口不存在/连不上）→ 才回退 v1 明文登录。

新版服务端的坑：v1 对**任何**输入都回 `-15 Password Incorrect`，所以 `-15` 只能说明"登录没成功"，**不能**区分账号不存在、密码错、还是打错了接口版本。诊断时先确认走的是 v2。

登出：`POST {base}/api/v1/user/logout`【上游实现】。当前用户：`GET {base}/api/v1/user/info`。

## 访问码（可选）

服务端可开启访问码。开启后必须先校验，通过后靠**会话 Cookie** 记住；这是 Cookie，不是令牌。

```
GET {根}/c/{urlencode(访问码)}      # 根 = 入口去掉结尾的 /v（访问码校验不在 /v 下）
```

- 码错或校验失败 → **404**（不是 401）。
- 未配置访问码 → 跳过。
- 设计上做成可选配置项；开启时才需要，且需要会话 Cookie 支持（请求要允许带 Cookie，与 Emby 侧默认禁用 Cookie 的策略相反，别照搬）。

## authx 签名（**未决项**）

所有请求带一个 `authx` 头，**形状**两来源一致：

```
authx: nonce={6位随机数字}&timestamp={毫秒时间戳}&sign={小写十六进制 MD5}
```

但 `sign` 的算法**两来源冲突**：

**A. 社区抓包文档【抓包·未实测】**

```
sign = MD5( nonce + timestamp + token )      # 直接拼接，无分隔符；token 为空串时也照样拼
```

**B. MoviePilot 生产实现【上游实现，带回归测试】**

```
data_hash = MD5( body )                       # POST：请求体 JSON 原文；GET：查询串；无则空串
sign      = MD5( APP_SECRET + "_" + api_path + "_" + nonce + "_" + timestamp + "_" + data_hash + "_" + APP_KEY )
```

- `api_path` 是**含 `/v` 前缀的完整路径**（如 `/v/api/v1/mediadb/list`）。
- `APP_SECRET = "NDzZTVxnRKP8Z0jXg1VAMonaG8akvh"`、`APP_KEY = "16CCEB3D-AB42-077D-36A1-F355324E4237"`（上游硬编码的应用级常量，**不是用户凭据**）。
- 这些常量属应用身份，不是账号密码；即便如此也按 CLAUDE.md 的凭据习惯处理，别写进日志正文。

两者不可能对同一版本同时成立。**接入前必须定案**，办法二选一：

1. 读官方前端 `{入口}/v/assets/` 下的签名代码（一手）。
2. 对目标服务器做一次最小验证：登录拿到 token 后，用同一公式发一次只读请求（如 `sys/version`），看是否被接受。

定案前不要把任何一版当权威；把结论、服务器版本和验证方式写进 PROBE/注释，并回写本文件。

## 请求头清单【上游实现】

```
Authorization: <token>              # 登录后；登录/访问码请求为空
authx: nonce=…&timestamp=…&sign=…   # 每个请求都要
Accept: application/json
Referer: {入口}
User-Agent: <自定义>
Content-Type: application/json      # 仅 POST
```

Cookie 在开启访问码时随会话携带。

## 响应语义

统一信封：

```json
{ "code": 0, "msg": "", "data": … }     // data 可能是对象、数组、布尔或字符串
```

- `code == 0` 成功；非 0 是业务错误。已知：`-15` = `Password Incorrect`。
- **HTTP 状态码基本不用于判错**——上游直接解析正文 `code`，`msg` 只进日志。因此不要照 Emby 的 401/403/404 → 异常映射，要另建：先判 HTTP 是否成功，再判正文 `code`。
- `msg` 可能为空、可能回显请求内容，别原样展示给用户。
- 部分接口成功时 `data` 是 `true`/`false`（如 `play/record`、`mdb/scanall`），别假定一定是对象。

## 凭据边界

- 令牌只走 `Authorization` 头；不进 URL、启动参数、日志。日志 URL 剥掉 query。
- 图片接口需要鉴权，**不能**当公共静态图直接给外部进程；经进程内 API 或已验证的进程间通道传递（同 [播放技能](../momoka-playback/SKILL.md) 的边界）。
- 云盘直链（`direct_link_qualities[].url`）指向**外部网盘**，属外部来源，播放前按 CLAUDE.md 的来源边界处理。
- 上游实现里访问码、密码等都只存配置；本仓沿用 DPAPI 加密存储的既有做法（见 CLAUDE.md「凭据」），不复用 Emby 的熵。
