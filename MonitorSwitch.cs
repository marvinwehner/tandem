// Schaltet den Eingang des Monitors per DDC/CI (VCP 0x60) um.
// Aufruf: MonitorSwitch.exe get           -> zeigt den aktuellen Eingang aller DDC-fähigen Monitore
//         MonitorSwitch.exe set <wert>    -> setzt den Eingang (dezimal oder 0x.., z. B. 0x0F = DisplayPort)
//         MonitorSwitch.exe toggle <eigener> <anderer>
//             -> sind Logitech-Geräte mit diesem PC verbunden: Geräte auf den anderen Kanal (1 <-> 2), Monitor auf <anderer>
//             -> sonst: steht der Eingang auf <eigener>, wird auf <anderer> geschaltet, sonst auf <eigener>
//         MonitorSwitch.exe listen <eigener> <anderer> -> bleibt im Hintergrund, Strg+Alt+K löst toggle aus
//         MonitorSwitch.exe devices        -> zeigt verbundene Logitech-Geräte und ihren Easy-Switch-Kanal
// Samsung ViewFinity S9 S95UF: DisplayPort = 0x0F, USB-C = 54 (0x36), HDMI 1 = 0x11, HDMI 2 = 0x12
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

public static class MonitorSwitch
{
    delegate bool MonEnum(IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string description;
    }

    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonEnum cb, IntPtr data);
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint count);
    [DllImport("dxva2.dll", CharSet = CharSet.Unicode)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint count, [Out] PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll")] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMon, byte code, IntPtr type, out uint current, out uint max);
    [DllImport("dxva2.dll")] static extern bool SetVCPFeature(IntPtr hMon, byte code, uint value);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);

    const byte VCP_INPUT = 0x60;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0 || (args[0] != "get" && args[0] != "set" && args[0] != "toggle" && args[0] != "listen" && args[0] != "devices")
            || (args[0] == "set" && args.Length < 2) || ((args[0] == "toggle" || args[0] == "listen") && args.Length < 3))
        {
            Console.WriteLine("Aufruf: MonitorSwitch.exe get | set <wert> | toggle <eigener> <anderer> | listen <eigener> <anderer> | devices");
            return 2;
        }
        if (args[0] == "devices") { LogiSwitch.PrintInfo(); return 0; }
        if (args[0] == "listen") return Listen(args);
        uint target;
        bool devices;
        if (args[0] != "toggle") return Execute(args, out target, out devices) ? 0 : 1;
        return ToggleWithOverlay(args);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

    // Läuft im Hintergrund (Autostart) und reagiert sofort auf Strg+Alt+K. Tastenkürzel von Verknüpfungen
    // startet der Explorer erst nach ~3 s, deshalb kein .lnk-Hotkey.
    static int Listen(string[] args)
    {
        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000, VK_K = 0x4B, WM_HOTKEY = 0x0312;
        // Nach der Installation gibt der Explorer einen alten Verknüpfungs-Hotkey eventuell erst verzögert frei.
        bool registered = false;
        for (int i = 0; i < 30 && !registered; i++)
        {
            registered = RegisterHotKey(IntPtr.Zero, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_K);
            if (!registered) Thread.Sleep(500);
        }
        if (!registered)
        {
            Overlay.Run("MonitorSwitch", () => "Strg+Alt+K ist von einem anderen Programm belegt");
            return 1;
        }
        var toggleArgs = new[] { "toggle", args[1], args[2] };
        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            if (msg.message == WM_HOTKEY) ToggleWithOverlay(toggleArgs);
        return 0;
    }

    // Hinweis sofort zeigen, weil das Umschalten von Monitor und Geräten ein paar Sekunden dauert.
    static int ToggleWithOverlay(string[] args)
    {
        int exitCode = 1;
        Overlay.Run("Umschalten …", () =>
        {
            uint t;
            bool d;
            bool ok = Execute(args, out t, out d);
            exitCode = ok ? 0 : 1;
            string text = ok ? "Monitor → " + InputName(t) : "Monitor nicht erreichbar";
            return d ? text + "\nMaus + Tastatur → anderer Laptop" : text;
        });
        return exitCode;
    }

    // Führt get, set oder toggle aus. target: gesetzter Eingang, devices: ob Logitech-Geräte umgeschaltet wurden.
    static bool Execute(string[] args, out uint target, out bool devices)
    {
        string mode = args[0];
        target = 0;
        devices = false;
        if (mode == "set") target = ParseValue(args[1]);
        if (mode == "toggle")
        {
            // Sind Maus/Tastatur hier verbunden, geht alles zum anderen PC: der Monitor fest auf <anderer>,
            // damit Monitor und Geräte nicht auseinanderlaufen.
            LogiSwitch.WaitForKeysReleased();
            devices = LogiSwitch.SwitchAway() > 0;
            if (devices) { mode = "set"; target = ParseValue(args[2]); }
        }

        var handles = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, dc, r, d) => { handles.Add(h); return true; }, IntPtr.Zero);

        bool anySet = false;
        foreach (IntPtr hMon in handles)
        {
            uint count;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out count) || count == 0) continue;
            var monitors = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMon, count, monitors)) continue;

            foreach (var m in monitors)
            {
                uint current, max;
                // Monitore ohne DDC/CI-Eingangsumschaltung (z. B. das Laptop-Panel) überspringen.
                if (!GetVCPFeatureAndVCPFeatureReply(m.hPhysicalMonitor, VCP_INPUT, IntPtr.Zero, out current, out max)) continue;

                if (mode == "get")
                {
                    Console.WriteLine(m.description + ": Eingang = " + current + " (0x" + current.ToString("X2") + ")");
                    continue;
                }
                if (mode == "toggle")
                {
                    uint a = ParseValue(args[1]);
                    target = (current == a) ? ParseValue(args[2]) : a;
                }
                // DDC/CI ist manchmal unzuverlässig, deshalb bis zu drei Versuche.
                for (int i = 0; i < 3; i++)
                {
                    if (SetVCPFeature(m.hPhysicalMonitor, VCP_INPUT, target)) { anySet = true; break; }
                    Thread.Sleep(200);
                }
            }
            DestroyPhysicalMonitors(count, monitors);
        }
        return mode == "get" || anySet;
    }

    static string InputName(uint value)
    {
        switch (value)
        {
            case 0x0F: return "DisplayPort";
            case 54: return "USB-C";
            case 0x11: return "HDMI 1";
            case 0x12: return "HDMI 2";
            default: return "Eingang " + value;
        }
    }

    static uint ParseValue(string s)
    {
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(s.Substring(2), 16)
            : Convert.ToUInt32(s);
    }
}
