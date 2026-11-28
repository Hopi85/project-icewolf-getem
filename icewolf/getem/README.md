# Getem

Local Windows app for video links and series/program search.

## Start

In Icewolf: the red "G" button in the toolbar or ☰ menu → **Getem** opens Getem in a browser tab.
Shift+click on the menu entry updates the download components (yt-dlp).
On its own: double-click `Start.cmd`; `Aktualisieren.cmd` updates the download components.
The standalone window runs with pywebview in WebView2, which comes preinstalled on Windows 11.
Requires Python 3.11 or newer.

## How to use it

Results appear as a wall of tiles, episodes slide in from the right, and the queue is always visible at the bottom.

1. Type a name into the search box and press Enter. Tubi, Pluto TV, Plex, ORF ON, ARD, ZDF and 3sat
   are searched at the same time. Each provider shows its number of results or an error;
   clicking a provider hides or shows its results.
2. Click a result. Its seasons and episodes appear on the right.
3. Tick episodes. The season box selects all episodes, Shift+click selects a range.
4. **Download** adds the selection to the queue. You can keep searching at any time.

**Paste links** at the top right opens a box for YouTube, TikTok and other video links,
one link per line. Pasting a link into the search box opens this box automatically. Format, quality and
download folder are in the button at the top right (for example "MP4 · 1080p").
**Check & download** checks pasted links with yt-dlp before adding them to the queue.
Supported websites, videos that can be detected generically and direct media links
(such as MP4, HLS or DASH) all use the same flow. While the check runs, the link box is
locked; each link has a time limit of 60 seconds.
Links that pass the check are added with their title and, where available, a preview image.
Links that fail stay in the box with an explanation; checking again does not add the
links that already succeeded a second time.
The check does not recognize every player and does not guarantee a later download:
availability, format choice and access can still change.
If a provider changed something, update the download components first.
The ✕ clears the search box; clicking the logo goes back to the search.
TikTok videos are downloaded without the extra download watermark automatically:
Getem skips TikTok's download copies and uses the playback versions. If none is available,
you get an error instead of a file with a watermark. Logos that are part of the original video
stay; existing files are not changed afterwards. MP3 is not affected.
Keyboard: Ctrl+K (search), Esc (clear search), Ctrl+Enter (download).

### Queue

One download runs at a time. **✕ Cancel** stops only the current one; the next one
starts right away. The ✕ on waiting entries removes them. **Cancel all** stops
the current download and clears the waiting ones, after confirming with a second click.
↻ restarts cancelled or failed entries. Partial files are kept, and the download
continues where it stopped.

Episodes that were already downloaded are marked **Downloaded**, based on the ID in the file name.
Tubi, Pluto TV and Plex provide the episode ID for this, which is also part of the file name.

### Thumbnails

Tubi provides images directly. MediathekView has no image fields, so the image comes from
the `og:image` of the program page as soon as a row becomes visible. Found addresses
are stored in `.cache/thumbs.json`. This works only partly for ARD; if an image is missing,
a title tile is shown instead.

### Search

Sorting: **Best match**, **Title A–Z / Z–A**, **Year (newest / oldest first)**.
For Tubi, unrelated recommendations are hidden. Apostrophes, upper/lower case and
accents are normalized: `hells kitchen` also finds `Hell’s Kitchen`.

**Plex:** guest access through `clients.plex.tv`, with the website host as a fallback on
HTTP 404/405. The search uses Plex's own Movies & Shows catalog;
series and season paths with `/children` are normalized. Images, description,
seasons and episodes are taken over. For a download, media information is requested,
and a playback session is created if parts are missing. Only available,
unprotected streams are downloaded. A live check of this part is still pending.

**Pluto TV:** search in the regional on-demand series catalog, season/episode selection
and its own download resolver with a fresh guest token. Live TV recordings are not
included. Categories are loaded page by page and series are de-duplicated. Pluto
can limit the titles within a category; the provider note in the tooltip
then points out that results may be missing. Completeness is not guaranteed.
The own resolver replaces yt-dlp's outdated Pluto path for single episodes.

ARTE is left out of the search because `Mediathek.search`
does not recognize the channels `ARTE.DE`, `ARTE.FR` etc. as `ARTE`.

Connected catalogs: Tubi, Pluto TV and Plex, plus ORF ON, ARD, ZDF, ARTE and 3sat through
MediathekView. Tubi uses the anonymous guest access, the season index and
episode requests page by page. Tokens stay in memory and are
neither saved nor logged.

MediathekView provides currently listed topics and items; complete seasons are not
guaranteed. Only clear SxxExx details are used as season numbers;
other items are listed under **No season assigned**. Search results
can also be magazines and documentaries. The search is limited to 1,000 items,
the episode list to 10,000; a note appears when a limit is reached.

Joyn Austria (PULS 4/PULS 24/ATV) and ServusTV On are candidates,
but not yet connected to the name search. YouTube and TikTok work through links in the search box.
More providers can be added through search(name) / episodes(show) in catalog.py.
There is no universal search interface for all streaming sites.
`discovery.py` is no longer used by the interface.

### Proton VPN

Connect the Proton VPN client you already have installed to the country you want
before searching. Getem uses the Windows network connection; no additional
VPN provider, no VPN password in Getem and no automatic location switching.
With split tunneling, Getem's Python runtime has to be inside the VPN tunnel.
After changing the country, search again so the episode list is loaded fresh as well.
Don't change the connection while downloads are running. The app does not
check automatically whether Proton is connected; providers may reject VPN addresses.

## Downloads

Errors don't stop the following downloads. Not every website is supported.

Default folder: `downloads` (in Icewolf: `Downloads\Getem`). Folder, format and quality are saved in
`.cache/settings.json`. Available formats: **MP4** (default, prefers H.264 so it plays on every
device; if a video doesn't fit, it becomes MKV), **MKV** (best available version regardless of
devices) or **MP3 (audio only)**.
Format and quality apply to everything added to the queue afterwards.
Quality is an upper limit; if there is no version below it, the smallest one is downloaded. Progress, speed and
remaining time refer to the current video/audio part. For segmented downloads,
up to four parts are downloaded at the same time.

## Limits and validation

Region, sign-in, DRM, expired content and provider changes can
limit catalogs and downloads. The app does not import browser cookies
and does not decrypt DRM. A catalog result is no guarantee that a download works.

Live-checked on 6 Nov 2026: search and episodes at Tubi, ORF, ARD, ZDF and 3sat; downloading
and cancelling Tubi episodes; a complete download of an ORF item.

Extension of 7 Nov 2026: Pluto/Plex tested with simulated API answers
(guest access, catalog pages, season/episode assignment, playback resolution,
DRM filter and hand-over to the queue). Live access from the
development environment was blocked by its network sandbox (WinError 10013),
so a successful real Pluto/Plex download is not confirmed yet.

The old Tkinter version is in `_alt_tkinter/` (in the original Getem project, not shipped with Icewolf).

TikTok extension of 14 Nov 2026: 71 tests passed, including the
watermark filter, short-link redirects and download routing. A live check
against TikTok failed because of a DNS timeout in the development environment, so a real
download without a watermark is not confirmed here yet.

Tests (in the original Getem project): `.venv\Scripts\python.exe -m unittest discover -s tests -v`

## Technical sources

- [yt-dlp](https://github.com/yt-dlp/yt-dlp)
- [MediathekViewWeb API](https://github.com/mediathekview/mediathekviewweb/blob/master/server/app.ts)
- [Tubi catalog protocol](https://github.com/Breezyslasher/Kodi-Addons/blob/main/plugin.video.tubitv/resources/lib/tubi_api.py)
- [Tubi guest access protocol](https://github.com/Breezyslasher/Kodi-Addons/blob/main/plugin.video.tubitv/resources/lib/tubi_auth.py)
- [Plex Provider API](https://developer.plex.tv/pms/)
- [Pluto boot and playback protocol in Streamlink](https://github.com/streamlink/streamlink/blob/master/src/streamlink/plugins/pluto.py)
- [Pluto catalog protocol in OpenViX](https://github.com/OpenViX/PlutoTV/blob/master/src/PlutoDownload.py)
