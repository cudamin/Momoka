using System.Text.Json;

namespace Momoka.MoviePilot;

public sealed record MoviePilotDownloadOptions(string? Downloader = null, string? SavePath = null);
public sealed record MoviePilotDownloadChoices(IReadOnlyList<string> Downloaders, IReadOnlyList<string> Paths);

public sealed partial class MoviePilotService
{
    /// <summary>目录与资源绑定到同一连接，只投影显示需要的名称和下载路径。</summary>
    public async Task<MoviePilotDownloadChoices> DownloadChoicesAsync(MoviePilotResource resource, CancellationToken cancellationToken)
    {
        var identity = RequireResultConnection(resource.ConnectionStamp);
        if (resource.ConnectionGeneration is { } generation && generation != identity.Generation)
            throw new MoviePilotOperationBlockedException("MoviePilot 配置曾发生变化，请重新搜索 AniBT 资源");
        var clients = CallAsync((address, token) => _client.GetAsync(address, token, "download/clients", cancellationToken), cancellationToken, identity);
        var paths = CallAsync((address, token) => _client.GetAsync(address, token, "download/paths", cancellationToken), cancellationToken, identity);
        await Task.WhenAll(clients, paths).ConfigureAwait(false);
        return new(Values(await clients.ConfigureAwait(false), "name"), Values(await paths.ConfigureAwait(false), "save_path"));
    }

    private static IReadOnlyList<string> Values(JsonElement data, string key) => data.ValueKind == JsonValueKind.Array
        ? data.EnumerateArray().Select(item => MoviePilotTransfer.Text(item, key))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray()
        : throw new MoviePilotException("MoviePilot 返回的下载选项格式不正确");
}
