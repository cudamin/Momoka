namespace Momoka.FnNas;

/// <summary>Only the authenticated FnNas subtitle dl route may receive a fresh request signature.</summary>
public sealed class FnNasSubtitleAuthorization
{
    private readonly Uri _apiBase;
    private readonly string _prefix;

    public FnNasSubtitleAuthorization(Uri apiBase)
    {
        FnNasEndpoint.Validate(apiBase);
        _apiBase = apiBase;
        _prefix = FnNasEndpoint.Api(apiBase, "api/v1/subtitle/dl/").AbsolutePath;
    }

    internal void Apply(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head
            || request.RequestUri is not { IsAbsoluteUri: true } uri || !FnNasEndpoint.SameOrigin(_apiBase, uri)
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Query.Length != 0
            || !uri.AbsolutePath.StartsWith(_prefix, StringComparison.Ordinal)) return;
        var encoded = uri.AbsolutePath[_prefix.Length..];
        try
        {
            if (FnNasEndpoint.Segment(Uri.UnescapeDataString(encoded)) != encoded) return;
        }
        catch (ArgumentException) { return; }
        // Created at the actual managed upstream request, including a reconnect or same-origin hop.
        request.Headers.TryAddWithoutValidation("authx", FnNasSigner.Create(request.Method, uri));
    }
}
