# R12 全部18份shader核查

本树11921ff；source hash与固定来源逐文件列于[R12验证](R12-verification.json)的upstreamPins。执行代码/每个pass全文分批读取，嵌入数据全字节hash及数值/结构核对，不声称人工逐系数判断画质。共96自动链＋1固定链；无新增必要shader缺陷。

| 文件（assets/shaders下） | 行数 | HOOK/WHEN/BIND/尺寸 | 宿主与来源 |
| --- | ---: | --- | --- |
| ArtCNN/ArtCNN_C4F16.glsl | 1407 | 8LUMA compute；宽高输出比均严格>1.3；LUMA及conv链，最终2× | 动画低档slight/sweet/large；>30fps改RAVU；MIT/ArtCNN |
| ArtCNN/ArtCNN_C4F32.glsl | 3927 | 8LUMA compute；同门限；32通道4w×2h中间打包，最终2× | 动画中档slight/sweet；MIT/ArtCNN |
| ArtCNN/ArtCNN_C4F32_DS.glsl | 3927 | 同8compute/门限，独立DS系数，最终2× | 动画高档slight/sweet；sweet后adaptive；MIT |
| ArtCNN/ArtCNN_C4F32_DN.glsl | 3927 | 同8compute/门限，独立DN系数，最终2× | 动画中/高large，单套放大网络；MIT |
| CfL/CfL_Prediction_Lite.glsl | 181 | 2CHROMA；CHROMA.w<LUMA.w；HOOKED/LUMA→低分辨率LUMA→LUMA尺寸，ALIGN | 低GPU自动链；亮度放大器后，MIT/CfL |
| CfL/CfL_Prediction.glsl | 363 | 4CHROMA；前3同门限，末无WHEN但BIND LUMA_LR，缺纹理跳过；PARAM不被宿主改 | 中/高GPU自动链；与Lite互斥，MIT/CfL |
| ravu/ravu-lite-ar-r2.hook | 152 | 2LUMA fragment；宽高HOOKED/OUTPUT<0.707106；HOOKED/lut→int→2×；lut5×288 | 低GPUsweet/large；门限约1.414，LGPL3+ |
| ravu/ravu-lite-ar-r3.hook | 202 | 同门限与2×；lut13×288 | 中GPUsweet/large；sweet带SSSR，LGPL3+ |
| ravu/ravu-lite-ar-r4.hook | 298 | 同门限与2×；lut25×288 | 高GPUsweet/large；large后adaptive，LGPL3+ |
| ravu/ravu-zoom-ar-r2.hook | 218 | 1LUMA；宽高HOOKED<OUTPUT；双lut18×2592→OUTPUT尺寸 | 真人/高帧动画slight；不附永久闭门SSSR，LGPL3+ |
| an3223/hdeband.glsl | 220 | 1代码块3HOOK LUMA/CHROMA/RGB；无WHEN/改尺寸，仅HOOKED | vintage最前，deband=no前置随链，LGPL2.1+ |
| an3223/nlmeans_light.glsl | 2723 | 3代码块×LUMA/CHROMA＝6HOOK；无WHEN；HOOKED/G/GC；同尺寸，SF1/T0默认非时间处理 | vintage中/高hdeband后、放大前，LGPL2.1+ |
| igv/SSimDownscaler.glsl | 214 | 4POSTKERNEL；高度或宽度缩小时；PREKERNEL/HOOKED/L2/MR→目标 | shrink及固定Anime4K；dscale=mitchell/linear-downscaling=no/correct=yes，LGPL3+ |
| igv/SSimSuperRes.glsl | 203 | 4POSTKERNEL；NATIVE_CROPPED<OUTPUT分轴；HOOKED/LOWRES/PREKERNEL/var | 仅真人/高帧动画中高sweet，RAVU前序2×后仅>2×至2.2部分执行，LGPL3+ |
| igv/adaptive-sharpen.glsl | 230 | 1OUTPUT fragment，无WHEN/尺寸；HOOKED，curve_height0.5 | 高GPU部分链，与SSSR顶层锐化互斥；BSD2，唯一局部改动 |
| igv/KrigBilateral.glsl | 213 | 3CHROMA；CHROMA.w<LUMA.w；HOOKED/LUMA/LOWRES_Y→LUMA尺寸，ALIGN | 固定Anime4K首项，后不加CfL；LGPL3+ |
| Anime4K/glsl/Restore/Anime4K_Restore_CNN_Soft_M.glsl | 275 | 8MAIN fragment，无WHEN，MAIN/conv→MAIN同尺寸 | 固定第二项为修复，未误标放大器，MIT |
| Anime4K/glsl/Restore/Anime4K_Clamp_Highlights.glsl | 90 | 2MAIN统计＋1PREKERNEL clamp；无WHEN/尺寸；MAIN/STATSMAX | 固定第三项，DESC Compute-Statistics没有COMPUTE声明；MIT |

ArtCNN首6pass打包中间通道，不是连续倍增8次。全部MAC输入定义及V4/M4参数arity/finite核对；F16标量12360，三个F32各47752，SoftM2128。RAVU五纹理分别23040/59904/115200/746496/746496字节，均符合w×h×4×4，float32初始数据finite；rgba16f纹理初始数据不是float16，最初据此猜长度双倍的候选已撤回。

目录前置的8个名字全在NeutralOptions，ShaderSwitch按本场profile/default恢复，再应用目标链前置，glsl列表末项。CfL与放大器列表顺序由固定libplacebo各shader自己的保存时点支持，不由“CHROMA较晚”单独推导；SSSR的NATIVE_CROPPED尺寸也会受前序放大改变。1:1闭门不表示零中间纹理开销。

来源17份同字节，adaptive仅curve_height1.0→0.5。原15份最初取件commit没有入库，当前可证明本轮固定上游比对，不能倒写为历史原始版本。README/NOTICE/三份许可全文923行，与来源和唯一局部修改吻合；许可正文与固定SPDX v3.27.0核对。Shell csproj复制GLSL/HOOK/TXT，发布校验逐文件hash/额外拒绝；本轮没有执行发布或画面/GPU成本验收。
