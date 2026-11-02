// Monitoreingang per DDC/CI lesen und setzen (VCP 0x60 "Input Source" über dxva2).
// Monitore ohne DDC/CI-Eingangsumschaltung (z. B. das Laptop-Panel) werden übersprungen.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

public static class Ddc
{
    public sealed class MonitorInput
    {
        public MonitorInput(string name, uint value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; private set; }
        public uint Value { get; private set; }
    }

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

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
    const int SetAttempts = 3, RetryDelayMs = 200;

    public static List<MonitorInput> ReadInputs()
    {
        var result = new List<MonitorInput>();
        ForEachMonitor((handle, name, current) => result.Add(new MonitorInput(name, current)));
        return result;
    }

    // Setzt den Eingang aller DDC-fähigen Monitore. true, wenn es bei mindestens einem geklappt hat.
    public static bool SetInput(uint value)
    {
        bool anySet = false;
        ForEachMonitor((handle, name, current) =>
        {
            // DDC/CI ist manchmal unzuverlässig, deshalb mehrere Versuche.
            for (int i = 0; i < SetAttempts; i++)
            {
                if (SetVCPFeature(handle, VcpInput, value)) { anySet = true; return; }
                Thread.Sleep(RetryDelayMs);
            }
        });
        return anySet;
    }

    // Ruft action mit Handle, Name und aktuellem Eingang für jeden Monitor auf, der den Eingang per DDC/CI meldet.
    static void ForEachMonitor(Action<IntPtr, string, uint> action)
    {
        var handles = new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, dc, r, d) => { handles.Add(h); return true; }, IntPtr.Zero);

        foreach (IntPtr hMonitor in handles)
        {
            uint count;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out count) || count == 0) continue;
            var monitors = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors)) continue;
            try
            {
                foreach (PHYSICAL_MONITOR m in monitors)
                {
                    uint current, max;
                    if (GetVCPFeatureAndVCPFeatureReply(m.hPhysicalMonitor, VcpInput, IntPtr.Zero, out current, out max))
                        action(m.hPhysicalMonitor, m.description, current);
                }
            }
            finally { DestroyPhysicalMonitors(count, monitors); }
        }
    }
}
