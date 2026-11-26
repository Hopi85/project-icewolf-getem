"""Provider adapters for name search and show/season/episode catalogues."""
import base64
import hashlib
import hmac
import html
import json
import re
import secrets
import sys
import time
import uuid
import unicodedata
from urllib.parse import urlencode, urlsplit
from urllib.request import Request, urlopen
from urllib.error import HTTPError

PROVIDERS = {"Tubi": "tubi", "Plex": "plex", "Pluto TV": "pluto", "ORF ON (MediathekView)": "ORF", "ARD (MediathekView)": "ARD",
             "ZDF (MediathekView)": "ZDF", "ARTE (MediathekView)": "ARTE",
             "3sat (MediathekView)": "3sat"}

SORT_MODES = ("Beste Übereinstimmung", "Titel A–Z", "Titel Z–A", "Jahr: neueste zuerst", "Jahr: älteste zuerst")


def normalized(text):
    text = unicodedata.normalize("NFKD", str(text).casefold())
    text = "".join(c for c in text if not unicodedata.combining(c))
    text = re.sub(r"['’‘`ʼ]", "", text)
    return " ".join(re.findall(r"[^\W_]+", text))


def relevance(title, query):
    title, query = normalized(title), normalized(query)
    if not query:
        return 0
    if title == query:
        return 0
    if title.startswith(query + " "):
        return 1
    words = title.split()
    if all(any(word.startswith(token) for word in words) for token in query.split()):
        return 2
    return None


def sort_shows(shows, query, mode=SORT_MODES[0]):
    title = lambda s: (normalized(s.get("title", "")), str(s.get("id", "")))
    if mode in SORT_MODES[1:3]:
        return sorted(shows, key=title, reverse=mode == SORT_MODES[2])
    if mode in SORT_MODES[3:]:
        def year_key(show):
            year = number(show.get("year"))
            return (year is None, -(year or 0) if mode == SORT_MODES[3] else year or 0, title(show))
        return sorted(shows, key=year_key)
    return sorted(shows, key=lambda s: (relevance(s.get("title", ""), query)
        if relevance(s.get("title", ""), query) is not None else 99, title(s)))


def request_json(url, payload=None, headers=None):
    data = payload.encode() if isinstance(payload, str) else json.dumps(payload).encode() if payload is not None else None
    request = Request(url, data=data, headers={"User-Agent": "Mozilla/5.0", "Accept": "application/json",
                                              "Content-Type": "application/json", **(headers or {})})
    with urlopen(request, timeout=25) as response:
        return json.load(response)


def web_url(value):
    return isinstance(value, str) and urlsplit(value).scheme in ("http", "https") and bool(urlsplit(value).hostname)


def number(value):
    try:
        return int(value)
    except (ValueError, TypeError):
        return None


def first_image(*candidates):
    """First usable web URL from the image lists a provider returns."""
    for images in candidates:
        for url in images if isinstance(images, list) else []:
            if web_url(url):
                return url
    return None


def page_image(url, opener=urlopen):
    """Preview image of a broadcaster page; MediathekView itself has no image fields."""
    if not web_url(url):
        return None
    request = Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with opener(request, timeout=10) as response:
        page = response.read(700_000).decode("utf-8", "replace")
    for tag in re.findall(r"<meta[^>]+>", page):
        if re.search(r"(?:property|name)=[\"']og:image[\"']", tag):
            found = re.search(r"content=[\"']([^\"']+)", tag)
            image = html.unescape(found[1]) if found else None
            if web_url(image):
                return image
    return None


def group_episodes(rows):
    """Retain unknown seasons, sort known numbers, and deduplicate playable URLs."""
    groups, seen = {}, set()
    for row in rows:
        if not web_url(row.get("url")) or row["url"] in seen:
            continue
        seen.add(row["url"])
        season = number(row.get("season"))
        groups.setdefault(season, []).append(row)
    return [{"number": season, "title": f"Staffel {season}" if season is not None else "Ohne Staffelzuordnung",
             "episodes": sorted(items, key=lambda e: (number(e.get("number")) is None, number(e.get("number")) or 0, e.get("title", "")))}
            for season, items in sorted(groups.items(), key=lambda p: (p[0] is None, p[0] or 0))]


class Selection:
    def __init__(self, seasons):
        self.seasons = seasons
        self.selected = set()

    def set_season(self, index, enabled):
        urls = {e["url"] for e in self.seasons[index]["episodes"]}
        self.selected.update(urls) if enabled else self.selected.difference_update(urls)

    def state(self, index):
        urls = {e["url"] for e in self.seasons[index]["episodes"]}
        count = len(urls & self.selected)
        return "all" if count and count == len(urls) else "some" if count else "none"

    def episodes(self):
        return [e for s in self.seasons for e in s["episodes"] if e["url"] in self.selected]


class Tubi:
    account = "https://account.production-public.tubi.io"
    content = "https://content-cdn.production-public.tubi.io"

    def __init__(self, request=request_json):
        self.request = request
        self.device = str(uuid.uuid4())
        self.headers = {"Origin": "https://tubitv.com", "Referer": "https://tubitv.com/", "Accept-Language": "en-US"}

    def authorize(self):
        verifier = secrets.token_hex(16)
        challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode()
        key = self.request(self.account + "/device/anonymous/signing_key", {
            "challenge": challenge, "version": "1.0.0", "platform": "web", "device_id": self.device}, self.headers)
        path = "/device/anonymous/token"
        body = json.dumps({"verifier": verifier, "id": key["id"], "platform": "web", "device_id": self.device}, separators=(",", ":"))
        digest = lambda data: hashlib.sha256(data.encode()).hexdigest()
        stamp = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
        canonical = f"POST\n{path}\n\ncontent-type:application/json\n\ncontent-type\n{digest(body)}"
        signing = b"TUBI" + base64.b64decode(key["key"])
        for part in (stamp[:8], "tubi_request"):
            signing = hmac.digest(signing, part.encode(), "sha256")
        signature = hmac.new(signing, f"TUBI-HMAC-SHA256\n{stamp}\n{digest(canonical)}".encode(), "sha256").hexdigest()
        query = urlencode({"X-Tubi-Algorithm": "TUBI-HMAC-SHA256", "X-Tubi-Date": stamp,
                           "X-Tubi-Expires": 30, "X-Tubi-SignedHeaders": "content-type", "X-Tubi-Signature": signature})
        token = self.request(self.account + path + "?" + query, body, self.headers)
        self.headers["Authorization"] = "Bearer " + token["access_token"]

    def get(self, host, path, params):
        if "Authorization" not in self.headers:
            self.authorize()
        return self.request(host + path + "?" + urlencode(params), headers=self.headers)

    def search(self, name):
        data = self.get("https://search.production-public.tubi.io", "/api/v3/search",
                        {"search": name, "is_kids_mode": "false"})
        contents = data.get("contents")
        if not isinstance(contents, dict):
            raise ValueError("Tubi liefert ein unbekanntes Suchformat.")
        results = []
        seen = set()
        for item in contents.values():
            if item.get("type") not in ("s", "series") or not item.get("id"):
                continue
            if relevance(item.get("title", ""), name) is None or str(item["id"]) in seen:
                continue
            seen.add(str(item["id"]))
            results.append({"id": str(item["id"]), "title": item.get("title") or str(item["id"]),
                            "year": number(item.get("year") or item.get("release_year")),
                            "detail": str(item.get("year") or item.get("release_year") or ""), "provider": "tubi",
                            "desc": item.get("description") or "", "tags": list(item.get("tags") or [])[:3],
                            "rating": ((item.get("ratings") or [{}])[0] or {}).get("value"),
                            "wide": first_image(item.get("landscape_images"), item.get("thumbnails")),
                            "poster": first_image(item.get("posterarts"))})
        return {"shows": sort_shows(results, name), "notice": "Nur passende Serientitel; unpassende Empfehlungen ausgeblendet. Verfügbarkeit abhängig von Region und Anbieter."}

    def episodes(self, show):
        sid = show["id"]
        index = self.get(self.content, f"/api/v3/series/{sid}/episodes", {"platform": "web"})
        seasons = index.get("episodes_by_season")
        if not isinstance(seasons, list):
            raise ValueError("Tubi liefert keine lesbare Staffelliste.")
        rows = []
        for season in seasons:
            sn = season.get("season")
            if sn is None:
                raise ValueError("Staffelnummer fehlt in der Tubi-Antwort.")
            expected = len(season.get("episodes") or [])
            seen = set()
            for page in range(1, 501):
                data = self.get(self.content, "/api/v3/content", {"app_id": "tubitv", "platform": "web",
                    "device_id": self.device, "content_id": sid, "pagination[season]": sn,
                    "pagination[page_in_season]": page, "pagination[page_size_in_season]": 20})
                episodes = [e for child in data.get("children", []) for e in child.get("children", [])]
                fresh = [e for e in episodes if e.get("id") is not None and str(e["id"]) not in seen]
                if episodes and not fresh:
                    raise ValueError("Tubi wiederholt eine Ergebnisseite; bitte später erneut versuchen.")
                for e in fresh:
                    seen.add(str(e["id"]))
                    rows.append({"url": f"https://tubitv.com/tv-shows/{e['id']}", "season": sn,
                                 "number": e.get("episode_number"), "title": e.get("title") or f"Episode {e['id']}",
                                 "id": str(e["id"]), "dur": number(e.get("duration")), "desc": e.get("description") or "",
                                 "img": first_image(e.get("thumbnails"))})
                if (expected and len(seen) >= expected) or len(episodes) < 20:
                    if expected and len(seen) < expected:
                        raise ValueError(f"Staffel {sn} unvollständig geladen ({len(seen)}/{expected}). Bitte erneut versuchen.")
                    break
            else:
                raise ValueError("Zu viele Ergebnisseiten. Keine vollständige Liste geladen.")
        if not rows:
            raise ValueError("Für diese Serie wurden keine Episoden zurückgegeben.")
        return {"title": show["title"], "seasons": group_episodes(rows), "notice": "Alle vom Anbieter gelieferten Episoden. Downloadverfügbarkeit wird beim Start geprüft."}


class Plex:
    host = "https://vod.provider.plex.tv"

    def __init__(self, request=request_json):
        self.request = request
        self.headers = {"X-Plex-Client-Identifier": str(uuid.uuid4()), "X-Plex-Product": "Getem",
                        "X-Plex-Version": "1.0", "X-Plex-Platform": "Web", "Accept": "application/json",
                        "X-Plex-Device": "Windows", "X-Plex-Provides": "player",
                        "X-Plex-Features": "external-media,indirect-media,hub-style-list",
                        "X-Plex-Language": "en", "Origin": "https://watch.plex.tv"}

    def authorize(self):
        if "X-Plex-Token" in self.headers:
            return
        # clients.plex.tv is the API host; the website host may return 404.
        for host in ("https://clients.plex.tv", "https://plex.tv"):
            try:
                auth = self.request(host + "/api/v2/users/anonymous", {}, self.headers)
            except HTTPError as exc:
                if host == "https://clients.plex.tv" and exc.code in (404, 405):
                    continue
                raise
            token = auth.get("authToken")
            if not isinstance(token, str) or not token:
                raise ValueError("Plex hat keinen Gastzugang bereitgestellt.")
            self.headers["X-Plex-Token"] = token
            return

    @staticmethod
    def container(data):
        container = data.get("MediaContainer")
        if not isinstance(container, dict):
            raise ValueError("Plex liefert ein unbekanntes Katalogformat.")
        return container

    def get(self, path, params=None, paging=None):
        if not re.fullmatch(r"/library/metadata/[\w-]+(?:/children)?", path) and path != "/hubs/search":
            raise ValueError("Ungültiger Plex-Katalogpfad.")
        self.authorize()
        url = self.host + path + ("?" + urlencode(params) if params else "")
        data = self.request(url, headers={**self.headers, **(paging or {})})
        return self.container(data)

    def playback(self, key):
        self.metadata_key({"key": key})
        self.authorize()
        query = urlencode({"uri": "provider://tv.plex.provider.vod" + key, "type": "video", "continuous": 0})
        return self.container(self.request(self.host + "/playQueues?" + query, {}, self.headers))

    @staticmethod
    def metadata_key(item):
        key = item.get("key") or "/library/metadata/" + str(item.get("ratingKey", ""))
        # Series/season keys commonly point at their children, unlike episode keys.
        if isinstance(key, str) and key.endswith("/children"):
            key = key[:-9]
        if not re.fullmatch(r"/library/metadata/[\w-]+", key):
            raise ValueError("Plex liefert keine eindeutige Medien-ID.")
        return key

    def search(self, name):
        data = self.get("/hubs/search", {"query": name, "limit": 100, "type": 2})
        items = list(data.get("Metadata") or [])
        for hub in data.get("Hub") or []:
            items.extend(hub.get("Metadata") or [])
        shows = {}
        for item in items:
            if item.get("type") != "show" or relevance(item.get("title", ""), name) is None:
                continue
            key = self.metadata_key(item)
            shows[key] = {"id": key, "provider": "plex", "title": item["title"],
                          "year": number(item.get("year")), "detail": str(item.get("year") or ""),
                          "desc": item.get("summary") or "", "rating": item.get("contentRating"),
                          "wide": first_image([item.get("art"), item.get("thumb")]),
                          "poster": first_image([item.get("thumb")])}
        return {"shows": sort_shows(list(shows.values()), name), "notice":
                "Passende Serien aus Plex Movies & Shows (bis zu 100 Treffer). Keine Verweise auf fremde Streaming-Abos. Downloads nur bei verfügbaren, ungeschützten Streams."}

    def children(self, path):
        # Validate before any authenticated request, even for loaded UI state.
        self.metadata_key({"key": path})
        rows, seen = [], set()
        for start in range(0, 10000, 100):
            data = self.get(path + "/children", paging={"X-Plex-Container-Start": str(start), "X-Plex-Container-Size": "100"})
            page = data.get("Metadata") or []
            for item in page:
                key = self.metadata_key(item)
                if key in seen:
                    raise ValueError("Plex wiederholt Katalogeinträge; Liste nicht vollständig geladen.")
                seen.add(key)
                rows.append(item)
            total = number(data.get("totalSize"))
            if total is not None and len(rows) >= total:
                return rows
            if len(page) < 100:
                if total is not None and len(rows) < total:
                    raise ValueError("Plex lieferte eine unvollständige Episodenliste.")
                return rows
        raise ValueError("Plex-Ergebnislimit erreicht; Liste nicht vollständig geladen.")

    def episodes(self, show):
        rows = []
        for season in self.children(show["id"]):
            if season.get("type") != "season":
                continue
            for episode in self.children(self.metadata_key(season)):
                if episode.get("type") != "episode":
                    continue
                rows.append({"url": self.host + self.metadata_key(episode),
                             "season": episode.get("parentIndex", season.get("index")),
                             "number": episode.get("index"), "title": episode.get("title") or "Ohne Titel",
                             "id": self.metadata_key(episode).rsplit("/", 1)[-1],
                             "dur": (number(episode.get("duration")) or 0) // 1000,
                             "desc": episode.get("summary") or "", "img": first_image([episode.get("thumb")])})
        if not rows:
            raise ValueError("Plex liefert keine verfügbaren Episoden für diese Serie.")
        return {"title": show["title"], "seasons": group_episodes(rows), "notice":
                "Plex-Katalog: Verfügbarkeit und DRM werden vor jedem Download geprüft. Nicht jeder gelistete Titel ist herunterladbar."}


class Mediathek:
    def __init__(self, channel, request=request_json):
        self.channel, self.request = channel, request

    def rows(self, query, field, limit):
        result, seen = [], set()
        for offset in range(0, limit, 200):
            data = self.request("https://mediathekviewweb.de/api/query", {
                "queries": [{"fields": ["channel"], "query": self.channel}, {"fields": [field], "query": query}],
                "sortBy": "timestamp", "sortOrder": "desc", "future": False, "offset": offset, "size": 200})
            payload = data.get("result", {})
            page = payload.get("results")
            if not isinstance(page, list):
                raise ValueError("MediathekView liefert ein unbekanntes Antwortformat.")
            fresh = [row for row in page if str(row.get("id") or row.get("url_website") or row.get("url_video")) not in seen]
            if page and not fresh:
                raise ValueError("MediathekView wiederholt Ergebnisse; Laden abgebrochen.")
            for row in fresh:
                seen.add(str(row.get("id") or row.get("url_website") or row.get("url_video")))
            result.extend(fresh)
            total = payload.get("queryInfo", {}).get("totalResults")
            if len(page) < 200 or (isinstance(total, int) and offset + len(page) >= total):
                return result, False
        return result, True

    def search(self, name):
        rows, limited = self.rows(name, "topic", 1000)
        shows = {}
        for row in rows:
            channel, topic = row.get("channel", ""), row.get("topic", "")
            if channel.casefold() != self.channel.casefold() or not topic:
                continue
            # Rows arrive newest first, so the first one supplies text and preview page.
            show = shows.setdefault(topic, {"id": topic, "title": topic, "provider": self.channel, "detail": channel,
                                            "desc": row.get("description") or "", "page": row.get("url_website"), "count": 0})
            show["count"] += 1
        return {"shows": sorted(shows.values(), key=lambda s: s["title"]),
                "notice": "Suche auf 1.000 Beiträge begrenzt; Suchbegriff ggf. genauer eingeben." if limited else
                "Sendungen/Themen aus MediathekView; enthält auch Dokus, Shows und Magazine."}

    def episodes(self, show):
        rows, limited = self.rows(show["id"], "topic", 10000)
        episodes = []
        for row in rows:
            if row.get("channel", "").casefold() != self.channel.casefold() or row.get("topic") != show["id"]:
                continue
            title = row.get("title") or "Ohne Titel"
            match = re.search(r"\bS(\d+)\s*[: ._-]*E(\d+)\b|\((\d+)/(\d+)\)", title, re.I)
            season, episode = (number(match[1]), number(match[2])) if match and match[1] else (None, None)
            url = row.get("url_website") or row.get("url_video_hd") or row.get("url_video")
            episodes.append({"url": url, "title": title, "season": season, "number": episode,
                             "dur": number(row.get("duration")), "desc": row.get("description") or "",
                             "page": row.get("url_website")})
        seasons = group_episodes(episodes)
        if not seasons:
            raise ValueError("Keine aktuell gelisteten Folgen gefunden.")
        return {"title": show["title"], "seasons": seasons, "notice":
                ("Achtung: Ergebnislimit erreicht; Liste unvollständig. " if limited else "") +
                "Aktuell im Index verfügbare Beiträge, keine Garantie vollständiger Staffeln. Ohne eindeutige Staffelnummer: Ohne Staffelzuordnung."}


def client_for(provider):
    if provider not in PROVIDERS.values():
        raise ValueError("Unbekannter Anbieter.")
    if provider == "pluto":
        from pluto_catalog import Pluto
        return Pluto()
    return Tubi() if provider == "tubi" else Plex() if provider == "plex" else Mediathek(provider)


def friendly_error(exc):
    """No request headers, session tokens, or signed URLs in user-facing errors."""
    from urllib.error import HTTPError, URLError
    if isinstance(exc, HTTPError):
        return f"Anbieter antwortet mit HTTP {exc.code}. Region, Verfügbarkeit oder Schnittstelle prüfen."
    if isinstance(exc, (URLError, TimeoutError)):
        return "Anbieter nicht erreichbar. Internetverbindung prüfen und erneut versuchen."
    return str(exc)


def main():
    job = json.loads(sys.argv[1])
    client = client_for(job["provider"])
    if job["action"] == "search":
        name = job["query"].strip()
        if not name:
            raise ValueError("Bitte einen Seriennamen eingeben.")
        data = client.search(name)
    else:
        data = client.episodes(job["show"])
    print(json.dumps(data, ensure_ascii=False))


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(friendly_error(exc), file=sys.stderr)
        sys.exit(1)
