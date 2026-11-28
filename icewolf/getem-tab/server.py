"""Getem server mode (Icewolf Getem Edition: Getem inside a browser tab).

Runs Getem's existing API behind a small local web server, so the interface can live in an
Icewolf tab instead of its own window. Started with:  pythonw app.py --server

Security:
- listens on 127.0.0.1 only (not reachable from the network)
- a random token per start, known only to Icewolf (read from .cache/server.json); the browser's
  own toolbar button sends it in a header
- the Getem tab never sees the token: Icewolf opens it with a one-time code (useless once used),
  which the server swaps for an HttpOnly, SameSite=Lax session cookie - so the token is never in
  the address bar, the history or the session restore data
- the page is served with a strict Content-Security-Policy (only its own scripts, with a fresh nonce)
- the Host header must match exactly (protects against DNS rebinding)
- API calls need a custom header + JSON body, which forces a CORS preflight that this server
  never approves, so other websites cannot call it
- only the functions the Getem interface uses are reachable
- stops by itself when the Getem tab is gone and no download is running
"""
import hmac
import re
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
CODE_SECONDS = 60          # a one-time code for opening the tab is valid this long

BRIDGE = """<script>
/* Icewolf: talks to the local Getem server instead of the pywebview window bridge */
(() => {
  const call = name => async (...args) => {
    const r = await fetch("/api/" + name, {
      method: "POST", cache: "no-store", credentials: "same-origin",
      headers: {"Content-Type": "application/json", "X-Getem": "1"},
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
            "p = f.askdirectory(initialdir=sys.argv[1] or None, title='Getem - download folder')\n"
            "sys.stdout.write(p or '')\n")
    try:
        result = subprocess.run([sys.executable, "-c", code, initial or ""], capture_output=True, text=True,
                                encoding="utf-8", errors="replace", timeout=900,
                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        return result.stdout.strip() or None
    except (OSError, subprocess.SubprocessError):
        return None


def _watch_browser(pid, on_closed):
    """Calls on_closed() as soon as the Icewolf process `pid` has ended (Windows).
    The process handle is opened while the browser runs, so a re-used process number
    can never be mistaken for it."""
    if os.name != "nt" or not pid:
        return False
    import ctypes
    from ctypes import wintypes
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.OpenProcess.restype = wintypes.HANDLE
    k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    k32.WaitForSingleObject.restype = wintypes.DWORD
    k32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    k32.CloseHandle.argtypes = [wintypes.HANDLE]
    SYNCHRONIZE = 0x00100000
    handle = k32.OpenProcess(SYNCHRONIZE, False, pid)
    if not handle:
        return False                      # browser already gone / not visible: idle timer still applies

    def wait():
        try:
            while k32.WaitForSingleObject(handle, 1000) != 0:   # 0 = process has ended
                pass
            on_closed()
        finally:
            k32.CloseHandle(handle)

    threading.Thread(target=wait, daemon=True).start()
    return True


def run(api, root, browser_pid=None):
    token = secrets.token_urlsafe(32)           # for Icewolf's own requests (header)
    session = secrets.token_urlsafe(32)         # for the Getem tab (HttpOnly cookie)
    codes = {}                                  # one-time code -> expiry time
    codes_lock = threading.Lock()
    cache = root / ".cache"
    cache.mkdir(exist_ok=True)
    info_file = cache / "server.json"
    html = (root / "ui" / "index.html").read_text(encoding="utf-8")
    first = html.find("<script")
    page = (html[:first] + BRIDGE + html[first:]) if first >= 0 else html + BRIDGE

    def page_for(nonce):
        return re.sub(r"<script(?=[\s>])", '<script nonce="%s"' % nonce, page).encode("utf-8")

    def csp(nonce):
        return ("default-src 'none'; script-src 'nonce-%s'; style-src 'self' 'unsafe-inline'; "
                "img-src https: http: data: blob:; connect-src 'self'; font-src 'self' data:; "
                "base-uri 'none'; form-action 'none'; frame-ancestors 'none'" % nonce)

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

        def _send(self, code, body, ctype="application/json; charset=utf-8", extra=None):
            data = body if isinstance(body, bytes) else body.encode("utf-8")
            self.send_response(code)
            for name, value in (extra or {}).items():
                self.send_header(name, value)
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

        def _origin(self):
            return "http://127.0.0.1:%d" % state["port"]

        def _token_ok(self, value):
            return hmac.compare_digest((value or "").encode(), token.encode())

        def _cookie_name(self):
            return "getem_%d" % state["port"]   # cookies are shared by all ports of a host

        def _session_ok(self):
            for part in (self.headers.get("Cookie") or "").split(";"):
                name, _, value = part.strip().partition("=")
                if name == self._cookie_name() and hmac.compare_digest(value.encode(), session.encode()):
                    return True
            return False

        def _new_code(self):
            code = secrets.token_urlsafe(24)
            now = time.time()
            with codes_lock:
                for c in [c for c, t in codes.items() if t < now]:
                    del codes[c]
                codes[code] = now + CODE_SECONDS
            return code

        def _use_code(self, code):
            if not code:
                return False
            with codes_lock:
                expiry = codes.pop(code, None)      # single use
            return expiry is not None and expiry >= time.time()

        def _authorized_post(self):
            """Icewolf's own requests: token header (they carry no Origin, or "null").
            The Getem tab: session cookie + its own Origin + the custom header."""
            origin = self.headers.get("Origin")
            if self._token_ok(self.headers.get("X-Getem-Token")):
                return origin in (None, "null", self._origin())
            return (origin == self._origin() and self.headers.get("X-Getem") == "1" and self._session_ok())

        def do_GET(self):
            if not self._host_ok():
                return self._send(403, "{}")
            url = urlsplit(self.path)
            if url.path == "/ping":
                if self._token_ok(self.headers.get("X-Getem-Token")):
                    return self._send(200, '{"ok":true}')
                return self._send(403, "{}")
            if url.path in ("/", "/index.html"):
                code = (parse_qs(url.query).get("c") or [""])[0]
                if code:
                    if not self._use_code(code):
                        return self._send(403, "<!doctype html><title>Getem</title><p>This link has expired. "
                                          "Please open Getem with its Icewolf button or menu entry.</p>",
                                          "text/html; charset=utf-8")
                    # swap the one-time code for a session cookie and drop the code from the address bar
                    touch()
                    cookie = "%s=%s; Path=/; HttpOnly; SameSite=Lax" % (self._cookie_name(), session)
                    return self._send(303, "", "text/plain", {"Location": "/", "Set-Cookie": cookie})
                if self._session_ok():
                    touch()
                    nonce = secrets.token_urlsafe(18)
                    return self._send(200, page_for(nonce), "text/html; charset=utf-8",
                                      {"Content-Security-Policy": csp(nonce)})
                return self._send(403, "<!doctype html><title>Getem</title><p>Please open Getem with its "
                                  "Icewolf button or menu entry.</p>", "text/html; charset=utf-8")
            return self._send(404, "{}")

        def do_POST(self):
            if not self._host_ok():
                return self._send(403, "{}")
            if not self._authorized_post():
                return self._send(403, "{}")
            if not (self.headers.get("Content-Type") or "").startswith("application/json"):
                return self._send(415, "{}")
            url = urlsplit(self.path)
            if url.path == "/session":
                # Icewolf asks for a one-time code to open the Getem tab with (token header only)
                if not self._token_ok(self.headers.get("X-Getem-Token")):
                    return self._send(403, "{}")
                return self._send(200, json.dumps({"code": self._new_code()}))
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

    # Icewolf closed -> Getem stops right away (running downloads are cancelled).
    def browser_closed():
        print("Icewolf was closed - stopping Getem")
        httpd.shutdown()
    if _watch_browser(browser_pid, browser_closed):
        print("watching Icewolf process", browser_pid)

    try:
        httpd.serve_forever(poll_interval=0.5)
    finally:
        httpd.server_close()
        try:
            if json.loads(info_file.read_text(encoding="utf-8")).get("token") == token:
                info_file.unlink()
        except (OSError, ValueError):
            pass
        # stop the download queue, but never hang on it
        closer = threading.Thread(target=api._close, daemon=True)
        closer.start()
        closer.join(5)
        sys.stdout.flush()
        # End the whole process now: leftover download threads would otherwise keep Python
        # running in the background. Helper programs (e.g. ffmpeg) end with it (job object).
        os._exit(0)
