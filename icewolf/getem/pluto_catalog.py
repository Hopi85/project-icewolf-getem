"""Regional Pluto on-demand catalogue; each operation starts a new guest session."""
import re
import uuid
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit

from catalog import first_image, group_episodes, number, relevance, request_json, sort_shows


class Pluto:
    host = "https://api.pluto.tv"

    def __init__(self, request=request_json):
        self.request = request
        self.headers = {"Origin": "https://pluto.tv", "Referer": "https://pluto.tv/"}
        self.session = None

    def boot(self):
        if self.session is None:
            params = {"appName": "web", "appVersion": "8.0.0", "deviceVersion": "1.0.0",
                      "deviceModel": "web", "deviceMake": "chrome", "deviceType": "web",
                      "clientID": str(uuid.uuid4()), "clientModelNumber": "1.0.0", "serverSideAds": "false"}
            data = self.request("https://boot.pluto.tv/v4/start?" + urlencode(params), headers=self.headers)
            if not isinstance(data.get("sessionToken"), str) or not data["sessionToken"]:
                raise ValueError("Pluto TV did not provide guest access. Check the region.")
            self.session = data
        return self.session

    def get(self, path, **params):
        token = self.boot()["sessionToken"]
        return self.request(self.host + path + "?" + urlencode({"includeItems": "true", "deviceType": "web", **params}),
                            headers={**self.headers, "Authorization": "Bearer " + token})

    @staticmethod
    def identifier(value):
        value = str(value or "")
        if not re.fullmatch(r"[a-zA-Z0-9_-]+", value):
            raise ValueError("Pluto TV returned no valid media ID.")
        return value

    @staticmethod
    def image(item):
        covers = item.get("covers") or []
        candidates = [c.get("url") or c.get("path") for c in covers if c.get("aspectRatio") == "16:9"]
        for name in ("poster16_9", "featuredImage", "thumbnail", "poster"):
            value = item.get(name)
            candidates.append(value.get("path") or value.get("url") if isinstance(value, dict) else value)
        return first_image(candidates)

    def search(self, name):
        shows, seen_categories, limited = {}, set(), False
        for page in range(1, 21):
            data = self.get("/v3/vod/categories", offset=100, page=page)
            categories = data.get("categories") if isinstance(data, dict) else data
            if not isinstance(categories, list):
                raise ValueError("Pluto TV liefert ein unbekanntes Katalogformat.")
            fresh = 0
            for category in categories:
                cid = category.get("_id") or category.get("id") or category.get("name")
                if cid in seen_categories:
                    continue
                seen_categories.add(cid)
                fresh += 1
                items = category.get("items") or []
                limited |= (number(category.get("totalItemsCount")) or len(items)) > len(items)
                for item in items:
                    if item.get("type") != "series" or relevance(item.get("name", ""), name) is None:
                        continue
                    sid = self.identifier(item.get("_id") or item.get("id"))
                    shows[sid] = {"id": sid, "provider": "pluto", "title": item.get("name") or sid,
                                  "year": number(item.get("year")), "rating": item.get("rating"),
                                  "desc": item.get("summary") or item.get("description") or "",
                                  "wide": self.image(item), "detail": item.get("genre") or ""}
            if len(categories) < 100:
                break
            if not fresh:
                raise ValueError("Pluto TV repeats catalog pages; the search is not complete.")
        else:
            limited = True
        notice = "Series from the regional Pluto TV on-demand catalog; no live channels."
        if limited:
            notice += " Pluto limits catalog lists; more series may be missing."
        return {"shows": sort_shows(list(shows.values()), name), "notice": notice}

    def episodes(self, show):
        sid = self.identifier(show["id"])
        data = self.get(f"/v3/vod/series/{sid}/seasons")
        seasons = data.get("seasons")
        if not isinstance(seasons, list):
            raise ValueError("Pluto TV returned no readable season list.")
        rows, seen = [], set()
        for season in seasons:
            episodes = season.get("episodes") or []
            expected = number(season.get("totalEpisodesCount"))
            if expected is not None and expected > len(episodes):
                raise ValueError("Pluto TV returned an incomplete season. Please try again.")
            for episode in episodes:
                eid = self.identifier(episode.get("_id") or episode.get("id"))
                if eid in seen:
                    continue
                seen.add(eid)
                # Stable catalogue links, never expiring stream URLs or session tokens.
                rows.append({"url": f"https://pluto.tv/on-demand/series/{sid}/episode/{eid}",
                             "id": eid, "season": episode.get("season", season.get("number")),
                             "number": episode.get("number"), "title": episode.get("name") or eid,
                             "dur": (number(episode.get("duration")) or 0) // 1000,
                             "desc": episode.get("description") or episode.get("summary") or "",
                             "img": self.image(episode)})
        if not rows:
            raise ValueError("Pluto TV offers no on-demand episodes of this series here.")
        return {"title": show["title"], "seasons": group_episodes(rows),
                "notice": "Regional on-demand episodes from Pluto TV. Connect Proton VPN before searching and stay connected while downloading."}

    def stream(self, episode_id):
        eid = self.identifier(episode_id)
        boot = self.boot()
        server = urlsplit(boot.get("servers", {}).get("stitcher", ""))
        # Only send the guest JWT to a Pluto-controlled endpoint.
        if server.scheme != "https" or not (server.hostname or "").endswith(".pluto.tv") or server.username or server.password:
            raise ValueError("Pluto TV returned no valid playback server.")
        params = dict(parse_qsl(boot.get("stitcherParams") or ""))
        params.update(jwt=boot["sessionToken"], masterJWTPassthrough="true", includeExtendedEvents="true")
        return urlunsplit((server.scheme, server.netloc, f"/v2/stitch/hls/episode/{eid}/master.m3u8", urlencode(params), ""))
