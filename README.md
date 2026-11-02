# Tandem

Zwei Laptops, ein Monitor, eine Maus, eine Tastatur. Mit **Strg+Alt+K** wechselt alles zusammen zum anderen Laptop.

## Was beim Hotkey passiert

1. Ein kleines Hinweisfenster erscheint sofort („Umschalten …“, danach das Ergebnis).
2. Tandem prüft, ob der Monitor per DDC/CI erreichbar ist. Wenn nicht, bleiben Maus und Tastatur hier.
3. Logitech-Geräte, die per Bluetooth mit diesem Laptop verbunden sind, wechseln per HID++ auf den anderen Easy-Switch-Kanal (1 ↔ 2).
4. Der Monitor wechselt auf den Eingang des anderen Laptops.

Sind keine Logitech-Geräte verbunden (z. B. Hotkey auf der eingebauten Laptop-Tastatur), wird nur der Monitor zwischen beiden Eingängen umgeschaltet.

## Setup

|                   | Privatlaptop         | Arbeitslaptop |
|-------------------|----------------------|---------------|
| Monitoreingang    | DisplayPort (`0x0F`) | USB-C (`54`)  |
| Easy-Switch-Kanal | 2                    | 1             |

Getestet mit Samsung ViewFinity S9 S95UF (LS49F950U), Logitech LIFT und Logitech ERGO K860 unter Windows 11.

## Installation

Auf jedem Laptop einmal:

1. Repo klonen: `git clone https://github.com/marvinwehner/tandem.git`
2. `Installieren.cmd` starten und den Laptop wählen: **A** Arbeitslaptop (USB-C) oder **P** Privatlaptop (DisplayPort)

Der Installer baut `Tandem.exe` aus dem Quellcode (der C#-Compiler gehört zu Windows), kopiert sie nach `%LOCALAPPDATA%\Programs\Tandem`, trägt sie in den Autostart ein und startet sie. Dazu kommt der Startmenü-Eintrag „Tandem umschalten“ zum Umschalten per Klick. Adminrechte braucht es nicht.

## Updates

Tandem prüft eine Minute nach dem Start und danach täglich, ob es auf GitHub ein neueres Release gibt. Gefragt wird erst, wenn du an diesem Laptop arbeitest, denn sonst zeigt der Monitor gerade den anderen. Nach **Ja** lädt Tandem den Quellcode, baut ihn und startet die neue Version, das dauert ein paar Sekunden. Klappt dabei etwas nicht, läuft die bisherige Version weiter.

Sofort prüfen:

```
"%LOCALAPPDATA%\Programs\Tandem\Tandem.exe" update
```

Ein Build ohne Release-Tag (Entwicklungsstand) prüft nicht automatisch, nur mit `update`.

## Deinstallieren

```
"%LOCALAPPDATA%\Programs\Tandem\Tandem.exe" uninstall
```

Entfernt Autostart, Startmenü-Eintrag und den Ordner `%LOCALAPPDATA%\Programs\Tandem`. Den Repo-Klon löschst du selbst.

## Kommandozeile

```
Tandem.exe listen <rolle>      im Hintergrund auf Strg+Alt+K warten (Autostart)
Tandem.exe toggle <rolle>      einmal umschalten (wie der Hotkey)
Tandem.exe get                 aktuellen Monitoreingang anzeigen
Tandem.exe set <wert>          Monitoreingang setzen (dezimal oder 0x..)
Tandem.exe devices             verbundene Logitech-Geräte und ihren Kanal anzeigen
Tandem.exe version             Version anzeigen
Tandem.exe install <rolle>     für diesen Laptop installieren
Tandem.exe uninstall           deinstallieren
Tandem.exe update              sofort nach Updates suchen

<rolle>: privat, arbeit oder <eigener> <anderer> (Eingangswerte, z. B. 0x0F 54)
```

`get`, `devices` und `version` geben nur etwas aus, wenn die Ausgabe umgeleitet wird, z. B. `Tandem.exe get | more`.

## Fehlersuche

Tandem schreibt ein Protokoll nach `%LOCALAPPDATA%\Programs\Tandem\tandem.log`: Start, Umschalten, Update-Prüfungen und Fehler.

## Hinweise

- Tastenkürzel von Verknüpfungen (.lnk) startet der Explorer erst nach ~3 s. Deshalb läuft ein Hintergrundprozess mit `RegisterHotKey`.
- Strg+Alt entspricht auf deutschen Tastaturen AltGr. Als Hotkey keine Taste mit AltGr-Zeichen nehmen (Q = @, E = €, M = µ, Ziffern …).
- USB-C steht nicht in der DDC-Capabilities-Liste des Samsung, der Wert `54` (`0x36`) funktioniert trotzdem. Werte anderer Monitore mit `get` auslesen, während der jeweilige Eingang aktiv ist.
- DDC/CI funktioniert beim Samsung auch, wenn der eigene Eingang gerade nicht angezeigt wird.

## Entwickeln

`build.cmd` baut `Tandem.exe`, `test.cmd` baut und startet die Tests. Aufbau, Regeln und Release-Ablauf stehen in [CLAUDE.md](CLAUDE.md).
