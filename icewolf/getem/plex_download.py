"""Resolve Plex catalogue episodes at download time, using clear streams only."""
import sys
from urllib.parse import urljoin, urlsplit

import yt_dlp
from yt_dlp.extractor.common import InfoExtractor
from yt_dlp.utils import ExtractorError

from catalog import Plex


def protected(item):
    return any(item.get(key) not in (None, False, 0, "0", "false", "") for key in ("drm", "drmEnabled"))


class GetemPlexIE(InfoExtractor):
    _VALID_URL = r'https://vod\.provider\.plex\.tv/library/metadata/(?P<id>[\w-]+)$'

    def _real_extract(self, url):
        video_id = self._match_id(url)
        client = Plex()
        try:
            container = client.get("/library/metadata/" + video_id, {"includeMedia": 1})
        except Exception:
            raise ExtractorError("Plex metadata not available. Check your connection or regional availability.", expected=True)
        rows = container.get("Metadata") or []
        if not rows:
            raise ExtractorError("Plex does not offer this episode here.", expected=True)
        item = rows[0]
        # Catalogue metadata can omit playback parts. Request a fresh play queue
        # for this episode, rather than assuming a catalogue entry is a stream.
        if not any(m.get("Part") for m in item.get("Media") or []):
            try:
                playback = client.playback("/library/metadata/" + video_id)
                selected = next((row for row in playback.get("Metadata", [])
                                 if str(row.get("ratingKey")) == video_id
                                 or row.get("key") == "/library/metadata/" + video_id), None)
                if selected:
                    item = {**item, **selected}
            except Exception:
                raise ExtractorError("Plex playback not available. Check region or availability.", expected=True) from None
        formats = []
        for media in item.get("Media") or []:
            if protected(media):
                continue
            for part in media.get("Part") or []:
                if protected(part):
                    continue
                stream = urljoin(client.host, part.get("key") or "")
                if not part.get("key") or urlsplit(stream).scheme != "https":
                    continue
                headers = client.headers if urlsplit(stream).hostname == "vod.provider.plex.tv" else {}
                path = urlsplit(stream).path.lower()
                if path.endswith(".m3u8"):
                    formats.extend(self._extract_m3u8_formats(stream, video_id, "mp4", headers=headers, fatal=False))
                elif path.endswith(".mpd"):
                    formats.extend(self._extract_mpd_formats(stream, video_id, headers=headers, fatal=False))
                elif path.endswith(".mp4"):
                    formats.append({"url": stream, "ext": "mp4", "height": media.get("height"), "http_headers": headers})
        formats = [f for f in formats if not f.get("has_drm")]
        for fmt in formats:
            if urlsplit(fmt.get("url", "")).hostname == "vod.provider.plex.tv":
                fmt["http_headers"] = {**fmt.get("http_headers", {}), **client.headers}
        if not formats:
            raise ExtractorError("Plex offers no supported unprotected stream. DRM or a regional restriction is possible.", expected=True)
        return {"id": video_id, "title": item.get("title") or video_id, "formats": formats,
                "series": item.get("grandparentTitle"), "season_number": item.get("parentIndex"),
                "episode_number": item.get("index")}


if __name__ == "__main__":
    parsed = yt_dlp.parse_options(sys.argv[1:])
    with yt_dlp.YoutubeDL(parsed.ydl_opts, auto_init=False) as ydl:
        ydl.add_info_extractor(GetemPlexIE())
        ydl.add_default_info_extractors()
        sys.exit(ydl.download(parsed.urls))
