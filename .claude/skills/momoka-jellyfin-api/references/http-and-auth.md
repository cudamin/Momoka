# Transport and authentication against Jellyfin

This records the differences from Emby at the level `EmbyHttp` and `DeviceIdentity` work at. Read [momoka-emby-api/references/http-and-auth.md](../../momoka-emby-api/references/http-and-auth.md) for the shared decisions — redirects, error translation, timeouts, `.part` downloads, `Redact` — which are not repeated here and are not product-specific.

## Base address

Jellyfin documents its API at the server root. Its OpenAPI declares `servers: [{ "url": "http://localhost" }]`, and the paths begin at `/System/Info/Public`, `/Users/AuthenticateByName` and so on. There is no product segment to add.

The `/emby/` prefix was an alias for older clients and is gone in Jellyfin 12: the 12.0 release notes say "support for the legacy `/emby/` and `/mediabrowser/` addresses has been removed". `EmbyServerAddress.TryNormalize` appends exactly that suffix, so a Jellyfin connection built from it is aimed at a path the server no longer serves.

Practical shape for a Jellyfin base:

- Root (`http://host:8096/`) or a reverse-proxy subpath (`https://host/jellyfin/`).
- Keep the trailing slash — `EmbyUrl.Combine`'s relative resolution depends on it, and the Emby code path relies on the same property.
- `ToDisplayString` strips `/emby`; whatever replaces it must strip the Jellyfin equivalent (nothing, or the user's subpath) so the address shown in the UI re-normalizes to the same base.

Not measured here: whether a pre-12 Jellyfin still needs `/emby/`. Treat "use the plain base" as the documented choice for 12.x and verify against the target server rather than assuming either way.

## Product detection

`GET /System/Info/Public` is the anonymous probe. It declares no `security` in the specification, so it answers before login — which is what makes it usable for "what am I connected to" at the connection step rather than after it.

`PublicSystemInfo` in Jellyfin 12.2.0:

| Field | Notes |
| --- | --- |
| `ProductName` | The assembly product name. This is the discriminator; the draft contract compares it to `"Jellyfin"` with `OrdinalIgnoreCase`. |
| `ServerName`, `Version`, `Id` | Same names Momoka already reads. |
| `LocalAddress` | Present. |
| `OperatingSystem` | Present but marked deprecated in the specification. |
| `StartupWizardCompleted` | Present. |

Momoka's `PublicSystemInfo` includes `ProductName`; `DetectAsync` reads its value before sending credentials. Emby answers the same route with similar field names, so the discriminator is the value, not merely the presence of a field.

## The authorization header

Jellyfin's specification registers exactly one security scheme:

```json
"CustomAuthentication": { "type": "apiKey", "name": "Authorization", "in": "header" }
```

The header value is the MediaBrowser authorization string. `DeviceIdentity.ToAuthorizationHeader` already produces it:

```
Authorization: MediaBrowser Client="Momoka", Device="<machine>", DeviceId="<stable id>", Version="<version>", Token="<access token>"
```

The parsing lives in Jellyfin's `Jellyfin.Server.Implementations/Security/AuthorizationContext.cs`. Rules that matter when writing the header:

- **Scheme name.** The first space-separated word must be `MediaBrowser` (compared `OrdinalIgnoreCase`). `Emby` as the scheme name is accepted only while legacy authorization is on.
- **Attribute names are matched exactly.** The parser builds a `Dictionary<string,string>` keyed by whatever text precedes `=` in the header, then looks up `DeviceId`, `Device`, `Client`, `Version` and `Token`. The default comparer is case-sensitive, so `deviceid="…"` parses but is never read. `DeviceIdentity` already emits the right spellings.
- **Values are URL-decoded.** The parser runs `WebUtility.UrlDecode` over each value after trimming surrounding quotes. A value containing a comma must be quoted, because a comma outside quotes ends the pair. Momoka's `Sanitize` instead *strips* `"`, `\`, `,` and `;`, which is safe (it cannot corrupt the header) but lossy — a machine name with a comma silently loses part of itself. If Jellyfin support is added, decide deliberately between stripping and percent-encoding rather than inheriting the Emby comment.
- **The token rides in the same header.** There is no second header. `Token="…"` is just another attribute, and omitting it is how a request is sent anonymously.

## What Jellyfin 12 disables, and what it does not

`EnableLegacyAuthorization` gates four fallbacks, and a server migration (`20260531160000_DisableLegacyAuthorization`, in `Jellyfin.Server/Migrations/Routines/`) sets it to `false` on upgrade. The 12.0 release notes describe the same change as "the deprecated way of signing in is now disabled, including on existing servers".

| Form | Status on Jellyfin 12 |
| --- | --- |
| `Authorization: MediaBrowser …` (with `Token=`) | Supported |
| `X-Emby-Authorization: MediaBrowser …` | Read only when `EnableLegacyAuthorization` |
| `X-Emby-Token: …` | Read only when `EnableLegacyAuthorization` |
| `X-MediaBrowser-Token: …` | Read only when `EnableLegacyAuthorization` |
| `?api_key=…` | Read only when `EnableLegacyAuthorization` |
| `?ApiKey=…` | Read unconditionally |
| Scheme name `Emby` instead of `MediaBrowser` | Accepted only when `EnableLegacyAuthorization` |

The `?ApiKey=` row is the surprise: the capitalised query key is not behind the gate in the source as read here. Do not treat that as an invitation — putting a token in a URL is exactly what CLAUDE.md's credential rules forbid, and it would leak through `Redact`-adjacent logging paths and into any URL the user can copy. It is recorded so that a future reader who finds it in a working client knows why it still functions.

`EmbyHttp.SendAsync` and playback use `MediaServerHeaders`. The Emby branch sends `X-Emby-Authorization` and `X-Emby-Token`; the Jellyfin branch sends `Authorization`. Sending only the Emby pair to default Jellyfin 12 would be treated as anonymous: public-info success cannot establish that later authenticated requests will work.

## JSON shape

Jellyfin offers three content types for the same body: `application/json`, `application/json; profile="CamelCase"` and `application/json; profile="PascalCase"`. The profile form is Jellyfin-specific and exists so clients can ask for a fixed casing.

`EmbyHttp.Json` already sets `PropertyNameCaseInsensitive`, so the default `application/json` works without asking for a profile. Adding `Accept: application/json` stays correct; there is no need to request a profile unless a specific field proves casing-sensitive.

`AllowReadingFromString` stays relevant for the same reason it is on Emby: numeric fields arriving as strings.

## Evidence status

- Specification facts: `work/jellyfin/openapi.json`, sha256 `bd31bb194443d1c57616fa0a8521e099b17d096b92794e471c0e0725c50949c8`, identical to `https://api.jellyfin.org/openapi/jellyfin-openapi-stable.json`, `info.version` `12.2.0`.
- Parser and gate behaviour: Jellyfin source at `Jellyfin.Server.Implementations/Security/AuthorizationContext.cs` and `Jellyfin.Server/Migrations/Routines/20260531160000_DisableLegacyAuthorization.cs`, as read from the project's `master` branch.
- Release-notes statements: `https://jellyfin.org/posts/jellyfin-release-12.0/` (2026-09-07).
- **Not** verified: no request in this list has been sent to a running Jellyfin server from this repository.
