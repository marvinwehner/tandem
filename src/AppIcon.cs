// Das App-Icon für Hinweisfenster, Meldungen und Tray. src\tandem.ico steckt zweimal in der exe: als Win32-Icon
// (Explorer, Startmenü, Task-Manager, MessageBox) und als Ressource Tandem.ico (Hinweisfenster, Tray). Dazu kommen
// die Tray-Varianten, bei denen nur die Hälfte des Laptops hell ist, der gerade den Monitor hat.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

public static class AppIcon
{
    // Unter dieser ID legt csc das Icon aus /win32icon ab (IDI_APPLICATION). tests\Tests.cs prüft das am Build.
    public const int Win32ResourceId = 32512;

    public const string Normal = "Tandem.ico";
    public const string MonitorLeft = "TrayLinks.ico";   // Monitor beim Laptop links (Arbeitslaptop, USB-C)
    public const string MonitorRight = "TrayRechts.ico"; // Monitor beim Laptop rechts (Privatlaptop, DisplayPort)

    const int BaseSize = 32;
    // Größen aus der .ico, die das Hinweisfenster ohne Strecken zeichnen kann.
    static readonly int[] OverlaySizes = { 32, 40, 48, 64 };

    static readonly Dictionary<string, Icon> cache = new Dictionary<string, Icon>();
    static readonly object gate = new object();

    // Nächste vorhandene Größe zu 32 px mal Skalierung des Bildschirms, bei Gleichstand die größere.
    public static int SizeFor(float scale)
    {
        float wanted = BaseSize * scale;
        int best = OverlaySizes[0];
        foreach (int size in OverlaySizes)
            if (Math.Abs(size - wanted) <= Math.Abs(best - wanted)) best = size;
        return best;
    }

    public static Icon Get(int size)
    {
        return Get(Normal, size);
    }

    // Icon aus der Ressource in dieser Größe, null wenn es sich nicht laden lässt. Bleibt bis zum Programmende
    // geladen, damit eine Serie von Hotkeys nicht jedes Mal neu lädt und ein Fehler nur einmal im Protokoll steht.
    // Nicht freigeben.
    public static Icon Get(string resource, int size)
    {
        string key = resource + "@" + size;
        lock (gate)
        {
            Icon icon;
            if (cache.TryGetValue(key, out icon)) return icon;
            icon = Load(resource, size);
            cache[key] = icon;
            return icon;
        }
    }

    // Ein Icon-Fehler darf nie das Umschalten blockieren, deshalb wird jede Ausnahme hier abgefangen.
    static Icon Load(string resource, int size)
    {
        try
        {
            using (Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream(resource))
                return new Icon(stream, size, size);
        }
        catch (Exception ex)
        {
            Log.Error("Icon " + resource + " mit " + size + " px ließ sich nicht laden", ex);
            return null;
        }
    }
}
