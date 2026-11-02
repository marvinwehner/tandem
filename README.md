# Tandem

Two laptops one monitor. A tool that enables switching between.

Zwei Laptops, ein Monitor, eine Maus, eine Tastatur. Mit **Strg+Alt+K** wechselt alles zusammen zum anderen Laptop.

## Was beim Hotkey passiert

1. Ein kleines Hinweisfenster erscheint sofort („Umschalten …“, danach das Ergebnis).
2. Logitech-Geräte, die per Bluetooth mit diesem Laptop verbunden sind, wechseln per HID++ auf den anderen Easy-Switch-Kanal (1 ↔ 2).
3. Der Monitor wechselt per DDC/CI auf den Eingang des anderen Laptops.

Sind keine Logitech-Geräte verbunden (z. B. Hotkey auf der eingebauten Laptop-Tastatur), wird nur der Monitor zwischen beiden Eingängen umgeschaltet.

## Setup

|                   | Laptop A          | Laptop B   |
|-------------------|-------------------|------------|
| Monitoreingang    | DisplayPort (`0x0F`) | USB-C (`54`) |
| Easy-Switch-Kanal | 2                 | 1          |

Getestet mit Samsung ViewFinity S9 S95UF (LS49F950U), Logitech LIFT und Logitech ERGO K860 unter Windows 11.

## Installation

Auf jedem Laptop das Repo klonen und je nach Anschluss am Monitor ausführen:

- `Installieren - Laptop an DisplayPort.cmd`
- `Installieren - Laptop an USB-C.cmd`

Der Installer baut `MonitorSwitch.exe` aus dem Quellcode (`build.cmd`, der C#-Compiler gehört zu Windows), kopiert sie nach `%LOCALAPPDATA%\Programs\MonitorSwitch`, legt einen Autostart-Eintrag an und startet den Hintergrundprozess. Zusätzlich gibt es den Startmenü-Eintrag „Monitor umschalten“ zum Umschalten per Klick.

**Deinstallieren:** Prozess `MonitorSwitch` beenden, `MonitorSwitch.lnk` im Autostart-Ordner (`shell:startup`) und „Monitor umschalten“ im Startmenü löschen, `%LOCALAPPDATA%\Programs\MonitorSwitch` löschen.

## Kommandozeile

```
MonitorSwitch.exe get                          aktuellen Monitoreingang anzeigen
MonitorSwitch.exe set <wert>                   Eingang setzen (dezimal oder 0x..)
MonitorSwitch.exe toggle <eigener> <anderer>   einmal umschalten (wie der Hotkey)
MonitorSwitch.exe listen <eigener> <anderer>   im Hintergrund auf Strg+Alt+K warten
MonitorSwitch.exe devices                      verbundene Logitech-Geräte und Kanal anzeigen
```

`get` und `devices` geben nur etwas aus, wenn die Ausgabe umgeleitet wird, z. B. `MonitorSwitch.exe get | more`.

## Hinweise

- Tastenkürzel von Verknüpfungen (.lnk) startet der Explorer erst nach ~3 s. Deshalb läuft ein Hintergrundprozess mit `RegisterHotKey`.
- Strg+Alt entspricht auf deutschen Tastaturen AltGr. Als Hotkey keine Taste mit AltGr-Zeichen nehmen (Q = @, E = €, M = µ, Ziffern …).
- USB-C steht nicht in der DDC-Capabilities-Liste des Samsung, der Wert `54` (`0x36`) funktioniert trotzdem. Werte anderer Monitore mit `get` auslesen, während der jeweilige Eingang aktiv ist.
- DDC/CI funktioniert beim Samsung auch, wenn der eigene Eingang gerade nicht angezeigt wird.
