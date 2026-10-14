---
name: "momoka-jellyfin-api"
description: "Momoka's REST protocol layer for Jellyfin servers: base address without the /emby/ prefix, the Authorization: MediaBrowser header and which legacy auth forms Jellyfin 12 disables by default, product detection through GET /System/Info/Public, the route map from the current Emby calls to their Jellyfin 12 equivalents, and the version drift between Jellyfin 12 and the legacy routes older clients still use. Use when adding, reviewing or debugging Jellyfin server support in Core/Emby (EmbyServerAddress, DeviceIdentity, EmbyHttp, EmbyUrl, EmbyClient*), or when a route that works on Emby 404s or silently misbehaves on Jellyfin; Emby-only behavior belongs to momoka-emby-api, and feature semantics belong to the domain skills and docs/."
---

# Momoka — Jellyfin Server REST protocol layer

Rules live in [CLAUDE.md](../../../CLAUDE.md): authorization tiers, credentials, gates and delivery. This skill records only what is easy to get wrong about talking to a Jellyfin server; it does not define a second policy.

[momoka-emby-api](../momoka-emby-api/SKILL.md) owns the Emby side of the same terrain and should be read first. The transport, redirect, error-translation and download decisions in `EmbyHttp` are shared and product-neutral; this skill records where Jellyfin differs. Feature semantics belong to the domain skills and `docs/媒体库管理.md` / `docs/用户管理.md`.

## Current state: there is no Jellyfin code yet

`src/Momoka.Core/Emby/` is written against Emby and names Emby in its types. As of this writing the repository has no Jellyfin branch, no `MediaServerKind` and no product check — Jellyfin appears only in comments explaining which parameters must *not* be copied from it (`EmbyFilterBy`, `ItemMenu`, `EmbyClient`). Treat everything below as a specification reading, not as behaviour this client has been observed to perform.

So "make Momoka work with Jellyfin" does not start with a new client class. It starts with deciding how a connection learns which product it is talking to, and then making the four things in the next section product-aware. A draft contract for that — `MediaServerKind` on `EmbyConnection`, `ProductName` on `PublicSystemInfo` — sits in the gitignored `work/jellyfin/contract.patch`. It is a proposal, not landed code, and it is not a prerequisite for reading this skill.

## Four things that break on contact

### 1. The base address does not end in `/emby/`

`EmbyServerAddress.TryNormalize` (`Core/Emby/EmbyServerAddress.cs`) hard-codes the suffix: it strips a trailing `/emby` and appends `/emby/`, so every request URL is built under it. That is right for Emby and **wrong for Jellyfin 12**, whose release notes state that "support for the legacy `/emby/` and `/mediabrowser/` addresses has been removed".

Jellyfin documents its API at the server root — its OpenAPI declares `servers: [{ "url": "http://localhost" }]`. Use the plain base: root, or the reverse-proxy subpath. Keep the trailing slash, because relative resolution in `EmbyUrl.Combine` depends on it.

Do not "fix" this by special-casing `/emby/` at the request site and leaving the rest alone. The suffix is baked into `ToDisplayString` too, and the display string is what the user reads and what gets re-normalized on the next launch.

### 2. Auth moves to one `Authorization` header, and the old forms are off

Emby's spec registers `apikeyauth` (`?api_key=`, or `X-Emby-Token`) and `embyauth`; `EmbyHttp.SendAsync` sends `X-Emby-Authorization` plus `X-Emby-Token`. Jellyfin's spec registers exactly one scheme — a header named `Authorization` — and Jellyfin 12 disables the legacy forms by default.

The good news: `DeviceIdentity.ToAuthorizationHeader` already emits the Jellyfin scheme name. It returns `MediaBrowser Client="…", Device="…", DeviceId="…", Version="…"`. What changes is the header it goes under and where the token goes.

Two of the details are worth knowing before opening the reference: the token is an **attribute of the same header** (`Token="…"`), not a second header; and the attribute names are matched case-sensitively, so `DeviceId` cannot be spelled `deviceid`. The full gate list and the parsing rules are in [Transport and authentication](references/http-and-auth.md).

### 3. User-scoped routes lost their `userId` path segment

This is the largest difference and the dangerous one, because it produces wrong data rather than an error. Jellyfin 12.2.0 has no `/Users/{userId}/Items`, `/Users/{userId}/Views`, `/Users/{userId}/Items/Resume`, `/Users/{userId}/PlayedItems/{itemId}` or `/Users/{userId}/FavoriteItems/{itemId}`. The user comes from the token, and where a route still takes a user it takes `userId` as a **query** parameter.

| Momoka calls today | Jellyfin 12.2.0 |
| --- | --- |
| `GET /Users/{UserId}/Views` | `GET /UserViews?userId=` |
| `GET /Users/{UserId}/Items` | `GET /Items?userId=` |
| `GET /Users/{UserId}/Items/{Id}` | `GET /Items/{itemId}?userId=` |
| `GET /Users/{UserId}/Items/Resume` | `GET /UserItems/Resume?userId=` |
| `GET /Users/{UserId}/Items/Latest` | `GET /Items/Latest?userId=` |
| `POST` / `DELETE` `/Users/{UserId}/PlayedItems/{Id}` | `POST` / `DELETE` `/UserPlayedItems/{itemId}` |
| `POST` / `DELETE` `/Users/{UserId}/FavoriteItems/{Id}` | `POST` / `DELETE` `/UserFavoriteItems/{itemId}` |
| `GET` / `POST` `/Users/{Id}/Images/Primary` | `GET` / `POST` `/UserImage?userId=` |

The per-call table, including the routes with no documented Jellyfin equivalent, is in [Endpoint map](references/endpoints.md). Read it before changing any path.

### 4. "Not in the spec" cuts both ways

`momoka-emby-api` records a case where this client uses routes the *Emby* spec omits. Jellyfin is the mirror image, and it adds a policy: its release notes say that "if an endpoint is not listed in the OpenAPI specification it should not be used".

Both halves matter:

- **The legacy routes still answer.** A working third-party Jellyfin client in this repository's scratch tree (`work/moviepilot-protocol-20260924-M8nFhS/backend-v2/app/modules/jellyfin/jellyfin.py`) calls `/Users/{user}/Items`, `/Users/{user}/Items/Resume` and `/Users/{user}/Items/Latest` — the pre-12 shapes. A request built the Emby way can therefore return 200 and plausible data on some servers.
- **That is not a contract.** The same release notes list routes removed precisely because they had been obsolete for a full release cycle. A client leaning on undocumented aliases breaks on a server upgrade with no deprecation it can act on.

Prefer the documented route; when you cannot, record which server version you measured it against. `/Library/SelectableMediaFolders` is the live example: absent from the spec, used by that third-party client, so "answers, but unverified here".

## Terrain

| File | What a Jellyfin branch would touch |
| --- | --- |
| `EmbyServerAddress.cs` | The `/emby/` suffix — §1 |
| `DeviceIdentity.cs` | Header name and token placement — §2 |
| `EmbyHttp.cs` | Which headers each request carries (`SendAsync`, the `X-Emby-Authorization` line) |
| `EmbyResponses.cs` | `PublicSystemInfo` needs `ProductName` to tell the products apart |
| `EmbyConnection` | Where product identity would live, so `IsSameIdentityAs` still means "same server, same person" |
| `EmbyUrl.cs`, `EmbyClient*.cs` | Every path in §3 |

Error translation, redirect rules, timeouts, `.part` downloads and log redaction stay in `momoka-emby-api`; do not fork them.

## Getting the specification, and knowing which one you got

The server serves its own document, and the version is what matters:

```bash
curl --fail --show-error --silent "http://host:8096/openapi.json" -o work/jellyfin-openapi.json
```

`GET /System/Info/Public` answers without credentials and carries `ProductName`, `Version` and `Id` — the cheap way to ask "is this Jellyfin, and which one". `ProductName` is the assembly product name; the draft contract compares it to `"Jellyfin"` case-insensitively.

The snapshot behind this skill is `work/jellyfin/openapi.json`, byte-identical (sha256 `bd31bb194443d1c57616fa0a8521e099b17d096b92794e471c0e0725c50949c8`) to `https://api.jellyfin.org/openapi/jellyfin-openapi-stable.json`, `info.version` `12.2.0`, 294 paths. **That is an observation, not a constant.** Jellyfin 12.0 regenerated the document (Swashbuckle v10) and removed routes; re-fetch from the target server before trusting a route. Against a pre-12 server, expect the §3 table to be inverted: the `/Users/{userId}/…` shapes are the documented ones there.

## Verification

- Route claims here are static: they come from an OpenAPI document and from Jellyfin's own source. They are not evidence that a request succeeds against a running server. Say which of the two you have.
- When adding or changing a path, fetch the target server's `/openapi.json` and compare by **structure**, not by literal grep — parameter names differ between products and between Jellyfin versions (`{itemId}` vs `{Id}`, `{routeItemId}`).
- Anything touching `EmbyHttp` headers, redirect handling, error classification or `EmbyUrl` has unit coverage; run the test project rather than only UI checks. Jellyfin-specific transport behaviour has **no** coverage yet, so a new branch needs new tests, and until they exist the gap is real.
- A live check against a Jellyfin server is a new authorization question, not a continuation of the Emby one. CLAUDE.md's three playback tiers and credential rules apply as written: a Jellyfin server is not the user's Emby library, and read-only is still the default.

## Delivery

When Jellyfin support lands, update this skill, the "which product" boundary in `momoka-emby-api`, [docs/开发与验证.md](../../../docs/开发与验证.md), and the domain docs that declare which server they were written against. Keep this skill from drifting into a copy of the Jellyfin specification: link the version you measured and name the route you chose.
