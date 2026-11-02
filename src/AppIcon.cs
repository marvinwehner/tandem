// Das App-Icon für Hinweisfenster und Meldungen. src\tandem.ico steckt zweimal in der exe: als Win32-Icon
// (Explorer, Startmenü, Task-Manager, MessageBox) und als Ressource Tandem.ico (Hinweisfenster).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

public static class AppIcon
{
    // Unter dieser ID legt csc das Icon aus /win32icon ab (IDI_APPLICATION). tests\Tests.cs prüft das am Build.
    public const int Win32ResourceId = 32512;

    const string ResourceName = "Tandem.ico";
    const int BaseSize = 32;
    // Größen aus der .ico, die das Hinweisfenster ohne Strecken zeichnen kann.
    static readonly int[] OverlaySizes = { 32, 40, 48, 64 };

    static readonly Dictionary<int, Icon> cache = new Dictionary<int, Icon>();
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

    // Icon in dieser Größe, null wenn es sich nicht laden lässt. Bleibt bis zum Programmende geladen, damit eine
    // Serie von Hotkeys nicht jedes Mal neu lädt und ein Fehler nur einmal im Protokoll steht. Nicht freigeben.
    public static Icon Get(int size)
    {
        lock (gate)
        {
            Icon icon;
            if (cache.TryGetValue(size, out icon)) return icon;
            icon = Load(size);
            cache[size] = icon;
            return icon;
        }
    }

    // Ein Icon-Fehler darf nie das Umschalten blockieren, deshalb wird jede Ausnahme hier abgefangen.
    static Icon Load(int size)
    {
        try
        {
            using (Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName))
                return new Icon(stream, size, size);
        }
        catch (Exception ex)
        {
            Log.Error("App-Icon mit " + size + " px ließ sich nicht laden", ex);
            return null;
        }
    }
}
