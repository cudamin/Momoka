# R-031：MoviePilot 登录跳转边界

## 取舍

`MoviePilotClient` 自己持有的默认 `SocketsHttpHandler` 及直接注入的原生 `SocketsHttpHandler`／`HttpClientHandler` 明确 `AllowAutoRedirect=false`。**同源也拒绝**，配置必须指向最终 API 来源；不新增共享传输抽象，不放宽 Emby／Plex 规则。

密码登录使用 `ResponseHeadersRead`，收到任何300–399立即抛带原始状态码的 `MoviePilotException`，提示不自动跳转及最终来源；不缓存、解析或向异常带入跳转正文／Location，不转换方法、不自动重发密码或写操作。原配置的初次请求已经可能到达服务器，不把拒绝下一跳说成“没有提交任何请求”。

直接成功／非跳转错误仍有原120秒总期限与响应缓冲上限，调用者取消和内部期限分类分开。其它API和图片沿原读取行为，所属原生handler也不跳转。任意自定义假handler只是响应替身，不能凭它证明默认网络边界。

## 当前工作树执行

先按 `CLAUDE.md` 成功编译当前 Release Tests／Core；复制来源文件若继承较早mtime，确认实际编译了变更（或只更新时间戳触发），不能用exit0的增量跳过代替fresh代码。

```powershell
& "$HOME/.dotnet/dotnet.exe" run --project ./tests/Momoka.Tests/Momoka.Tests.csproj -c Release --no-build --no-restore -- moviepilot-login-redirect-fixture
```

`MoviePilotTests.Register()` 已注册全部43项；专项路径仅调用 `MoviePilotLoginRedirectTests.Register()`，不再重复注册。与完整Core重叠，不相加。

## 43项实际覆盖

- 39项实际IPv4双回环；35项无参生产构造、4项直接原生handler，另加2项真实观察器边界（半请求取消和迟到接受最终否决）。所有监听器使用 `127.0.0.1:0` 原子分配端口，不访问真实服务。请求正文只有固定占位用户名／密码，接收证据保存布尔值和计数，不保存表单明文。
- 301／302／303／307／308同／跨源，绝对／相对Location、缺Location、带敏感占位响应、有限和循环链。第二站接收、用户名、密码次数必须为零，原站只收到一次；登录不得接受第二站的伪200会话。额外JSON POST、Bearer GET、图片GET均不重放或跟随。
- 首次请求前取消、等待响应头取消、收到头后正文取消、停住的跳转正文收到头即拒、成功正文内部期限、直接200有效／无效会话、401／403／连接探针失败边界。
- 观察器在 TCP accept 立即累加接受数，完整解析请求另列。全部39回环先停止并等待两个接收线程，再核最终接受数和关闭前排队连接，不能把运行中Count采样当最终零下一跳证据。半请求取消后仍计1；迟到半请求负例先零→接收→停止后，最终零断言必须失败。初新增半请求42项41过1败→收紧计数后42绿；完整套因sender提前FIN出现EOF夹具竞态的2837项2836过1败保留，改为sender存活到receiver取消后（不吞EOF）；再补最终Stop/join断言和迟到反例后43全过。来源41项／主42项／最终43项各自hash分开，不混同字节。
- 2项通过HTTP响应注入测试HTTPS→HTTP的策略拒绝，不建立TLS、不能宣称证书或真实降级链已验。

## 来源与本树证据

来源 `c1142536f3b300d94e1b87cd6984c593814ff011`，只合四份题内源／测试／MP技能，不复制来源PROGRESS或DLL。来源目录：`C:/Users/89400/.codex/worktrees/r031-login-origin-final/Momoka/artifacts/staged-repair/batch07/r031/`。

- 稳定修前41项10过31败，修后41全过，完整Core2686全过。39份真实回环收据另保，稳定红与绿测试hash一致。首轮夹具关闭竞态的原失败仍保留，修正夹具后才记录稳定红。
- 来源跨源307/308第二站修前各收到一次POST／用户名／密码、返回伪会话，修后均为零且无会话；301/302/303的修前GET伪成功也阻断，循环source/next从26/25变1/0。均为占位证据，不是真实秘密泄漏复现。
- 本树目录 `artifacts/staged-repair/batch07/r031/`。首次Copy-Item继承较早source mtime，增量构建误跳CoreCompile且沿用R003旧Core，导致主专项41项10过31败；旧日志/源与DLL元数据保留，`incremental-stale-correction.json`归因。只更新实际产品源mtime、SHA不变后重新编译，`fresh-current-r031.log`为41全绿。最终批次固定完整src及DLLhash，不能拿首次exit0构建冒作current绿。

## 未覆盖

没有真实MoviePilot登录、生产配置／明文凭据／DPAPI、真实TLS／证书、服务器写入、WinUI、native或媒体播放。真实libmpv门禁红、旧快捷方式不变，专项通过不等于发布或交付。
