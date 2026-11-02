# Tandem

Zwei Laptops an einem Monitor. `MonitorSwitch.exe` schickt per Strg+Alt+K Monitor (DDC/CI) und Logitech-Bluetooth-Geräte (HID++) zum anderen Laptop. Bedienung und Installation: [README.md](README.md).

## Aufbau
- `MonitorSwitch.cs`: Einstieg und Kommandos (`get`, `set`, `toggle`, `listen`, `devices`), DDC/CI über dxva2 (VCP 0x60)
- `LogiSwitch.cs`: Logitech HID++ 2.0 über BLE (Usage Page 0xFF43, Long Report 0x11, Device-Index 0xFF), Feature 0x1814 CHANGE_HOST
- `Overlay.cs`: Hinweisfenster (WinForms, Per-Monitor-DPI V2)
- `build.cmd`: baut mit dem csc des .NET Framework 4.x, der zu Windows gehört
- `Install.ps1` + `Installieren - Laptop an *.cmd`: baut, kopiert nach `%LOCALAPPDATA%\Programs\MonitorSwitch`, Autostart mit `listen`

## Bauen und testen
- `build.cmd` erzeugt `MonitorSwitch.exe` (in `.gitignore`)
- Gefahrlos, nur lesend: `MonitorSwitch.exe get | Out-String` und `MonitorSwitch.exe devices | Out-String` (winexe, Ausgabe nur bei Umleitung)
- `set` und `toggle` schalten wirklich um, also den User vorher warnen. Monitor zurück: `set 0x0F` (DP) oder `set 54` (USB-C)
- Logitech-Umschaltung geht nur, wenn die Geräte mit diesem Laptop verbunden sind. Danach sind sie weg, zurück per Easy-Switch-Taste
- Overlay ohne Umschalten testen: kleines Testprogramm mit `Overlay.Run(...)` bauen und mit `Graphics.CopyFromScreen` Screenshots machen

## Regeln
- Nur C# 5 (csc des .NET Framework): keine String-Interpolation, kein `?.`, kein `out var`, keine Tuples
- Keine externen Abhängigkeiten, nur Windows-Bordmittel
- Code-Kommentare und UI-Texte auf Deutsch
- Installer und Autostart nicht selbst ausführen: Der Auto-Mode blockiert das als Persistenz, der User startet die `.cmd` selbst

## Setup (verifiziert 2026-11-02)
- Monitor Samsung ViewFinity S9 S95UF (LS49F950U), DDC-Caps-Modell `LEMON25`. VCP 0x60: DP `0x0F`, HDMI 1 `0x11`, HDMI 2 `0x12`, **USB-C `54` (0x36)**. USB-C fehlt in der Caps-Liste, funktioniert aber. DDC geht auch, wenn der eigene Eingang nicht angezeigt wird
- Laptop A (Lenovo, RTX 4070) an DP, Easy-Switch-Kanal 2. Laptop B (Windows) an USB-C, Kanal 1
- Logitech LIFT (PID B031) und ERGO K860 (PID B359) per Bluetooth LE. Logi Options+ läuft parallel und stört nicht
- `toggle`: Sind Geräte hier verbunden, geht alles zum anderen Laptop (Monitor fest auf `<anderer>`). Sonst wird nur der Monitor umgeschaltet

## Stolpersteine
- `.lnk`-Hotkeys startet der Explorer erst nach ~3 s, deshalb `listen` mit `RegisterHotKey`
- Strg+Alt = AltGr auf deutschen Tastaturen: keine Hotkey-Taste mit AltGr-Zeichen. Strg+Alt+M ist auf Laptop A schon belegt
- `WaitForKeysReleased` vor dem Gerätewechsel, sonst bleiben Strg/Alt auf dem alten Laptop gedrückt
- Mit dem Framework-csc gebaute Programme sprechen standardmäßig nur SSL3/TLS 1.0. Für GitHub vorher `ServicePointManager.SecurityProtocol = Tls12` setzen
- Git Credential Manager zeigt eine Kontoauswahl, sobald mehrere GitHub-Konten gespeichert sind. Auf Laptop A behoben (altes Konto per `git credential-manager github logout <konto>` abgemeldet, `git config --global credential.https://github.com.username marvinwehner`). Git aus Programmen oder Skripten nur mit `GCM_INTERACTIVE=never` und `GIT_TERMINAL_PROMPT=0` aufrufen, sonst erscheinen Fenster oder der Aufruf hängt

## Zusammenarbeit
- Der User hat Claude erlaubt, diese Datei und die README.md selbstständig aktuell zu halten. Bei jeder Änderung prüfen, ob die Doku betroffen ist, und sie im selben Commit anpassen
- Erkenntnisse (Hardware-Verhalten, Stolpersteine, Entscheidungen mit Begründung) hier festhalten, nicht im lokalen Claude-Memory: Das gilt nur auf einem Laptop, das Repo ist auf beiden gleich
- Größere Vorhaben: erst Plan in `PLAN.md`, per Sub-Agents reviewen, dem User vorstellen, nach Freigabe phasenweise umsetzen. Weicht die Umsetzung vom Plan ab, Plan anpassen und die Abweichung nennen
- Der User legt Wert auf sauberen, wartbaren Code: kleine Klassen mit einer klaren Aufgabe, reine Logik getrennt von Win32-Aufrufen, aber nichts auf Vorrat
- Kommunikation, Commits und Doku auf Deutsch

## Offen (Stand 2026-11-02)
- Installer mit `listen` und Autostart auf Laptop A noch nicht ausgeführt und verifiziert
- Laptop B: noch nicht installiert, DDC über USB-C von dort aus ungetestet
- Idee File-Share: vorher PowerToys „Mouse Without Borders“ prüfen (Zwischenablage und Dateien zwischen PCs)
- Plan für Aufräumen, einen Installer und Auto-Update: [PLAN.md](PLAN.md), freigegeben, Umsetzung läuft
