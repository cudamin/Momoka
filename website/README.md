# Momoka 官网

Momoka 的官方介绍站点。Momoka 是面向 Windows 的原生媒体库客户端，使用 WinUI 3 与 libmpv，支持 Emby、Jellyfin、飞牛影视与 Plex。

- 主项目：[cudamin/Momoka](https://github.com/cudamin/Momoka)
- 站点是纯静态 HTML/CSS/JavaScript，可以直接部署到 Cloudflare Pages，也可以放进任意 Nginx 容器。

## 目录结构

```
website/
├── dist/                 # 实际部署内容：index.html、styles.css、main.js、assets/
├── index.html            # 源文件
├── styles.css
├── main.js
├── assets/
├── worker/               # Cloudflare Workers 入口（静态资源 + 内置弹幕凭据）
├── wrangler.jsonc        # Cloudflare Workers 配置
├── docker-compose.yml
├── nginx.conf
└── README.md
```

修改源文件后，把最新文件同步到 `dist/` 再部署：

```bash
cp index.html styles.css main.js dist/
cp -r assets/* dist/assets/
```

## 内置弹幕凭据端点

`worker/` 除静态资源外还提供 `GET /api/danmaku/credential`：从环境变量读取弹弹play 应用凭据，用 AES-GCM 加密后返回；客户端解密后在本机签名。它只下发凭据，不代理搜索或弹幕请求。

部署到 Cloudflare Workers：

```bash
cd website
npx wrangler secret put DANDANPLAY_APP_ID
npx wrangler secret put DANDANPLAY_APP_SECRET
npx wrangler secret put DANDANPLAY_CREDENTIAL_KEY   # 与 src/Momoka.Core/Danmaku/DandanPlayBuiltIn.cs 的 Key 相同
npx wrangler secret put DANDANPLAY_CREDENTIAL_KID  # 任意短标识，例如 momoka-2026-10
npx wrangler deploy
```

部署后把得到的地址（形如 `https://momoka-site.<账户子域>.workers.dev/api/danmaku/credential`）填回 `DandanPlayBuiltIn.Endpoint`，重新构建客户端。若继续用 `momoka-site.pages.dev` 的纯静态 Pages 部署，这个端点不会被包含；纯静态 Pages 需要改用 Workers 或 Pages Functions。

验证：

```bash
node --test worker/credential.test.mjs
```

**安全边界**：端点公开，加密密钥随客户端分发，愿意动手的人可以把 AppSecret 解出来；它挡不住恶意提取，也不改变弹弹play 的共享额度。不要把 AppSecret 写进 `wrangler.jsonc`、`dist/` 或仓库；建议在 Cloudflare 控制台给该路径配置频率限制（例如 60 次/分钟/IP）并监控异常流量。真正保密需要服务端代理。

## 飞牛 Docker 部署

1. 在飞牛「文件管理」里新建目录，例如：

   ```text
   /vol1/1000/docker/momoka-site
   ```

2. 把 `dist/`、`docker-compose.yml`、`nginx.conf` 上传到这个目录。也可以把整个 `website` 目录传上去；Compose 只会把 `dist/` 挂载给容器。

   最终目录至少应是这样：

   ```text
   momoka-site/
   ├── dist/
   │   ├── index.html
   │   ├── styles.css
   │   ├── main.js
   │   └── assets/
   ├── docker-compose.yml
   └── nginx.conf
   ```

3. 打开飞牛「Docker → Compose → 新建项目」，工作目录选上面的 `momoka-site`，使用其中的 `docker-compose.yml` 部署。

   或者在 NAS 的 SSH 里执行：

   ```bash
   cd /vol1/1000/docker/momoka-site
   docker compose up -d
   ```

4. 浏览器访问：

   ```text
   http://飞牛IP:8080/
   ```

   容器内 Nginx 监听 80，飞牛宿主机映射到 8080；首次启动需要先拉取 `nginx:alpine`。

## 更新与排查

```bash
# 查看状态
docker compose ps

# 查看日志
docker logs --tail 50 momoka-site

# 修改 nginx.conf 后重启
docker compose restart momoka-site

# 检查本机是否能访问
curl -I http://127.0.0.1:8080/
```

- **页面 404**：确认挂载的是 `./dist`，且 `dist/index.html` 确实存在。
- **端口被占用**：改 `docker-compose.yml` 里 `"8080:80"` 左边的端口，例如 `"8090:80"`。
- **改了文件没生效**：HTML/CSS/JS 默认不缓存，直接强制刷新；图片缓存 7 天，可换文件名或清 Cloudflare 缓存。

## 用 443 访问（Lucky / Cloudflare 终止 TLS）

容器仍然只提供 HTTP，映射到宿主机 `8080`；443 交给 Lucky 或 Cloudflare Tunnel，不要改成 `443:80`。

### Lucky 监听 443

1. 在 Lucky「Web 服务」里新建规则：
   - 监听端口：`443`，开启 HTTPS
   - 域名：你的域名
   - 证书：用 Lucky 申请 Let's Encrypt，或上传 Cloudflare Origin Certificate
   - 后端地址：`http://127.0.0.1:8080`
     - Lucky 与容器在同一台飞牛：用 `127.0.0.1`
     - Lucky 在 Docker 里：用 `http://172.17.0.1:8080` 或宿主机局域网 IP
     - Lucky 在另一台设备：用 `http://飞牛IP:8080`
2. 路由器把公网 `443` 转发到 Lucky 的 `443`；80 可选，用于跳转到 HTTPS。**不要**把 `8080` 转发到公网。
3. Cloudflare DNS 指向你的公网 IP（橙云开启），SSL/TLS 模式选 **Full (strict)**；Lucky 上的证书必须有效。家宽动态 IP 记得配 DDNS。

### 使用 Cloudflare Tunnel

Tunnel 的 Public Hostname 服务地址填 `http://飞牛IP:8080`（或 Tunnel 所在环境能访问到的容器地址）。这样访问域名自然走 443，不需要开放任何入站端口。

### 常见错误

- `443:80` 只是把明文 HTTP 映射到 443，浏览器和 Cloudflare 都会报 TLS 错误，不要这样改。
- 容器和 Lucky 不能同时监听同一个 443；若 Lucky 已占用 443，保持容器在 8080 即可。
- 公开访问前建议加一层访问控制（Cloudflare Access、Lucky 基础认证等）。

- 不要把 Qoder 的 `*.qoder.zone` 预览地址直接 CNAME 到 Cloudflare：登录态、Cookie 和 Host 校验都会失效；要自定义域名就自行托管这份静态文件。

## 不用 Compose 的启动方式

```bash
docker run -d \
  --name momoka-site \
  -p 8080:80 \
  -v "$(pwd)/nginx.conf:/etc/nginx/conf.d/default.conf:ro" \
  -v "$(pwd)/dist:/usr/share/nginx/html:ro" \
  --restart unless-stopped \
  nginx:alpine
```

## 部署到 Cloudflare Pages（推荐）

这是纯静态站点，Pages 比 Worker 更直接，不需要写服务端代码，也不需要 NAS。

### 控制台拖拽上传

1. Cloudflare 控制台 → Workers & Pages → Create → Pages → Upload assets。
2. 项目名填 `momoka-site`；把 `dist/` 里的内容拖进去（不要上传整个 `website` 目录）。
3. 点击 Deploy，得到 `https://momoka-site.pages.dev`。
4. 进入项目 → Custom domains → Set up a custom domain，填你的域名。域名在同一个 Cloudflare 账号时，DNS 和证书会自动配置。

### 用 Wrangler 部署

```bash
cd C:\Users\89400\Momoka\website
npx wrangler@latest login
npx wrangler@latest pages deploy dist --project-name momoka-site
```

- 更新站点：把源文件同步到 `dist/`，重新拖拽或重跑 `wrangler pages deploy`。
- Pages 的生产地址默认公开；需要私密访问时，在自定义域名前加 Cloudflare Access。
- 项目里的 `wrangler.jsonc` 和 `worker/` 是 Workers 方案备选，使用 Pages 时不需要。

## 备选：部署到 Cloudflare Workers（无需 NAS）

这是纯静态站点，Worker 只负责返回 `dist/` 里的文件。需要本机有 Node.js 和 Cloudflare 账号。

```bash
cd C:\Users\89400\Momoka\website
npx wrangler@latest login
npx wrangler@latest deploy
```

- [wrangler.jsonc](wrangler.jsonc) 已把 `dist/` 声明为静态资源目录，入口是 `worker/index.js`。
- `login` 会打开浏览器授权；部署成功后终端会给出 `https://momoka-site.<你的子域>.workers.dev`。
- 绑定自定义域名：Cloudflare 控制台 → Workers & Pages → `momoka-site` → Settings → Domains & Routes → Add → Custom Domain。域名与 Worker 在同一个 Cloudflare 账号时，DNS 和证书会自动配置。
- 更新站点：同步源文件到 `dist/`，再执行一次 `npx wrangler@latest deploy`。
- 不想用命令行的话，也可以在 Cloudflare 用 Pages 直接上传 `dist/` 文件夹，效果相同。

