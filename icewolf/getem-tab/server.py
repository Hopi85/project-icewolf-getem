"""Getem server mode (Icewolf "GetemTab" test fork).

Runs Getem's existing API behind a small local web server, so the interface can live in an
Icewolf tab instead of its own window. Started with:  pythonw app.py --server

Security:
- listens on 127.0.0.1 only (not reachable from the network)
- a random token per start; every request must carry it (pages from other sites cannot read it)
- the Host header must match exactly (protects against DNS rebinding)
- API calls need a custom header + JSON body, which forces a CORS preflight that this server
  never approves, so other websites cannot call it
- only the functions the Getem interface uses are reachable
- stops by itself when the Getem tab is gone and no download is running
"""
import hmac
import json
import os
import secrets
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlsplit

ALLOWED = {
    "settings", "poll", "search", "load_show", "thumbnail", "have", "enqueue", "add_links",
    "cancel", "cancel_all", "retry", "clear_finished", "set_quality", "set_format",
    "choose_folder", "open_folder",
}
IDLE_SECONDS = 45          # no request from a Getem tab for this long ...
START_GRACE_SECONDS = 300  # ... or never contacted after start (first start installs components)
MAX_BODY = 1_000_000

BRIDGE = """<script>
/* Icewolf: talks to the local Getem server instead of the pywebview window bridge */
(() => {
  const token = new URLSearchParams(location.search).get("t") || "";
  const call = name => async (...args) => {
    const r = await fetch("/api/" + name, {
      method: "POST", cache: "no-store",
      headers: {"Content-Type": "application/json", "X-Getem-Token": token},
      body: JSON.stringify(args)
    });
    if (!r.ok) throw new Error("Getem " + r.status);
    return r.json();
  };
  const api = new Proxy({}, {get: (_, name) => name === "then" ? undefined : call(String(name))});
  window.pywebview = {api};
})();
</script>
"""


def _pick_folder(initial):
    """Windows folder dialog (tkinter in a short-lived helper process, no console window)."""
    code = ("import sys, tkinter as t, tkinter.filedialog as f\n"
            "r = t.Tk(); r.withdraw(); r.attributes('-topmost', True)\n"
            "p = f.askdirectory(initialdir=sys.argv[1] or None, title='Getem - Zielordner')\n"
            "sys.stdout.write(p or '')\n")
    try:
        result = subprocess.run([sys.executable, "-c", code, initial or ""], capture_output=True, text=True,
                                encoding="utf-8", errors="replace", timeout=900,
                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        return result.stdout.strip() or None
    except (OSError, subprocess.SubprocessError):
        return None


def run(api, root):
    token = secrets.token_urlsafe(32)
    cache = root / ".cache"
    cache.mkdir(exist_ok=True)
    info_file = cache / "server.json"
    html = (root / "ui" / "index.html").read_text(encoding="utf-8")
    first = html.find("<script")
    page = (html[:first] + BRIDGE + html[first:]) if first >= 0 else html + BRIDGE
    page_bytes = page.encode("utf-8")

    state = {"port": 0, "last": time.time(), "contacted": False}

    def touch():
        state["last"] = time.time()
        state["contacted"] = True

    def choose_folder():
        picked = _pick_folder(api._settings.get("folder"))
        return api._save(folder=picked) if picked else api.settings()

    class Handler(BaseHTTPRequestHandler):
        server_version = "Getem"
        sys_version = ""

        def log_message(self, *args):
            pass

        def _send(self, code, body, ctype="application/json; charset=utf-8"):
            data = body if isinstance(body, bytes) else body.encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("Referrer-Policy", "no-referrer")   # the token never leaks to image hosts
            self.send_header("X-Content-Type-Options", "nosniff")
            self.send_header("X-Frame-Options", "DENY")
            self.end_headers()
            self.wfile.write(data)

        def _host_ok(self):
            return self.headers.get("Host") == "127.0.0.1:%d" % state["port"]

        def _token_ok(self, value):
            return hmac.compare_digest((value or "").encode(), token.encode())

        def do_GET(self):
            if not self._host_ok():
                return self._send(403, "{}")
            url = urlsplit(self.path)
            if url.path == "/ping":
                if self._token_ok(self.headers.get("X-Getem-Token")):
                    return self._send(200, '{"ok":true}')
                return self._send(403, "{}")
            if url.path in ("/", "/index.html"):
                if self._token_ok((parse_qs(url.query).get("t") or [""])[0]):
                    touch()
                    return self._send(200, page_bytes, "text/html; charset=utf-8")
                return self._send(403, "<!doctype html><title>Getem</title><p>Bitte Getem über das Icewolf-Menü öffnen.</p>",
                                  "text/html; charset=utf-8")
            return self._send(404, "{}")

        def do_POST(self):
            if not self._host_ok():
                return self._send(403, "{}")
            # The Getem tab sends its own origin; the browser's own button sends none or "null".
            # Every other origin is rejected (and could not know the key anyway).
            origin = self.headers.get("Origin")
            if origin not in (None, "null", "http://127.0.0.1:%d" % state["port"]):
                return self._send(403, "{}")
            if not self._token_ok(self.headers.get("X-Getem-Token")):
                return self._send(403, "{}")
            if not (self.headers.get("Content-Type") or "").startswith("application/json"):
                return self._send(415, "{}")
            url = urlsplit(self.path)
            name = url.path[5:] if url.path.startswith("/api/") else ""
            if name not in ALLOWED:
                return self._send(404, "{}")
            try:
                length = int(self.headers.get("Content-Length") or 0)
            except ValueError:
                return self._send(400, "{}")
            if length < 0 or length > MAX_BODY:
                return self._send(413, "{}")
            try:
                args = json.loads(self.rfile.read(length) or b"[]")
            except ValueError:
                return self._send(400, "{}")
            if not isinstance(args, list):
                return self._send(400, "{}")
            touch()
            try:
                result = choose_folder() if name == "choose_folder" else getattr(api, name)(*args)
                return self._send(200, json.dumps(result, ensure_ascii=False))
            except Exception as exc:  # report to the page instead of crashing the server
                return self._send(500, json.dumps({"error": str(exc)}, ensure_ascii=False))

        def do_OPTIONS(self):
            # Never approve cross-origin preflight requests.
            return self._send(403, "{}")

    httpd = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    httpd.daemon_threads = True
    state["port"] = httpd.server_address[1]

    tmp = info_file.with_suffix(".tmp")
    tmp.write_text(json.dumps({"port": state["port"], "token": token, "pid": os.getpid()}), encoding="utf-8")
    os.replace(tmp, info_file)

    def busy():
        try:
            return any(item.get("status") in ("wait", "run") for item in api._queue.snapshot()["items"])
        except Exception:
            return False

    def watchdog():
        while True:
            time.sleep(5)
            limit = IDLE_SECONDS if state["contacted"] else START_GRACE_SECONDS
            if not busy() and time.time() - state["last"] > limit:
                httpd.shutdown()
                return

    threading.Thread(target=watchdog, daemon=True).start()
    try:
        httpd.serve_forever(poll_interval=0.5)
    finally:
        httpd.server_close()
        try:
            if json.loads(info_file.read_text(encoding="utf-8")).get("token") == token:
                info_file.unlink()
        except (OSError, ValueError):
            pass
        api._close()
