/* ICEWOLF-GETEM-BEGIN
 * Icewolf: "Getem" entry in the main menu (hamburger menu), below "Downloads",
 * and a one-time restore of the Downloads / "Forget" toolbar buttons.
 * Click: start Getem.  Shift+click: update Getem's download components.
 * It can only start the fixed helper program "getem-host\GetemHost.exe" next to the
 * engine folder - websites have no access to it.
 */
try {
  (function () {
    const { classes: Cc, interfaces: Ci, utils: Cu } = Components;
    const Svc = (typeof Services !== "undefined") ? Services : null;
    if (!Svc) return;

    const ITEM = "appMenu-getem-button";

    function helper() {
      const f = Svc.dirsvc.get("GreD", Ci.nsIFile).parent;   // install / portable folder
      f.append("getem-host");
      f.append("GetemHost.exe");
      return f;
    }

    function launch(arg) {
      try {
        const exe = helper();
        if (!exe.exists()) return;
        const proc = Cc["@mozilla.org/process/util;1"].createInstance(Ci.nsIProcess);
        proc.init(exe);
        proc.runw(false, [arg], 1);
      } catch (e) { Cu.reportError(e); }
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
      item.setAttribute("tooltiptext", "Video search & download\nShift+click: update download components");
      item.addEventListener("command", (event) => {
        launch(event && event.shiftKey ? "/update" : "/start");
        try { win.PanelUI.hide(); } catch (e) { }
      });
      anchor.after(item);
    }

    // Toolbar: put the Downloads and "Forget" (delete browsing data) buttons back once.
    // After that, any change the user makes to the toolbar is kept.
    const TOOLBAR_PREF = "icewolf.toolbar.restored.1";
    let toolbarChecked = false;

    function loadCUI() {
      const urls = [
        "resource:///modules/CustomizableUI.sys.mjs",
        "moz-src:///browser/components/customizableui/CustomizableUI.sys.mjs"
      ];
      for (const url of urls) {
        try { return ChromeUtils.importESModule(url).CustomizableUI; } catch (e) { }
      }
      return null;
    }

    function restoreToolbar() {
      if (toolbarChecked) return;
      toolbarChecked = true;
      if (Svc.prefs.getBoolPref(TOOLBAR_PREF, false)) return;
      const CUI = loadCUI();
      if (!CUI) return;
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

    Svc.obs.addObserver({
      observe(subject) {
        try { restoreToolbar(); } catch (e) { Cu.reportError(e); }
        try { addToMenu(subject); } catch (e) { Cu.reportError(e); }
      }
    }, "browser-delayed-startup-finished");
  })();
} catch (e) {
  try { Components.utils.reportError(e); } catch (e2) { }
}
/* ICEWOLF-GETEM-END */
