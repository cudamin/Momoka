# Plex endpoint map

Every route this client calls, and the ones it deliberately does not. Paths are relative to the server
base (root or reverse-proxy subpath, with a trailing slash).

**Evidence:** none of this has been checked against a live server. The routes come from Plex's public
interface conventions; the offline suite pins what this client builds and how it reads the answers. Treat
the table as "what Momoka sends", not "what a Plex server is known to answer".

## Account service (plex.tv, not the server)

| Purpose | Call | Notes |
| --- | --- | --- |
| Exchange account credentials for a token | `POST /api/v2/users/signin` | JSON body `{login, password}`; requires `X-Plex-Client-Identifier`; answers `{user:{id,username}, authToken}` |

The password is never placed in the query string. The URI is built inside `PlexHttp` from a constant.

## Server: identity and browse

| Purpose | Call | Notes |
| --- | --- | --- |
| Anonymous identity probe | `GET /identity` | No token. Answers `machineIdentifier`, `version`. Used by 测试连接 and by the automatic product probe. |
| Server description, and token validity for this server | `GET /` | Token required; 401 means the token is not good for this server. Answers `friendlyName`, `version`, `machineIdentifier`. |
| Library sections | `GET /library/sections` | `Directory[]` with `key`, `title`, `type`. Only `movie` and `show` sections are kept. |
| Items in a section | `GET /library/sections/{key}/all` | `Metadata[]` for films, `Directory[]` for shows. Query: `sort`, `type`, `unwatched`. Paging via headers. |
| Recently added | `GET /library/recentlyAdded` or `GET /library/sections/{key}/recentlyAdded` | Section-scoped when a library is given. |
| Continue watching | `GET /library/onDeck` | Plex's own rule for that row; also the source for 接下来播放. |
| Search | `GET /search?query=` | Answers `Metadata[]` (older servers) or `Hub[].Metadata[]`. |
| Item detail | `GET /library/metadata/{ratingKey}` | `Metadata[]`; carries `Media[].Part[].Stream[]`. |
| Children (seasons, episodes) | `GET /library/metadata/{ratingKey}/children` | One route serves both; the parent's type decides which. |
| Similar | `GET /library/metadata/{ratingKey}/similar` | |
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

| Purpose | Call | Notes |
| --- | --- | --- |
| Mark watched | `GET /:/scrobble?key={ratingKey}&identifier=com.plexapp.plugins.library` | |
| Mark unwatched | `GET /:/unscrobble?key={ratingKey}&identifier=…` | |
| Playback report | `GET /:/timeline?ratingKey=&key=&state=&time=&duration=&identifier=…` | `time` and `duration` in **milliseconds**; `key` is the part key; `state` is `playing`, `paused` or `stopped`. |

`/:/…` cannot be expressed as a relative `Uri` — the leading colon reads as a scheme — which is what
`PlexEndpoint.Special` exists for.

## Not called

| Feature | Why |
| --- | --- |
| Favourites | Plex's server API has no per-item favourite flag; favourites live in the account service. `SetFavoriteAsync` throws rather than faking it as a label. |
| Filter values (genres/tags/years) | No equivalent of Emby's `/Items/Filters`; the filter panel is hidden for Plex. |
| Letter jump | No `NameStartsWithOrGreater` equivalent; the letter bar is hidden. |
| Collections, add-to-collection | Not mapped; the row is not offered. |
| Metadata edit, identify, reset, delete | Plex's editing model is not this client's; the rows are not offered. |
| Chapter thumbnails | No route this client uses; `GetChapterImageBytesAsync` throws. |
| Missing episodes | No equivalent of Emby's `Shows/Missing`. |
| Remote subtitle search | No equivalent. |
| Notifications, web console | Emby-only features; already gated by product. |
| Music and photo libraries | Listed by the server, dropped by `GetViewsAsync` — this client has no music or photo face. |
