using System.Xml;

namespace Momoka.Danmaku;

public sealed partial class BilibiliClient
{
    private async Task<IReadOnlyList<DanmakuComment>> ReadCommentsAsync(long cid, CancellationToken token)
    {
        if (cid <= 0) throw new DandanPlayException("B站弹幕编号无效。");
        var session = Session();
        using var nav = await ValidateIdentityAsync(session, token).ConfigureAwait(false);
        var bytes = await GetBytesAsync(new Uri("https://comment.bilibili.com/" + cid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".xml"), null, token).ConfigureAwait(false);
        using var memory = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(memory, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = MaximumResponseBytes,
        });
        var result = new List<DanmakuComment>();
        try
        {
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "i") throw new DandanPlayException("B站返回了无法识别的弹幕数据。");
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element || reader.Name != "d") continue;
                var parts = reader.GetAttribute("p")?.Split(',');
                using var entry = reader.ReadSubtree();
                entry.Read();
                var text = entry.ReadElementContentAsString();
                // B站XML p字段的第三项是字号，第四项才是十进制颜色。
                if (parts is { Length: >= 8 } && DanmakuComments.Parse(parts[0] + "," + parts[1] + "," + parts[3], text) is { } comment) result.Add(comment);
                if (result.Count == DanmakuComments.MaximumCount) break;
            }
        }
        catch (XmlException) { throw new DandanPlayException("B站返回的弹幕 XML 无法解析。"); }
        token.ThrowIfCancellationRequested();
        var sorted = result.OrderBy(comment => comment.Time).ToArray();
        token.ThrowIfCancellationRequested();
        return sorted;
    }
}
