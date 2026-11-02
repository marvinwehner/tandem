# Plan: Aufräumen, ein Installer, Auto-Update

Stand 2026-11-02, nach zwei Reviews vom User freigegeben. Entscheidungen: E1 ja, E2 öffentlich (Repo ist seit 2026-11-02 öffentlich), E3 ja, E4 alles in einem PR statt Phasen, E5 auch auf dem Arbeitslaptop automatisch (der User ist sicher, dass es dort klappt, Phase 0 entfällt).

## 1. Ist-Stand

Gute Substanz: rund 500 Zeilen C#, keine Abhängigkeiten, gut kommentiert, Hardware-Wissen sauber dokumentiert. Kein Neuschreiben nötig, aber gezieltes Aufräumen.

| # | Wo | Befund |
|---|----|--------|
| B1 | `MonitorSwitch.cs` | Eine Datei für Kommandozeile, Hotkey-Schleife, DDC/CI-Aufrufe, Umschaltlogik und Eingangsnamen |
| B2 | `MonitorSwitch.cs:99` `Execute` | Modus als String, `toggle` wird intern zu `set` umgebogen, `get` schreibt mitten in der Schleife, zwei `out`-Parameter. Schwer lesbar, nicht testbar |
| B3 | `MonitorSwitch.cs:38`, `:75` | Argumentprüfung als langer bool-Ausdruck. Ungültiger Wert: `set` stürzt ab, `listen` meldet den Fehler erst beim ersten Hotkey |
| B4 | `listen` | Kein Log. Fehler sieht man nur kurz im Hinweisfenster oder im WinForms-Fehlerdialog |
| B5 | `listen` | Keine Einzelinstanz. Ein zweiter Start meldet nach 15 s fälschlich „von einem anderen Programm belegt“ |
| B6 | `Overlay.cs:83`, `MonitorSwitch.cs:77` | Jeder Hotkey startet eine eigene Message-Loop. Ab dem zweiten scheitert `SetProcessDpiAwarenessContext` und es wird jedes Mal `SetProcessDPIAware` gerufen. Die `GetMessage`-Schleife ruft kein `DispatchMessage`, die Bildschirmliste wird nach Monitorwechseln vermutlich nicht aktualisiert (nicht verifiziert) |
| B7 | `LogiSwitch.cs` | Handles werden von Hand freigegeben statt per `using`. Kein Leck (Finalizer), aber unsauber |
| B8 | `LogiSwitch.cs:196` | `WaitForKeysReleased` gehört zum Hotkey, nicht zu Logitech. Fragt alle 247 Tasten ab statt nur Strg, Alt und K |
| B9 | `LogiSwitch.cs` | Magische Zahlen (`0x1814`, `0x0005`, `0x11`, `0xFF`) ohne Namen. Pfad-Parsing mit festen Offsets (`r + 10`, `v + 15`) ungetestet |
| B10 | Setup-Wissen | Eingänge `0x0F`/`54` stehen in zwei `.cmd`, `Install.ps1`, `InputName` und Kommentaren |
| B11 | Installation | Zwei `.cmd` mit vertauschten Parametern, `Install.ps1` mit COM-Verknüpfungen, Deinstallation nur von Hand |
| B12 | `build.cmd` | Quelldateien einzeln aufgezählt |
| B13 | Allgemein | Keine Version, keine Tests. README beginnt englisch, Rest deutsch |
| B14 | `MonitorSwitch.cs:109` | Maus und Tastatur werden weggeschickt, bevor feststeht, dass der Monitor per DDC erreichbar ist. Scheitert DDC, sitzt man ohne Eingabegeräte vor diesem Laptop |

Live geprüft am 2026-11-02 auf Laptop A (Lenovo 82WM, RTX 4070):
- Installiert ist nur `%LOCALAPPDATA%\Programs\MonitorSwitch\MonitorSwitch.exe` und „Monitor umschalten“ im Startmenü. Kein Autostart, kein `listen`-Prozess
- Repo `marvinwehner/tandem` ist privat, noch keine Tags oder Releases
- Eine mit dem Framework-csc gebaute exe spricht standardmäßig nur SSL3/TLS 1.0, GitHub lehnt das ab. Mit `ServicePointManager.SecurityProtocol = Tls12` klappt es
- `gh auth token --hostname github.com --user marvinwehner` liefert einen Token. Damit funktionieren `GET /repos/.../releases/latest` (ohne Release: 404) und `GET /repos/.../zipball/<ref>` (Weiterleitung, 15 KB Zip)
- Der Git Credential Manager zeigte bei jedem Zugriff eine Kontoauswahl, weil ein altes Konto gespeichert war. Behoben, danach geht Git ohne Dialog, auch mit `GCM_INTERACTIVE=never`
- Eine laufende exe lässt sich umbenennen und durch eine neue Datei ersetzen
- Der Monitor meldete gerade `54` (USB-C), obwohl ich auf Laptop A (DisplayPort) war. Die Rolle lässt sich also nicht per DDC erraten

## 2. Ziele und Nicht-Ziele

Ziele:
1. Aufgeräumter, testbarer Code mit klaren Zuständigkeiten
2. Ein Installer für beide Laptops, der fragt: Arbeitslaptop (USB-C) oder Privatlaptop (DisplayPort)
3. Auto-Update von GitHub: beim Start prüfen, nach Bestätigung installieren, neue Version läuft sofort
4. README und CLAUDE.md bleiben aktuell, Erkenntnisse und Arbeitsweise stehen in CLAUDE.md

Nicht-Ziele (Ideen für später): Tray-Icon, Release-Notizen im Update-Dialog, frei wählbarer Hotkey, Datei-Austausch zwischen den Laptops, signierte Builds.

## 3. Architektur

Leitlinien, passend zur Größe des Tools:
- Kleine Klassen mit genau einer Aufgabe. Statische Klassen für Win32-Wrapper sind in Ordnung. Keine Interfaces, DI oder Abstraktionen auf Vorrat
- Drei Ebenen: **Hardware** (DDC/CI, Logitech HID++) → **Abläufe** (Umschalten, Installieren, Update) → **Oberfläche** (Kommandozeile, Hintergrundprozess, Hinweisfenster). Untere Ebenen kennen keine UI
- Reine Logik aus dem P/Invoke-Code herauslösen, damit sie testbar ist: Versionen, HID-Pfad, Argumente, Profile
- Ergebnisobjekte statt `out`-Parameter. Ausnahmen an einer Stelle fangen, loggen und als Hinweis zeigen
- Setup-Wissen (Eingänge, Repo) an genau einer Stelle: `Profile.cs`
- Weiter nur C# 5 und Windows-Bordmittel

Struktur (Name „Tandem“ vorausgesetzt, siehe E1), 10 Dateien:

```
src/
  Program.cs       Einstieg, Kommandos, Argumente prüfen, zentrale Fehlerbehandlung
  Profile.cs       Rollen privat/arbeit: eigener und anderer Eingang, Eingangsnamen
  Switcher.cs      Ablauf beim Hotkey: Tasten loslassen abwarten, DDC prüfen, Geräte, Monitor; liefert SwitchResult
  Ddc.cs           Monitoreingang lesen/setzen (dxva2)
  Logitech.cs      HID++: Geräte finden, Kanal lesen/wechseln
  Listener.cs      Hintergrundprozess: unsichtbares Fenster, RegisterHotKey, Einzelinstanz, Update-Takt
  Overlay.cs       Hinweisfenster, ohne eigene Message-Loop
  Installer.cs     install / uninstall: kopieren, Autostart, Startmenü
  Updater.cs       Update prüfen, laden, bauen, übergeben; AppVersion (parsen, vergleichen)
  Log.cs           tandem.log im Installationsordner, begrenzt auf 1 MB
  tandem.rsp       csc-Optionen und Quelldateien, von build.cmd und Updater gemeinsam genutzt
tests/
  Tests.cs         Minimaler Test-Runner ohne Framework: csc /main:Tests src\*.cs tests\Tests.cs
build.cmd          schreibt src\BuildInfo.cs (Version), dann csc @src\tandem.rsp
test.cmd           baut und startet die Tests
Installieren.cmd   fragt per choice nach der Rolle, build.cmd, dann Tandem.exe install <rolle>
```

Hintergrundprozess: Statt roher `GetMessage`-Schleife plus verschachteltem `Application.Run` je Hotkey ein `ApplicationContext` mit unsichtbarem Fenster (`NativeWindow`), das `WM_HOTKEY` empfängt. Overlay und Update-Takt laufen auf dem UI-Thread. Umschalten, Netzwerk und Kindprozesse laufen im Hintergrund-Thread, damit der Hotkey nie blockiert. Ein Busy-Flag ignoriert weitere Hotkeys, bis das Overlay zu ist (heute verwirft die verschachtelte Loop sie zufällig; ohne Flag würde ein Doppeldruck sofort zurückschalten). DPI-Modus und `Application.SetUnhandledExceptionMode` werden einmal vor dem ersten Fenster gesetzt.

Einzelinstanz per benanntem Mutex (`AbandonedMutexException` zählt als „frei“). Benannte Events für „Beenden“ (sanftes Stoppen durch Installer und Deinstallation) und „Jetzt prüfen“ (`Tandem.exe update` gibt dem laufenden Prozess Bescheid, statt selbst parallel zu aktualisieren).

Die Rolle steht als Argument im Autostart (`listen privat`), keine Konfigurationsdatei. Der Easy-Switch-Kanal gehört nicht ins Profil, der Code kippt einfach 1 ↔ 2.

Kommandos:

```
Tandem.exe listen <rolle>        Hintergrundprozess (Autostart)
Tandem.exe toggle <rolle>        einmal umschalten (Startmenü)
Tandem.exe get | set <wert> | devices | version
Tandem.exe install <rolle>       installieren bzw. aktualisieren
Tandem.exe uninstall             Prozess beenden, Autostart, Startmenü und Ordner entfernen
Tandem.exe update                laufenden Prozess sofort prüfen lassen, mit sichtbarer Rückmeldung
```

`listen <eigener> <anderer>` und `toggle <eigener> <anderer>` mit Zahlenwerten bleiben als Override erhalten.

## 4. Installer

`Installieren.cmd` ersetzt beide `.cmd` und `Install.ps1`, kein PowerShell mehr:
1. Frage per `choice`: „Welcher Laptop ist das? [A] Arbeitslaptop (USB-C) [P] Privatlaptop (DisplayPort)“
2. `build.cmd` baut `Tandem.exe` (Version aus `git describe`, sonst `0.0.0-dev`)
3. `Tandem.exe install <rolle>`:
   - laufenden Tandem-Prozess sanft beenden (Event, Mutex abwarten), nur als letzter Schritt hart, nie den eigenen Prozess
   - exe nach `%LOCALAPPDATA%\Programs\Tandem` kopieren (erst `Tandem.exe.new`, dann alte nach `.bak` umbenennen, neue an ihren Platz)
   - Autostart als Wert unter `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (kein COM). Hat der User den Autostart im Task-Manager deaktiviert, warnen
   - Startmenü „Tandem – umschalten“ (`.lnk` über `WScript.Shell`, ca. 10 Zeilen)
   - `Tandem.exe listen <rolle>` starten (Arbeitsverzeichnis = Installationsordner), Ergebnis anzeigen

Rollen (in `Profile.cs`):

| Rolle | Laptop | eigener Eingang | anderer Eingang |
|-------|--------|-----------------|-----------------|
| privat | Lenovo, RTX 4070 (Laptop A), Easy-Switch 2 | DisplayPort `0x0F` | USB-C `54` |
| arbeit | Laptop B, Easy-Switch 1 | USB-C `54` | DisplayPort `0x0F` |

Das USB-Kabel am Privatlaptop (Monitor-USB-Hub) spielt fürs Umschalten keine Rolle.

`uninstall`: Prozess sanft beenden, Run-Wert und Startmenü-Eintrag löschen, den eigenen Ordner nach dem Beenden über ein abgekoppeltes `cmd /c` löschen lassen.

Die alte Installation auf Laptop A (Ordner `Programs\MonitorSwitch` und „Monitor umschalten“) wird einmal von Hand per Checkliste entfernt, kein Migrationscode.

## 5. Auto-Update

**Release** = Git-Tag `vX.Y.Z` plus GitHub-Release (ohne Binärdateien). Entwürfe und Pre-Releases werden nicht angeboten. Releases erstellt Claude per `gh release create` nur nach Rückfrage, denn jeder Release löst auf beiden Laptops eine Update-Frage aus.

**Version** = Tag, eine Quelle der Wahrheit. `build.cmd <version>` schreibt sie als `[AssemblyInformationalVersion]` in `src\BuildInfo.cs` (bei jedem Build neu, in `.gitignore`). Ohne Argument: `git describe --tags --match v*`, ohne Git `0.0.0-dev`. Vergleich numerisch über `X.Y.Z`. Entwicklungsstände (`-dev`, `v1.2.0-3-gabc`) prüfen nicht automatisch, damit ein Klick keinen Teststand überschreibt. `Tandem.exe update` geht dort trotzdem.

**Woher** hängt von E2 ab:

| | Repo öffentlich (Empfehlung) | Repo privat |
|---|---|---|
| Anmeldung | keine | `gh auth token --hostname github.com --user marvinwehner`, gh muss auf beiden Laptops angemeldet sein |
| Prüfen | Weiterleitung von `github.com/marvinwehner/tandem/releases/latest` auf `.../tag/vX.Y.Z` auswerten (kein API-Limit, noch zu prüfen) | `GET api.github.com/repos/.../releases/latest`, Tag aus dem JSON. 401/404 bei `update` sichtbar melden |
| Laden | `github.com/.../archive/refs/tags/vX.Y.Z.zip` | `GET .../zipball/<tag>` |

Für beide: TLS 1.2, `User-Agent` setzen, Proxy mit `DefaultCredentials`, Timeouts. Kindprozesse mit Timeout, `CreateNoWindow`, `GH_PROMPT_DISABLED=1`, `GH_NO_UPDATE_NOTIFIER=1`. Token und Download-URLs nie ins Log.

**Takt** im Hintergrundprozess: stündlich ticken und den Zeitstempel der letzten Prüfung vergleichen (übersteht den Ruhezustand). Erste Prüfung 60 s nach dem Start, dann alle 24 h, nach einem Netzfehler nach 1 h.

**Ablauf** bei neuer Version:
1. Fragen erst, wenn an diesem Laptop jemand arbeitet (`GetLastInputInfo` jünger als 2 min). Sonst sieht man den Dialog nicht, weil der Monitor gerade den anderen Laptop zeigt
2. MessageBox „Tandem 1.3.0 ist verfügbar (installiert: 1.2.0). Jetzt aktualisieren?“, im Vordergrund, Standardknopf **Nein**, damit ein Enter beim Tippen nichts auslöst. Nein → nächste Prüfung fragt wieder
3. Ja: Hinweisfenster „Update wird installiert …“. Zip nach `update\<tag>\` (vorher leeren) entpacken, `BuildInfo.cs` schreiben, `csc @src\tandem.rsp` direkt aufrufen (kein `.cmd`, Ausgabe ins Log)
4. Prüfen per `FileVersionInfo`: neue exe hat genau die Tag-Version. Sonst abbrechen, Meldung mit Log-Pfad, diese Version nicht noch einmal anbieten bis zum Neustart
5. Übergabe mit Aufpasser: Die alte Version sperrt den Hotkey (Busy), meldet ihn ab, gibt den Mutex frei, startet `update\<tag>\Tandem.exe install --update <rolle>` und wartet bis zu 30 s auf das Event „Bereit“
6. Der neue Installer beendet im `--update`-Modus nichts, ersetzt die exe (`.new` → umbenennen, alte wird `.bak`) und startet `Tandem.exe listen <rolle>`. Sobald der neue Prozess Mutex und Hotkey hat, setzt er „Bereit“, die alte Version beendet sich
7. Kommt „Bereit“ nicht: Die alte Version beendet hängende neue Prozesse, stellt `.bak` wieder her, holt Mutex und Hotkey zurück und meldet den Fehler. Der Hotkey bleibt also immer bei einer laufenden Version
8. Aufräumen nach Möglichkeit: `update\` beim nächsten Start, `.bak` bleibt bis zum nächsten Update

Warum aus Quellcode bauen statt fertiger exe: keine Binärdateien im Repo, keine GitHub Action, `csc` ist auf jedem Windows, der Installer macht es heute schon so.

## 6. Doku und Zusammenarbeit

- README.md: nur Deutsch, für Nutzer: Installation, Update, Deinstallation, Kommandos, Hinweise
- CLAUDE.md: für die Arbeit am Code: Aufbau, Bauen/Testen, Release-Ablauf, Regeln, Setup, Stolpersteine, Entscheidungen mit Begründung, Offenes
- Abschnitt „Zusammenarbeit“ in CLAUDE.md ist schon eingefügt: Erlaubnis zum Aktualisieren der Doku, Erkenntnisse ins Repo statt ins lokale Claude-Memory (das gilt nur auf einem Laptop), Ablauf Plan → Review → Vorstellen → Umsetzen
- Jede Phase passt die Doku im selben Commit an

## 7. Umsetzung in Phasen

Je Phase ein Branch mit PR (E4). Am Ende jeder Phase: `build.cmd` und `test.cmd` grün, Doku aktuell. Installer, Autostart und echtes Umschalten führst du aus (Auto-Mode blockiert Persistenz, Umschalten nimmt die Geräte mit), Claude liefert jeweils eine Checkliste.

| Phase | Inhalt | Prüfung |
|-------|--------|---------|
| 0 Machbarkeit Laptop B (du, ca. 10 min, mit dem heutigen Code) | Repo klonen, `build.cmd`, `get`, `devices`, `toggle 54 0x0F`. Dazu: Kommt eine Konto-Auswahl? Ist `gh` vorhanden/erlaubt? Erreicht `curl.exe -sI https://github.com` das Netz? Meldet sich Defender oder die IT? | Ergebnis entscheidet E2 und E5, bevor Code geschrieben wird |
| 1 Umbau ohne Verhaltensänderung | `src/`-Struktur, Aufteilung (B1–B3, B7, B9, B10, B12), `SwitchResult`, Konstanten, `using`, `tandem.rsp`, `test.cmd` mit ersten Tests (HID-Pfad, Argumente, Eingangsnamen) | Ausgaben von `get` und `devices` vor/nach gleich. Du testest `toggle 0x0F 54` aus dem Build-Ordner |
| 2 Hintergrundprozess und Installer | Listener (B4–B6), Busy-Flag, Mutex, Events, Log, B8, B14 (bewusste Verhaltensänderung), Rollen, `install`/`uninstall`, `Installieren.cmd`, Umbenennung (E1), alte Installer entfernen | Du installierst auf Laptop A. Checkliste: Rollenfrage, Autostart nach Ab-/Anmelden, Hotkey hin und zurück, Doppeldruck, zweite Instanz, Overlay nach Zurückschalten, DDC-Fehler lässt Geräte hier, `uninstall` und neu installieren, alte Einträge entfernt |
| 3 Version und Updater | `BuildInfo.cs`, `version`, `AppVersion`, Updater, `update` | Tests: Versionsvergleich (`1.10.0 > 1.9.0`, `-dev`, `-3-gabc`), Build in Temp-Ordner (heil → Version stimmt, Syntaxfehler → Abbruch). Erster Release `v1.1.0` nach Rückfrage. Durchlauf: mit `build.cmd 1.0.0` installieren, `update` bietet 1.1.0 an, nach Ja läuft 1.1.0 mit Hotkey. Dazu: WLAN aus, Nein, Dialog erst nach Eingabe |
| 4 Laptop B | `Installieren.cmd` mit Rolle „arbeit“ | DDC über USB-C, Logitech in beide Richtungen, Update-Prüfung dort |

Die bisherige Installation kann sich nicht selbst aktualisieren. Ab `v1.1.0` einmal je Laptop von Hand installieren, danach kommen Updates automatisch.

## 8. Offene Entscheidungen

| # | Frage | Empfehlung |
|---|-------|------------|
| E1 | Programm in `Tandem.exe` umbenennen (Repo heißt so, es kann mehr als den Monitor)? | Ja |
| E2 | Repo öffentlich machen oder privat lassen? | Öffentlich: keine Anmeldung, kein Token mit Vollzugriff auf alle deine privaten Repos auf dem Firmen-Laptop, deutlich weniger Code. Dann sichtbar: Commit-Adresse, Hardware und Laptop-Modelle aus CLAUDE.md. Privat geht auch, dann mit `gh` auf beiden Laptops |
| E3 | Nur beim Start prüfen oder zusätzlich alle 24 h? | Start + alle 24 h, Laptops werden meist nur zugeklappt |
| E4 | Branch + PR je Phase oder direkt auf `main`? | Branch + PR, du mergst auf GitHub |
| E5 | Auf dem Arbeitslaptop automatisch installieren oder nur „Update verfügbar“ melden? | Nach Phase 0 entscheiden. „Herunterladen, kompilieren, starten, Autostart“ sieht für Firmen-Virenschutz aus wie Schadsoftware. Ob selbst gebaute Tools dort erlaubt sind, weiß nur deine IT |

## 9. Risiken

- **Arbeitslaptop**: AppLocker/WDAC, Defender for Endpoint oder ein Proxy können Build, Start oder Download blockieren oder melden. Phase 0 klärt das zuerst. Der Updater ruft `csc` direkt auf statt eines `.cmd` und fragt nur Strg/Alt/K ab statt aller Tasten, beides wirkt weniger verdächtig
- **Update führt Code aus dem Repo aus**: Schutz sind dein GitHub-Login (2FA), dass nur Releases zählen, und die Bestätigung. Für ein persönliches Tool angemessen
- **Kaputtes Update**: Build-Prüfung vor der Übergabe, Aufpasser mit Rückfall auf `.bak`, notfalls älteren Tag klonen und `Installieren.cmd`

## 10. Review

Zwei unabhängige Reviews (Architektur; Robustheit und Sicherheit) am 2026-11-02. Übernommen: Phase 0 auf Laptop B, kleinere Phase 1, Busy-Flag gegen Doppeldruck, Befund B14, Aufpasser bei der Übergabe, Dialog erst bei Anwesenheit und mit Standard „Nein“, Netzwerk nicht im UI-Thread, Rolle als Argument statt Konfigurationsdatei, `choice` statt eigenem Dialog, Run-Wert statt `.lnk`-Autostart, `csc @rsp` statt `.cmd` im Update, kein Migrationscode, nur ein Anmeldeweg, Empfehlung „öffentlich“ bei E2, korrigierte Befunde B3, B4, B6, B7. Verworfen: Rolle beim Installieren per DDC vorbelegen (der Monitor zeigt nicht unbedingt den Laptop, auf dem man installiert, siehe Abschnitt 1).
