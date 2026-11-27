/* ICEWOLF-CORE
 * No automatic "pin to taskbar / Start menu" offers from the engine: it would create its own
 * shortcut under the engine's built-in name. Pin Icewolf yourself via its Start menu entry
 * (right-click > Pin to taskbar) - that uses the Icewolf name and logo.
 */
try {
  (function () {
    const Svc = (typeof Services !== "undefined") ? Services : null;
    if (!Svc) return;
    function neutralizePinning() {
      for (const url of ["resource:///modules/ShellService.sys.mjs",
                         "moz-src:///browser/components/shell/ShellService.sys.mjs"]) {
        try {
          const ShellService = ChromeUtils.importESModule(url).ShellService;
          if (!ShellService) continue;
          ShellService.doesAppNeedPin = async () => false;
          ShellService.doesAppNeedStartMenuPin = async () => false;
          ShellService.pinCurrentAppToTaskbarAsync = async () => { };
          ShellService.pinToStartMenu = async () => false;
          return url;
        } catch (e) { }
      }
      return null;
    }
    let pinResult = null;   // logged once the profile folder is available (see below)
    Svc.obs.addObserver({ observe() { try { pinResult = neutralizePinning(); } catch (e) { } } }, "final-ui-startup");

    // ---- DuckDuckGo: make sure it exists and is the default (once - you can change it later).
    // Added here instead of by policy: a policy engine with the same name as a built-in one can
    // stop the whole search list from loading. Steps are logged to icewolf-search-log.txt (profile).
    function slog(msg) {
      try {
        const f = Svc.dirsvc.get("ProfD", Ci.nsIFile); f.append("icewolf-search-log.txt");
        if (f.exists() && f.fileSize > 64 * 1024) f.remove(false);
        const fos = Cc["@mozilla.org/network/file-output-stream;1"].createInstance(Ci.nsIFileOutputStream);
        fos.init(f, 0x02 | 0x08 | 0x10, 0o600, 0);
        const out = Cc["@mozilla.org/intl/converter-output-stream;1"].createInstance(Ci.nsIConverterOutputStream);
        out.init(fos, "UTF-8"); out.writeString(new Date().toISOString() + "  " + msg + "\r\n"); out.close();
      } catch (e) { }
    }
    // The search service moved around in newer engine versions - find it wherever it is.
    function findSearchService(win) {
      const tries = [
        () => Svc.search,
        () => win && win.Services && win.Services.search,
        () => ChromeUtils.importESModule("moz-src:///toolkit/components/search/SearchService.sys.mjs").SearchService,
        () => ChromeUtils.importESModule("resource://gre/modules/SearchService.sys.mjs").SearchService,
        () => Cc["@mozilla.org/browser/search-service;1"].getService(Ci.nsISearchService)
      ];
      for (let i = 0; i < tries.length; i++) {
        try {
          const s = tries[i]();
          if (s && typeof s.init === "function" && typeof s.getEngines === "function") { slog("search service found (way " + (i + 1) + ")"); return s; }
        } catch (e) { }
      }
      return null;
    }
    // Diagnostics for the search engine list (written to icewolf-search-log.txt)
    async function diagnoseSearch(win) {
      const pref = (n) => {
        try {
          const t = Svc.prefs.getPrefType(n);
          if (t === Svc.prefs.PREF_STRING) return JSON.stringify(Svc.prefs.getStringPref(n));
          if (t === Svc.prefs.PREF_BOOL) return String(Svc.prefs.getBoolPref(n));
          if (t === Svc.prefs.PREF_INT) return String(Svc.prefs.getIntPref(n));
          return "(not set)";
        } catch (e) { return "(error " + e + ")"; }
      };
      for (const n of ["general.config.filename", "librewolf.cfg.version", "services.settings.server",
                       "services.settings.load_dump", "browser.search.region", "intl.locale.requested"])
        slog("pref " + n + " = " + pref(n));
      try {
        const U = ChromeUtils.importESModule("resource://services-settings/Utils.sys.mjs").Utils;
        slog("RS server " + U.SERVER_URL + " | load dumps " + U.LOAD_DUMPS);
        try { slog("RS dump search-config-v2: " + await U.hasLocalDump("main", "search-config-v2")); } catch (e) { slog("hasLocalDump: " + e); }
      } catch (e) { slog("RS Utils: " + e); }
      try {
        const r = await win.fetch("resource://app-defaults/settings/main/search-config-v2.json");
        const t = await r.text();
        slog("dump file readable: " + r.ok + ", " + t.length + " chars");
      } catch (e) { slog("dump file NOT readable: " + e); }
      try {
        const RS = ChromeUtils.importESModule("resource://services-settings/remote-settings.sys.mjs").RemoteSettings;
        const recs = await RS("search-config-v2").get({ syncIfEmpty: false });
        slog("RS search-config-v2 records: " + recs.length);
      } catch (e) { slog("RS get failed: " + e + (e && e.stack ? "\n" + e.stack : "")); }
    }

    async function ensureDuckDuckGo(win) {
      slog("pin-to-taskbar prompts: " + (pinResult ? "switched off (" + pinResult + ")" : "NOT switched off - engine module not found"));
      try { await diagnoseSearch(win); } catch (e) { slog("diagnose: " + e); }
      const search = findSearchService(win);
      if (!search) { slog("ERROR search service not found"); return; }
      slog("Icewolf search check started (" + Svc.appinfo.version + ")");
      // init() normally takes milliseconds; if it hangs, say so instead of waiting forever
      await Promise.race([
        search.init(),
        new Promise((_, reject) => ChromeUtils.importESModule("resource://gre/modules/Timer.sys.mjs")
          .setTimeout(() => reject(new Error("search service did not start within 30 s")), 30000))
      ]);
      slog("search service ready");
      let engines = [];
      try { engines = await search.getEngines(); } catch (e) { slog("getEngines failed: " + e); }
      slog("engines: " + (engines.map((x) => x.name).join(", ") || "(none)"));
      const find = () => engines.find((x) => x.name === "DuckDuckGo") ||
                         engines.find((x) => /^duckduckgo/i.test(x.name) && !/lite|html/i.test(x.name)) ||
                         engines.find((x) => /duckduckgo/i.test(x.name));
      let ddg = find();
      if (!ddg) {
        const url = "https://duckduckgo.com/?q={searchTerms}";
        const tries = [
          () => search.addUserEngine({ name: "DuckDuckGo", url, alias: "@ddg" }),
          () => search.addUserEngine("DuckDuckGo", url, "@ddg")
        ];
        for (const t of tries) {
          try { await t(); break; } catch (e) { slog("addUserEngine: " + e); }
        }
        try { engines = await search.getEngines(); } catch (e) { }
        ddg = find();
        slog(ddg ? "added DuckDuckGo" : "could not add DuckDuckGo");
      }
      if (!ddg) return;
      const done = "icewolf.search.duckduckgo.default";
      if (Svc.prefs.getBoolPref(done, false) && search.defaultEngine) return;
      const reason = (Ci.nsISearchService && Ci.nsISearchService.CHANGE_REASON_USER) || 0;
      try { await search.setDefault(ddg, reason); } catch (e) { try { search.defaultEngine = ddg; } catch (e2) { slog("setDefault: " + e2); } }
      try { await search.setDefaultPrivate(ddg, reason); } catch (e) { try { search.defaultPrivateEngine = ddg; } catch (e2) { } }
      Svc.prefs.setBoolPref(done, true);
      slog("default engine: " + (search.defaultEngine && search.defaultEngine.name));
    }
    Svc.obs.addObserver({
      observe(subject, topic) {
        Svc.obs.removeObserver(this, topic);
        ensureDuckDuckGo(subject).catch((e) => slog("ERROR " + (e && e.stack ? e.message + "\n" + e.stack : e)));
      }
    }, "browser-delayed-startup-finished");
  })();
} catch (e) { }
/* ICEWOLF-CORE-END */
