// Ablauf beim Hotkey: Maus, Tastatur und Monitor zum anderen Laptop.
using System;
using System.Runtime.InteropServices;
using System.Threading;

public sealed class SwitchResult
{
    public SwitchResult(bool monitorFound, bool monitorSet, uint target, int devices)
    {
        MonitorFound = monitorFound;
        MonitorSet = monitorSet;
        Target = target;
        Devices = devices;
    }

    public bool MonitorFound { get; private set; }
    public bool MonitorSet { get; private set; }
    public uint Target { get; private set; }
    // Anzahl der Logitech-Geräte, die zum anderen Laptop gewechselt sind
    public int Devices { get; private set; }

    // Text für das Hinweisfenster
    public string Describe()
    {
        if (!MonitorFound) return "Monitor nicht erreichbar\nMaus + Tastatur bleiben hier";
        string text = MonitorSet ? "Monitor → " + Profile.InputName(Target) : "Monitor ließ sich nicht umschalten";
        return Devices > 0 ? text + "\nMaus + Tastatur → anderer Laptop" : text;
    }
}

public static class Switcher
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    const int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_K = 0x4B;
    const int KeyReleaseTimeoutMs = 3000;

    // Sind Maus und Tastatur hier verbunden, geht alles zum anderen Laptop: der Monitor fest auf den anderen
    // Eingang, damit Monitor und Geräte nicht auseinanderlaufen. Sonst wechselt nur der Monitor zwischen beiden.
    public static SwitchResult Toggle(Profile profile)
    {
        WaitForKeysReleased();
        // Erst prüfen, ob der Monitor per DDC erreichbar ist. Sonst säße man ohne Maus und Tastatur vor diesem Laptop.
        var inputs = Ddc.ReadInputs();
        if (inputs.Count == 0) return new SwitchResult(false, false, 0, 0);

        int devices = Logitech.SwitchAway();
        uint target = (devices > 0 || inputs[0].Value == profile.Own) ? profile.Other : profile.Own;
        return new SwitchResult(true, Ddc.SetInput(target), target, devices);
    }

    // Wartet, bis der Hotkey losgelassen ist. Sonst bleiben Strg/Alt auf diesem PC eventuell "gedrückt",
    // weil die Tastatur schon beim anderen PC ist, bevor sie das Loslassen meldet.
    static void WaitForKeysReleased()
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(KeyReleaseTimeoutMs);
        while (DateTime.UtcNow < deadline && (IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_K)))
            Thread.Sleep(20);
    }

    static bool IsDown(int vk)
    {
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }
}
