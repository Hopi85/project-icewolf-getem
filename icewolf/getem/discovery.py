"""Discover links from playlists and linked overview pages; never download media."""
import fnmatch
import json
import re
import sys
from html.parser import HTMLParser
from urllib.parse import urljoin, urlsplit, urlunsplit


def tubi_episode_id(url):
    parts = urlsplit(url)
    if parts.hostname not in ("tubitv.com", "www.tubitv.com"):
        return None
    match = re.fullmatch(r"/(?:[a-z]{2}-[a-z]{2}/)?tv-shows/(\d+)(?:/[^*]*)?", parts.path)
    return int(match[1]) if match else None


class EpisodeScripts(HTMLParser):
    def __init__(self):
        super().__init__()
        self.active = False
        self.scripts = []
        self.current = []

    def handle_starttag(self, tag, attrs):
        if tag == "script" and dict(attrs).get("type") == "application/ld+json":
            self.active = True
            self.current = []

    def handle_data(self, data):
        if self.active:
            self.current.append(data)

    def handle_endtag(self, tag):
        if tag == "script" and self.active:
            self.scripts.append("".join(self.current))
            self.active = False


def tubi_metadata(html, episode_id):
    """Only trust the requested episode record or explicit TVEpisode metadata."""
    from yt_dlp.extractor.common import InfoExtractor
    from yt_dlp.utils import js_to_json
    from yt_dlp import YoutubeDL
    raw = {}
    if re.search(r"window\.__data\s*=", html):
        with YoutubeDL({"quiet": True}, auto_init=False) as parser:
            data = InfoExtractor(parser)._search_json(r"window\.__data\s*=", html, "data", str(episode_id),
                                                     transform_source=js_to_json)
        raw = data.get("video", {}).get("byId", {}).get(str(episode_id), {})
    title = raw.get("title") or ""
    season = raw.get("season_number")
    series = raw.get("series_id")
    series_name = raw.get("series_title") or raw.get("series_name")
    if isinstance(raw.get("series"), dict):
        series = series or raw["series"].get("id")
        series_name = series_name or raw["series"].get("title") or raw["series"].get("name")
    match = re.search(r"\bS(\d+)\s*[:_-]\s*E\d+", title, re.I)
    if season is None and match:
        season = int(match[1])
    parser = EpisodeScripts()
    parser.feed(html)
    episodes = []
    for script in parser.scripts:
        try:
            data = json.loads(script)
        except ValueError:
            continue
        nodes = data if isinstance(data, list) else [data]
        for node in nodes:
            if not isinstance(node, dict):
                continue
            nodes_in_graph = node.get("@graph", [node])
            for item in nodes_in_graph:
                if isinstance(item, dict) and item.get("@type") == "TVEpisode":
                    episodes.append(item)
    # Multiple episodes may be recommendations. Require an explicit matching URL.
    candidates = [item for item in episodes if tubi_episode_id(item.get("url", "")) == episode_id]
    if not candidates and len(episodes) == 1 and not episodes[0].get("url"):
        candidates = episodes
    for item in candidates[:1]:
        parent = item.get("partOfSeries") or {}
        part = item.get("partOfSeason") or {}
        if isinstance(parent, dict):
            series_name = series_name or parent.get("name")
        if isinstance(part, dict) and season is None:
            season = part.get("seasonNumber")
        title = title or item.get("name") or ""
    if series is not None:
        key = "id:" + str(series)
    elif isinstance(series_name, str) and series_name.strip():
        key = "name:" + " ".join(series_name.casefold().split())
    else:
        raise ValueError("Could not tell which series this episode belongs to. Please use the series overview.")
    try:
        season = int(season)
    except (ValueError, TypeError):
        raise ValueError("Could not tell the season number of this episode.") from None
    return {"series_key": key, "series_name": series_name or f"Series {series}",
            "season_number": season, "title": title}


def scan_tubi(start_id, read_metadata, report, limit=100):
    """Walk forward only; every ID must independently confirm series and season."""
    result = []
    expected = None
    for offset in range(limit):
        episode_id = start_id + offset
        url = f"https://tubitv.com/tv-shows/{episode_id}"
        report(f"Checking episode ID {episode_id} …")
        try:
            info = read_metadata(url, episode_id)
            if not info.get("series_key") or info.get("season_number") is None:
                raise ValueError("Series/season details missing.")
        except Exception as exc:
            if not result:
                raise
            report(f"Search stopped at {episode_id}: {exc} Links already confirmed are kept; the season may be incomplete.")
            return result
        group = info["series_key"], info["season_number"]
        if expected is None:
            expected = group
            report(f"Found: {info.get('series_name', group[0])}, season {group[1]}.")
        elif group != expected:
            report(f"Search finished: ID {episode_id} belongs to another series or season.")
            return result
        result.append(url)
        report(f"Found: {url} – {info.get('title', '')}")
    report(f"Search limit of {limit} episodes reached; the list may be incomplete.")
    return result


class Links(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "a" and attrs.get("href"):
            self.links.append(attrs["href"])


def identity(url):
    parts = urlsplit(url)
    # Episode slugs may be omitted or changed by the provider.
    match = re.search(r"/(?:tv-shows|video|videos)/(\d+)(?:/|$)", parts.path)
    return (parts.hostname, match[0].rstrip("/") if match else parts.path.rstrip("/"),
            "" if match else parts.query)


def entry_url(entry):
    url = entry.get("webpage_url") or entry.get("url") or ""
    if urlsplit(url).scheme in ("http", "https"):
        return url
    if entry.get("ie_key", "").lower() == "youtube" and re.fullmatch(r"[\w-]{11}", url):
        return "https://www.youtube.com/watch?v=" + url
    return None


def entries(info):
    if not info or "entries" not in info:
        return []
    result = []
    for item in info.get("entries") or []:
        if not item:
            continue
        if "entries" in item:
            result.extend(entries(item))
        else:
            url = entry_url(item)
            if url:
                result.append({**item, "url": url})
    return result


def matches(entry, pattern):
    if fnmatch.fnmatchcase(entry["url"], pattern):
        return True
    season = re.search(r"/s(\d+)\*$", urlsplit(pattern).path, re.I)
    if season:
        number = entry.get("season_number")
        if number is not None:
            return str(number).lstrip("0") == season[1].lstrip("0")
        title = entry.get("title") or ""
        found = re.search(r"\bS(\d+)[ :_-]*E\d+", title, re.I)
        if found:
            return int(found[1]) == int(season[1])
    return False


def discover(pattern, extract, fetch):
    wildcard = "*" in urlsplit(pattern).path
    parts = urlsplit(pattern)
    if wildcard:
        # Start from the concrete parent of the wildcard segment.
        path = parts.path[:parts.path.index("*")].rsplit("/", 1)[0] + "/"
        seed = urlunsplit((parts.scheme, parts.netloc, path, parts.query, ""))
    else:
        seed = pattern
    errors = []

    def read(url):
        try:
            return entries(extract(url))
        except Exception as exc:
            errors.append(str(exc))
            return []

    def selected(rows):
        return list(dict.fromkeys(row["url"] for row in rows if not wildcard or matches(row, pattern)))

    rows = read(seed)
    found = selected(rows)
    if found:
        return found
    if not wildcard:
        raise ValueError("No series/playlist found. Please enter an overview or playlist link.")

    parser = Links()
    parser.feed(fetch(seed))
    links = list(dict.fromkeys(urljoin(seed, href) for href in parser.links))
    links = [url for url in links if urlsplit(url).scheme in ("http", "https")
             and urlsplit(url).netloc == parts.netloc]
    # Literal wildcard matches on the page do not require a site-specific extractor.
    found = selected([{"url": url} for url in links])
    if found:
        return found
    # Look for overview links, but only accept a playlist containing the seed.
    # This prevents recommendation carousels from selecting a different series.
    candidates = [url for url in links if re.search(
        r"/(?:series|shows|show|seasons|season|playlist|collection)(?:/|\?)|[?&]list=", url, re.I)]
    for url in candidates[:8]:
        rows = read(url)
        if not any(identity(row["url"]) == identity(seed) for row in rows):
            continue
        found = selected(rows)
        if found:
            return found
    raise ValueError("No matching episodes found. Please use the series/season/playlist link "
                     "instead of an episode link. The page must link its episodes "
                     "or offer them as a playlist." + (" Last loading error: " + errors[-1] if errors else ""))


def main():
    import yt_dlp
    from yt_dlp.networking import Request
    options = {"quiet": True, "no_warnings": True, "extract_flat": "in_playlist",
               "skip_download": True, "socket_timeout": 15, "retries": 1,
               "extractor_retries": 1}
    with yt_dlp.YoutubeDL(options) as ydl:
        def fetch(url):
            with ydl.urlopen(Request(url)) as response:
                return response.read(8 * 1024 * 1024).decode("utf-8", "replace")
        start_id = tubi_episode_id(sys.argv[1])
        if start_id is not None:
            urls = scan_tubi(start_id, lambda url, eid: tubi_metadata(fetch(url), eid),
                             lambda text: print(text, flush=True))
        else:
            urls = discover(sys.argv[1], lambda url: ydl.extract_info(url, download=False), fetch)
    print(json.dumps({"discovered": urls}, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        sys.exit(1)
