# R12 固定上游与核对边界

本轮来源详表/URL/hash在[R12验证](R12-verification.json)；ignored证据E=`artifacts/review/11921ff/R12/20261106-first`，固定源码缓存E/upstream，历史固定mpv源码仍在R11证据目录。先读实现再判断，不用文档推断实际图像。

| 来源 | 固定提交/版本 | 实际核对 |
| --- | --- | --- |
| mpv | 270367e8af7e2f49fc3d3f8da03c985f5ae3b402 | gpu/video.c2869–2883曲线白名单、gpu-next2909–2937对照；client.c1787–1829稳定事件payload；parse_config101–115固定长度引用、运行load_config/批量option通知；vf_vapoursynth809–874动态库/API加载 |
| FFmpeg | 随包N-127522-gc705686a3，取同前缀固定源码 | vf_minterpolate.c205–247fps/MCI/OBMC/AOBMC/BIDIR/BILAT/vsbmc/仅8bit格式；327–397尺寸/内存分配；1160–1208输出fps与帧props。不对完整FFmpeg内部作本区全审声明 |
| libplacebo | 1dd022687b9ac1dd996855b51433bf770ae76aab | custom_mpv.c1253–1258保存、1323–1357绑定/缺纹理跳过、1681–1706保存stage；renderer1058–1196hook顺序与2050调整ref_rect。clean源码支持当前链；随包读回带dirty，补丁未核 |
| ArtCNN | 0837e5326e710f49e2ab0a8d995da0708047442e | GLSL原4文件全字节与MIT许可，权重/模板结构 |
| CfL | 066dff964b83aa34e893de25d590874bf8c8e94f | 根目录原2文件全字节与MIT许可 |
| mpv-prescalers | b3f0a59d68f33b7162051ea5970a5169558f0ea2 | 根目录原4hook全字节/LGPL3+，不拿compute/gather同名版本替代 |
| AN3223/dotfiles | 4413db6c1b47aa70ffce40c5a91158e518881718 | .config/mpv/shaders原2文件；不取LQ同名hdeband |
| SSimDown gist36508af3ffc84410fe39761d6969be10 | 38992bce7f9ff844f800820df0908692b65bb74a | 全字节相同/LGPL3+ |
| SSimSuper gist2364ffa6e81540f29cb7ab4c9bc05b6b | 15d93440d0a24fc4b8770070be6a9fa2af6f200b | 全字节相同/LGPL3+ |
| adaptive gist8a77e4eb8276753b54bb94c1c50c317e | 572f59099cd0e3eb5e321a6da0a3d90a7382e2dc | BSD2；仅curve_height1.0→0.5后全字节相同 |
| dyphire/mpv-config | b6c687b4c08144e87524a738152d07ad3735915b | shaders/Krig和两Anime4K全字节；mpv.conf830–833固定组次序 |

ArtCNN/CfL/RAVU/AN3223/gists为本轮解析后固定上游版本，原入库日期不是原commit；dyphire为README已有固定历史版本。17份同字节、adaptive一处已声明修改。许可证判断来自实际源码头/对应license，三份入库文本全文及固定SPDX版本比对；不将现上游HEAD自动安装或升级到产品。

本树批准DLL SHA256 `fe583af31036bf3f3adacf6659e1bd448b568091b0334675573a3e303ec89d17`，101,242,880B，Vulkan hash `13dd448a97495763b5cce84b8ade84cd2fa13c71f7e871f3c7d509eca845ad0f`。每次native使用前核批准值。版本实读mpv v0.41.0-1163-g270367e8a，FFmpeg同上，libplacebo v7.375.0 (v7.360.0-153-g1dd0226-dirty)。没有加载用户VSScript/Python，也没有改批准清单。

来源代理最初portable_config路径404/匿名API限流，改同固定Git ZIP读取；母代理存档同名路径歧义先拒绝，再按已核根路径选择。失败日志保留，未关TLS检查。字节一致是来源证据，native接受是选项证据，托管seam是控制流证据，都不冒充shader运行/听感/帧率或正式交付。
