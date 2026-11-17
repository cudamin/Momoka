---
name: "momoka-plex-api"
description: "Develop or review Momoka's Plex backend: the Core/Plex transport, Plex-account sign-in through plex.tv, library browsing, media mapping and playback reporting. Use when changing PlexEndpoint, PlexHttp, PlexClient, PlexMediaAdapter, PlexPlaybackSelection or PlexRealtimeProtocol, when diagnosing a Plex address, token, redirect, browse or playback problem, or when deciding which features Plex hides. Shared session and playback lifetimes belong to momoka-winui-shell and momoka-playback."
---

# Momoka — Plex protocol layer

Project rules live in [CLAUDE.md](../../../CLAUDE.md). This skill records only what is easy to get wrong about talking to a Plex Media Server. It defines no second policy.

**Nothing here has been checked against a live Plex server or a live plex.tv account.** The suites (`PlexProtocolTests`, `PlexMediaTests`, `PlexV2LoginTests`) drive fake transports, so their pass proves Momoka's behavior on the supplied answers, not that a service returns them. For v2 account sign-in, the [archived official static evidence](references/v2-login-official-evidence.json) separately records the official form's bare-object response consumption and the desktop UserModel's field types. The remaining routes reflect Momoka's current public-interface choices; say "protocol-level, offline-verified", never "works on Plex". The live-coverage boundary is written out in [docs/Plex支持.md](../../../docs/Plex支持.md).

## Implemented boundary

`src/Momoka.Core/Plex/`:

| File | Responsibility |
| --- | --- |
| `PlexEndpoint` | Address normalisation, the default port, the `/:/` special-endpoint rule, `RelativeKey` |
| `PlexHttp` | The sole HTTP boundary, including fixed account sign-in and conditional permission-profile reads |
| `PlexClient` | Paths, field names, paging headers, the timeline/scrobble verbs |
| `PlexMediaAdapter` + `.Models.cs` | Plex JSON → `EmbyItem` / `MediaSource` / `MediaStream` / `ItemsResult` |
| `PlexPlaybackSelection` | The metadata key and whole-version duration a playback report needs |

Session, DI, `EmbySessionScope` and capability-dependent visibility are shared and owned by [momoka-winui-shell](../momoka-winui-shell/SKILL.md). Playback planning, mpv options and progress reporting are owned by [momoka-playback](../momoka-playback/SKILL.md). `MediaServerKind.Plex` is a branch inside `EmbyClient`, `EmbySession`, `EmbyServerGateway`, `EmbyServerAddress` and `MediaServerHeaders` — there is no per-backend factory in DI, and adding one would be a redesign, not a Plex change.

## Address normalization

`PlexEndpoint.TryNormalize` determines whether a port was explicitly written from the original authority, before `/`, `\`, `?` or `#`. Only a colon after an IPv6 literal's closing bracket denotes its port. `Uri.Authority` drops explicit scheme-default ports, so it cannot answer that question; let `Uri` validate and supply the numeric port instead. Pass `Uri.Host` unchanged to `UriBuilder` rather than wrapping an IPv6 host in another pair of brackets.

A plain HTTP host without a port uses 32400; an explicit HTTP port, including 80, wins. HTTPS without a port keeps 443. Reverse-proxy paths survive, browser-only `/web` or `/web/index.html` suffixes are stripped, and userinfo is rejected. `PlexEndpoint.ToDisplayString` retains HTTP `:80` for a stable normalize → display → normalize round trip. `PlexProtocolTests.Endpoint` exercises these pure functions with placeholder DNS, IPv4 and IPv6 addresses, without a transport or account exchange. Shared profile-display callers in `EmbyServerAddress` delegate the Plex branch to this function, retaining HTTP `:80` there too. The endpoint suite tests both direct and shared display round trips.

## Four things that break on contact

### 1. A Plex server does not verify an account password

Sign-in is two steps and both are required. `POST https://plex.tv/api/v2/users/signin` exchanges the account credentials for an **account** token; `GET {server}` with `X-Plex-Token` then proves the account has been shared *this* server. A login that stopped after the first step looks successful and then 401s on every browse — `PlexMediaAdapter.SignInAsync` throws `EmbyAuthenticationException` for that case rather than adopting a token that cannot be used.

The password goes in the JSON body, never in the query string. Account sign-in and the conditional nonAnonymousAccount profile read use fixed paths inside `PlexHttp` with `AccountAuthority`, so no caller can point either elsewhere; foreign redirects remain rejected.

**R-005 response contract:** the official v2 password form passes the JSON success object through and its completion consumes top-level `id` and `authToken`. The official desktop UserModel declares integer `id` and string `authToken`, `username` and `title`. Read one bare account object; do not join its token to an unproven nested `user` identity or reuse the PMS XML-string coercion helpers for credentials. No official evidence here establishes string-valued account ids, legacy nested signin envelopes or a PIN/2FA flow. See [the source manifest](references/v2-login-official-evidence.json) for URLs, versions, hashes and exact marker offsets.

The existing Momoka request body is JSON; the official form uses jQuery form data. Static response-contract evidence and fake HTTP cannot prove the real service accepts Momoka's Content-Type. Keep JSON-body acceptance, live sign-in, target-PMS permissions, two-factor verification and PIN/link flows explicitly unverified; do not broaden this response-parser fix into speculative transport compatibility.

### 2. The token lives in `X-Plex-Token` and nowhere else

Same rule as the Emby token, for the same reason. `PlexHttp` adds it as a request header; `MediaServerHeaders.Create` returns it for mpv's `--http-header-fields`; `PlexEndpoint` never builds a URL carrying it. A media address that arrives with its own query, fragment or userinfo is refused by `PlexPlaybackSelection.MediaUrl` rather than handed to mpv.

`/identity` is the one endpoint that answers without a token — that is what makes it usable for the automatic product probe. Do not "simplify" detection into `GET /`; that one needs the token and would turn detection into a login.

### 3. Numbers and booleans arrive as strings

Plex's JSON is a rendering of its XML, so `"viewCount": "0"` and `"default": "1"` are normal alongside real numbers. `PlexMediaAdapter.Text` reads through `JsonNode.ToString()`, and `Number` / `Integer` / `Flag` all go through it, so both spellings work. A typed `JsonSerializer` model over these fields would break on whichever spelling the server happened to send.

The same applies to the response envelope: everything the **server** answers is wrapped in `MediaContainer`, and `PlexClient.RootAsync` unwraps it. The **account service** answers with a bare object — wrapping that one in `MediaContainer` in a test fixture is an easy way to get a null `authToken` and a confusing failure.

### 4. Paging is a header, and the item array is named after its contents

Plex pages with `X-Plex-Container-Start` / `X-Plex-Container-Size` **headers**, not query keys, which is why `PlexClient` owns the paging mechanism and `PlexMediaAdapter.Query` deliberately does not produce those two.

The shared gateway continues to the separate Plex default-port candidate after an unreachable bare HTTP endpoint. Explicit ports and HTTPS keep their origin, cancellation stops the sequence, and an unsuccessful sequence retains the original unreachable result. `Momoka.ServerSettingsReviewTests` exercises the actual gateway with anonymous transport; it does not establish a live server response.

Typed searches scan the existing `/search` endpoint's raw pages in batches of 100, then expose filtered offsets and totals. This deliberately avoids assuming unsupported server-side type filtering or changing shared cursors. Every filtered page rescans the raw result set, so very broad searches cost more requests; there is no persistent search cache. Cancellation or a raw-page failure aborts the filtered page, and `Limit` caps returned matches rather than raw consumption. Anonymous regressions cover single/aggregate cursors, empty-match raw pages, duplicate prevention, cancellation and later-page failure.

Show-scoped next-up scans the existing `/library/onDeck` raw pages until it has enough matching episodes or reaches the end. Advance by raw item count, retain server order, and use `totalSize` when available; without a total, a short/empty page ends the scan. A later failure or cancellation propagates rather than returning an incomplete empty or partial result. This may require more requests for a show far down the list. Anonymous paging and `ResumeRow` regressions do not establish live PMS ordering or WinUI replacement behavior.

A container's array is named for what is inside it: `Metadata` for films and episodes, `Directory` for shows, seasons and library sections, `Video` for some episode lists. `PlexMediaAdapter.Items` reads all three. Reading only `Metadata` is the classic mistake — it silently returns an empty show library.

Library sections are not media entries: `Library` assigns the section's `key` to `EmbyItem.Id` and throws `InvalidDataException` when that key is missing, null or blank, rather than returning a clickable empty-ID library. Do not fall back to `ratingKey` for a section or change `Item` to read `key` globally: films, shows and seasons still use their media `ratingKey`. `PlexMediaTests.Libraries` feeds `GetViews` results directly into browse, per-library recently-added and random-carousel requests, and checks distinct section IDs and source-scoped navigation keys without signing in.

## Library notifications

Plex uses `/:/websockets/notifications` under the confirmed server/proxy base. Send `X-Plex-Token` and `X-Plex-Client-Identifier` in headers; the official browser's query-string token is not copied. Receive notifications after connecting without sending a subscription command. `PlexRealtimeProtocol` parses events and the shared `EmbyRealtimeConnection` owns socket lifetime, rejected redirects, latest-identity reconnect and 1/2/4/8/16/30-second backoff.

- `NotificationContainer.type=timeline`: consider `TimelineEntry` only for `identifier=com.plexapp.plugins.library`, positive `itemID/sectionID` and numeric integer state 0/5/9. State 5 is metadata processed, not proof that a new import completed.
- `type=status`: `StatusNotification.notificationName=LIBRARY_UPDATE` also wakes the check. Preserve the official older `_elementType` / `_children` envelope support; unrelated playback/progress/DVR messages do not qualify.
- Home checks the first 24 IDs/order before refreshing shelves and carousel. It has no generic 30-second media polling, no reconnect-triggered HTTP catch-up and no guaranteed replay of missed events; page entry/manual reads remain.
- Official Plex Web **4.163.0 / 8246fd2** uses notifications to invalidate Home hubs. Its 10-second scan polling belongs only to `DirectoryHubsPageContent`, followed by a scan-end refresh. Momoka exposes Plex `Items`, not that recommended-hubs page; do not move its timer to Home or add activities/hubs solely as a fallback.

The fixed source/byte evidence and local filtering differences are in [媒体库实时更新](../../../docs/媒体库实时更新.md). Plex was **protocol-tested only**: there is no configured real Plex source, live handshake or real import-delivery result. `EmbyRealtimeTests.Push.cs`, `realtime-handshake-fixture` and Home/resource tests verify anonymous messages, headers and lifecycle; official source and these tests do not establish live PMS availability.

## Terrain

| Concern | Where it lives |
| --- | --- |
| Default port, `/web` stripping, `/:/` endpoints, part keys | `PlexEndpoint` |
| Header set, redirect and origin rules, error translation | `PlexHttp`, `PlexApiException` |
| Every path and query key | `PlexClient` |
| Item, source, track and artwork mapping | `PlexMediaAdapter.Models.cs` |
| Which operations are refused | `PlexMediaAdapter` (`NotSupportedException`), `ItemMenu.Supports(..., plex: true)`, `SettingsViewModel.AdministrationCategories` |

Routes and parameters, including what this client does not call, are in [endpoints.md](references/endpoints.md). Header, redirect, credential and error rules are in [http-and-auth.md](references/http-and-auth.md).

## Card and detail affordances

Library roots read the PMS section's actual Type/Filter/Sort catalog with `includeDetails=1&includeAdvanced=1`. `PlexLibraryCatalog` and `PlexFilterSelection` keep native values separate from Emby conditions; supported fields are genre/year/watched/resolution and declared media types. Sort uses the returned key/descKey; alphabet navigation uses firstCharacter buckets with the same conditions. Empty catalogs hide unavailable controls, and an unavailable saved condition must not widen a query. `LibraryViewModel.Plex` owns visit/type/selection lifetimes. This remains offline protocol coverage without live PMS verification.

Media and stream `bitrate` values are Plex kbps. `PlexMediaAdapter.Bitrate` keeps the existing integer parsing, multiplies using a wide intermediate, and returns null when the shared bit/s integer cannot represent the result. Do not change shared display units to compensate for Plex. Show/season watched state uses valid integral `leafCount` and `viewedLeafCount`; empty, missing or contradictory counts do not establish fully watched state. `UnplayedItemCount` is available only for valid counts. Movie/episode still use `viewCount`. The fixed official Web 4.163.0 / 8246fd2 source and offline regression record are preserved in the fourth repair commit history; no live PMS or rendered UI verification is implied.

**详情请求带 `includeChapters=1&includeMarkers=1`**（`PlexClient.GetMetadataAsync`）；不点名，服务器就什么都不发。`Chapter` 折成 `EmbyItem.Chapters`（`startTimeOffset` 是毫秒，只有名字和起点，没有章节图）；`Marker` 的 `type=intro|credits` 折成 `EmbyItem.SkipMarkers`（两端都有，`commercial` 丢掉）。一部片可能有先后两条 `credits`（片中彩蛋前、片尾名单前），两条都留，播到哪条按哪条跳。Plex 的 JSON 是 XML 折过来的：重复元素是数组，单条可能是对象，读章节与 marker 时两种都要认（`PlexMediaAdapter.Elements`）。缺章节与标记时，跳过的服务端一半为空，播放器按 mpv 章节兜底。

Favorites are a separate capability from watched state. `CardItem.CanFavorite` and `DetailViewModel.FavoriteActionVisibility` use the item’s captured scope and `SupportsFavorites`; they must not hide Plex scrobble/unscrobble actions with a shared UserState flag. The direct old command paths reject favorites before HTTP, state ordering, DTO updates or notices. The detail command also retains activity/Busy, seed ID and detail-source consistency during reattachment.

`SetWatchedAsync` confirms only its original successful HTTP write and returns null; no complete UserData is fabricated. `GetPlexUserDataAsync` reads metadata separately, so a read-side 401 can reauthenticate and retry only the read. Card commands reuse `RefreshConfirmed` to enrich progress/counts without undoing the acknowledged bit. Detail retains that bit before its independent reload and reports read failure as saved-but-refresh-failed. This does not change successful-response body IO, Session-wide retry policy, detail-wide field ordering or ResumeSlot's read-failure consumption. Anonymous regressions and real VM execution do not establish live PMS behavior.

Plex person and genre links use `PlexBrowseRelation` with the metadata's local `librarySectionID`, numeric movie/show type and native tag id. `Role`, `Director` and `Writer` map readable credits; a credit without identity remains static content. Local navigation uses the section `/all` query with `actor/director/writer/genre=id`; `/library/people` in official module 3632 targets `myPlex` cloud metadata and is not a PMS route. Genre links are evaluated individually: missing ids stay text, and stale detail events cannot borrow a new scope. Cross-library navigation remains unimplemented. `DetailPage` repaints after capability changes and reentry. Fixed source offsets, intended differences and offline evidence are recorded in this skill. `Momoka.PlexAffordanceTests` executes current Shell detail VM, actual cards and extracted page paint/events with fake HTTP and inert framework values; this is not rendered WinUI, UIA or live PMS evidence.

## Verification

### Ordinary video playlists and collections

`PlexClient.Containers` owns distinct video playlist and per-section collection directories, native membership, and explicit permissions from the local library provider's `/media/providers` Feature catalog. A playlist is editable only with explicit `smart=false`, `readOnly=false`, and a usable playlist feature; collections additionally require collection + manage features and same-section movie/show metadata. `nonAnonymousAccount` is checked through `PlexHttp.GetAccountProfileAsync` at fixed `https://plex.tv/api/v2/user`, requiring strict numeric matching id and explicit `anonymous=false`; a saved AccountId is not proof of this flag. Other unknown account `requires` stays unavailable. Failed/malformed profile checks leave VerificationError and the affected actions read-only without concealing PMS provider failure. The missing-flag restriction deliberately narrows the official web's `!readOnly` branch rather than inventing ownership.

Keep `playlistItemID` separate from media ratingKey, including duplicate entries. Removal and `after` ordering use that entry identity; collections remove a ratingKey from their children route and retain disk media. Current collections expose create/add/remove, not custom ordering. Creation needs initial media selection; no empty-URI support was established. `PlexPlaylistType` routes video lists separately from the existing shared music browser.

`PlexContainerPage.Consumed` advances through raw entries even when local smart filtering removes rows; HasMore handles missing totalSize without truncating a full page. Every write rechecks current provider/container/member state and invokes the optional beforeWrite guard after its asynchronous preflight reads, so the caller can reject a changed permission revision immediately before sending. `PlexHttp.WriteAsync` confirms HTTP 2xx headers through the optional callback before reading receipt metadata; no usable creation identity is still acknowledged success. `PlexWriteUncertainException` is not a retryable token failure. Session callers keep confirmed state through late cancellation and refresh separately. Fixed byte/source positions, intentional differences and offline scope are recorded in this skill; no real PMS or rendered UI coverage is claimed.

### Multipart versions

One `Media` is one selectable version. Its ordered `Part` files become `MediaSource.MediaParts` and `PlaybackRequest.MediaParts`, then `MpvMediaResource.Build` constructs one EDL timeline after every URL is mapped by the transport. Never expand Parts into versions or substitute the first Part for an incomplete timeline. Reject a version with missing/nonpositive/nonfinite/overflowing Part durations or incompatible internal stream layout; preserve its other usable Media versions. Multipart server sidecar subtitles are hidden because they need a parallel timeline; keep embedded tracks and single-Part sidecars. A stale explicit hidden track selection is refused.

Fixed mpv `270367e8af` source: `demux_edl.c` parses UTF-8 byte-length quoting, uses named `length` with the `start` parameter absent, chooses `layout=this` from the first Part and accumulates segment time. Unknown native lengths can become one second, so do not rely on that fallback. `demux_timeline.c` rebases timestamps and continues at internal EOF; only the final segment yields whole-file EOF. EDL needs `access-references` enabled. The offline probe accepts only the approved DLL and self-generated PCM, with null outputs and no configuration, scripts or automatic sidecars; it does not prove backend/window/server behavior.

Fixed official `plexinc/plex-for-kodi` `97191f248a20223bb93296789c010ff2204a9c76`, `lib/_included_packages/plexnet/nowplayingmanager.py:171-187`, reports `timeline.item.key`, not `Part.key`; `lib/player.py:174-186` uses current playback plus the Part offset. `PlexPlaybackSelection.MetadataKey` is the item metadata path; report position and duration stay on the whole-version clock. Older part-key wording was incorrect. See [Plex支持](../../../docs/Plex支持.md) and `PlexMultipartTests` for boundaries and repeatable fixtures.

- `PlexProtocolTests` covers address normalisation, escape guards, token placement, redirects, error translation, the account exchange, paging headers and query building.
- `PlexMediaTests` covers sign-in, re-authentication after a 401, restore without a second account login, library filtering, search, detail mapping, seasons/episodes, next-up, playback planning, reporting and the menu — all through `EmbySession` against a fake transport.
- `PlexV2LoginTests.Register()` is called by the existing `PlexMediaTests.Register()`; no shared runner edit is needed for the full Core suite. The dedicated `plex-login-fixture` driver should call `PlexV2LoginTests.Register();` followed by `TestHarness.Run()` once. It exercises the current `EmbySession`, gateway and Core/Plex path with a synthetic handler; no real socket is opened. Persistence is isolated under the current worktree's `artifacts/staged-repair/batch07/r005/fixtures`.
- R-005 coverage includes the top-level account-to-PMS handshake, no adoption/persistence before PMS approval, 401/403, missing/wrong-type identity/token, profile-name ownership, unproven nested/string-id rejection, token headers and redirect/cancellation, reauthentication identity changes, and saved-token/password restore. Existing media/protocol account fixtures use the same bare v2 shape, not a wrapper that only makes tests green.
- All suites are offline. They say what this client sends and how it reads the answers it is handed. Official static code corroborates the v2 response contract and the notification behavior described above; neither source reading nor fake HTTP is a live-account, PMS, DPAPI, playback, 2FA or PIN pass.
- A live check is a new authorization question under CLAUDE.md's three tiers, and a Plex server is not the user's Emby library. Read-only first: address → 「连接」 → confirm the product and server name → then sign in. Record the server version and the observed response shapes in [docs/Plex支持.md](../../../docs/Plex支持.md).
- Anything touching `PlexHttp` headers, redirect handling, error classification or `PlexEndpoint` needs the test project run, not only UI checks. No live-server coverage exists; report that gap as a gap.

## Delivery

When the supported boundary changes, update this skill, [docs/Plex支持.md](../../../docs/Plex支持.md), `README.md`'s supported-server list, and the CLAUDE.md skill index if the skill is added or renamed. Keep this skill from drifting into a copy of Plex's API documentation: name the route you chose and say which of the two kinds of evidence you have.
