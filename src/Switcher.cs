// Ablauf beim Umschalten: Maus, Tastatur und Monitor zum anderen Laptop.
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
    // Vorher Hotkey.WaitForRelease aufrufen, damit keine Taste auf diesem Laptop hängen bleibt.
    public static SwitchResult Toggle(Profile profile)
    {
        using (Ddc ddc = Ddc.Open())
        {
            // Ohne erreichbaren Monitor bleiben Maus und Tastatur hier, sonst säße man ohne sie vor diesem Laptop.
            if (ddc.Monitors.Count == 0) return new SwitchResult(false, false, 0, 0);
            int devices = Logitech.SwitchAway();
            uint target = ChooseTarget(profile, ddc.Monitors[0].Input, devices);
            return new SwitchResult(true, ddc.SetInput(target), target, devices);
        }
    }

    // Sind Maus und Tastatur zum anderen Laptop gegangen, folgt der Monitor fest dorthin, damit beide nicht
    // auseinanderlaufen. Sonst wechselt nur der Monitor zwischen beiden Eingängen.
    public static uint ChooseTarget(Profile profile, uint current, int devicesSwitched)
    {
        return (devicesSwitched > 0 || current == profile.Own) ? profile.Other : profile.Own;
    }
}
