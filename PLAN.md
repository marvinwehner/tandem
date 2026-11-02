# Plan: Tray-Icon für Tandem

Stand 2026-11-02, nach Review per Sub-Agent, noch nicht freigegeben.

## Ziel

Ein Symbol im Infobereich der Taskleiste, solange der Hintergrundprozess läuft. Es zeigt, welcher Laptop gerade den Monitor hat, und bietet: umschalten, nach Updates suchen, Info, Protokoll, beenden.

Nicht-Ziele: Einstellungen im Tray (Rolle, Hotkey, Autostart), Windows-Benachrichtigungen (Toasts), ein Umbau der Update-Logik (die ist am Gerät noch ungeprüft).

## Bedienung

Rechts- und Linksklick öffnen dasselbe Menü:

```
 Tandem 1.2.0 · Privatlaptop        (grau, nur Anzeige)
 Monitor beim Arbeitslaptop         (grau, nur Anzeige)
 ───────────────────────────────
 Umschalten               Strg+Alt+K
 Nach Updates suchen
 Info …
 Protokoll öffnen
 ───────────────────────────────
 Beenden
```

- Natives Windows-Menü (`ContextMenu`): sieht aus wie Windows 11 und skaliert pro Bildschirm von selbst
- **Umschalten** macht genau, was Strg+Alt+K macht, inklusive Busy-Sperre. Neutral benannt, weil es ohne verbundene Geräte den Monitor hierher holt statt weg
- **Nach Updates suchen** = `Tandem.exe update`, Rückmeldung im Hinweisfenster
- **Info …** zeigt eine Meldung mit Tandem-Icon: Version, Rolle mit Eingängen, Monitor-Zustand mit Uhrzeit, letztes Umschalten, letzte Update-Prüfung mit Ergebnis, Autostart an/aus, Pfad der exe und des Protokolls. Text aus einer reinen, getesteten Funktion
- **Protokoll öffnen** öffnet `tandem.log` im Standard-Editor
- **Beenden** ohne Rückfrage, danach Hinweisfenster „Tandem beendet. Strg+Alt+K wirkt wieder nach der nächsten Anmeldung“. Läuft gerade ein Umschalten, wird erst danach beendet (gilt auch für `Tandem.Beenden` vom Installer, diese Lücke gibt es schon heute)
- Linksklick öffnet das Menü über die interne Methode `NotifyIcon.ShowContextMenu` (Reflection, abgesichert; .NET Framework 4.8.1 ändert sich nicht mehr). Klappt das nicht, passiert beim Linksklick nichts
- Tooltip „Tandem 1.2.0 – Monitor beim Privatlaptop“, im Code auf 63 Zeichen gekürzt (sonst wirft WinForms)

## Symbol mit Zustand

Wie im Entwurf `design/icon-board.html`. Die Hälfte des Laptops, der den Monitor hat, leuchtet: links (Bernstein) DisplayPort = Privatlaptop, rechts (Blau) USB-C = Arbeitslaptop. Auf beiden Laptops gleich.

| Monitoreingang | Symbol |
|---|---|
| DisplayPort `0x0F` | links hell, rechts gedimmt |
| USB-C `54` | rechts hell, links gedimmt |
| anderer Eingang, Monitor aus, nicht erreichbar, noch unbekannt | normales Icon |

- Der Generator erzeugt `tray-links.ico` und `tray-rechts.ico` in 16, 20, 24 und 32 px (gedimmte Hälfte zu 25 % über dem dunklen Rahmen), eingebettet als Ressourcen
- `AppIcon` cacht nach Ressource und Größe, Tray-Größe nach `SystemInformation.SmallIconSize`. Icon nur bei Zustandswechsel setzen, gecachte Icons nie freigeben
- Reine Regel `TrayState.For(uint? input)` mit Test

Der Zustand wird gelesen:
- sofort aus dem Ergebnis jedes eigenen Umschaltens
- alle 30 s per DDC im Hintergrund, aber nur, wenn an diesem Laptop in den letzten 2 min Eingaben kamen. Wer vom anderen Laptop zurückschaltet, bringt die Geräte mit. So läuft über Nacht kein DDC-Verkehr
- Ergebnis per `ui.Post` an den UI-Thread, ins Protokoll nur bei Zustandswechsel

Ehrlich gesagt: Auf dem großen Monitor zeigt das Symbol fast immer „hier“. Nützlich ist es vor allem auf dem Laptop-Display. Deshalb bleibt dieser Teil bewusst klein.

## DDC-Sperre

Abfrage und Umschalten dürfen nicht gleichzeitig mit dem Monitor sprechen, sonst scheitert im schlimmsten Fall das Setzen, nachdem die Geräte schon weg sind. Das Startmenü-`toggle` läuft als eigener Prozess, deshalb ein benannter Mutex `Local\Tandem.Ddc` direkt in `Ddc`: `Open` nimmt ihn, `Dispose` gibt ihn frei. Die Abfrage versucht es mit `WaitOne(0)` und lässt die Runde sonst aus, das Umschalten wartet bis zu 5 s.

## Visual Styles

`src\tandem.manifest` per `/win32manifest` in der rsp: Common Controls 6, `trustInfo asInvoker` und `supportedOS` für Windows 10/11 (das eigene Manifest ersetzt das Standardmanifest von csc). DPI bleibt im Code (`Overlay.InitDpi`). `Application.EnableVisualStyles()` ganz an den Anfang von `Main`, vor `Command.Parse`. Ergebnis: moderne Knöpfe auch in den MessageBoxen, das erledigt den offenen Punkt aus CLAUDE.md.

## Lebenszyklus

- Symbol anlegen, sobald der Hotkey registriert ist
- Bei der Übergabe an eine neue Version sofort ausblenden, bei `TakeBack` wieder einblenden, in `ExitThreadCore` entfernen. So gibt es nach einem Update genau ein Symbol
- Explorer-Neustart: `NotifyIcon` legt sich selbst neu an (TaskbarCreated)

## Aufbau

- `src/Tray.cs`: NotifyIcon, Menü, `TrayState`, Tooltip. Meldet Klicks per Events, kennt keine Abläufe
- `src/MonitorWatch.cs`: die DDC-Abfrage im Hintergrund mit Sperre und Anwesenheitsprüfung, meldet den Eingang zurück
- `src/Ddc.cs`: benannter Mutex in `Open`/`Dispose`
- `src/Listener.cs`: legt Tray und Abfrage an, verdrahtet die Events mit dem Vorhandenen, merkt sich letztes Umschalten und letzte Update-Prüfung für die Info, schiebt Beenden während des Umschaltens auf
- `src/AppIcon.cs`: Cache nach Ressource und Größe
- Info-Text als reine Funktion, z. B. `Tray.InfoText(...)`

## Prüfen

- Tests: `TrayState.For`, Tooltip-Kürzung, Info-Text, Tray-Icons in allen Größen in den Ressourcen, exe mit Manifest (Common Controls 6)
- Sichtprüfung per `PrintWindow`: Info-Meldung und Warnung mit modernen Knöpfen, Tray-Varianten als Bildbogen
- Am Gerät (User): Symbol erscheint nach `Installieren.cmd`, Zustand wechselt in beide Richtungen, auch beim Umschalten vom anderen Laptop, Linksklick öffnet das Menü, Beenden entfernt das Symbol, Update-Übergabe hinterlässt genau ein Symbol, Explorer-Neustart
- README: Windows 11 versteckt neue Symbole zuerst im Überlauf (^). Anheften unter Einstellungen › Personalisierung › Taskleiste › Andere Symbole der Taskleiste, das kann ein Programm nicht selbst

## Offene Entscheidungen

| # | Frage | Empfehlung |
|---|---|---|
| T1 | Info als einfache Meldung (wie oben) oder als eigenes Fenster mit Protokollauszug und Knöpfen? | Meldung: halb so viel Code, gleiche Infos, „Protokoll öffnen“ steht im Menü |
| T2 | Nur „Umschalten“ wie Strg+Alt+K oder zusätzlich „Nur Monitor umschalten“ (Maus und Tastatur bleiben)? | Nur „Umschalten“ |
| T3 | Linksklick: Menü öffnen oder nichts? | Menü |

## Ablauf

Ein Branch, ein PR, Code-Review per Sub-Agent, Merge auf `main`. Danach Release `v1.2.0` (Icon und Tray) nach Rückfrage.
