// Monitoreingang per DDC/CI lesen und setzen (VCP 0x60 "Input Source" über dxva2).
// Monitore ohne DDC/CI-Eingangsumschaltung (z. B. das Laptop-Panel) werden übersprungen.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

public sealed class Ddc : IDisposable
{
    public sealed class Monitor
    {
        public Monitor(IntPtr handle, string name, uint input, Rectangle bounds)
        {
            Handle = handle;
            Name = name;
            Input = input;
            Bounds = bounds;
        }

        public IntPtr Handle { get; private set; }
        public string Name { get; private set; }
        // Eingang beim Öffnen
        public uint Input { get; private set; }
        // Fläche auf dem Windows-Desktop, in physischen Pixeln (Per-Monitor-DPI V2, siehe Overlay.InitDpi)
        public Rectangle Bounds { get; private set; }
    }

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string description;
    }

    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
    [DllImport("dxva2.dll", CharSet = CharSet.Unicode)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll")] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte code, IntPtr type, out uint current, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetVCPFeature(IntPtr hMonitor, byte code, uint value);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    const byte VcpInput = 0x60;
    // DDC/CI ist manchmal unzuverlässig, deshalb mehrere Versuche beim Setzen.
    const int SetAttempts = 3, RetryDelayMs = 200;
    // Nur ein Zugriff auf den Monitor zur Zeit, auch über Prozesse hinweg (Hintergrundprozess, Startmenü-toggle).
    // Sonst kann das Setzen scheitern, wenn gleichzeitig die Zustandsabfrage fürs Tray-Symbol liest.
    const string GateName = @"Local\Tandem.Ddc";
    const int GateTimeoutMs = 5000;

    readonly Mutex gate = new Mutex(false, GateName);
    bool ownsGate;
    readonly List<PHYSICAL_MONITOR[]> opened = new List<PHYSICAL_MONITOR[]>();
    readonly List<Monitor> monitors = new List<Monitor>();

    Ddc() { }

    // Öffnet alle Monitore, die ihren Eingang per DDC/CI melden. Gesetzt wird danach über dieselben Handles
    // ohne erneutes Lesen: Scheiterte ein zweites Lesen, wären Maus und Tastatur schon weg, der Monitor aber nicht.
    // Wartet bis zu 5 s auf einen anderen Zugriff, danach geht es trotzdem weiter: Umschalten hat Vorrang.
    public static Ddc Open()
    {
        return Open(GateTimeoutMs, true);
    }

    // Wie Open, aber null, wenn gerade ein anderer Zugriff läuft. Für die Zustandsabfrage, die dann eine Runde aussetzt.
    public static Ddc TryOpen()
    {
        return Open(0, false);
    }

    static Ddc Open(int timeoutMs, bool openWithoutGate)
    {
        var ddc = new Ddc();
        try
        {
            ddc.ownsGate = Acquire(ddc.gate, timeoutMs);
            if (!ddc.ownsGate && !openWithoutGate)
            {
                ddc.Dispose();
                return null;
            }
            ddc.Enumerate();
            return ddc;
        }
        catch
        {
            ddc.Dispose();
            throw;
        }
    }

    // Ein abgestürzter Vorgänger hinterlässt einen verwaisten Mutex, der gilt als frei.
    static bool Acquire(Mutex mutex, int timeoutMs)
    {
        try { return mutex.WaitOne(timeoutMs); }
        catch (AbandonedMutexException) { return true; }
    }

    public IList<Monitor> Monitors
    {
        get { return monitors.AsReadOnly(); }
    }

    // Setzt den Eingang aller geöffneten Monitore. true, wenn es bei mindestens einem geklappt hat.
    public bool SetInput(uint value)
    {
        bool anySet = false;
        foreach (Monitor m in monitors)
        {
            for (int i = 0; i < SetAttempts; i++)
            {
                if (SetVCPFeature(m.Handle, VcpInput, value)) { anySet = true; break; }
                Thread.Sleep(RetryDelayMs);
            }
        }
        return anySet;
    }

    public void Dispose()
    {
        foreach (PHYSICAL_MONITOR[] array in opened) DestroyPhysicalMonitors((uint)array.Length, array);
        opened.Clear();
        monitors.Clear();
        if (ownsGate)
        {
            gate.ReleaseMutex();
            ownsGate = false;
        }
        gate.Dispose();
    }

    void Enumerate()
    {
        var handles = new List<IntPtr>();
        var areas = new List<Rectangle>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref RECT r, IntPtr d) =>
        {
            handles.Add(h);
            areas.Add(Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom));
            return true;
        }, IntPtr.Zero);

        for (int i = 0; i < handles.Count; i++)
        {
            IntPtr hMonitor = handles[i];
            uint count;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out count) || count == 0) continue;
            var array = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, array)) continue;
            opened.Add(array);
            foreach (PHYSICAL_MONITOR m in array)
            {
                uint current, max;
                if (GetVCPFeatureAndVCPFeatureReply(m.hPhysicalMonitor, VcpInput, IntPtr.Zero, out current, out max))
                    monitors.Add(new Monitor(m.hPhysicalMonitor, m.description, current, areas[i]));
            }
        }
    }
}
