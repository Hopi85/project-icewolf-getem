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
          return;
        } catch (e) { }
      }
    }
    Svc.obs.addObserver({ observe() { try { neutralizePinning(); } catch (e) { } } }, "final-ui-startup");
  })();
} catch (e) { }
/* ICEWOLF-CORE-END */
