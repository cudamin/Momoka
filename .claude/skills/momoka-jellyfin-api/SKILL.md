---
name: "momoka-jellyfin-api"
description: "Momoka's REST protocol layer for Jellyfin servers: base address without the /emby/ prefix, the Authorization: MediaBrowser header and which legacy auth forms Jellyfin 12 disables by default, product detection through GET /System/Info/Public, the route map from the current Emby calls to their Jellyfin 12 equivalents, and the version drift between Jellyfin 12 and the legacy routes older clients still use. Use when adding, reviewing or debugging Jellyfin server support in Core/Emby (EmbyServerAddress, DeviceIdentity, EmbyHttp, EmbyUrl, EmbyClient*), or investigating Jellyfin library notifications, ForceKeepAlive, Home refresh, or routes that work on Emby but fail on Jellyfin; Emby-only behavior belongs to momoka-emby-api, and feature semantics belong to the domain skills and docs/."
---

# Momoka — Jellyfin Server REST protocol layer

Rules live in [CLAUDE.md](../../../CLAUDE.md): authorization tiers, credentials, gates and delivery. This skill records only what is easy to get wrong about talking to a Jellyfin server; it does not define a second policy.

[momoka-emby-api](../momoka-emby-api/SKILL.md) owns the Emby side of the same terrain and should be read first. The transport, redirect, error-translation and download decisions in `EmbyHttp` are shared and product-neutral; this skill records where Jellyfin differs. Feature semantics belong to the domain skills and the feature documents under `docs/`.

## Implemented boundary

The native Jellyfin metadata form preserves unchanged list arrays, quotes entries containing list delimiters or quotes, and supports doubled quotes inside a quoted entry. The fixed v10.10.7 ItemUpdateController assigns Tags as an array and SqliteItemRepository stores them joined by `|`; commas are legitimate content. Metadata saves now use a button deferral: submit a cloned original document, close only on success, keep failed drafts and allow retries, and block closing while the request is pending. `Momoka.ServerSettingsReviewTests` links the actual dialog and extracts the current command/IO helpers with anonymous endpoints; this is not real WinUI or server-write evidence.

`src/Momoka.Core/Emby/` shares its transport and session code between Emby and Jellyfin. `MediaServerKind` and the server version live on `EmbyConnection` and the saved profile. `EmbyServerGateway.DetectAsync` identifies the product through anonymous public system information before authentication. Jellyfin refreshes the version before restoring saved browsing so an upgrade can select the correct route dialect.

Jellyfin uses a plain service base and `Authorization: MediaBrowser`; REST and playback share the header rule. Settings gives a confirmed administrator (`Policy.IsAdministrator`, not access to `/System/Info`) the five dashboard groups from `JellyfinServerSettingsMenu` in Momoka's own rail; 通知 has no Jellyfin counterpart and is never listed, and 网页控制台 survives only as the `--show-settings` alias for 控制台. A saved Emby-kind profile pointing at Jellyfin is corrected from public system information during login/restore. See [Jellyfin support](../../../docs/Jellyfin支持.md) for the supported boundary.

Jellyfin Web uses `web/index.html#/dashboard` and `jellyfin_credentials`. Server entries contain `Id`, `UserId`, `AccessToken` and lowercase `manualAddressOnly` directly, without Emby's per-server `Users` list. `EmbyWebConsole.Jellyfin.cs` isolates profiles by server and user and limits credential seeding to the trusted origin and Web path. Emby theme keys are not written to Jellyfin; `JellyfinNativeSkinScript` does the local WinUI-style theming in the WebView instead. The admin routes for 10.10.7 are `/dashboard`, `/dashboard/settings`, `/dashboard/users`, `/dashboard/libraries{,/display,/metadata,/nfo}`, `/dashboard/playback/{transcoding,resume,streaming,trickplay}`, `/dashboard/{devices,activity,livetv,recordings}`, `/dashboard/plugins{,/catalog}`, `/dashboard/{networking,keys,logs,tasks}`, and `/metadata` for the metadata manager. Plugin pages come from `web/ConfigurationPages?enableInMainMenu=true`, not Emby's three-parameter shape. Jellyfin Web's realtime socket is `/socket`, not `/embywebsocket`. In-document route switching uses the same `Emby.Page.show` shim.

The reference tables below record protocol differences, not live-server results. User-prefixed routes remain documented for older Jellyfin versions; inspect the 10.x/12.x branch before changing a path. The scratch contract patch is historical and is not a prerequisite.

**片头片尾不在章节里。** Jellyfin 的 `ChapterInfo` 没有 Emby 的 `MarkerType`；`Fields=Chapters` 只回无类型的断点。10.10 起可跳过内容由媒体段发布：`GET /MediaSegments/{itemId}` → `{"Items":[{"Type":"Intro","StartTicks":…,"EndTicks":…}]}`，`Type ∈ Intro/Outro/Recap/Preview/Commercial/Unknown`。`EmbyClient.GetItemAsync` 只在详情请求（`Fields` 点名 `Chapters`）且 `EmbyConnection.SupportsMediaSegments`（10.10+）时读它，折成 `EmbyItem.SkipMarkers`；404 按「这台服务器没有」处理，播放器的章节规则继续兜底。只有 `Intro`/`Outro` 变成跳过按钮，商业、回顾与预告不产生按钮。来源：Jellyfin master 的 `MediaSegmentsController` / `MediaSegmentDto` 与官方 Media segments 文档（2026-11-03 核对）。

## Four things that break on contact

### 1. Do not append `/emby/` to a Jellyfin base

The Emby branch of `EmbyServerAddress.TryNormalize` (`Core/Emby/EmbyServerAddress.cs`) strips a trailing `/emby` and appends `/emby/`. The Jellyfin branch preserves the supplied root or proxy subpath and appends only a slash. Jellyfin 12 removed the legacy product aliases; a proxy subpath literally named `/emby/` is still a user-supplied path and must not be silently removed.

Jellyfin documents its API at the server root — its OpenAPI declares `servers: [{ "url": "http://localhost" }]`. Use the plain base: root, or the reverse-proxy subpath. Keep the trailing slash, because relative resolution in `EmbyUrl.Combine` depends on it.

Do not "fix" this by special-casing `/emby/` at the request site and leaving the rest alone. The suffix is baked into `ToDisplayString` too, and the display string is what the user reads and what gets re-normalized on the next launch.

### 2. Auth moves to one `Authorization` header, and the old forms are off

Emby's spec registers `apikeyauth` (`?api_key=`, or `X-Emby-Token`) and `embyauth`; the Emby branch sends `X-Emby-Authorization` plus `X-Emby-Token`. Jellyfin's spec registers exactly one scheme — a header named `Authorization` — and Jellyfin 12 disables the legacy forms by default. `MediaServerHeaders` selects the shared REST/playback header representation.

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

| File | Product-specific responsibility |
| --- | --- |
| `EmbyServerAddress.cs` | The `/emby/` suffix — §1 |
| `DeviceIdentity.cs` | Header name and token placement — §2 |
| `EmbyHttp.cs` | Which headers each request carries (`SendAsync`, the `X-Emby-Authorization` line) |
| `EmbyResponses.cs` | `PublicSystemInfo.ProductName` distinguishes the products |
| `EmbyConnection` | Product identity and version, so `IsSameIdentityAs` still means "same server, same person" |
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

Extras are the documented old-version route exception: the fixed 10.10.7 controller already exposes top-level `Items/{itemId}/SpecialFeatures?UserId=`, while its Users alias is obsolete. Use that same inspected route, a bare DTO array and Web's `SpecialFeatureCount > 0` prerequisite. Keep this separate from AdditionalParts and generic browse route versioning. The [endpoint map](references/endpoints.md#extras-route-exception-2026-11-11) records fixed evidence and separate Core/actual-VM/native coverage.

Native local subtitle upload uses fixed 10.10.7/12.4 `POST Videos/{versionItemId}/Subtitles`, raw-byte Base64 and the five DTO fields. Require the current explicit `EnableSubtitleManagement` grant independently of downloading/admin; restore and source caches retain the grant. 204 confirms receipt, while readback/indexing can lag; unknown responses remain read-only. See [fourth-batch evidence](../../../docs/媒体库第四批-Jellyfin字幕上传.md), `jellyfin-subtitle-upload-fixture`, `Momoka.SubtitleDialogTests` and anonymous native `jellyfin-upload`. No real upload coverage is implied.

Subtitle management binds file-source `Id` to the version item's GUID for all three actions and readback. Do not use the parent item's route with a `MediaSourceId` query: the fixed 10.10.7/12.4 controllers ignore that unsupported query and select the route item. See [the endpoint notes](references/endpoints.md#subtitle-version-targets-2026-11-07). `subtitle-target-fixture` verifies both source orders, source/path/snapshot changes and the separate Emby source parameter through actual Core operations; no real subtitle write is covered.

- Route claims here are static: they come from an OpenAPI document and from Jellyfin's own source. They are not evidence that a request succeeds against a running server. Say which of the two you have.
- When adding or changing a path, fetch the target server's `/openapi.json` and compare by **structure**, not by literal grep — parameter names differ between products and between Jellyfin versions (`{itemId}` vs `{Id}`, `{routeItemId}`).
- Anything touching `EmbyHttp` headers, redirect handling, error classification or `EmbyUrl` has unit coverage; run the test project rather than only UI checks. Jellyfin transport, versioned routes and administration guards have offline tests; report real-server coverage separately.
- A live check against a Jellyfin server is a new authorization question, not a continuation of the Emby one. CLAUDE.md's three playback tiers and credential rules apply as written: a Jellyfin server is not the user's Emby library, and read-only is still the default.

## Restoring a legacy product label

A profile saved as Emby may really belong to Jellyfin. `EmbySession.ResolveRestoreConnectionAsync` probes the original profile address anonymously within the Emby/Jellyfin family, then validates the saved server ID and current user before adopting the corrected product, version, permissions and confirmed API base. Preserve real proxy paths ending in `/emby/`; `Rebase` must not infer which suffix was client-added. Without a confirmed alternative base, keep the address where authentication actually succeeded. Mismatched identity, 403 and 5xx do not clear credentials or trigger password fallback. Only 401 from the confirmed endpoint may use a saved password.

## Realtime

Append `socket` to the confirmed Jellyfin API base, preserving proxy prefixes. The handshake uses `Authorization: MediaBrowser` as REST does; never put the token in the URL or fall back to `api_key` for Jellyfin 12 with legacy authentication disabled. The shared `EmbyRealtimeConnection` rejects all redirects, reads the original source's latest connection at reconnect, and cancels when Home or that source exits.

`LibraryChanged` wakes Home's first-24-ID/order comparison; playback/session/user-data messages do not trigger a full media refresh. **Home has no periodic media-list polling and no extra HTTP read triggered solely by socket reconnection.** The verified 10.10.7 `recentlyAdded.ts` / `emby-itemscontainer.js` chain uses library-change events without a refresh interval. Official remote changes merge for 10 seconds; Momoka retains its existing single queue and fingerprint check, including pending changes during an active read. Entering Home and manual refresh still read; missed events have no replay guarantee.

`ForceKeepAlive` is an application message: on `{"MessageType":"ForceKeepAlive","Data":60}`, immediately send `{"MessageType":"KeepAlive"}` and repeat at half the supplied period. Frame Ping/Pong is not a substitute. `JellyfinRealtimeKeepAlive` maintains one send loop; a changed period wakes the wait without cancelling an in-flight write, and source/page/socket exit cancels it. Its accepted integer range 2..3600 seconds is a local resource limit, not the server's advertised compatibility range.

On 2026-11-10 the configured Jellyfin **10.10.7** accepted the header handshake with 101; 150 seconds of observation received ForceKeepAlive once and KeepAlive five times without remote disconnect. No real import or LibraryChanged was observed; this does not establish real import delivery or Jellyfin 12 compatibility. `realtime-handshake-fixture`, `HomePushRefreshTests` and websocket resource tests supply separate offline coverage. See [媒体库实时更新](../../../docs/媒体库实时更新.md) for fixed server/client source revisions and evidence.

## Automatic restore final identity

A saved profile's legacy Emby label may be corrected only by the existing anonymous original-address probe. Password fallback retains that corrected connection as the expected identity; before saving or adopting the authentication result, it compares product, API base, user ID and the server ID confirmed in this attempt. It must not compare against the old mislabeled profile or accept loss of a just-confirmed ID merely because the shared identity predicate tolerates missing IDs for legacy callers. Explicit sign-in remains a separate path; no Gateway-wide restriction was added.

`restore-identity-fixture` executes the actual Core session/gateway with anonymous HTTP scripts and isolated placeholder-only settings/protection IO. It covers valid Emby→Jellyfin correction, auth/public server mismatch, missing IDs and late cancellation/generation/address changes. API-base-only cases call the actual identity predicate. This is not a real Jellyfin/Emby login, DPAPI or WinUI restore run.

## Measured live playback

On 2026-10-26 Jellyfin 10.10.7 passed saved-token restore, library/detail reads, original Range access, built-in libmpv standalone playback and persisted resume. The server reported `DirectStream` without transcoding; start/progress/stop reporting had no failures. This does not validate Jellyfin 12, management writes, embedded Web login, all files or integrated/external playback. UIA reads are not visual evidence when the captured screen is blank. See [the live report](../../../docs/实服验证-飞牛与Jellyfin-2026-10-26.md).

## Delivery

When the supported boundary changes, update this skill, the "which product" boundary in `momoka-emby-api`, [docs/开发与验证.md](../../../docs/开发与验证.md), and the domain docs that declare which server they were written against. Keep this skill from drifting into a copy of the Jellyfin specification: link the version you measured and name the route you chose.

## Collection management permission and membership

Jellyfin collection operations allow `IsAdministrator` or `EnableCollectionManagement`; this maps to the dedicated `CanManageCollections`, independently of media deletion and server administration. The current source and capability snapshot own this grant; a late policy read must not overwrite a newer identity revision. Preserve unknown as unknown and reject a confirmed denial before writes. Emby instead follows its own item/admin predicate.

Concurrent permission probes also check their probe generation before source confirmation and cache publication. A newer same-value denied result need not change `PermissionRevision`, so source revision alone cannot reject an older allowed response.

`CollectionManagement` checks source ownership and current permission on every authenticated attempt. DELETE `Collections/{collectionId}/Items?Ids=...` only removes members. A `CollectionMembership` prepared for the current BoxSet page binds the collection, member and query lifetime; ordinary folders and actor/search pages cannot borrow it. Existing create/add remain POST. The `subtitle-target-fixture`, `library-resume-paging-fixture` and `Momoka.DetailDeleteCommandTests` cover anonymous protocol, actual VM context and actual command callbacks, not real Jellyfin changes or WinUI rendering.

The library section's `LibraryCollectionsAsync` / inspect `InspectLibraryCollections` uses a nonadministrator anonymous Jellyfin policy with collection management granted and media deletion denied. It exercises the actual card menu and success refresh, then denial/restoration; do not claim rendered coverage until its current-tree run and screenshots pass.
