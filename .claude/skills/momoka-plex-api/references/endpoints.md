# Plex endpoint map

Every route this client calls, and the ones it deliberately does not. Paths are relative to the server
base (root or reverse-proxy subpath, with a trailing slash).

**Evidence:** none of this has been checked against a live server. The routes come from Plex's public
interface conventions; the offline suite pins what this client builds and how it reads the answers. Treat
the table as "what Momoka sends", not "what a Plex server is known to answer".

## Account service (plex.tv, not the server)

| Purpose | Call | Notes |
| --- | --- | --- |
| Exchange account credentials for a token | `POST /api/v2/users/signin` | Momoka sends JSON body `{login, password}` and `X-Plex-Client-Identifier`; the official v2 form consumes one bare success object `{id, username, authToken}` (see [official evidence](v2-login-official-evidence.json)); JSON request-body acceptance is not live-verified |

The password is never placed in the query string. The URI is built inside `PlexHttp` from a constant.

## Server: identity and browse

| Purpose | Call | Notes |
| --- | --- | --- |
| Anonymous identity probe | `GET /identity` | No token. Answers `machineIdentifier`, `version`. Used by 测试连接 and by the automatic product probe. |
| Server description, and token validity for this server | `GET /` | Token required; 401 means the token is not good for this server. Answers `friendlyName`, `version`, `machineIdentifier`. |
| Library sections | `GET /library/sections` | `Directory[]` with `key`, `title`, `type`. Only `movie` and `show` sections are kept. |
| Items in a section | `GET /library/sections/{key}/all` | `Metadata[]` for films, `Directory[]` for shows. Query: `sort`, `type`, `unwatched`. Paging via headers. |
| Library discovery catalog | `GET /library/sections/{key}?includeDetails=1&includeAdvanced=1` | Reads actual `Type/Filter/Sort` entries; section `filters` and supported fields govern the filter UI. |
| Filter choices | `GET /library/sections/{key}/{field}` | Only validated declared genre/year/resolution endpoints, with current type and other filters. Display labels remain separate from native values. |
| Alphabet buckets | `GET /library/sections/{key}/firstCharacter` | Current type and filter combination; `Directory.key/title/size` determines target positions. |
| Recently added | `GET /library/recentlyAdded` or `GET /library/sections/{key}/recentlyAdded` | Section-scoped when a library is given. |
| Continue watching | `GET /library/onDeck` | Plex's own rule for that row; also the source for 接下来播放. |
| Search | `GET /search?query=` | Answers `Metadata[]` (older servers) or `Hub[].Metadata[]`. |
| Item detail | `GET /library/metadata/{ratingKey}` | `Metadata[]`; carries `Media[].Part[].Stream[]`. |
| Children (seasons, episodes) | `GET /library/metadata/{ratingKey}/children` | One route serves both; the parent's type decides which. |
| Similar | `GET /library/metadata/{ratingKey}/similar` | |
| Person/genre relation | `GET /library/sections/{section}/all` | Runtime `PlexBrowseRelation` fixes section, movie/show type and one native `actor/director/writer/genre` id. Missing identity stays readable static content. See [batch evidence](../../../../docs/媒体库第二批.md); no live PMS coverage. |
| Image | `GET {path}` | The `thumb` / `art` path the item itself carried, plus `?width=`. |

Query keys used on a section listing:

| Key | Value |
| --- | --- |
| `sort` | `titleSort`, `addedAt`, `originallyAvailableAt`, `rating`, `random`; `:desc` suffix for descending |
| `type` | `1` movie, `2` show, `3` season, `4` episode — sent only when exactly one type maps |
| `unwatched` | `1` unwatched only, `0` watched only |

`type` is deliberately omitted when the caller asked for several types (a movies library asks for
`Movie,Video,BoxSet`): Plex takes one value, and sending the first would silently hide every file the
server failed to match to a film.

## Server: state and playback

When a local library provider requires `nonAnonymousAccount` for list edits, the client additionally
reads fixed `GET https://plex.tv/api/v2/user`. Only a strict numeric id matching the captured account and
an explicit boolean `anonymous=false` confirm this requirement; unknown/failed profile reads retain a
VerificationError and read-only list state. This is a read at the account service, not a PMS user route.

| Purpose | Call | Notes |
| --- | --- | --- |
| Mark watched | `GET /:/scrobble?key={ratingKey}&identifier=com.plexapp.plugins.library` | |
| Mark unwatched | `GET /:/unscrobble?key={ratingKey}&identifier=…` | |
| Playback report | `GET /:/timeline?ratingKey=&key=&state=&time=&duration=&identifier=…` | Whole-item `time` and `duration` in **milliseconds**; `key` is the item metadata key, confirmed in official Kodi `97191f2` `nowplayingmanager.py`; `state` is `playing`, `paused` or `stopped`. |

`/:/…` cannot be expressed as a relative `Uri` — the leading colon reads as a scheme — which is what
`PlexEndpoint.Special` exists for.

## Ordinary video lists

The fixed official Web 4.163.0 source, permissions and intentional limits are in [batch 5](../../../../docs/媒体库第五批-Plex清单.md). These calls remain offline protocol coverage without real PMS verification.

| Purpose | Call | Notes |
| --- | --- | --- |
| Account-visible features | `GET /media/providers` | Match library provider Feature; playlist capability, collection + manage, unknown requires refused. |
| Video playlist directory | `GET /playlists/all?type=15&playlistType=video&smart=0&sort=titleSort:asc` | Raw paging headers; smart/non-video rows filtered separately. |
| Collection directory | `GET /library/sections/{section}/collections?subtype=1|2&smart=0&sort=titleSort:asc` | One movie/show section at a time. |
| Container metadata | `GET /playlists/{id}` or `GET /library/collections/{id}` | Explicit smart/readOnly/capability checks before edits. |
| Members | `GET /playlists/{id}/items` or `GET /library/collections/{id}/children` | Preserve raw order and distinct playlistItemID. |
| Create | `POST /playlists?type=video&title=&smart=0&uri=` or `POST /library/collections?type=1|2&sectionId=&title=&smart=0&uri=` | Initial same-server media required; collection inputs revalidated for section/type. |
| Add | `PUT /playlists/{id}/items?uri=` or `PUT /library/collections/{id}/items?uri=` | Source URI uses current machineIdentifier and library metadata IDs. |
| Remove member | `DELETE /playlists/{id}/items/{playlistItemID}` or `DELETE /library/collections/{id}/children/{ratingKey}` | Never deletes disk media. |
| Playlist ordering | `PUT /playlists/{id}/items/{playlistItemID}/move?after={entry}` | Omit after to move first; no collection custom-sort management in this batch. |

## Not called

| Feature | Why |
| --- | --- |
| Favourites | Plex's server API has no per-item favourite flag; favourites live in the account service. `SetFavoriteAsync` throws rather than faking it as a label. |
| Arbitrarily nested filters | First batch supports declared single-level fields, not a general AND/OR editor. |
| Smart playlists, collaborative grants, cloud Watchlist and cross-server lists | Outside ordinary single-account list scope. |
| Metadata edit, identify, reset, delete | Plex's editing model is not this client's; the rows are not offered. |
| Chapter thumbnails | No route this client uses; `GetChapterImageBytesAsync` throws. |
| Missing episodes | No equivalent of Emby's `Shows/Missing`. |
| Remote subtitle search | No equivalent. |
| Notifications, web console | Emby-only features; already gated by product. |
| Music and photo libraries | Listed by the server, dropped by `GetViewsAsync` — this client has no music or photo face. |
