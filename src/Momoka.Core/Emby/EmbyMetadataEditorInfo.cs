namespace Momoka.Emby;

/// <summary>Emby supplies these item-specific choices; do not assume another product shares them.</summary>
public sealed class EmbyMetadataEditorInfo
{
    public List<EmbyMetadataRating> ParentalRatingOptions { get; set; } = [];
    public List<EmbyMetadataCountry> Countries { get; set; } = [];
    public List<EmbyMetadataCulture> Cultures { get; set; } = [];
    public List<ExternalIdInfo> ExternalIdInfos { get; set; } = [];
}

public sealed class EmbyMetadataRating
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
}

public sealed class EmbyMetadataCountry
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string TwoLetterISORegionName { get; set; } = "";
}

public sealed class EmbyMetadataCulture
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string TwoLetterISOLanguageName { get; set; } = "";
}

public static class EmbyMetadataLink
{
    /// <summary>Only escaped, credential-free HTTP(S) links can be handed to the browser.</summary>
    public static Uri? Create(ExternalIdInfo info, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(info.UrlFormatString)) return null;
        try
        {
            var url = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                info.UrlFormatString, Uri.EscapeDataString(value.Trim()));
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && uri.UserInfo.Length == 0 ? uri : null;
        }
        catch (FormatException) { return null; }
    }
}
