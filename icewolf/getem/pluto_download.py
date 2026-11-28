"""Resolve Pluto episodes just before downloading, instead of yt-dlp's legacy API."""
import sys

import yt_dlp
from yt_dlp.extractor.common import InfoExtractor
from yt_dlp.utils import ExtractorError

from pluto_catalog import Pluto


class GetemPlutoIE(InfoExtractor):
    _VALID_URL = r'https?://(?:www\.)?pluto\.tv/(?:[a-z]{2}(?:-[a-z]{2})?/)?on-demand/series/(?P<series>[\w-]+)/(?:seasons?/\d+/)?episode/(?P<id>[\w-]+)/?(?:[?#].*)?$'

    def _real_extract(self, url):
        match = self._match_valid_url(url)
        sid, requested = match.group("series", "id")
        client = Pluto()
        try:
            data = client.get(f"/v3/vod/series/{sid}/seasons")
            episode = next((e for s in data.get("seasons", []) for e in s.get("episodes", [])
                            if requested in (e.get("_id"), e.get("id"), e.get("slug"))), None)
            if episode is None:
                raise ValueError("Pluto TV does not offer this episode in the current region.")
            eid = client.identifier(episode.get("_id") or episode.get("id"))
            stream = client.stream(eid)
        except Exception:
            raise ExtractorError("Pluto TV episode not available. Check connection, region and availability.", expected=True) from None
        formats, subtitles = self._extract_m3u8_formats_and_subtitles(
            stream, eid, "mp4", headers=client.headers, fatal=False)
        formats = [f for f in formats if not f.get("has_drm")]
        if not formats:
            raise ExtractorError("Pluto TV offers no supported unprotected stream. Check region or DRM.", expected=True)
        return {"id": eid, "title": episode.get("name") or eid, "series": data.get("name"),
                "season_number": episode.get("season"), "episode_number": episode.get("number"),
                "formats": formats, "subtitles": subtitles}


if __name__ == "__main__":
    parsed = yt_dlp.parse_options(sys.argv[1:])
    with yt_dlp.YoutubeDL(parsed.ydl_opts, auto_init=False) as ydl:
        ydl.add_info_extractor(GetemPlutoIE())
        ydl.add_default_info_extractors()
        sys.exit(ydl.download(parsed.urls))
