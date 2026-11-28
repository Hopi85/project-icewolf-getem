"""Keep TikTok's watermarked download copies out of video format selection."""
import sys

import yt_dlp
from yt_dlp.extractor.tiktok import TikTokIE as BaseTikTokIE
from yt_dlp.utils import ExtractorError


class TikTokIE(BaseTikTokIE):
    # Preserve the TikTok extractor key: short-link redirects refer to this key.
    def _real_extract(self, url):
        info = super()._real_extract(url)
        formats = []
        for fmt in info.get("formats", []):
            note = str(fmt.get("format_note") or "").lower()
            format_id = str(fmt.get("format_id") or "").lower()
            # Exclude download copies even when the API omits its watermark flag.
            if "watermark" in note or format_id.startswith("download"):
                continue
            formats.append(fmt)
        if not any(f.get("vcodec") != "none" for f in formats):
            raise ExtractorError(
                "TikTok offers no video version without a watermark. "
                "Please try again later.", expected=True)
        info["formats"] = formats
        return info


if __name__ == "__main__":
    parsed = yt_dlp.parse_options(sys.argv[1:])
    with yt_dlp.YoutubeDL(parsed.ydl_opts, auto_init=False) as ydl:
        ydl.add_info_extractor(TikTokIE())
        ydl.add_default_info_extractors()
        sys.exit(ydl.download(parsed.urls))
