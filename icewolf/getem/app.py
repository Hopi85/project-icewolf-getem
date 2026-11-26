"""Small local video downloader. Run through Start.cmd on Windows."""
from collections import deque
from concurrent.futures import ThreadPoolExecutor
import itertools
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import threading
from urllib.parse import urlsplit

import catalog

ROOT = Path(__file__).resolve().parent
CACHE = ROOT / ".cache"
QUALITIES = {"Beste Qualität": None, "Bis 1080p": 1080, "Bis 720p": 720, "Bis 480p": 480}
FORMATS = ("MP4", "MKV", "MP3 (nur Ton)")
SEARCH_PROVIDERS = ("tubi", "ORF", "ARD", "ZDF", "3sat", "plex", "pluto")
NO_WINDOW = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
FINISHED_FILE = re.compile(r"\[([^\]]+)\]\.(?:mkv|mp4|webm|mov|avi|m4v|ts|mp3)$", re.I)


def validate_url(value):
    value = value.strip()
    parsed = urlsplit(value)
    host = (parsed.hostname or "").lower()
    # Accessing port also validates malformed/out-of-range port values.
    parsed.port
    if (parsed.scheme not in ("http", "https") or not host
            or parsed.username is not None or parsed.password is not None
            or any(char.isspace() or ord(char) < 32 for char in value)
            or "\\" in parsed.netloc):
        raise ValueError("Bitte einen vollständigen Weblink mit https:// oder http:// einfügen.")
    return value


def parse_urls(text):
    urls = []
    for number, line in enumerate(text.splitlines(), 1):
        if not line.strip():
            continue
        try:
            if any(char.isspace() for char in line.strip()):
                raise ValueError("Bitte nur einen Link pro Zeile eingeben.")
            url = validate_url(line)
        except ValueError as exc:
            raise ValueError(f"Zeile {number}: {exc}") from exc
        if url not in urls:
            urls.append(url)
    if not urls:
        raise ValueError("Bitte mindestens einen Videolink einfügen.")
    return urls


def parse_progress(line):
    fields = line.removeprefix("GETEM:").split("|", 2)
    match = re.search(r"([\d.]+)%", fields[0])
    percent = min(100, max(0, float(match[1]))) if match else None
    speed = fields[1].strip() if len(fields) > 1 else ""
    eta = fields[2].strip() if len(fields) > 2 else ""
    speed = speed if speed not in ("", "NA", "Unknown") else "wird ermittelt"
    eta = eta if eta not in ("", "NA", "Unknown") else "wird ermittelt"
    return percent, speed, eta


def format_args(fmt, height):
    limit = f"[height<={height}]" if height else ""
    # The quality setting is a ceiling; without anything below it, take the smallest version.
    selector = f"bv*{limit}+ba/b{limit}/wv*+ba/w" if height else "bv*+ba/b"
    if fmt == "MKV":
        return ["-f", selector, "--merge-output-format", "mkv", "--remux-video", "mkv"]
    if fmt == FORMATS[2]:
        return ["-f", "ba/b", "-x", "--audio-format", "mp3", "--audio-quality", "0"]
    # MP4 exists for compatibility, so prefer codecs every player handles.
    return ["-f", selector, "-S", "res,vcodec:h264,acodec:aac", "--merge-output-format", "mp4/mkv"]


def command(url, probe=False, folder=None, quality="Beste Qualität", fmt="MP4"):
    args = [sys.executable, "-m", "yt_dlp", "--ignore-config", "--no-playlist",
            "--no-colors", "--newline", "--socket-timeout", "20", "--retries", "3"]
    if re.fullmatch(r"https://vod\.provider\.plex\.tv/library/metadata/[\w-]+", url):
        args = [sys.executable, str(ROOT / "plex_download.py"), *args[3:]]
    elif (urlsplit(url).hostname or "").lower() in ("pluto.tv", "www.pluto.tv"):
        args = [sys.executable, str(ROOT / "pluto_download.py"), *args[3:]]
    elif not probe and fmt != FORMATS[2]:
        host = (urlsplit(url).hostname or "").lower().rstrip(".")
        if host == "tiktok.com" or host.endswith(".tiktok.com"):
            args = [sys.executable, str(ROOT / "tiktok_download.py"), *args[3:]]
    if shutil.which("node"):
        args += ["--js-runtimes", "node"]
    if probe:
        return args + ["--dump-single-json", "--skip-download", "--", url]
    import imageio_ffmpeg
    tools_dir = ROOT / ".tools"
    tools_dir.mkdir(exist_ok=True)
    ffmpeg = tools_dir / ("ffmpeg.exe" if os.name == "nt" else "ffmpeg")
    if not ffmpeg.exists():
        shutil.copy2(imageio_ffmpeg.get_ffmpeg_exe(), ffmpeg)
    return args + ["--ffmpeg-location", str(ffmpeg), *format_args(fmt, QUALITIES[quality]),
                   "--windows-filenames", "--trim-filenames", "180",
                   "--concurrent-fragments", "4", "--abort-on-unavailable-fragments",
                   "--progress-delta", "0.5",
                   "--progress-template", "download:GETEM:%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s",
                   "-P", str(folder), "-o", "%(title)s [%(id)s].%(ext)s", "--", url]


def error_text(lines):
    """Turn the last yt-dlp error into a sentence that fits one queue row."""
    errors = [line for line in lines if line.startswith("ERROR")]
    last = (errors or lines or ["Download fehlgeschlagen."])[-1].removeprefix("ERROR:").strip()
    lowered = last.lower()
    if "drm" in lowered:
        return "DRM-geschützt, kein ungeschützter Stream verfügbar."
    if "unsupported url" in lowered:
        return "Kein unterstütztes Video auf dieser Seite."
    if any(term in lowered for term in ("sign in", "log in", "login required", "authentication required", "http error 401")):
        return "Anmeldung erforderlich. Dieser Link ist ohne Anmeldung nicht verfügbar."
    if any(term in lowered for term in ("http error 403", "http error 429", "captcha", "not a bot")):
        return "Die Website blockiert den Zugriff oder begrenzt die Anfragen. Bitte später erneut versuchen."
    if any(term in lowered for term in ("timed out", "timeout", "getaddrinfo failed", "name resolution", "unable to download webpage", "connection refused")):
        return "Website nicht erreichbar. Bitte Verbindung prüfen und erneut versuchen."
    if "geo restricted" in lowered or "not available in your country" in lowered:
        return "In dieser Region nicht verfügbar."
    if any(term in lowered for term in ("http error 404", "video unavailable", "video has been removed")):
        return "Video nicht mehr verfügbar oder Link nicht gefunden."
    if any(term in lowered for term in ("no video formats", "requested format is not available", "no media found")):
        return "Kein passender ungeschützter Stream gefunden."
    return safe_download_log(last)[:160]


def safe_download_log(line):
    # Download tools can include signed manifest URLs in warnings. Preserve the
    # useful host/path but never expose the query credentials in the UI log.
    return re.sub(r"(https?://[^\s?]+)\?[^\s]+", r"\1?[redacted]", line)


def finished_ids(folder):
    """IDs of completed downloads. Partial files never end in ``[id].ext``."""
    try:
        names = os.listdir(folder)
    except OSError:
        return []
    return sorted({match[1] for match in map(FINISHED_FILE.search, names) if match})


class DownloadQueue:
    """Runs one download at a time. A cancelled or failed item never stops the next one."""

    def __init__(self, build_command=command):
        self.build_command = build_command
        self.lock = threading.Lock()
        self.wake = threading.Condition(self.lock)
        self.items = []
        self.ids = itertools.count(1)
        self.process = None
        self.current = None
        self.stopping = False
        self.log = deque(maxlen=200)
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def add(self, entry, folder, quality, fmt="MP4"):
        with self.wake:
            item = {"id": next(self.ids), "url": entry["url"], "title": entry.get("title") or entry["url"],
                    "show": entry.get("show") or "", "img": entry.get("img"), "page": entry.get("page"),
                    "folder": str(folder), "quality": quality, "format": fmt, "status": "wait", "pct": None,
                    "speed": "", "eta": "", "note": "", "error": "", "cancel": False}
            self.items.append(item)
            self.wake.notify_all()
            return item["id"]

    def update(self, item_id, **fields):
        with self.lock:
            for item in self.items:
                if item["id"] == item_id:
                    item.update(fields)

    def snapshot(self):
        with self.lock:
            return {"items": [{key: value for key, value in item.items() if key != "cancel"} for item in self.items],
                    "log": list(self.log)}

    def cancel(self, item_id):
        """Waiting items leave the list; the running item stops and the next one starts."""
        with self.lock:
            item = next((i for i in self.items if i["id"] == item_id), None)
            if item is None:
                return
            if item["status"] == "wait":
                self.items.remove(item)
                return
            if item["status"] != "run":
                return
            item["cancel"] = True
            process = self.process if self.current is item else None
        self.kill(process)

    def cancel_all(self):
        with self.lock:
            self.items = [item for item in self.items if item["status"] != "wait"]
            process = None
            if self.current is not None:
                self.current["cancel"] = True
                process = self.process
        self.kill(process)

    def retry(self, item_id):
        with self.wake:
            for item in self.items:
                if item["id"] == item_id and item["status"] in ("failed", "cancelled"):
                    item.update(status="wait", pct=None, speed="", eta="", note="", error="", cancel=False)
                    self.wake.notify_all()

    def clear_finished(self):
        with self.lock:
            self.items = [item for item in self.items if item["status"] in ("wait", "run")]

    def shutdown(self):
        with self.wake:
            self.stopping = True
            self.wake.notify_all()
        self.cancel_all()

    @staticmethod
    def kill(process):
        if process and process.poll() is None:
            if os.name == "nt":
                subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                               capture_output=True, creationflags=NO_WINDOW)
            else:
                process.terminate()

    def run(self):
        while True:
            with self.wake:
                while not self.stopping and not any(i["status"] == "wait" for i in self.items):
                    self.wake.wait()
                if self.stopping:
                    return
                item = next(i for i in self.items if i["status"] == "wait")
                item["status"] = "run"
                self.current = item
                self.log.append(f"Start: {item['title']}")
            try:
                ok, lines = self.download(item)
            except Exception as exc:
                ok, lines = False, [str(exc)]
            with self.lock:
                self.process = None
                self.current = None
                if item["cancel"]:
                    item["status"] = "cancelled"
                elif ok:
                    item.update(status="done", pct=100)
                else:
                    item.update(status="failed", error=error_text(lines))
                item.update(speed="", eta="", note="")
                self.log.append({"cancelled": "Abgebrochen", "done": "Fertig", "failed": "Fehlgeschlagen"}[item["status"]]
                                + f": {item['title']}")

    def download(self, item):
        folder = Path(item["folder"]).expanduser().resolve()
        folder.mkdir(parents=True, exist_ok=True)
        args = self.build_command(item["url"], False, folder, item["quality"], item["format"])
        process = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                                   encoding="utf-8", errors="replace", creationflags=NO_WINDOW,
                                   env={**os.environ, "PYTHONIOENCODING": "utf-8"})
        with self.lock:
            self.process = process
            cancelled = item["cancel"]
        if cancelled:
            self.kill(process)
        lines = []
        try:
            for line in process.stdout:
                line = safe_download_log(line.strip())
                if line.startswith("GETEM:"):
                    percent, speed, eta = parse_progress(line)
                    with self.lock:
                        item.update(speed=speed, eta=eta, note="", **({} if percent is None else {"pct": percent}))
                elif line:
                    lines.append(line)
                    with self.lock:
                        self.log.append(line[:300])
                        if line.startswith("[Merger]"):
                            item["note"] = "Wird zusammengeführt"
            return process.wait() == 0, lines
        finally:
            process.stdout.close()


class Api:
    """Methods the page calls through pywebview. Attributes stay private so pywebview ignores them."""

    def __init__(self):
        self._window = None
        self._queue = DownloadQueue()
        self._events = deque()
        self._pool = ThreadPoolExecutor(8)
        self._thumb_pool = ThreadPoolExecutor(5)
        self._thumbs = self._read_json("thumbs.json")
        self._thumbs_lock = threading.Lock()
        self._pending = set()
        self._settings = {"folder": str(ROOT / "downloads"), "quality": "Bis 1080p", "format": FORMATS[0],
                          **self._read_json("settings.json")}
        if self._settings["format"] not in FORMATS:
            self._settings["format"] = FORMATS[0]
        if self._settings["quality"] not in QUALITIES:
            self._settings["quality"] = "Bis 1080p"

    @staticmethod
    def _read_json(name):
        try:
            return json.loads((CACHE / name).read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return {}

    @staticmethod
    def _write_json(name, data):
        CACHE.mkdir(exist_ok=True)
        (CACHE / name).write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")

    def _save(self, **changes):
        self._settings.update(changes)
        self._write_json("settings.json", self._settings)
        return self.settings()

    def settings(self):
        return {**self._settings, "qualities": list(QUALITIES), "formats": list(FORMATS), "providers": list(SEARCH_PROVIDERS)}

    def poll(self):
        events = []
        while self._events:
            events.append(self._events.popleft())
        return {"events": events, "queue": self._queue.snapshot()}

    def search(self, token, query):
        query = str(query).strip()
        for provider in SEARCH_PROVIDERS:
            self._pool.submit(self._search_one, token, provider, query)
        return True

    def _search_one(self, token, provider, query):
        event = {"type": "provider", "token": token, "provider": provider}
        try:
            event.update(catalog.client_for(provider).search(query))
        except Exception as exc:
            event.update(error=catalog.friendly_error(exc))
        self._events.append(event)

    def load_show(self, token, show):
        self._pool.submit(self._load_show, token, show)
        return True

    def _load_show(self, token, show):
        event = {"type": "show", "token": token}
        try:
            event["data"] = catalog.client_for(show["provider"]).episodes(show)
        except Exception as exc:
            event["error"] = catalog.friendly_error(exc)
        self._events.append(event)

    def thumbnail(self, page):
        """Reply through poll(), so results never hold up the list."""
        with self._thumbs_lock:
            if page in self._thumbs:
                self._events.append({"type": "thumb", "page": page, "image": self._thumbs[page]})
            elif page not in self._pending:
                self._pending.add(page)
                self._thumb_pool.submit(self._load_thumbnail, page)
        return True

    def _load_thumbnail(self, page):
        try:
            image = catalog.page_image(page)
        except Exception:
            image = None
        with self._thumbs_lock:
            self._pending.discard(page)
            if image:
                self._thumbs[page] = image
                self._write_json("thumbs.json", self._thumbs)
        self._events.append({"type": "thumb", "page": page, "image": image})

    def _target(self):
        return self._settings["folder"], self._settings["quality"], self._settings["format"]

    def have(self):
        return finished_ids(self._settings["folder"])

    def enqueue(self, entries):
        added = 0
        for entry in entries:
            try:
                entry["url"] = validate_url(entry["url"])
            except (KeyError, ValueError, AttributeError):
                continue
            self._queue.add(entry, *self._target())
            added += 1
        return added

    def add_links(self, text):
        try:
            urls = parse_urls(text)
            if any("*" in urlsplit(url).path for url in urls):
                raise ValueError("Bitte einzelne Videolinks verwenden, keine Platzhalter.")
        except ValueError as exc:
            return {"error": str(exc)}
        target = self._target()
        added, failures = 0, []
        for url, result in zip(urls, self._pool.map(self._probe, urls)):
            if "error" in result:
                failures.append({"url": url, "error": result["error"]})
            else:
                self._queue.add(result, *target)
                added += 1
        return {"added": added, "failures": failures}

    def _probe(self, url):
        """Check extraction without downloading; retain the original URL for fresh streams."""
        try:
            result = subprocess.run(command(url, probe=True), capture_output=True, text=True, encoding="utf-8",
                                    errors="replace", timeout=60, creationflags=NO_WINDOW,
                                    env={**os.environ, "PYTHONIOENCODING": "utf-8"})
            if result.returncode:
                return {"error": error_text(result.stderr.splitlines() or result.stdout.splitlines())}
            info = json.loads(next(line for line in result.stdout.splitlines() if line.startswith("{")))
            if not isinstance(info, dict):
                raise ValueError("Invalid metadata")
        except subprocess.TimeoutExpired:
            return {"error": "Die Linkprüfung dauert zu lange. Bitte erneut versuchen."}
        except OSError:
            return {"error": "Linkprüfung konnte nicht gestartet werden. Bitte Aktualisieren.cmd ausführen."}
        except (StopIteration, ValueError, subprocess.SubprocessError):
            return {"error": "Keine gültigen Videoinformationen erhalten. Bitte Aktualisieren.cmd ausführen und erneut versuchen."}
        if info.get("_type") in ("playlist", "multi_video"):
            return {"error": "Bitte einen einzelnen Videolink statt einer Sammlung verwenden."}
        if info.get("has_drm"):
            return {"error": "DRM-geschützt, kein ungeschützter Stream verfügbar."}
        if not info.get("url") and not any(f.get("url") and not f.get("has_drm") for f in info.get("formats") or []):
            return {"error": "Kein passender ungeschützter Stream gefunden."}
        image = info.get("thumbnail")
        return {"url": url, "title": info.get("title") or urlsplit(url).hostname,
                "img": image if catalog.web_url(image) else None}

    def cancel(self, item_id):
        self._queue.cancel(item_id)

    def cancel_all(self):
        self._queue.cancel_all()

    def retry(self, item_id):
        self._queue.retry(item_id)

    def clear_finished(self):
        self._queue.clear_finished()

    def set_quality(self, quality):
        return self._save(quality=quality) if quality in QUALITIES else self.settings()

    def set_format(self, fmt):
        return self._save(format=fmt) if fmt in FORMATS else self.settings()

    def choose_folder(self):
        import webview
        picked = self._window.create_file_dialog(webview.FileDialog.FOLDER)
        return self._save(folder=picked[0]) if picked else self.settings()

    def open_folder(self):
        folder = Path(self._settings["folder"]).expanduser().resolve()
        folder.mkdir(parents=True, exist_ok=True)
        os.startfile(str(folder))

    def _close(self):
        self._queue.shutdown()


def main():
    import webview
    api = Api()
    api._window = webview.create_window("Getem", str(ROOT / "ui" / "index.html"), js_api=api, width=1500, height=920,
                                        min_size=(1120, 640), background_color="#F1F2F4")
    api._window.events.closing += api._close
    webview.start()


if __name__ == "__main__":
    main()
