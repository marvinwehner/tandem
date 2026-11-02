# Tandem

Zwei Laptops an einem Monitor. `Tandem.exe` schickt per Strg+Alt+K oder Mittelklick am Monitorrand Monitor (DDC/CI) und Logitech-Bluetooth-Geräte (HID++) zum anderen Laptop, zeigt im Tray, welcher Laptop den Monitor hat, und aktualisiert sich selbst von GitHub. Bedienung und Installation: [README.md](README.md).

## Aufbau
Drei Ebenen: Hardware (`Ddc`, `Logitech`) → Abläufe (`Switcher`, `Installer`, `Updater`) → Oberfläche (`Program`, `Listener`, `Hotkey`, `EdgeClick`, `Tray`, `Overlay`, `Dialog`). Untere Ebenen kennen keine UI, Meldungen zeigt die Oberfläche. Querschnitt: `Profile`, `AppVersion`, `AppIcon`, `Log`.
- `src/Program.cs`: Einstieg, `Command.Parse` prüft die Kommandozeile vollständig, Abläufe für install/uninstall mit Meldungen, zentrale Fehlerbehandlung
- `src/Profile.cs`: Rollen `privat`/`arbeit` mit eigenem und anderem Monitoreingang, Eingangsnamen
- `src/Switcher.cs`: Ablauf beim Umschalten (DDC prüfen, Geräte, Monitor), `ChooseTarget` als reine Regel, `SwitchResult`
- `src/Ddc.cs`: Monitoreingang über dxva2 (VCP 0x60). `Ddc.Open()` hält die Handles, damit Lesen und Setzen dieselben Monitore treffen, und den Mutex `Local\Tandem.Ddc`, damit nie zwei Zugriffe gleichzeitig laufen (`TryOpen` für die Abfrage, die dann aussetzt)
- `src/Logitech.cs`: HID++ 2.0 über BLE (Usage Page 0xFF43, Long Report 0x11, Device-Index 0xFF), Feature 0x1814 CHANGE_HOST
- `src/Listener.cs`: Hintergrundprozess, Mutex `Local\Tandem`, Events `Local\Tandem.Beenden`/`.Pruefen`/`.Bereit`, Update-Takt mit Zustand `UpdateState`, Übergabe an neue Versionen
- `src/Hotkey.cs`: Strg+Alt+K über ein unsichtbares Nachrichtenfenster, `WaitForRelease` vor dem Gerätewechsel
- `src/Tray.cs`: Tray-Symbol mit nativem Menü (Umschalten, Updates, Info, Protokoll, Beenden), meldet Klicks per Events. Reine Funktionen `StateFor`, `MonitorText`, `Tooltip`, `InfoText`
- `src/MonitorWatch.cs`: liest Eingang und Bereich des Monitors im Hintergrund (`MonitorReading`) für Tray-Symbol und Mittelklick-Rand
- `src/EdgeClick.cs`: Mittelklick am Monitorrand zum anderen Laptop. `WH_MOUSE_LL` auf eigenem Thread, `Filter` entscheidet was geschluckt wird, `IsOnEdge` und `Describe` als reine Funktionen
- `src/Installer.cs`: Dateien, Autostart `HKCU\...\Run\Tandem`, Startmenü „Tandem umschalten“, Prozesse beenden, Sicherung zurückholen. Ordner `%LOCALAPPDATA%\Programs\Tandem`
- `src/Updater.cs`: Release prüfen (`ShouldOffer`), Quellcode laden, bauen; `src/AppVersion.cs`: Version parsen und vergleichen
- `src/Overlay.cs` (Hinweisfenster mit Icon, Per-Monitor-DPI V2), `src/Dialog.cs` (MessageBoxIndirect im Vordergrund, Tandem-Icon bei Hinweis und Frage), `src/Log.cs` (`tandem.log`)
- `src/AppIcon.cs`: Icons aus den Ressourcen (`Tandem.ico`, `TrayLinks.ico`, `TrayRechts.ico`), je Ressource und Größe einmal geladen, und die Win32-Ressourcen-ID 32512 für Meldungen
- `src/tandem.ico`: App-Icon „Geteilter Bildschirm“ in 16–256 px, `src/tray-links.ico` und `src/tray-rechts.ico` mit gedimmter Hälfte in 16–32 px. Alle erzeugt von `tools/IconGenerator.cs` über `tools\icon.cmd`. Entwurf und Begründung: `design/icon-board.html`
- `src/tandem.manifest`: Common Controls 6 (moderne Knöpfe), `asInvoker`, Windows 10/11
- `src/tandem.rsp`: csc-Optionen, Icons, Manifest und Quellen, gemeinsam für `build.cmd`, `test.cmd` und den Updater
- `tests/Tests.cs`: Tests ohne Framework für die reine Logik und den Build
- `Installieren.cmd`: fragt nach der Rolle, `build.cmd`, dann `Tandem.exe install <rolle>`

## Bauen und testen
- `build.cmd [version]` erzeugt `Tandem.exe` im Repo-Ordner (in `.gitignore`). Version aus `git describe --tags`, ohne Tag `0.0.0-dev`
- `test.cmd` baut und startet `tests\Tests.exe`. Vor jedem Commit grün halten
- Gefahrlos, nur lesend: `Tandem.exe get | Out-String` (Eingang und Bereich des Monitors), `Tandem.exe devices | Out-String`, `Tandem.exe version | Out-String` (winexe, Ausgabe nur bei Umleitung)
- Ungültige Aufrufe und Fehler öffnen eine MessageBox und blockieren, bis jemand klickt. In Skripten nur gültige Kommandos aufrufen
- `set` und `toggle` schalten wirklich um, also den User vorher warnen. Monitor zurück: `set 0x0F` (DP) oder `set 54` (USB-C)
- `listen` nicht selbst starten: Es belegt Strg+Alt+K und den Mittelklick am Rand, ein Druck oder Klick des Users schickt dann die Geräte weg
- Mittelklick-Hook ohne Umschalten testen: Probe-Programm mit eigenem `EdgeClick` und einer Zähl-Aktion statt `Toggle`, `Overlay.InitDpi()` zuerst, kleines Fenster am Rand fängt durchgelassene Klicks. Zeiger per `SetCursorPos`, Klick per `SendInput` (absolute Koordinaten treffen den 2-Pixel-Rand nicht sicher). Bewegt den echten Mauszeiger, also vorher den User fragen. Geht nicht bei gesperrtem Bildschirm
- Logitech-Umschaltung geht nur, wenn die Geräte mit diesem Laptop verbunden sind. Danach sind sie weg, zurück per Easy-Switch-Taste
- Update-Pfad ohne Release testen: kleines Probe-Programm mit `csc /target:exe /main:Probe /out:... @src\tandem.rsp Probe.cs` aus dem Repo-Ordner, das ein Branch-Archiv (`https://github.com/marvinwehner/tandem/archive/refs/heads/<branch>.zip`) lädt, entpackt und mit `Updater.Build` baut. So am 2026-11-02 verifiziert
- Tray-Menü ohne Installation ansehen: im Probe-Programm `new Tray(...)`, `Show()`, Menü über die interne Methode `NotifyIcon.ShowContextMenu` öffnen, das Fenster der Klasse `#32768` per `PrintWindow` speichern und mit Esc schließen
- Overlay und Meldungen ohne Umschalten testen: Probe-Programm wie oben mit `Overlay.Run(...)` bzw. `Dialog.*`, Fenster per `PrintWindow(hwnd, hdc, 2)` als Bild speichern und MessageBoxen per `PostMessage` schließen. `CopyFromScreen` liefert nur Schwarz, wenn der Bildschirm gesperrt ist (User per Fernzugriff)
- Ein Probe-Programm mit `@src\tandem.rsp` schreibt bei Fehlern ins echte `%LOCALAPPDATA%\Programs\Tandem\tandem.log`. Danach aufräumen, wenn Tandem dort nicht installiert ist

## Release
1. Auf `main` gemergt, `test.cmd` grün
2. Nur nach Rückfrage beim User, denn jeder Release löst auf beiden Laptops die Update-Frage aus: `gh release create vX.Y.Z --target main --title "Tandem X.Y.Z" --notes "<was ist neu>"`
3. Versionen: Fehlerbehebung erhöht Z, neue Funktion Y
4. Installierte Versionen fragen innerhalb von 24 h, sofort mit `Tandem.exe update`. Lokale Klone brauchen `git pull --tags`, damit `build.cmd` die Version kennt

## Regeln
- Nur C# 5 (csc des .NET Framework): keine String-Interpolation, kein `?.`, kein `out var`, keine Tuples, kein `nameof`, keine Getter-only-Properties (`{ get; private set; }` nehmen)
- Keine externen Abhängigkeiten, nur Windows-Bordmittel
- Code-Kommentare und UI-Texte auf Deutsch, Bezeichner englisch
- Neue Quelldateien gehören nach `src/`, die rsp nimmt `src\*.cs` automatisch mit. Neue Referenzen in `src\tandem.rsp` eintragen. Werkzeuge mit eigenem `Main`, die nicht in die exe gehören, liegen in `tools/`
- `.cmd`-Dateien mit CRLF (`.gitattributes`), Texte darin ohne Umlaute. `.ico` ist binär
- Icon ändern: `tools\IconGenerator.cs` anpassen, `tools\icon.cmd` ausführen, `src\tandem.ico`, `src\tray-links.ico` und `src\tray-rechts.ico` committen. Bis 48 px sind die Formen von Hand aufs Pixelraster gesetzt
- Installer und Autostart nicht selbst ausführen: Der Auto-Mode blockiert das als Persistenz, der User startet `Installieren.cmd` selbst

## Setup (verifiziert 2026-11-02)
- Monitor Samsung ViewFinity S9 S95UF (LS49F950U), DDC-Caps-Modell `LEMON25`. VCP 0x60: DP `0x0F`, HDMI 1 `0x11`, HDMI 2 `0x12`, **USB-C `54` (0x36)**. USB-C fehlt in der Caps-Liste, funktioniert aber. DDC geht auch, wenn der eigene Eingang nicht angezeigt wird
- Aufstellung: links der Arbeitslaptop, in der Mitte der Samsung, rechts der Privatlaptop (`Profile.Position`). Auf dem Privatlaptop liegt der Samsung in Windows bei x = −5120…0 links vom Laptop-Display (2560×1600, primär), sein linker Rand ist der Rand des virtuellen Desktops
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
- csc legt das Icon aus `/win32icon` als Gruppe 32512 ab (`AppIcon.Win32ResourceId`, Test prüft das). `System.Drawing.Icon` ignoriert PNG-Einträge in einer .ico, deshalb sind alle Größen bis 64 px als BMP gespeichert, nur 256 als PNG
- Zeigen Startmenü oder Explorer nach einem Update noch das alte Icon, liegt das am Icon-Cache von Windows (`ie4uinit.exe -show`), nicht am Code
- Ein eigenes Manifest ersetzt das Standardmanifest von csc ganz, deshalb stehen `asInvoker` und `supportedOS` mit drin. Moderne Knöpfe in `MessageBoxIndirect` bringt nur das Manifest, `Application.EnableVisualStyles` wirkt dort nicht und ist ohne WinForms-Knöpfe überflüssig. Falls es später nötig wird: `SetCompatibleTextRenderingDefault` nicht aufrufen, das ändert die Schrift im Hinweisfenster
- `NotifyIcon.Text` wirft über 63 Zeichen (`Tray.Tooltip` kürzt). Ein Menü per Linksklick gibt es in WinForms nur über die interne Methode `ShowContextMenu`. `NotifyIcon.Click` kommt auch beim Rechtsklick, deshalb `MouseClick` mit Taste prüfen
- Low-Level-Maus-Hook: Windows wartet bei jeder Mausbewegung darauf und entfernt ihn still, wenn er länger als `LowLevelHooksTimeout` braucht (z. B. nach dem Ruhezustand). Deshalb eigener Thread, minimale Hook-Prozedur ohne Protokoll und Neusetzen bei jeder Randaktualisierung. Delegate in einem Feld halten (GC), Ausnahmen im Rückruf abfangen (sonst Prozessende). `MSLLHOOKSTRUCT.pt` und das Rechteck aus `EnumDisplayMonitors` sind unter Per-Monitor-DPI V2 beide physische Pixel
- `SystemEvents.DisplaySettingsChanged` kommt auf dem Thread mit dem SynchronizationContext beim Abonnieren an. Statisches Event, beim Beenden abmelden
- Logi Options+ kann die mittlere Taste der LIFT umbelegen, Logi Flow nutzt den Bildschirmrand selbst. Beides stört den Mittelklick am Rand
- Umlaute in Konsolenausgaben kommen bei Umleitung kaputt an (`get`, `devices`). Ausgaben deshalb möglichst ohne Umlaute
- Windows 11 legt neue Tray-Symbole zuerst in den Überlauf (^). Anheften kann nur der User (Einstellungen › Personalisierung › Taskleiste › Andere Symbole der Taskleiste)
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
- Icon „Geteilter Bildschirm“: ein Monitor, wie auf dem Schreibtisch links Blau `#3D7BF5` (Arbeitslaptop, USB-C), rechts Bernstein `#F5A524` (Privatlaptop, DisplayPort). Bis 1.2.0 war es andersherum, `design/icon-board.html` zeigt noch die alte Seite, Rahmen in den Farben des Hinweisfensters (`#282828`, `#6E6E6E`). Trägt bei 16 px und im hellen wie dunklen Design. Meldungen zeigen es bei Hinweis und Frage, Warnungen behalten das Windows-Warnsymbol
- Mittelklick am Rand: in den äußersten 2 px des Monitors zur Seite des anderen Laptops (`Profile.OtherSide`). Entschieden wird beim Drücken (dann auch das Loslassen schlucken), umgeschaltet beim Loslassen am Rand. Immer an, keine Einstellung. Hook statt Raw Input, weil nur der Hook den Klick schlucken kann. Bei geänderten Anzeigeeinstellungen ist der Rand aus, bis die nächste Abfrage ihn liefert; antwortet nur DDC nicht, bleibt der letzte Rand
- Tray-Symbol: Die helle Hälfte zeigt, welcher Laptop den Monitor hat (links Arbeitslaptop, rechts Privatlaptop, wie auf dem Schreibtisch und auf beiden Laptops gleich). Natives `ContextMenu` statt `ContextMenuStrip` (Windows-11-Optik, skaliert pro Bildschirm). Info als MessageBox statt eigenem Fenster (halb so viel Code, Protokoll im Menü)
- Zustand fürs Tray: nach eigenem Umschalten aus dem Ergebnis, sonst alle 30 s per DDC, aber nur, wenn an diesem Laptop in den letzten 2 min Eingaben kamen (kein DDC-Verkehr über Nacht). Ins Protokoll nur bei Wechsel
- Beenden aus dem Tray ohne Rückfrage, danach Hinweis. Beenden während des Umschaltens (Tray oder Installer) wird bis zum Ende aufgeschoben
- Die Update-Logik im Listener wurde fürs Tray bewusst nicht umgebaut, solange Übergabe und Rückfall am Gerät ungeprüft sind

## Zusammenarbeit
- Der User hat Claude erlaubt, diese Datei und die README.md selbstständig aktuell zu halten. Bei jeder Änderung prüfen, ob die Doku betroffen ist, und sie im selben Commit anpassen
- Erkenntnisse (Hardware-Verhalten, Stolpersteine, Entscheidungen mit Begründung) hier festhalten, nicht im lokalen Claude-Memory: Das gilt nur auf einem Laptop, das Repo ist auf beiden gleich
- Größere Vorhaben: erst Plan in `PLAN.md`, per Sub-Agents reviewen, dem User vorstellen, nach Freigabe umsetzen. Weicht die Umsetzung vom Plan ab, Plan anpassen und die Abweichung nennen. Nach Abschluss die Entscheidungen hierher übernehmen und `PLAN.md` löschen (bleibt in der Git-Historie)
- Nach der Umsetzung Code-Review per Sub-Agents, Befunde einarbeiten, dann PR
- 2026-11-02: Der User wollte alles in einem PR und hat das direkte Mergen erlaubt. Bei künftigen Vorhaben trotzdem kurz nachfragen
- Der User legt Wert auf sauberen, wartbaren Code: kleine Klassen mit einer klaren Aufgabe, reine Logik getrennt von Win32-Aufrufen, aber nichts auf Vorrat
- Kommunikation, Commits und Doku auf Deutsch

## Offen (Stand 2026-11-02)
- Privatlaptop: v1.2.0 am 2026-11-02 installiert. Laut Protokoll laufen Start, Update-Prüfung, Umschalten und die Tray-Zustandserkennung (auch Zurückschalten vom anderen Laptop). Arbeitslaptop: noch nicht installiert (`git pull --tags`, dann `Installieren.cmd`)
- Am echten Gerät noch ungeprüft: Doppeldruck, Autostart nach Anmeldung, Update-Übergabe und Rückfall, `uninstall`. Tray: Linksklick, Beenden, genau ein Symbol nach einem Update, Explorer-Neustart, Darstellung bei 125/150 %
- Mittelklick am Rand am Gerät ungeprüft: Rand außen auf dem Arbeitslaptop (Protokoll), Options+ und Logi Flow, Maus flüssig, nach Ruhezustand, Admin-Fenster im Vordergrund, nach Update genau ein Hook
- Arbeitslaptop: DDC über USB-C von dort aus ungetestet
- Idee File-Share: vorher PowerToys „Mouse Without Borders“ prüfen (Zwischenablage und Dateien zwischen PCs)
- Idee: nicht umschalten, wenn der andere Laptop aus ist (sonst Monitor schwarz, Geräte auf totem Kanal). Per DDC nicht erkennbar, am 2026-11-02 getestet: USB-C mit Arbeitslaptop an und aus liefert dieselben Werte, das Samsung-eigene VCP 0xE3 zeigt nur den gewählten Eingang (DP `0x1010`, USB-C `0x2010`, HDMI 1 `0x0010`). Ob der Arbeitslaptop beim Test ein Bild zeigte, ist unbestätigt. Logitech-Geräte kennen nur gekoppelte Kanäle, nicht ob der Host an ist. Bliebe eine Abfrage übers Netzwerk zwischen beiden Tandem-Instanzen: Firewall und VPN des Firmenlaptops ungeklärt, ein schlafender Laptop antwortet auch nicht
