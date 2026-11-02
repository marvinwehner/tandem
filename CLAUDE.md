# Tandem

Zwei Laptops an einem Monitor. `Tandem.exe` schickt per Strg+Alt+K Monitor (DDC/CI) und Logitech-Bluetooth-Geräte (HID++) zum anderen Laptop und aktualisiert sich selbst von GitHub. Bedienung und Installation: [README.md](README.md).

## Aufbau
Drei Ebenen: Hardware (`Ddc`, `Logitech`) → Abläufe (`Switcher`, `Installer`, `Updater`) → Oberfläche (`Program`, `Listener`, `Hotkey`, `Overlay`, `Dialog`). Untere Ebenen kennen keine UI, Meldungen zeigt die Oberfläche. Querschnitt: `Profile`, `AppVersion`, `Log`.
- `src/Program.cs`: Einstieg, `Command.Parse` prüft die Kommandozeile vollständig, Abläufe für install/uninstall mit Meldungen, zentrale Fehlerbehandlung
- `src/Profile.cs`: Rollen `privat`/`arbeit` mit eigenem und anderem Monitoreingang, Eingangsnamen
- `src/Switcher.cs`: Ablauf beim Umschalten (DDC prüfen, Geräte, Monitor), `ChooseTarget` als reine Regel, `SwitchResult`
- `src/Ddc.cs`: Monitoreingang über dxva2 (VCP 0x60). `Ddc.Open()` hält die Handles, damit Lesen und Setzen dieselben Monitore treffen
- `src/Logitech.cs`: HID++ 2.0 über BLE (Usage Page 0xFF43, Long Report 0x11, Device-Index 0xFF), Feature 0x1814 CHANGE_HOST
- `src/Listener.cs`: Hintergrundprozess, Mutex `Local\Tandem`, Events `Local\Tandem.Beenden`/`.Pruefen`/`.Bereit`, Update-Takt mit Zustand `UpdateState`, Übergabe an neue Versionen
- `src/Hotkey.cs`: Strg+Alt+K über ein unsichtbares Nachrichtenfenster, `WaitForRelease` vor dem Gerätewechsel
- `src/Installer.cs`: Dateien, Autostart `HKCU\...\Run\Tandem`, Startmenü „Tandem umschalten“, Prozesse beenden, Sicherung zurückholen. Ordner `%LOCALAPPDATA%\Programs\Tandem`
- `src/Updater.cs`: Release prüfen (`ShouldOffer`), Quellcode laden, bauen; `src/AppVersion.cs`: Version parsen und vergleichen
- `src/Overlay.cs` (Hinweisfenster, Per-Monitor-DPI V2), `src/Dialog.cs` (MessageBox im Vordergrund), `src/Log.cs` (`tandem.log`)
- `src/tandem.rsp`: csc-Optionen und Quellen, gemeinsam für `build.cmd`, `test.cmd` und den Updater
- `tests/Tests.cs`: Tests ohne Framework für die reine Logik und den Build
- `Installieren.cmd`: fragt nach der Rolle, `build.cmd`, dann `Tandem.exe install <rolle>`

## Bauen und testen
- `build.cmd [version]` erzeugt `Tandem.exe` im Repo-Ordner (in `.gitignore`). Version aus `git describe --tags`, ohne Tag `0.0.0-dev`
- `test.cmd` baut und startet `tests\Tests.exe`. Vor jedem Commit grün halten
- Gefahrlos, nur lesend: `Tandem.exe get | Out-String`, `Tandem.exe devices | Out-String`, `Tandem.exe version | Out-String` (winexe, Ausgabe nur bei Umleitung)
- Ungültige Aufrufe und Fehler öffnen eine MessageBox und blockieren, bis jemand klickt. In Skripten nur gültige Kommandos aufrufen
- `set` und `toggle` schalten wirklich um, also den User vorher warnen. Monitor zurück: `set 0x0F` (DP) oder `set 54` (USB-C)
- `listen` nicht selbst starten: Es belegt Strg+Alt+K, ein Druck des Users schickt dann die Geräte weg
- Logitech-Umschaltung geht nur, wenn die Geräte mit diesem Laptop verbunden sind. Danach sind sie weg, zurück per Easy-Switch-Taste
- Update-Pfad ohne Release testen: kleines Probe-Programm mit `csc /target:exe /main:Probe /out:... @src\tandem.rsp Probe.cs` aus dem Repo-Ordner, das ein Branch-Archiv (`https://github.com/marvinwehner/tandem/archive/refs/heads/<branch>.zip`) lädt, entpackt und mit `Updater.Build` baut. So am 2026-11-02 verifiziert
- Overlay ohne Umschalten testen: kleines Testprogramm mit `Overlay.Run(...)` bauen und mit `Graphics.CopyFromScreen` Screenshots machen

## Release
1. Auf `main` gemergt, `test.cmd` grün
2. Nur nach Rückfrage beim User, denn jeder Release löst auf beiden Laptops die Update-Frage aus: `gh release create vX.Y.Z --target main --title "Tandem X.Y.Z" --notes "<was ist neu>"`
3. Versionen: Fehlerbehebung erhöht Z, neue Funktion Y
4. Installierte Versionen fragen innerhalb von 24 h, sofort mit `Tandem.exe update`. Lokale Klone brauchen `git pull --tags`, damit `build.cmd` die Version kennt

## Regeln
- Nur C# 5 (csc des .NET Framework): keine String-Interpolation, kein `?.`, kein `out var`, keine Tuples, kein `nameof`, keine Getter-only-Properties (`{ get; private set; }` nehmen)
- Keine externen Abhängigkeiten, nur Windows-Bordmittel
- Code-Kommentare und UI-Texte auf Deutsch, Bezeichner englisch
- Neue Quelldateien gehören nach `src/`, die rsp nimmt `src\*.cs` automatisch mit. Neue Referenzen in `src\tandem.rsp` eintragen
- `.cmd`-Dateien mit CRLF (`.gitattributes`), Texte darin ohne Umlaute
- Installer und Autostart nicht selbst ausführen: Der Auto-Mode blockiert das als Persistenz, der User startet `Installieren.cmd` selbst

## Setup (verifiziert 2026-11-02)
- Monitor Samsung ViewFinity S9 S95UF (LS49F950U), DDC-Caps-Modell `LEMON25`. VCP 0x60: DP `0x0F`, HDMI 1 `0x11`, HDMI 2 `0x12`, **USB-C `54` (0x36)**. USB-C fehlt in der Caps-Liste, funktioniert aber. DDC geht auch, wenn der eigene Eingang nicht angezeigt wird
- Privatlaptop (Rolle `privat`, Lenovo 82WM, RTX 4070) an DP, Easy-Switch-Kanal 2. Das USB-Kabel zum Monitor-Hub spielt fürs Umschalten keine Rolle
- Arbeitslaptop (Rolle `arbeit`) an USB-C, Easy-Switch-Kanal 1
- Logitech LIFT (PID B031) und ERGO K860 (PID B359) per Bluetooth LE. Logi Options+ läuft parallel und stört nicht
- `toggle`: Ohne DDC-Monitor passiert nichts, die Geräte bleiben hier. Sind Geräte hier verbunden, geht alles zum anderen Laptop (Monitor fest auf den anderen Eingang). Sonst wechselt nur der Monitor, das Ziel richtet sich nach dem ersten DDC-Monitor

## Stolpersteine
- `.lnk`-Hotkeys startet der Explorer erst nach ~3 s, deshalb `listen` mit `RegisterHotKey`
- Strg+Alt = AltGr auf deutschen Tastaturen: keine Hotkey-Taste mit AltGr-Zeichen. Strg+Alt+M ist auf dem Privatlaptop schon belegt
- Vor dem Gerätewechsel warten, bis Strg, Alt, K und Enter (Startmenü) losgelassen sind, sonst bleiben sie auf diesem Laptop gedrückt
- Mit dem Framework-csc gebaute Programme sprechen standardmäßig nur SSL3/TLS 1.0. Für GitHub vorher `ServicePointManager.SecurityProtocol = Tls12` setzen. GitHub verlangt außerdem einen `User-Agent`
- csc: `/target` und `/out` müssen vor den Quelldateien stehen (CS2022), deshalb nicht in die rsp. Pfade in einer rsp gelten relativ zum Arbeitsverzeichnis, nicht zur rsp
- GitHub: `/releases/latest` leitet auf `/releases/tag/<tag>` weiter, ohne Release auf `/releases`. Das Archiv `archive/refs/tags/<tag>.zip` enthält genau einen Ordner `tandem-<version>`
- Eine laufende exe lässt sich umbenennen, aber nicht überschreiben. Darauf beruht das Update (`Tandem.exe` → `.bak`, `.new` → `Tandem.exe`)
- Der Task-Manager merkt sich abgeschaltete Autostarts unter `HKCU\...\Explorer\StartupApproved\Run` (erstes Byte 02 an, 03 aus)
- Git Credential Manager zeigt eine Kontoauswahl, sobald mehrere GitHub-Konten gespeichert sind. Auf dem Privatlaptop behoben (altes Konto per `git credential-manager github logout <konto>` abgemeldet, `git config --global credential.https://github.com.username marvinwehner`). Git aus Programmen oder Skripten nur mit `GCM_INTERACTIVE=never` und `GIT_TERMINAL_PROMPT=0` aufrufen, sonst erscheinen Fenster oder der Aufruf hängt

## Entscheidungen (2026-11-02)
- Repo öffentlich: Updates ohne Anmeldung, kein GitHub-Token mit Vollzugriff auf dem Firmen-Laptop, weniger Code
- Update aus Quellcode statt fertiger exe: keine Binärdateien im Repo, keine GitHub Action, csc ist auf jedem Windows. Der Updater ruft csc direkt auf, nicht `build.cmd`, weil Firmenrichtlinien Skripte eher sperren
- Version = Git-Tag, keine Versionsnummer im Code (`src\BuildInfo.cs` wird beim Bauen erzeugt)
- Rolle als Argument in Autostart und Startmenü, keine Konfigurationsdatei
- Übergabe mit Aufpasser: Die alte Version gibt Hotkey und Mutex frei, startet `install <rolle> --handover` der neuen und wartet 30 s auf `Tandem.Bereit`. Sonst beendet sie die neue, holt `.bak` zurück und macht weiter. Ein laufendes Umschalten wird vorher abgewartet
- Update-Frage erst, wenn an diesem Laptop in den letzten 2 min Eingaben kamen, Standardknopf „Nein“
- Auch Entwicklungsstände prüfen automatisch: `git describe` hängt nach jedem Commit nach dem Tag einen Zusatz an, eine Installation aus `main` hätte sonst nie gefragt
- Ein gescheiterter Build wird bis zum Neustart nicht wieder angeboten, ein Netzfehler beim Laden nach 1 h schon
- Der Installer wartet auf `Tandem.Bereit` und meldet, wenn der Hintergrundprozess nicht läuft (z. B. Hotkey belegt)
- Autostart über den Run-Schlüssel statt `.lnk` im Autostart-Ordner (kein COM)
- Kein Migrationscode für die alte `MonitorSwitch`-Installation, die gab es nur auf dem Privatlaptop

## Zusammenarbeit
- Der User hat Claude erlaubt, diese Datei und die README.md selbstständig aktuell zu halten. Bei jeder Änderung prüfen, ob die Doku betroffen ist, und sie im selben Commit anpassen
- Erkenntnisse (Hardware-Verhalten, Stolpersteine, Entscheidungen mit Begründung) hier festhalten, nicht im lokalen Claude-Memory: Das gilt nur auf einem Laptop, das Repo ist auf beiden gleich
- Größere Vorhaben: erst Plan in `PLAN.md`, per Sub-Agents reviewen, dem User vorstellen, nach Freigabe umsetzen. Weicht die Umsetzung vom Plan ab, Plan anpassen und die Abweichung nennen. Nach Abschluss die Entscheidungen hierher übernehmen und `PLAN.md` löschen (bleibt in der Git-Historie)
- Nach der Umsetzung Code-Review per Sub-Agents, Befunde einarbeiten, dann PR
- 2026-11-02: Der User wollte alles in einem PR und hat das direkte Mergen erlaubt. Bei künftigen Vorhaben trotzdem kurz nachfragen
- Der User legt Wert auf sauberen, wartbaren Code: kleine Klassen mit einer klaren Aufgabe, reine Logik getrennt von Win32-Aufrufen, aber nichts auf Vorrat
- Kommunikation, Commits und Doku auf Deutsch

## Offen (Stand 2026-11-02)
- Erstes Release `v1.1.0` (nach Rückfrage), danach auf beiden Laptops `git pull --tags` und `Installieren.cmd`
- Privatlaptop: alte Installation von Hand entfernen (`%LOCALAPPDATA%\Programs\MonitorSwitch`, Startmenü „Monitor umschalten“, falls vorhanden `MonitorSwitch.lnk` in `shell:startup` und ein laufender `MonitorSwitch`-Prozess)
- Am echten Gerät noch ungeprüft: Hotkey im neuen Hintergrundprozess, Doppeldruck, Autostart nach Anmeldung, Update-Übergabe und Rückfall, `uninstall`
- Arbeitslaptop: noch nicht installiert, DDC über USB-C von dort aus ungetestet
- Idee File-Share: vorher PowerToys „Mouse Without Borders“ prüfen (Zwischenablage und Dateien zwischen PCs)
