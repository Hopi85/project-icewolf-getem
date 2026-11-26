# Getem

Lokale Windows-App für Videolinks und Serien-/Sendungssuche.

## Start

Start.cmd doppelklicken. Aktualisieren.cmd aktualisiert die Download-Komponenten.
Die Oberfläche läuft mit pywebview in WebView2, das unter Windows 11 vorinstalliert ist.

## Bedienung

Treffer erscheinen als Kachelwand, die Folgen fahren rechts ein, die Warteschlange liegt immer sichtbar unten.

1. Namen ins Suchfeld eingeben und Enter drücken. Tubi, Pluto TV, Plex, ORF ON, ARD, ZDF und 3sat
   werden gleichzeitig durchsucht. Jeder Anbieter zeigt Trefferzahl oder Fehler;
   ein Klick auf einen Anbieter blendet seine Treffer aus oder ein.
2. Links einen Treffer anklicken. In der Mitte erscheinen Staffeln und Folgen.
3. Folgen anhaken. Das Staffel-Kästchen wählt alle Folgen, Umschalt+Klick einen Bereich.
4. **Herunterladen** legt die Auswahl in die Warteschlange. Weitersuchen geht jederzeit.

**Links einfügen** oben rechts öffnet ein Feld für YouTube, TikTok und andere Videolinks,
ein Link pro Zeile. Ein Link im Suchfeld öffnet dieses Feld automatisch. Format, Qualität und Zielordner
stehen im Knopf oben rechts (zum Beispiel „MP4 · 1080p“).
**Prüfen & herunterladen** prüft eingefügte Links vor dem Einreihen mit yt-dlp.
Unterstützte Websites, generisch erkennbare Videos und direkte Medienlinks
(etwa MP4, HLS oder DASH) verwenden denselben Ablauf. Während der Prüfung ist
die Linkeingabe gesperrt; pro Link gilt eine Zeitgrenze von 60 Sekunden.
Erfolgreich geprüfte Links werden mit Titel und gegebenenfalls Vorschaubild
eingereiht. Fehlerhafte Links bleiben mit einer Erklärung im Eingabefeld;
erneutes Prüfen reiht die bereits erfolgreichen Links nicht nochmals ein.
Die Prüfung erkennt keine beliebigen Player und garantiert keinen späteren
Download: Verfügbarkeit, Formatwahl und Zugriff können sich weiterhin ändern.
Bei Anbieteränderungen zuerst Aktualisieren.cmd ausführen.
Das ✕ leert das Suchfeld, ein Klick aufs Logo führt zurück zur Suche.
TikTok-Videos werden automatisch ohne das zusätzliche Download-Wasserzeichen
geladen. Getem schließt die TikTok-Downloadkopien aus und verwendet die
Wiedergabefassungen. Ist keine verfügbar, erscheint ein Fehler statt einer
Datei mit Wasserzeichen. Bereits im Original enthaltene Logos bleiben erhalten;
vorhandene Dateien werden dadurch nicht nachträglich verändert. MP3 bleibt unverändert.
Tastatur: Strg+K (Suche), Esc (Suche leeren), Strg+Enter (Herunterladen).

### Warteschlange

Es läuft immer ein Download. **✕ Abbrechen** stoppt nur den aktuellen, der nächste
startet sofort. Das ✕ bei wartenden Einträgen entfernt sie. **Alle abbrechen** stoppt
den aktuellen und leert die Wartenden, nach einer Rückfrage per zweitem Klick.
↻ startet abgebrochene oder fehlgeschlagene Einträge neu. Teil-Dateien bleiben
erhalten, der Download setzt dort wieder an.

Bereits geladene Folgen werden anhand der ID im Dateinamen als **Vorhanden** markiert.
Tubi, Pluto TV und Plex liefern dafür die Folgen-ID, die auch im Dateinamen steht.

### Thumbnails

Tubi liefert Bilder direkt. MediathekView hat keine Bildfelder, deshalb kommt das Bild
aus dem `og:image` der Beitragsseite, sobald eine Zeile sichtbar wird. Gefundene Adressen
liegen in `.cache/thumbs.json`. Bei ARD klappt das nur teilweise; fehlt ein Bild,
erscheint eine Titelkachel.

### Suche

Sortierung: **Beste Übereinstimmung**, **Titel A–Z / Z–A**, **Jahr (neueste / älteste
zuerst)**. Bei Tubi werden unpassende Empfehlungen ausgeblendet. Apostrophe, Groß-/
Kleinschreibung und Akzente werden normalisiert: `hells kitchen` findet auch
`Hell’s Kitchen`.

**Plex:** Gastzugang über `clients.plex.tv`, mit Website-Host als Fallback bei
HTTP 404/405. Die Suche verwendet den Plex-eigenen Movies-&-Shows-Katalog;
Serien- und Staffelpfade mit `/children` werden normalisiert. Bilder, Beschreibung,
Staffeln und Folgen werden übernommen. Beim Download werden Medieninformationen
angefragt und bei fehlenden Parts eine Wiedergabesitzung erzeugt. Nur verfügbare
ungeschützte Streams werden geladen. Live-Verifikation dieser Änderung steht aus.

**Pluto TV:** Suche im regionalen On-Demand-Serienkatalog, Staffel-/Folgenauswahl
und eigener Download-Resolver mit frischem Gasttoken. Live-TV-Aufnahmen sind nicht
enthalten. Kategorien werden seitenweise geladen und Serien dedupliziert. Pluto
kann Titel innerhalb einer Kategorie begrenzen; der Anbieter-Hinweis im Tooltip
weist dann auf möglicherweise fehlende Treffer hin. Keine Vollständigkeitsgarantie.
Der eigene Resolver ersetzt für einzelne Folgen den veralteten yt-dlp-Pluto-Pfad.

ARTE ist aus der Suche genommen, weil `Mediathek.search`
die Sender `ARTE.DE`, `ARTE.FR` usw. nicht als `ARTE` erkennt.

Angebundene Kataloge: Tubi, Pluto TV und Plex sowie ORF ON, ARD, ZDF, ARTE und 3sat über
MediathekView. Tubi nutzt den anonymen Gastzugang, den Staffelindex und
seitenweise Episodenabfragen. Tokens bleiben im Arbeitsspeicher und werden
weder gespeichert noch protokolliert.

MediathekView liefert aktuell gelistete Themen und Beiträge, keine Garantie
vollständiger Staffeln. Nur eindeutige SxxExx-Angaben werden als Staffelnummer
verwendet; andere Beiträge stehen unter **Ohne Staffelzuordnung**. Suchtreffer
können auch Magazine und Dokumentationen sein. Die Suche ist auf 1.000 Beiträge,
die Folgenliste auf 10.000 begrenzt; bei Erreichen erscheint ein Hinweis.

Joyn Österreich (PULS 4/PULS 24/ATV) und ServusTV On sind Kandidaten,
aber noch nicht als Namenssuche angebunden. YouTube und TikTok funktionieren über Links im Suchfeld.
Weitere Anbieter können über search(name) / episodes(show) in catalog.py ergänzt
werden. Es gibt keine universelle Suchschnittstelle aller Streamingseiten.
`discovery.py` wird von der Oberfläche nicht mehr verwendet.

### Proton VPN

Den bereits installierten Proton-VPN-Client vor der Suche mit dem gewünschten
Land verbinden. Getem verwendet die Windows-Netzwerkverbindung; kein zusätzlicher
VPN-Anbieter, kein VPN-Passwort in Getem und keine automatische Standortumschaltung.
Bei Split-Tunneling muss die Python-Laufzeit von Getem im VPN-Tunnel liegen.
Nach einem Länderwechsel erneut suchen, damit auch die Folgenliste frisch geladen
wird. Während laufender Downloads die Verbindung nicht wechseln. Die App prüft
nicht automatisch, ob Proton verbunden ist; Anbieter können VPN-Adressen ablehnen.

## Downloads

Fehler stoppen nicht die folgenden Downloads. Nicht jede Website ist unterstützt.

Standardordner: downloads. Ordner, Format und Qualität bleiben in `.cache/settings.json` gespeichert. Format wählbar: **MP4** (Standard, bevorzugt H.264 für alle Geräte; passt ein Video nicht hinein, entsteht MKV),
**MKV** (beste verfügbare Fassung ohne Rücksicht auf Geräte) oder **MP3 (nur Ton)**.
Format und Qualität gelten für alles, was danach in die Warteschlange kommt.
Qualität ist eine Obergrenze; gibt es keine Fassung darunter, wird die kleinste geladen. Fortschritt, Tempo und
Restzeit gelten für den aktuellen Video-/Audio-Teil. Bei segmentierten Downloads
werden bis zu vier Abschnitte gleichzeitig geladen.

## Grenzen und Validierung

Region, Anmeldung, DRM, abgelaufene Inhalte und Anbieteränderungen können
Kataloge und Downloads einschränken. Die App importiert keine Browser-Cookies
und entschlüsselt kein DRM. Ein Katalogtreffer ist keine Downloadgarantie.

Live geprüft am 6.11.2026: Suche und Folgen bei Tubi, ORF, ARD, ZDF und 3sat; Downloads
und Abbrechen mit Tubi-Folgen; vollständiger Download eines ORF-Beitrags.

Erweiterung vom 7.11.2026: Pluto/Plex mit simulierten API-Antworten getestet
(Gastzugang, Katalogseiten, Staffel-/Folgenzuordnung, Wiedergabeauflösung,
DRM-Filter und Weitergabe an die Warteschlange). Live-Zugriffe aus der
Entwicklungsumgebung sind durch die Netzwerk-Sandbox blockiert (WinError 10013).
Ein erfolgreicher echter Pluto-/Plex-Download ist damit noch nicht bestätigt.

Die alte Tkinter-Fassung liegt in `_alt_tkinter/`.

TikTok-Erweiterung vom 14.11.2026: 71 Tests erfolgreich, einschließlich
Wasserzeichen-Filter, Kurzlink-Weiterleitung und Download-Routing. Live-Prüfung
an TikTok scheiterte am DNS-Timeout der Entwicklungsumgebung; ein echter
Download ohne Wasserzeichen ist hier noch nicht bestätigt.

Tests: `.venv\Scripts\python.exe -m unittest discover -s tests -v`

## Technische Quellen

- [yt-dlp](https://github.com/yt-dlp/yt-dlp)
- [MediathekViewWeb API](https://github.com/mediathekview/mediathekviewweb/blob/master/server/app.ts)
- [Tubi-Katalogprotokoll](https://github.com/Breezyslasher/Kodi-Addons/blob/main/plugin.video.tubitv/resources/lib/tubi_api.py)
- [Tubi-Gastzugangsprotokoll](https://github.com/Breezyslasher/Kodi-Addons/blob/main/plugin.video.tubitv/resources/lib/tubi_auth.py)
- [Plex Provider API](https://developer.plex.tv/pms/)
- [Pluto-Boot- und Wiedergabeprotokoll in Streamlink](https://github.com/streamlink/streamlink/blob/master/src/streamlink/plugins/pluto.py)
- [Pluto-Katalogprotokoll in OpenViX](https://github.com/OpenViX/PlutoTV/blob/master/src/PlutoDownload.py)
