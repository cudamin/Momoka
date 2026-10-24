---
name: "momoka-plex-api"
description: "Develop or review Momoka's Plex backend: the Core/Plex transport, Plex-account sign-in through plex.tv, library browsing, media mapping and playback reporting. Use when changing PlexEndpoint, PlexHttp, PlexClient, PlexMediaAdapter or PlexPlaybackSelection, when diagnosing a Plex address, token, redirect, browse or playback problem, or when deciding which features Plex hides. Shared session and playback lifetimes belong to momoka-winui-shell and momoka-playback."
---

# Momoka — Plex protocol layer

Project rules live in [CLAUDE.md](../../../CLAUDE.md). This skill records only what is easy to get wrong about talking to a Plex Media Server. It defines no second policy.

**Nothing here has been checked against a live Plex server or a live plex.tv account.** The suites (`PlexProtocolTests`, `PlexMediaTests`, `PlexV2LoginTests`) drive fake transports, so their pass proves Momoka's behavior on the supplied answers, not that a service returns them. For v2 account sign-in, the [archived official static evidence](references/v2-login-official-evidence.json) separately records the official form's bare-object response consumption and the desktop UserModel's field types. The remaining routes reflect Momoka's current public-interface choices; say "protocol-level, offline-verified", never "works on Plex". The live-coverage boundary is written out in [docs/Plex支持.md](../../../docs/Plex支持.md).

## Implemented boundary

`src/Momoka.Core/Plex/`:

| File | Responsibility |
| --- | --- |
| `PlexEndpoint` | Address normalisation, the default port, the `/:/` special-endpoint rule, `RelativeKey` |
| `PlexHttp` | The sole HTTP boundary, including the one request that leaves the server's origin |
| `PlexClient` | Paths, field names, paging headers, the timeline/scrobble verbs |
| `PlexMediaAdapter` + `.Models.cs` | Plex JSON → `EmbyItem` / `MediaSource` / `MediaStream` / `ItemsResult` |
| `PlexPlaybackSelection` | The part key and duration a playback report needs |

Session, DI, `EmbySessionScope` and capability-dependent visibility are shared and owned by [momoka-winui-shell](../momoka-winui-shell/SKILL.md). Playback planning, mpv options and progress reporting are owned by [momoka-playback](../momoka-playback/SKILL.md). `MediaServerKind.Plex` is a branch inside `EmbyClient`, `EmbySession`, `EmbyServerGateway`, `EmbyServerAddress` and `MediaServerHeaders` — there is no per-backend factory in DI, and adding one would be a redesign, not a Plex change.

## Address normalization

`PlexEndpoint.TryNormalize` determines whether a port was explicitly written from the original authority, before `/`, `\`, `?` or `#`. Only a colon after an IPv6 literal's closing bracket denotes its port. `Uri.Authority` drops explicit scheme-default ports, so it cannot answer that question; let `Uri` validate and supply the numeric port instead. Pass `Uri.Host` unchanged to `UriBuilder` rather than wrapping an IPv6 host in another pair of brackets.

A plain HTTP host without a port uses 32400; an explicit HTTP port, including 80, wins. HTTPS without a port keeps 443. Reverse-proxy paths survive, browser-only `/web` or `/web/index.html` suffixes are stripped, and userinfo is rejected. `PlexEndpoint.ToDisplayString` retains HTTP `:80` for a stable normalize → display → normalize round trip. `PlexProtocolTests.Endpoint` exercises these pure functions with placeholder DNS, IPv4 and IPv6 addresses, without a transport or account exchange. Shared profile-display callers in `EmbyServerAddress` delegate the Plex branch to this function, retaining HTTP `:80` there too. The endpoint suite tests both direct and shared display round trips.

## Four things that break on contact

### 1. A Plex server does not verify an account password

Sign-in is two steps and both are required. `POST https://plex.tv/api/v2/users/signin` exchanges the account credentials for an **account** token; `GET {server}` with `X-Plex-Token` then proves the account has been shared *this* server. A login that stopped after the first step looks successful and then 401s on every browse — `PlexMediaAdapter.SignInAsync` throws `EmbyAuthenticationException` for that case rather than adopting a token that cannot be used.

The password goes in the JSON body, never in the query string. The account URI is built inside `PlexHttp` from the `AccountAuthority` constant so no caller can point it elsewhere, and it is the only request in that class allowed off the server's origin.

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

A container's array is named for what is inside it: `Metadata` for films and episodes, `Directory` for shows, seasons and library sections, `Video` for some episode lists. `PlexMediaAdapter.Items` reads all three. Reading only `Metadata` is the classic mistake — it silently returns an empty show library.

Library sections are not media entries: `Library` assigns the section's `key` to `EmbyItem.Id` and throws `InvalidDataException` when that key is missing, null or blank, rather than returning a clickable empty-ID library. Do not fall back to `ratingKey` for a section or change `Item` to read `key` globally: films, shows and seasons still use their media `ratingKey`. `PlexMediaTests.Libraries` feeds `GetViews` results directly into browse, per-library recently-added and random-carousel requests, and checks distinct section IDs and source-scoped navigation keys without signing in.

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

Favorites are a separate capability from watched state. `CardItem.CanFavorite` and `DetailViewModel.FavoriteActionVisibility` use the item’s captured scope and `SupportsFavorites`; they must not hide Plex scrobble/unscrobble actions with a shared UserState flag. The direct old command paths reject favorites before HTTP, state ordering, DTO updates or notices. The detail command also retains activity/Busy, seed ID and detail-source consistency during reattachment.

Plex cross-library genre navigation is not implemented by this client. `DetailViewModel.CanOpenGenre` keeps its labels as plain text for Plex, rejects old clicks and never borrows the default management scope. `DetailPage` repaints after capability changes and immediately after resubscribing on reentry, even when the genre collection did not change. Emby/Jellyfin links and their routes stay intact. `Momoka.PlexAffordanceTests` executes the actual card commands, current Shell detail VM and extracted page paint/read/notification methods, with fake HTTP and inert framework values. Its template bindings and property assertions are not rendered WinUI or UIA evidence; see its repository README for commands and runtime gaps.

## Verification

- `PlexProtocolTests` covers address normalisation, escape guards, token placement, redirects, error translation, the account exchange, paging headers and query building.
- `PlexMediaTests` covers sign-in, re-authentication after a 401, restore without a second account login, library filtering, search, detail mapping, seasons/episodes, next-up, playback planning, reporting and the menu — all through `EmbySession` against a fake transport.
- `PlexV2LoginTests.Register()` is called by the existing `PlexMediaTests.Register()`; no shared runner edit is needed for the full Core suite. The dedicated `plex-login-fixture` driver should call `PlexV2LoginTests.Register();` followed by `TestHarness.Run()` once. It exercises the current `EmbySession`, gateway and Core/Plex path with a synthetic handler; no real socket is opened. Persistence is isolated under the current worktree's `artifacts/staged-repair/batch07/r005/fixtures`.
- R-005 coverage includes the top-level account-to-PMS handshake, no adoption/persistence before PMS approval, 401/403, missing/wrong-type identity/token, profile-name ownership, unproven nested/string-id rejection, token headers and redirect/cancellation, reauthentication identity changes, and saved-token/password restore. Existing media/protocol account fixtures use the same bare v2 shape, not a wrapper that only makes tests green.
- All suites are offline. They say what this client sends and how it reads the answers it is handed. Official static code corroborates the v2 response contract only; neither source reading nor fake HTTP is a live-account, PMS, DPAPI, playback, 2FA or PIN pass.
- A live check is a new authorization question under CLAUDE.md's three tiers, and a Plex server is not the user's Emby library. Read-only first: address → 「连接」 → confirm the product and server name → then sign in. Record the server version and the observed response shapes in [docs/Plex支持.md](../../../docs/Plex支持.md).
- Anything touching `PlexHttp` headers, redirect handling, error classification or `PlexEndpoint` needs the test project run, not only UI checks. No live-server coverage exists; report that gap as a gap.

## Delivery

When the supported boundary changes, update this skill, [docs/Plex支持.md](../../../docs/Plex支持.md), `README.md`'s supported-server list, and the CLAUDE.md skill index if the skill is added or renamed. Keep this skill from drifting into a copy of Plex's API documentation: name the route you chose and say which of the two kinds of evidence you have.
