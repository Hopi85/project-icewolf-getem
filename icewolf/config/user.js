// =====================================================================
//  Icewolf - user.js  (engine: LibreWolf)
//  Privacy / security / debloat preferences for the Icewolf profile.
//  Applied on every start. To change a setting permanently, edit it
//  HERE (changes made in about:config get overwritten on restart).
//
//  Design rules:
//   - Keep security on: process sandboxing, certificate checks and
//     uBlock Origin's malware lists stay ON. Engine updates come from
//     the Icewolf updater. Deliberate privacy trade-offs: Google Safe
//     Browsing is off and OCSP is replaced by CRLite (see README).
//   - Remove telemetry, ads, sponsored content, AI features and
//     anything that phones home without being needed.
//   - Don't break normal websites.
// =====================================================================

// ---------- Theme support (loads chrome/userChrome.css) ----------
user_pref("toolkit.legacyUserProfileCustomizations.stylesheets", true);
user_pref("extensions.activeThemeID", "firefox-compact-dark@mozilla.org");
user_pref("browser.theme.content-theme", 0);   // dark web content UI
user_pref("browser.theme.toolbar-theme", 0);

// ---------- Startup / first-run debloat ----------
user_pref("browser.aboutwelcome.enabled", false);
user_pref("browser.startup.homepage_override.mstone", "ignore");
user_pref("startup.homepage_welcome_url", "");
user_pref("startup.homepage_welcome_url.additional", "");
user_pref("browser.shell.checkDefaultBrowser", false);
user_pref("browser.aboutConfig.showWarning", false);

// ---------- New tab page: no ads, no news feed ----------
user_pref("browser.newtabpage.activity-stream.showSponsored", false);
user_pref("browser.newtabpage.activity-stream.showSponsoredTopSites", false);
user_pref("browser.newtabpage.activity-stream.feeds.section.topstories", false);
user_pref("browser.newtabpage.activity-stream.feeds.snippets", false);
user_pref("browser.newtabpage.activity-stream.feeds.telemetry", false);
user_pref("browser.newtabpage.activity-stream.telemetry", false);
user_pref("browser.newtabpage.activity-stream.default.sites", "");
user_pref("browser.newtabpage.activity-stream.showWeather", false);
user_pref("browser.newtabpage.activity-stream.system.showWeather", false);
user_pref("extensions.pocket.enabled", false);

// ---------- Telemetry, studies, crash reports: all off ----------
user_pref("datareporting.healthreport.uploadEnabled", false);
user_pref("datareporting.policy.dataSubmissionEnabled", false);
user_pref("datareporting.usage.uploadEnabled", false);
user_pref("toolkit.telemetry.enabled", false);
user_pref("toolkit.telemetry.unified", false);
user_pref("toolkit.telemetry.server", "data:,");
user_pref("toolkit.telemetry.archive.enabled", false);
user_pref("toolkit.telemetry.newProfilePing.enabled", false);
user_pref("toolkit.telemetry.shutdownPingSender.enabled", false);
user_pref("toolkit.telemetry.updatePing.enabled", false);
user_pref("toolkit.telemetry.bhrPing.enabled", false);
user_pref("toolkit.telemetry.firstShutdownPing.enabled", false);
user_pref("toolkit.telemetry.coverage.opt-out", true);
user_pref("toolkit.coverage.opt-out", true);
user_pref("toolkit.coverage.endpoint.base", "");
user_pref("app.shield.optoutstudies.enabled", false);
user_pref("app.normandy.enabled", false);
user_pref("app.normandy.api_url", "");
user_pref("browser.ping-centre.telemetry", false);
user_pref("browser.discovery.enabled", false);
user_pref("breakpad.reportURL", "");
user_pref("browser.tabs.crashReporting.sendReport", false);
user_pref("browser.crashReports.unsubmittedCheck.autoSubmit2", false);
user_pref("dom.private-attribution.submission.enabled", false); // ad "attribution" API

// ---------- AI: every AI feature and the local AI engine removed ----------
user_pref("browser.ml.enable", false);                     // the AI inference engine itself
user_pref("browser.ml.chat.enabled", false);               // AI chatbot
user_pref("browser.ml.chat.sidebar", false);
user_pref("browser.ml.chat.shortcuts", false);
user_pref("browser.ml.chat.shortcuts.custom", false);
user_pref("browser.ml.chat.menu", false);
user_pref("browser.ml.chat.page", false);
user_pref("browser.ml.chat.page.footerBadge", false);
user_pref("browser.ml.chat.page.menuBadge", false);
user_pref("browser.ml.chat.provider", "");
user_pref("browser.ml.linkPreview.enabled", false);        // AI link previews
user_pref("browser.ml.pageAssist.enabled", false);
user_pref("browser.ml.smartAssist.enabled", false);
user_pref("browser.tabs.groups.smart.enabled", false);     // AI tab grouping
user_pref("browser.tabs.groups.smart.userEnabled", false);
user_pref("extensions.ml.enabled", false);                 // AI API for extensions
user_pref("pdfjs.enableAltText", false);                   // AI image descriptions in PDFs
user_pref("pdfjs.enableAltTextModelDownload", false);
user_pref("pdfjs.enableGuessAltText", false);
user_pref("places.semanticHistory.featureGate", false);    // AI history search
user_pref("browser.urlbar.quicksuggest.mlEnabled", false);

// ---------- Recommendations / Mozilla marketing ----------
user_pref("extensions.getAddons.showPane", false);
user_pref("extensions.htmlaboutaddons.recommendations.enabled", false);
user_pref("browser.newtabpage.activity-stream.asrouter.userprefs.cfr.addons", false);
user_pref("browser.newtabpage.activity-stream.asrouter.userprefs.cfr.features", false);
user_pref("browser.preferences.moreFromMozilla", false);
user_pref("browser.vpn_promo.enabled", false);

// ---------- Search engine: DuckDuckGo everywhere (also locked by policy) ----------
user_pref("browser.search.separatePrivateDefault", false);
user_pref("browser.search.separatePrivateDefault.ui.enabled", false);
user_pref("browser.urlbar.placeholderName", "DuckDuckGo");
user_pref("browser.urlbar.placeholderName.private", "DuckDuckGo");

// ---------- Address bar: nothing you type leaves the PC ----------
user_pref("browser.search.suggest.enabled", false);
user_pref("browser.urlbar.suggest.searches", false);
user_pref("browser.urlbar.quicksuggest.enabled", false);
user_pref("browser.urlbar.suggest.quicksuggest.sponsored", false);
user_pref("browser.urlbar.suggest.quicksuggest.nonsponsored", false);
user_pref("browser.urlbar.trending.featureGate", false);
user_pref("browser.urlbar.addons.featureGate", false);
user_pref("browser.urlbar.mdn.featureGate", false);
user_pref("browser.urlbar.weather.featureGate", false);
user_pref("browser.urlbar.yelp.featureGate", false);
user_pref("browser.urlbar.speculativeConnect.enabled", false);
user_pref("browser.formfill.enable", false);

// ---------- No silent pre-connections ----------
user_pref("network.prefetch-next", false);
user_pref("network.dns.disablePrefetch", true);
user_pref("network.dns.disablePrefetchFromHTTPS", true);
user_pref("network.predictor.enabled", false);
user_pref("network.predictor.enable-prefetch", false);
user_pref("network.http.speculative-parallel-limit", 0);
user_pref("browser.places.speculativeConnect.enabled", false);
user_pref("network.captive-portal-service.enabled", false);
user_pref("network.connectivity-service.enabled", false);

// ---------- Fingerprinting: LibreWolf's strict "resist fingerprinting" OFF
// (it breaks dark mode, time zones and some sites); Firefox's fingerprinting
// protection ON instead (blocks known fingerprinting without breaking sites).
user_pref("privacy.resistFingerprinting", false);
user_pref("privacy.resistFingerprinting.pbmode", false);
user_pref("privacy.resistFingerprinting.letterboxing", false);
user_pref("privacy.fingerprintingProtection", true);
user_pref("privacy.fingerprintingProtection.pbmode", true);

// ---------- Tracking protection / privacy ----------
user_pref("browser.contentblocking.category", "strict");
user_pref("privacy.globalprivacycontrol.enabled", true);   // tells sites "don't sell my data"
user_pref("privacy.userContext.enabled", true);            // container tabs
user_pref("privacy.userContext.ui.enabled", true);
user_pref("privacy.query_stripping.enabled", true);        // strip tracking params from URLs
user_pref("privacy.query_stripping.enabled.pbmode", true);
user_pref("network.http.referer.XOriginTrimmingPolicy", 2); // send only the domain to other sites
user_pref("media.peerconnection.ice.default_address_only", true); // WebRTC: don't leak local IPs
user_pref("media.peerconnection.ice.proxy_only_if_behind_proxy", true);
user_pref("beacon.enabled", false);
user_pref("geo.provider.network.url", "https://beacondb.net/v1/geolocate"); // no Google location service
user_pref("permissions.default.geo", 0);                   // always ask (default)

// ---------- Security hardening ----------
user_pref("dom.security.https_only_mode", true);
user_pref("dom.security.https_only_mode_send_http_background_request", false);
user_pref("security.ssl.require_safe_negotiation", true);
user_pref("security.ssl.treat_unsafe_negotiation_as_broken", true);
user_pref("security.tls.enable_0rtt_data", false);         // no replay-able early data
user_pref("security.tls.version.min", 3);                  // TLS 1.2 minimum
user_pref("browser.xul.error_pages.expert_bad_cert", true);
user_pref("security.cert_pinning.enforcement_level", 2);   // strict pinning
user_pref("security.remote_settings.crlite_filters.enabled", true);
user_pref("security.pki.crlite_mode", 2);                  // revocation checks without leaking to OCSP
user_pref("pdfjs.enableScripting", false);                 // no JavaScript inside PDFs
user_pref("browser.download.always_ask_before_handling_new_types", true);
user_pref("browser.download.manager.addToRecentDocs", false);
user_pref("network.IDN_show_punycode", true);              // reveals look-alike phishing domains
user_pref("signon.formlessCapture.enabled", false);
user_pref("signon.autofillForms.http", false);
user_pref("network.auth.subresource-http-auth-allow", 1);  // block phishing login popups from embedded content
user_pref("extensions.postDownloadThirdPartyPrompt", false);

// Google Safe Browsing OFF (no connections to Google). Malware/phishing
// protection is provided locally by uBlock Origin's built-in lists
// (uBlock filters - Badware risks, Malicious URL Blocklist), which are on by default.
user_pref("browser.safebrowsing.malware.enabled", false);
user_pref("browser.safebrowsing.phishing.enabled", false);
user_pref("browser.safebrowsing.downloads.enabled", false);
user_pref("browser.safebrowsing.downloads.remote.enabled", false);
user_pref("browser.safebrowsing.downloads.remote.url", "");
user_pref("browser.safebrowsing.blockedURIs.enabled", false);
user_pref("browser.safebrowsing.provider.google4.updateURL", "");
user_pref("browser.safebrowsing.provider.google4.gethashURL", "");
user_pref("browser.safebrowsing.provider.google.updateURL", "");
user_pref("browser.safebrowsing.provider.google.gethashURL", "");

// =====================================================================
//  DEBLOAT - features removed because they are unnecessary, phone home,
//  or slow things down. Re-enable any of them by changing false -> true.
// =====================================================================

// Translation (downloads language models from Mozilla)
user_pref("browser.translations.enable", false);
user_pref("browser.translations.automaticallyPopup", false);
user_pref("browser.translations.select.enable", false);

// Firefox View, shopping review checker, screenshots tool
user_pref("browser.tabs.firefox-view", false);
user_pref("browser.tabs.firefox-view-next", false);
user_pref("browser.shopping.experience2023.enabled", false);
user_pref("browser.shopping.experience2023.active", false);
user_pref("browser.shopping.experience2023.ads.enabled", false);
user_pref("screenshots.browser.component.enabled", false);
user_pref("extensions.screenshots.disabled", true);

// Firefox Relay, Monitor, VPN and mobile-app promos
user_pref("signon.firefoxRelay.feature", "disabled");
user_pref("browser.contentblocking.report.lockwise.enabled", false);
user_pref("browser.contentblocking.report.monitor.enabled", false);
user_pref("browser.contentblocking.report.vpn.enabled", false);
user_pref("browser.contentblocking.report.show_mobile_app", false);
user_pref("browser.privatebrowsing.vpnpromourl", "");
user_pref("browser.promo.focus.enabled", false);
user_pref("browser.promo.pin.enabled", false);
user_pref("browser.promo.cookiebanners.enabled", false);
user_pref("identity.mobilepromo.android", "");
user_pref("identity.mobilepromo.ios", "");

// Mozilla account / Sync: completely removed (also locked by policy)
user_pref("identity.fxaccounts.enabled", false);
user_pref("identity.fxaccounts.toolbar.enabled", false);
user_pref("identity.fxaccounts.toolbar.pxiToolbarEnabled", false);
user_pref("identity.fxaccounts.pairing.enabled", false);
user_pref("identity.fxaccounts.commands.enabled", false);
user_pref("webchannel.allowObject.urlWhitelist", "");    // no privileged Mozilla web channels

// "Report broken site", web-compat reporter, Firefox Labs, rollouts
user_pref("ui.new-webcompat-reporter.enabled", false);
user_pref("extensions.webcompat-reporter.enabled", false);
user_pref("browser.preferences.experimental", false);
user_pref("nimbus.rollouts.enabled", false);
user_pref("messaging-system.rsexperimentloader.enabled", false);
user_pref("browser.newtabpage.activity-stream.asrouter.useRemoteL10n", false);

// More hidden telemetry / data collection
user_pref("toolkit.telemetry.reportingpolicy.firstRun", false);
user_pref("browser.urlbar.quicksuggest.dataCollection.enabled", false);
user_pref("browser.search.serpEventTelemetryCategorization.enabled", false);
user_pref("browser.newtabpage.activity-stream.discoverystream.enabled", false);
user_pref("browser.newtabpage.activity-stream.feeds.section.highlights", false);
user_pref("browser.newtabpage.activity-stream.feeds.recommendationprovider", false);
user_pref("browser.newtabpage.activity-stream.showRecentSaves", false);
user_pref("browser.newtabpage.activity-stream.feeds.wallpaperfeed", false);
user_pref("browser.newtabpage.activity-stream.newtabWallpapers.enabled", false);

// Push service (permanent connection to Mozilla's push server) and
// notification-permission popups from websites
user_pref("dom.push.enabled", false);
user_pref("dom.push.connection.enabled", false);
user_pref("permissions.default.desktop-notification", 2);

// Address bar: no engine/trending/weather/sponsored suggestions
user_pref("browser.urlbar.suggest.engines", false);
user_pref("browser.urlbar.suggest.trending", false);
user_pref("browser.urlbar.suggest.weather", false);
user_pref("browser.urlbar.suggest.yelp", false);
user_pref("browser.urlbar.suggest.pocket", false);
user_pref("browser.urlbar.suggest.fakespot", false);
user_pref("browser.urlbar.suggest.mdn", false);
user_pref("browser.urlbar.suggest.addons", false);

// Accessibility services: off = faster and less exposure.
// IMPORTANT: if you use a screen reader, change 1 -> 0.
user_pref("accessibility.force_disabled", 1);

// =====================================================================
//  ZERO TELEMETRY - second pass: every remaining background connection
//  that sends data about you or your usage is closed here.
// =====================================================================
user_pref("toolkit.telemetry.dap_enabled", false);                 // new "privacy-preserving" telemetry (DAP)
user_pref("toolkit.telemetry.dap.logLevel", "Off");
user_pref("browser.newtabpage.activity-stream.telemetry.structuredIngestion.endpoint", "");
user_pref("browser.region.network.url", "");                       // no IP-based region lookup
user_pref("browser.region.update.enabled", false);
user_pref("browser.topsites.contile.enabled", false);              // sponsored-tile service
user_pref("browser.topsites.useRemoteSetting", false);
user_pref("browser.newtabpage.activity-stream.feeds.system.topstories", false);
user_pref("browser.newtabpage.activity-stream.improvesearch.topSiteSearchShortcuts", false);
user_pref("extensions.getAddons.cache.enabled", false);            // don't send installed add-on list
user_pref("extensions.abuseReport.enabled", false);
user_pref("app.normandy.first_run", false);
user_pref("browser.crashReports.unsubmittedCheck.enabled", false);
user_pref("security.certerrors.mitm.priming.enabled", false);      // no MITM-detection ping
user_pref("browser.safebrowsing.provider.google4.dataSharing.enabled", false);
user_pref("browser.send_pings", false);                            // block <a ping> link tracking
user_pref("media.gmp-gmpopenh264.enabled", false);                 // no automatic Cisco plugin download
user_pref("media.gmp-gmpopenh264.autoupdate", false);
user_pref("browser.newtabpage.activity-stream.feeds.weatherfeed", false);

// =====================================================================
//  SECURITY - second pass
// =====================================================================
user_pref("browser.uitour.enabled", false);                        // Mozilla pages can't control the UI
user_pref("permissions.manager.defaultsUrl", "");                  // no special rights for Mozilla sites
user_pref("extensions.enabledScopes", 5);                          // block add-ons injected via registry/system folders
user_pref("devtools.debugger.remote-enabled", false);              // no remote debugging
user_pref("devtools.chrome.enabled", false);
user_pref("network.file.disable_unc_paths", true);                 // no Windows credential (NTLM) leaks via \\server links
user_pref("network.proxy.socks_remote_dns", true);                 // no DNS leaks when using a SOCKS proxy/VPN

// =====================================================================
//  NOTHING GOES OUT - third pass
// =====================================================================
user_pref("security.OCSP.enabled", 0);                          // no "which site are you visiting" checks to certificate authorities (CRLite checks locally)
user_pref("browser.urlbar.merino.endpointURL", "");             // Firefox Suggest server
user_pref("browser.urlbar.quicksuggest.contextualOptIn", false);
user_pref("browser.search.suggest.enabled.private", false);
user_pref("extensions.getAddons.discovery.api_url", "");
user_pref("extensions.htmlaboutaddons.discover.enabled", false);
user_pref("browser.contentblocking.cfr-milestone.enabled", false);
user_pref("toolkit.telemetry.pioneer-new-studies-available", false);
user_pref("default-browser-agent.enabled", false);
user_pref("media.eme.enabled", false);                          // DRM off: no Google Widevine download (see README to enable for Netflix)
user_pref("media.gmp-widevinecdm.enabled", false);
user_pref("media.gmp-widevinecdm.visible", false);
user_pref("browser.places.interactions.enabled", false);

// Never ask to become the default browser
user_pref("browser.shell.skipDefaultBrowserCheckOnFirstRun", true);
user_pref("browser.shell.didSkipDefaultBrowserCheckOnFirstRun", true);
user_pref("browser.defaultbrowser.notificationbar", false);

// Dark mode everywhere (menus, settings, built-in pages; websites get their dark theme)
user_pref("ui.systemUsesDarkTheme", 1);
user_pref("layout.css.prefers-color-scheme.content-override", 0);

// Import bookmarks & history from other browsers (Chrome, Edge, Brave, Opera,
// Vivaldi) or from a bookmarks HTML file: Settings > General > Import browser data,
// or the "Import bookmarks" button on the empty bookmarks toolbar.
user_pref("browser.migrate.chrome.enabled", true);
user_pref("browser.migrate.chromium-edge.enabled", true);
user_pref("browser.migrate.chromium-edge-beta.enabled", true);
user_pref("browser.migrate.brave.enabled", true);
user_pref("browser.migrate.opera.enabled", true);
user_pref("browser.migrate.opera-gx.enabled", true);
user_pref("browser.migrate.vivaldi.enabled", true);
user_pref("browser.migrate.bookmarks-file.enabled", true);
user_pref("browser.migrate.content-modal.enabled", true);
user_pref("browser.bookmarks.addedImportButton", false);
user_pref("browser.toolbars.bookmarks.visibility", "newtab");

// Downloads button always visible in the toolbar (not only after a download)
user_pref("browser.download.autohideButton", false);

// No "Open Icewolf when your computer starts" (the engine turns this on by itself on first run)
user_pref("browser.startup.windowsLaunchOnLogin.enabled", false);
user_pref("browser.startup.windowsLaunchOnLogin.disableLaunchOnLoginPrompt", true);

// No onboarding messages from the engine (incl. "pin to taskbar" offers)
user_pref("browser.newtabpage.activity-stream.asrouter.providers.onboarding", "{\"id\":\"onboarding\",\"type\":\"local\",\"localProvider\":\"OnboardingMessageProvider\",\"enabled\":false,\"exclude\":[]}");

// Misc
user_pref("browser.tabs.tabmanager.enabled", false);      // hide "List all tabs" arrow
user_pref("browser.download.open_pdf_attachments_inline", true);

// =====================================================================
//  DEBLOAT - third pass
// =====================================================================

// Password manager + form autofill: completely off (also locked by policy).
// Use a dedicated password manager (e.g. Bitwarden or KeePassXC) instead.
user_pref("signon.rememberSignons", false);
user_pref("signon.autofillForms", false);
user_pref("signon.generation.enabled", false);
user_pref("signon.management.page.breach-alerts.enabled", false);
user_pref("signon.management.page.vulnerable-passwords.enabled", false);
user_pref("signon.firefoxRelay.showToAllBrowsers", false);
user_pref("extensions.formautofill.addresses.enabled", false);
user_pref("extensions.formautofill.creditCards.enabled", false);

// Start page / new tab: completely empty - just the Icewolf logo (search via the address bar)
user_pref("browser.newtabpage.activity-stream.feeds.topsites", false);
user_pref("browser.newtabpage.activity-stream.showSearch", false);
user_pref("browser.newtabpage.activity-stream.newtabLayouts.variant-a", false);
user_pref("browser.newtabpage.pinned", "[]");
user_pref("browser.urlbar.suggest.topsites", false);           // no link list when clicking the address bar
user_pref("browser.urlbar.suggest.recentsearches", false);
user_pref("startup.homepage_override_url", "");               // no "What's new" tab after updates
user_pref("browser.startup.upgradeDialog.enabled", false);
user_pref("browser.messaging-system.whatsNewPanel.enabled", false);

// Taskbar: one Icewolf icon, no Firefox jump-list entries
user_pref("browser.privateWindowSeparation.enabled", false);
user_pref("browser.taskbar.lists.enabled", false);
user_pref("browser.taskbar.lists.frequent.enabled", false);
user_pref("browser.taskbar.lists.recent.enabled", false);
user_pref("browser.taskbar.lists.tasks.enabled", false);

// Other unnecessary features
user_pref("browser.urlbar.quickactions.enabled", false);
user_pref("browser.urlbar.shortcuts.quickactions", false);
user_pref("browser.urlbar.suggest.quickactions", false);
user_pref("browser.search.visualSearch.featureGate", false);   // Google Lens image search
user_pref("media.videocontrols.picture-in-picture.video-toggle.enabled", false); // hover button (PiP still in right-click menu)
user_pref("reader.parse-on-load.enabled", false);              // Reader View off (saves CPU on every page)
user_pref("browser.profiles.enabled", false);                  // new profile switcher UI
user_pref("browser.backup.enabled", false);                    // new backup feature
user_pref("browser.backup.scheduled.enabled", false);
user_pref("sidebar.revamp", false);
user_pref("browser.disableResetPrompt", true);                 // no "Refresh Firefox" nag
user_pref("dom.vr.enabled", false);                            // VR/XR API (privacy + not needed)
user_pref("permissions.default.xr", 2);

// ---------- Speed ----------
user_pref("browser.sessionstore.interval", 60000);  // write session to disk every 60s instead of 15s
user_pref("browser.tabs.unloadOnLowMemory", true);
