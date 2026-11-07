# 第十批修复与交付（2026-11-07）

本批只修 R01-002、R01-003，前九批不重复。用户已授权自行决定必要修复且不逐项暂停；基点483c22c，产品提交2eaedc6、e4944c4均已进入master。

- R01-002：Status 与四类写开关在所有写分支前拒绝；文件或目录只读取签名状态并返回，无签名证书/工具也能查询。完整生产控制流只替换外部证书/注册表/签名端点；两宿主112项，旧32失败，修后及主树112/112，文件哈希保持。没有真实签名、安全设置或证书读取。
- R01-003：SkipPublish 在ISCC前复用完整生产验收，显式按FrameworkDependent传自包含模式，失败阻止编译。正常publish原已有同一验收，不重复；已签名文件字节变化不豁免哈希。固定非PE完整载荷、真实脚本与验收链，两宿主174项，旧88失败，修后及主树174/174；成功/失败路径、SC/FD模式和文件保持均通过。

初始安装器夹具XML多PropertyGroup及PowerShell数组接参/哈希表remove成员冲突导致的过程失败分别保留，不计作产品红。独立复核仅发现回执在执行结束采样当前源码的来源问题，已改为实际运行副本；旧红原回执不改，另存executed-source-correction.json指明旧执行SHA86BA24…。没有剩余必要产品发现或待修次要项。正常发布验收复用、签后哈希拒绝、真实安装/签名及未改证书链的覆盖界限已明确。

主树最终源码e4944c4：Tests/Core当前Release/x64重编零警告错误，完整4135/4135、失败0跳过0；61脚本编码/语法通过，6变更文本LF/BOM/末尾换行/差异一致性通过。仅脚本和文档，无UI/播放/依赖/分析器变更，按日常批次完成主测试、受影响脚本与实际发布；没有冒称全方案/全量UI自检已运行。另一聊天主测试结束后串行运行本批主测试，无终止其进程。

主树publish -NoArchive -Shortcut成功，完整真实批准载荷/许可验收通过；用户确认关闭桌面程序后核实退出才发布。实际0.2.5、546文件300355063字节（286.4MB），快捷方式目标/工作目录为主树artifacts/publish/win-x64，参数空。已测/发布Core同SHA71D90022E7856CF48BEE0DAABD2865036E34E3E5E3408BD6AC425C4E7AFF7892，构建/发布Shell同SHABE41E7E811DE58D75F1FCAA2F6B22501E2E684DCEEDC3C058B6E160AF4134B9F。

证据artifacts/review-batch10-master：delivery-receipt.json、tested-source.json、tests.log、build-tests.log、sign.log、installer.log、publish.log、independent-review.txt；开发303份红绿证据逐SHA保存至development。主树两专项完整回执另在artifacts/review-batch10对应master目录。无上传、额外ZIP/MSIX、真实安装、签名、服务器或播放。primary托管协调树继续保留，本批无新工作树/临时分支或待交付产品。纯文档收尾不重建或重发。
