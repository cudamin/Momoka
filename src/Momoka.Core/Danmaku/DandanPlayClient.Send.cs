using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Momoka.Danmaku;

public sealed partial class DandanPlayClient
{
    public const int MaximumCommentLength = 100;
    private const string UnconfirmedSend = "发送结果未确认，可能已经送达；请勿立即重复发送。";

    public static double CommentTime(double position, double delay) =>
        double.IsFinite(position) && position >= 0 && double.IsFinite(delay) ? Math.Max(0, position - delay) : double.NaN;

    public static string ValidateComment(string text, double time)
    {
        text = text.Trim();
        if (text.Length is 0 or > MaximumCommentLength || text.Any(char.IsControl))
            throw new DandanPlayException("请输入 1～100 个字符的单行弹幕。");
        if (!double.IsFinite(time) || time is < 0 or > 86400)
            throw new DandanPlayException("当前播放时间不可用，请等待画面开始后再发送。");
        return text;
    }

    /// <summary>应用私有弹幕，白色滚动。POST 不跟随跳转、不自动重试；发送结果不确定时保留用户草稿。</summary>
    public async Task<long> SendCommentAsync(long episodeId, double time, string text, CancellationToken cancellationToken = default)
    {
        if (episodeId <= 0) throw new DandanPlayException("请先选择当前视频的弹幕库。");
        text = ValidateComment(text, time);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var entered = false;
        var dispatched = false;
        Connection? connection = null;
        try
        {
            await _requests.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            connection = await ResolveAsync(deadline.Token).ConfigureAwait(false);
            // 排队期间可能已关闭服务、换应用或切回内置凭据；不能再向旧连接发送。
            if (!IsCurrent(connection))
                throw new DandanPlayException("弹幕连接已改变，请重新选择弹幕库后发送。");
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(ApiOrigin + $"/api/v2/comment/{episodeId.ToString(CultureInfo.InvariantCulture)}/app"));
            request.Headers.Accept.ParseAdd("application/json");
            Sign(request, connection.AppId, connection.Secret);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            { time, mode = 1, color = 0xFFFFFF, comment = text, userName = "Momoka" }), Encoding.UTF8, "application/json");
            deadline.Token.ThrowIfCancellationRequested();
            dispatched = true;
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                if (connection.BuiltIn is not null
                    && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    _builtIn!.Invalidate();
                    throw new DandanPlayException("内置弹幕凭据已失效，已清除缓存；请再发送一次。");
                }
                throw new DandanPlayException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AuthenticationError(response),
                    HttpStatusCode.TooManyRequests => "弹弹play 发送额度或频率受限，请稍后再试。",
                    _ => UnconfirmedSend,
                });
            }
            byte[] bytes;
            try { bytes = await ReadBoundedAsync(response.Content, deadline.Token).ConfigureAwait(false); }
            catch (DandanPlayException) { throw new DandanPlayException(UnconfirmedSend); }
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new DandanPlayException(UnconfirmedSend);
            if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False
                || root.TryGetProperty("errorCode", out var errorCode) && errorCode.ValueKind == JsonValueKind.Number && errorCode.TryGetInt32(out var error) && error != 0)
                throw new DandanPlayException("弹弹play 未接受这条弹幕，请检查应用的发送额度和权限。");
            if (success.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("errorCode", out errorCode) || errorCode.ValueKind != JsonValueKind.Number || !errorCode.TryGetInt32(out error) || error != 0
                || !root.TryGetProperty("cid", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var cid) || cid <= 0)
                throw new DandanPlayException(UnconfirmedSend);
            return cid;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DandanPlayException(dispatched ? UnconfirmedSend : "发送等待超时，请稍后再试。");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException)
        {
            throw new DandanPlayException(dispatched ? UnconfirmedSend : "无法连接弹弹play，请检查网络。");
        }
        finally
        {
            // 成功或结果不明都有可能写入，不能继续保留发送之前的 GET 缓存；不额外消耗一次读取额度。
            if (dispatched && connection is not null)
                foreach (var related in new[] { false, true })
                    try { File.Delete(CachePath(connection.AppId, connection.Identity, CommentPath(episodeId, related))); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            if (entered) _requests.Release();
        }
    }

    private void Sign(HttpRequestMessage request, string appId, string secret)
    {
        var timestamp = _clock().ToUnixTimeSeconds();
        request.Headers.Add("X-AppId", appId);
        request.Headers.Add("X-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-Signature", Signature(appId, secret, timestamp, request.RequestUri!.AbsolutePath));
    }

    private static string CommentPath(long episodeId, bool related) =>
        $"/api/v2/comment/{episodeId.ToString(CultureInfo.InvariantCulture)}?withRelated={related.ToString().ToLowerInvariant()}&chConvert=0";

    private string CachePath(string appId, string protectedSecret, string path) => Path.Combine(_cacheDirectory,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(appId + "\n" + protectedSecret + "\n" + path))) + ".json");
}
