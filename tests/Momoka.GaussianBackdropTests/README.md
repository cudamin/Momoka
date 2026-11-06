# 背景模式专项

`C:\Users\89400\.dotnet\dotnet.exe run --project tests/Momoka.GaussianBackdropTests/Momoka.GaussianBackdropTests.csproj -c Release`

真实Core像素卷积、配置默认/JSON/备份恢复。高斯实现核定义核对 SciPy v1.16.3 `scipy/ndimage/_filters.py` 的 `_gaussian_kernel1d(order=0)`，本实现明确采用3σ截断与nearest边界。1280宽sigma12用于柔化图上文字背后的细节，保留构图；默认旧裁切模糊继续使用原盒式算法。

`Momoka.PageContractTests` 以源链接实际BannerSlide与缓存保留双算法兼容、迟到和Release拒绝回填；首页消费入口固定原图，不启用全图高斯。`Momoka.ImagePerformanceTests -- perf-images` 覆盖实际WinRT解码与详情cache双算法隔离。高斯仅限电影、剧、季详情沉浸，非零档保留深色阅读保护；模式切换、五主题和纯白背景的实际像素及屏幕另由Shell探针验证。
