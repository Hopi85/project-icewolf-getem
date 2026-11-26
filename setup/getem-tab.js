/* ICEWOLF-GETEM-BEGIN
 * Icewolf "GetemTab" test fork:
 *  - Main menu > Getem opens Getem inside an Icewolf tab (Shift+click: update components)
 *  - Toolbar button next to uBlock Origin: one click sends the current page to Getem
 *  - One-time restore of the Downloads / "Forget" toolbar buttons
 * Getem runs as a local server on 127.0.0.1 with a random per-start key (see getem\server.py).
 */
try {
  (function () {
    const { classes: Cc, interfaces: Ci, utils: Cu } = Components;
    const Svc = (typeof Services !== "undefined") ? Services : null;
    if (!Svc) return;

    const ITEM = "appMenu-getem-button";
    const GRAB = "icewolf-getem-grab";
    const GRAB_PLACED = "icewolf.getemgrab.placed";
    const TOOLBAR_PREF = "icewolf.toolbar.restored.1";
    const GRAB_TIP = "Send this page to Getem (one click)";
    const GRAB_ICON = "data:image/png;base64,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";   // red circle, yellow "G" (64 px PNG)

    // ------------------------------------------------------------ log (profile\getem-button-log.txt)
    function log(msg) {
      try {
        const f = Svc.dirsvc.get("ProfD", Ci.nsIFile); f.append("getem-button-log.txt");
        if (f.exists() && f.fileSize > 256 * 1024) f.remove(false);
        const fos = Cc["@mozilla.org/network/file-output-stream;1"].createInstance(Ci.nsIFileOutputStream);
        fos.init(f, 0x02 | 0x08 | 0x10, 0o600, 0);          // write | create | append
        const out = Cc["@mozilla.org/intl/converter-output-stream;1"].createInstance(Ci.nsIConverterOutputStream);
        out.init(fos, "UTF-8");
        out.writeString(new Date().toISOString() + "  " + msg + "\r\n");
        out.close();
      } catch (e) { }
    }
    async function readText(win, file) {
      try { return await win.IOUtils.readUTF8(file.path); } catch (e) { return ""; }
    }
    function cacheFile(name) { const f = getemDir(); f.append(".cache"); f.append(name); return f; }
    function lastLines(text, n) { return text.trim().split(/\r?\n/).slice(-n).join("\n"); }
    function showError(win, text) {
      try { Svc.prompt.alert(win, "Getem", text); } catch (e) { }
    }

    // ------------------------------------------------------------ paths & helper
    function rootDir() { return Svc.dirsvc.get("GreD", Ci.nsIFile).parent; }
    function helper() { const f = rootDir(); f.append("getem-host"); f.append("GetemHost.exe"); return f; }
    function getemDir() {
      const p = rootDir(); p.append("getem");
      const app = p.clone(); app.append("app.py");
      if (app.exists()) return p;
      const l = Svc.dirsvc.get("LocalAppData", Ci.nsIFile); l.append("Icewolf"); l.append("Getem");
      return l;
    }
    // Runs GetemHost.exe and resolves with its exit code (0 = started).
    function runHelper(arg) {
      return new Promise((resolve, reject) => {
        const exe = helper();
        log("start helper " + arg + " (" + exe.path + ", exists=" + exe.exists() + ")");
        if (!exe.exists()) { reject(new Error("The Getem helper is missing:\n" + exe.path)); return; }
        const proc = Cc["@mozilla.org/process/util;1"].createInstance(Ci.nsIProcess);
        proc.init(exe);
        proc.runwAsync([arg], 1, {
          observe(subject, topic) {
            if (topic === "process-finished") { log("helper exit code " + proc.exitValue); resolve(proc.exitValue); }
            else reject(new Error("The Getem helper could not be started (" + topic + ")."));
          }
        });
      });
    }

    // ------------------------------------------------------------ local Getem server
    async function readServer(win) {
      const f = getemDir(); f.append(".cache"); f.append("server.json");
      try {
        const info = await win.IOUtils.readJSON(f.path);
        if (info && Number.isInteger(info.port) && typeof info.token === "string" && info.token.length >= 20) return info;
      } catch (e) { }
      return null;
    }

    async function alive(win, info) {
      try {
        const ctrl = new win.AbortController();
        const timer = win.setTimeout(() => ctrl.abort(), 1500);
        const r = await win.fetch("http://127.0.0.1:" + info.port + "/ping",
          { headers: { "X-Getem-Token": info.token }, signal: ctrl.signal, cache: "no-store" });
        win.clearTimeout(timer);
        return r.ok;
      } catch (e) { return false; }
    }

    // Starts Getem once and waits until it is really running. The first start installs
    // Getem's components (a console window shows the progress) - that can take several minutes.
    let starting = null;
    async function ensureServer(win, onWait) {
      const old = await readServer(win);
      if (old && await alive(win, old)) return old;
      log("Getem not running (getem folder: " + getemDir().path + ") - starting it");
      if (onWait) onWait();
      if (!starting) {
        starting = (async () => {
          const code = await runHelper("/serve");
          const hostLog = await readText(win, cacheFile("host-log.txt"));
          if (code !== 0) {
            const errors = hostLog.split(/\r?\n/).filter((l) => l.indexOf("ERROR ") >= 0);
            throw new Error(errors.length ? errors[errors.length - 1].replace(/^.*?ERROR /, "")
                                          : "The Getem helper failed (exit code " + code + ").");
          }
          const startedAt = Date.now();
          for (let i = 0; i < 1800; i++) {          // up to 15 minutes (first start installs components)
            await new Promise((r) => win.setTimeout(r, 500));
            const info = await readServer(win);
            if (info && (!old || info.token !== old.token) && await alive(win, info)) {
              log("Getem is running on port " + info.port);
              return info;
            }
            if (i % 4 === 3) {                         // every 2 s: did the Getem server crash?
              const f = cacheFile("server-log.txt");
              let fresh = false;
              try { fresh = f.exists() && f.lastModifiedTime >= startedAt - 2000; } catch (e) { }
              if (fresh) {
                const t = await readText(win, f);
                if (t.indexOf("GETEM-SERVER-FAILED") >= 0)
                  throw new Error("Getem stopped with an error:\n\n" + lastLines(t, 6) +
                                  "\n\n(full text: getem\\.cache\\server-log.txt)");
              }
            }
          }
          throw new Error("Getem did not start within 15 minutes. Is Python 3.10+ installed? Check the installation window.");
        })().finally(() => { starting = null; });
      }
      return starting;
    }

    // An open Getem tab in any Icewolf window (or null).
    function findGetemTab(info) {
      const base = "http://127.0.0.1:" + info.port + "/";
      for (const w of Svc.wm.getEnumerator("navigator:browser")) {
        if (!w.gBrowser) continue;
        for (const tab of w.gBrowser.tabs) {
          try { if (tab.linkedBrowser.currentURI.spec.indexOf(base) === 0) return tab; } catch (e) { }
        }
      }
      return null;
    }

    function openTab(win, info) {
      const base = "http://127.0.0.1:" + info.port + "/";
      for (const tab of win.gBrowser.tabs) {
        if (tab.linkedBrowser.currentURI.spec.indexOf(base) === 0) { win.gBrowser.selectedTab = tab; return; }
      }
      win.gBrowser.selectedTab = win.gBrowser.addTrustedTab(base + "?t=" + encodeURIComponent(info.token));
    }

    async function openGetem(win) {
      try { openTab(win, await ensureServer(win)); }
      catch (e) { log("ERROR " + e.message); showError(win, "Getem could not be opened:\n\n" + e.message); }
    }

    // ------------------------------------------------------------ one-click "send to Getem"
    function flash(button, state, text) {
      if (!button || !button.setAttribute) return;
      const win = browserWindow(button);
      if (!win) return;
      button.setAttribute("getem-state", state);
      button.setAttribute("tooltiptext", text);
      if (button._getemTimer) win.clearTimeout(button._getemTimer);
      if (state !== "busy") {
        button._getemTimer = win.setTimeout(() => {
          button.removeAttribute("getem-state");
          button.setAttribute("tooltiptext", GRAB_TIP);
        }, state === "ok" ? 4000 : 9000);
      }
    }

    async function grab(win, button) {
      if (!win || !win.gBrowser) throw new Error("No browser window found.");
      const uri = win.gBrowser.currentURI;
      if (!uri || (uri.scheme !== "https" && uri.scheme !== "http")) {
        flash(button, "error", "Only web pages can be sent to Getem.");
        return;
      }
      if (uri.host === "127.0.0.1") { flash(button, "error", "This is Getem itself."); return; }
      flash(button, "busy", "Checking the link with Getem…");
      try {
        const info = await ensureServer(win, () =>
          flash(button, "busy", "Starting Getem… the link is sent automatically as soon as it is ready."));
        flash(button, "busy", "Checking the link with Getem…");
        const r = await win.fetch("http://127.0.0.1:" + info.port + "/api/add_links", {
          method: "POST", cache: "no-store",
          headers: { "Content-Type": "application/json", "X-Getem-Token": info.token },
          body: JSON.stringify([uri.spec])
        });
        const res = await r.json();
        log("add_links " + uri.spec + " -> HTTP " + r.status + " " + JSON.stringify(res));
        if (!r.ok || res.error) throw new Error(res.error || ("error " + r.status));
        if (res.failures && res.failures.length) throw new Error(res.failures[0].error);
        flash(button, "ok", "Added to the Getem queue ✓");
        // Getem not open yet? Then open it in a tab (with the link already in the queue).
        if (!findGetemTab(info)) openTab(win, info);
      } catch (e) {
        log("ERROR " + e.message);
        flash(button, "error", "Getem: " + e.message);
        showError(win, "The link could not be sent to Getem:\n\n" + e.message);
      }
    }

    // ------------------------------------------------------------ UI
    // The browser window a toolbar button belongs to (falls back to the active window).
    function browserWindow(node) {
      let win = null;
      try { win = node && node.ownerDocument && node.ownerDocument.defaultView; } catch (e) { }
      if (!win || !win.gBrowser) {
        try { win = node && node.ownerGlobal; } catch (e) { }
      }
      if (!win || !win.gBrowser) win = Svc.wm.getMostRecentWindow("navigator:browser");
      return win;
    }
    function loadCUI() {
      for (const url of ["resource:///modules/CustomizableUI.sys.mjs",
                         "moz-src:///browser/components/customizableui/CustomizableUI.sys.mjs"]) {
        try { return ChromeUtils.importESModule(url).CustomizableUI; } catch (e) { }
      }
      return null;
    }

    let globalDone = false;
    function setupGlobal() {
      if (globalDone) return;
      globalDone = true;
      const CUI = loadCUI();
      if (!CUI) return;

      try {
        CUI.createWidget({
          id: GRAB, type: "button", label: "Send to Getem", tooltiptext: GRAB_TIP,
          defaultArea: CUI.AREA_NAVBAR, removable: true,
          onCreated(node) {
            node.setAttribute("image", GRAB_ICON);
            log("button created");
          },
          onCommand(event) {
            const button = (event && (event.currentTarget || event.target)) || null;
            const win = browserWindow(button);
            log("button clicked");
            grab(win, button).catch((e) => { log("ERROR " + e.message); showError(win, String(e.message || e)); });
          }
        });
      } catch (e) { log("createWidget: " + e.message); }

      if (!Svc.prefs.getBoolPref(GRAB_PLACED, false)) {
        try {
          const ids = CUI.getWidgetIdsInArea(CUI.AREA_NAVBAR);
          const ub = ids.findIndex((id) => id.indexOf("ublock0") === 0);
          if (ub >= 0) CUI.addWidgetToArea(GRAB, CUI.AREA_NAVBAR, ub + 1);
          else CUI.addWidgetToArea(GRAB, CUI.AREA_NAVBAR);
          Svc.prefs.setBoolPref(GRAB_PLACED, true);
        } catch (e) { Cu.reportError(e); }
      }

      if (!Svc.prefs.getBoolPref(TOOLBAR_PREF, false)) {
        for (const id of ["downloads-button", "panic-button"]) {
          try {
            if (CUI.getPlacementOfWidget(id)) continue;
            const ids = CUI.getWidgetIdsInArea(CUI.AREA_NAVBAR);
            const pos = ids.indexOf("unified-extensions-button");
            if (pos >= 0) CUI.addWidgetToArea(id, CUI.AREA_NAVBAR, pos);
            else CUI.addWidgetToArea(id, CUI.AREA_NAVBAR);
          } catch (e) { Cu.reportError(e); }
        }
        Svc.prefs.setBoolPref(TOOLBAR_PREF, true);
      }
    }

    function addToMenu(win) {
      if (!helper().exists() || !win.PanelMultiView) return;
      const doc = win.document;
      let anchor = null;
      for (const id of ["appMenu-downloads-button", "appMenu-history-button", "appMenu-bookmarks-button"]) {
        try { anchor = win.PanelMultiView.getViewNode(doc, id); } catch (e) { anchor = null; }
        if (anchor) break;
      }
      if (!anchor || !anchor.parentNode || anchor.parentNode.querySelector("#" + ITEM)) return;
      const item = doc.createXULElement("toolbarbutton");
      item.id = ITEM;
      item.className = "subviewbutton";
      item.setAttribute("label", "Getem");
      item.setAttribute("tooltiptext", "Open Getem in a tab\nShift+click: update download components");
      item.addEventListener("command", (event) => {
        try { win.PanelUI.hide(); } catch (e) { }
        if (event && event.shiftKey) { try { runHelper("/update"); } catch (e) { Cu.reportError(e); } }
        else openGetem(win);
      });
      anchor.after(item);
    }

    log("Icewolf GetemTab script loaded");
    Svc.obs.addObserver({
      observe(subject) {
        try { setupGlobal(); } catch (e) { log("setup ERROR " + e.message); Cu.reportError(e); }
        try { addToMenu(subject); } catch (e) { Cu.reportError(e); }
      }
    }, "browser-delayed-startup-finished");
  })();
} catch (e) {
  try { Components.utils.reportError(e); } catch (e2) { }
}
/* ICEWOLF-GETEM-END */
