namespace Momoka.Emby;

public sealed partial class EmbyClient
{
    public async Task<ItemsResult> FindByProviderAsync(string provider, string mediaId, bool series, CancellationToken cancellationToken)
    {
        if (Connection.IsJellyfin)
        {
            // Jellyfin has no AnyProviderIdEquals parameter. Never let an ignored filter label arbitrary media as a match.
            var matches = new List<EmbyItem>();
            const int pageSize = 200;
            for (var offset = 0; ; offset += pageSize)
            {
                var page = await GetItemsAsync(new ItemQuery
                {
                    Recursive = true,
                    IncludeItemTypes = [series ? EmbyItemType.Series : EmbyItemType.Movie],
                    Fields = EmbyFields.Detail,
                    StartIndex = offset,
                    Limit = pageSize
                }, cancellationToken).ConfigureAwait(false);
                matches.AddRange(page.Items.Where(item => item.ProviderIds.Any(pair =>
                    pair.Key.Equals(provider, StringComparison.OrdinalIgnoreCase) && pair.Value == mediaId)));
                if (page.Items.Count < pageSize || offset + page.Items.Count >= page.TotalRecordCount) break;
            }
            return new ItemsResult { Items = matches, TotalRecordCount = matches.Count };
        }
        var url = EmbyUrl.Combine(ApiBase, $"Users/{Connection.UserId}/Items",
            ("AnyProviderIdEquals", provider + "." + mediaId), ("Recursive", "true"),
            ("IncludeItemTypes", series ? EmbyItemType.Series : EmbyItemType.Movie),
            ("Fields", EmbyFields.Detail), ("Limit", "100"));
        return await http.GetJsonAsync<ItemsResult>(url, Context, cancellationToken).ConfigureAwait(false);
    }
}
