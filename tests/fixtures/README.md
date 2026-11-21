# 商店预告离线素材

store-trailer.mp4由FFmpeg testsrc2与两条anullsrc生成，160×90 / 10fps / 2秒，H.264视频、两条无声音轨（eng/jpn），无服务器或账号数据，不含外部媒体。用于原生MediaPlayerElement解码、默认静音、音轨菜单及停止的隔离检查。字幕由探针在内存创建，不进入观看历史。

standalone-red.mp4 与 standalone-cyan.mp4 为 FFmpeg lavfi 生成的 640×360 / 30fps / 4秒 H.264 纯色视频，不含音轨，也不引用外部文件或网络。配合独占播放探针验证正式 libmpv 窗口、同窗换片、关闭、原生资源回收后重启；屏幕中心像素必须匹配红色或青色色块。

生成命令与实际样本记录见 artifacts/store-delivery，不依赖FFmpeg才能运行已提交的样本。
