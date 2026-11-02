# Plan: Umschalten per Mittelklick am Rand

Stand 2026-11-02, nach Review per Sub-Agent, noch nicht freigegeben.

## Idee

Physisch steht links der Arbeitslaptop, in der Mitte der Samsung, rechts der Privatlaptop. Steht die Maus auf dem Samsung ganz am Rand zum anderen Laptop hin und kommt ein Klick mit der mittleren Taste, schaltet Tandem um, genau wie Strg+Alt+K.

| Dieser Laptop | steht | Rand des Samsung |
|---|---|---|
| Arbeitslaptop | links | rechter Rand |
| Privatlaptop | rechts | linker Rand |

Bewertung: gute Idee. Man „schiebt“ die Maus Richtung anderer Laptop und klickt, das ist intuitiv und braucht keine zweite Hand. Liegt der Rand außen am Windows-Desktop, bleibt die Maus dort hängen wie an einer Wand und man trifft ihn blind. Auf dem Privatlaptop ist das so (geprüft am 2026-11-02): Samsung bei x = −5120…0 links vom Laptop-Display, sein linker Rand ist der Rand des ganzen Desktops.

## Verhalten

- Entschieden wird beim **Drücken** der mittleren Taste: Steht der Zeiger in den äußersten 2 Pixeln des Rands (volle Höhe des Samsung), werden das Drücken und das nächste Loslassen geschluckt, egal wo das Loslassen ist. Sonst laufen beide normal durch. So bleibt die Taste nie systemweit „gedrückt“
- Umgeschaltet wird beim **Loslassen**, wenn auch das am Rand ist. Die Taste ist dann schon oben, bevor die Maus zum anderen Laptop wechselt
- Danach dasselbe wie beim Hotkey: Hinweisfenster, Busy-Sperre, Beenden-Aufschub
- Aus (Klick läuft durch), solange kein Rand bekannt ist: kein DDC-Monitor, Anzeige gerade umgestellt, oder Tandem läuft mit eigenen Eingangswerten statt einer Rolle

## Technik

- **Profil:** eine neue Angabe „dieser Laptop steht links/rechts“ (`Arbeit` links, `Privat` rechts). Der Rand ist die Gegenseite. Die Tray-Hälfte (M2) folgt daraus
- **Bildschirmfläche:** `Ddc` merkt sich zu jedem Monitor das Rechteck aus dem `EnumDisplayMonitors`-Callback (physische Pixel unter Per-Monitor-DPI V2, wie die Koordinaten im Hook). `MonitorWatch` liefert Werte statt `Ddc.Monitor` zurück: Eingang und Fläche des ersten DDC-Monitors. `Tandem.exe get` zeigt die Fläche mit an
- **Aktualisieren:** bei jeder Abfrage (alle 30 s bei Anwesenheit, beim Start) und bei `SystemEvents.DisplaySettingsChanged`. Dann den Rand sofort auf „unbekannt“ setzen und neu abfragen. Läuft gerade eine Abfrage, merkt sich `MonitorWatch` „danach noch einmal“. Antwortet nur DDC nicht (Standby), bleibt die letzte Fläche gültig. Eigenes Umschalten ändert den Rand nicht
- **Protokoll:** bei jeder Randänderung Seite, x-Koordinate und ob der Rand außen am Desktop liegt
- **Hook:** `WH_MOUSE_LL`, nur so lässt sich der Klick schlucken. Eigener Thread mit eigener Nachrichtenschleife, damit ein blockierter UI-Thread (Übergabe, offene Meldung) nie die Maus im ganzen System ausbremst
  - Hook-Prozedur minimal: bei `nCode < 0` oder anderen Nachrichten als `WM_MBUTTONDOWN/UP` sofort weiter, Koordinaten per `Marshal.ReadInt32`, kein Protokoll, nur `ui.Post`. Alles in try/catch, denn eine Ausnahme im nativen Rückruf beendet den Prozess
  - Delegate in einem readonly-Feld (sonst sammelt ihn der GC ein), `hMod` = `GetModuleHandle(null)`
  - Windows entfernt einen Hook still, wenn er länger als `LowLevelHooksTimeout` braucht (z. B. nach dem Ruhezustand). Deshalb wird er bei jeder Randaktualisierung neu gesetzt
  - Injizierte Klicks nicht ausfiltern: Logi Options+ speist umbelegte Tasten eventuell selbst ein
  - Thread: `PeekMessage` vor dem Bereit-Signal, `GetCurrentThreadId` für `PostThreadMessage`, Stop per `WM_QUIT`, Unhook auf dem Hook-Thread, Join mit Timeout, Hintergrund-Thread. Scheitert der Start, steht es im Protokoll und der Hotkey läuft weiter
  - Der Rand liegt als unveränderliches Objekt vor, das der UI-Thread austauscht. Der Schluck-Zustand lebt nur auf dem Hook-Thread
- **Lebenszyklus:** Hook starten und stoppen direkt neben `hotkey.Register/Unregister`, also auch bei Übergabe und `TakeBack`. Prozessende entfernt ihn ohnehin
- **Neue Klasse** `src/EdgeClick.cs` mit Hook-Thread und reiner Funktion `IsOnEdge(punkt, fläche, seite)`. Der Listener verdrahtet nur

## Risiken

- **Logi Options+** kann die mittlere Taste der LIFT umbelegen. Dann kommt kein Mittelklick an. Am Gerät prüfen, ggf. in Options+ auf „Mittlere Taste“ stellen. **Logi Flow** muss aus sein, sonst nutzt es den Bildschirmrand selbst
- **Firmen-Laptop:** Ein globaler Maus-Hook kann einen Virenscanner aufmerksam machen. Maus-Hooks sind verbreitet und weniger verdächtig als Tastatur-Hooks, trotzdem am Arbeitslaptop beobachten
- **Anordnung in Windows:** Liegt auf dem Arbeitslaptop das Laptop-Display in Windows rechts vom Samsung, ist dessen rechter Rand innen, die Maus rutscht durch. Dann die Anordnung in den Anzeigeeinstellungen an die echte Aufstellung anpassen. Das Protokoll zeigt, ob der Rand außen liegt
- **Fenster mit Adminrechten** im Vordergrund: Ob der Hook dann Klicks sieht, am Gerät prüfen

## Prüfen

- Tests: `IsOnEdge` (beide Seiten, 2-Pixel-Grenze mit exklusivem rechten Rand, anderer Bildschirm, oben/unten), Seite und Rand je Rolle, Tray-Hälfte je Rolle
- `Tandem.exe get` zeigt die Fläche des Samsung (gefahrlos)
- Hook-Test per Probe-Programm, **nur nach deiner Rückfrage**, weil er den echten Mauszeiger bewegt und echte Klicks auslöst: Zeiger per `SetCursorPos` an den Rand, Mittelklick per `SendInput`, statt Umschalten nur zählen. Ein Probe-Fenster am Rand zählt durchgelassene Klicks. Fälle: Rand/Rand, Rand/Mitte, Mitte/Rand, jeweils mit `GetAsyncKeyState(VK_MBUTTON)` danach. Geht nicht bei gesperrtem Bildschirm
- Am Gerät (du): Mittelklick am Rand schaltet um, in der Mitte nicht, Options+ stört nicht, Maus läuft flüssig, nach Ruhezustand weiter aktiv, nach Update genau ein Hook, Admin-Fenster im Vordergrund

## Entscheidungen (Reviewer stimmt allen Empfehlungen zu)

| # | Frage | Empfehlung |
|---|---|---|
| M1 | Immer an, oder im Tray-Menü abschaltbar? Abschaltbar bräuchte eine gespeicherte Einstellung (bisher gibt es keine) | Immer an. Stört es, kommt der Schalter später, ein Update ist in 24 h da |
| M2 | Tray-Symbol und App-Icon passend zur Aufstellung drehen? Heute ist links Bernstein = Privatlaptop, physisch steht er rechts | Ja: links Blau = Arbeitslaptop, rechts Bernstein = Privatlaptop. Eigener Commit, folgt aus der Profil-Angabe |
| M3 | Low-Level-Hook (Klick wird geschluckt) oder Raw Input (kein Hook, aber die Anwendung bekommt den Mittelklick auch, z. B. Autoscroll im Browser) | Hook |

## Ablauf

Ein Branch, ein PR. Code-Review per Sub-Agent, dann Merge auf `main`. Release `v1.3.0` nach Rückfrage. Doku: README (Bedienung, Options+, Anordnung), CLAUDE.md (Aufstellung im Setup, Entscheidungen, Stolpersteine, veralteter Punkt „Release v1.1.0“ unter Offen).
