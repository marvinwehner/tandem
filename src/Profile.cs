// Die beiden Laptops am Monitor: Jede Rolle kennt den Monitoreingang dieses und des anderen Laptops.
// Werte für den Samsung ViewFinity S9 S95UF. Andere Monitore: mit "get" auslesen, während der Eingang aktiv ist.
using System;
using System.Collections.Generic;
using System.Globalization;

public sealed class Profile
{
    public const uint DisplayPort = 0x0F, Hdmi1 = 0x11, Hdmi2 = 0x12;
    // USB-C steht nicht in der DDC-Capabilities-Liste des Samsung, funktioniert aber.
    public const uint UsbC = 0x36;

    public static readonly Profile Privat = new Profile("privat", "Privatlaptop", DisplayPort, UsbC);
    public static readonly Profile Arbeit = new Profile("arbeit", "Arbeitslaptop", UsbC, DisplayPort);

    Profile(string name, string title, uint own, uint other)
    {
        Name = name;
        Title = title;
        Own = own;
        Other = other;
    }

    // "privat", "arbeit" oder null bei eigenen Eingangswerten
    public string Name { get; private set; }
    public string Title { get; private set; }
    public uint Own { get; private set; }
    public uint Other { get; private set; }

    // Liest eine Rolle ("privat", "arbeit") oder zwei Eingangswerte (<eigener> <anderer>).
    public static Profile Parse(IList<string> args)
    {
        if (args.Count == 1 && args[0] == Privat.Name) return Privat;
        if (args.Count == 1 && args[0] == Arbeit.Name) return Arbeit;
        if (args.Count == 2) return new Profile(null, "Eigene Eingänge", ParseInput(args[0]), ParseInput(args[1]));
        throw new ArgumentException("Erwartet: privat, arbeit oder <eigener> <anderer>.");
    }

    // Für Kommandozeilen, die das Profil weitergeben (Autostart, Startmenü, Update).
    public string ToArguments()
    {
        return Name ?? FormatInput(Own) + " " + FormatInput(Other);
    }

    public override string ToString()
    {
        return Title + " (" + InputName(Own) + ")";
    }

    // Dezimal oder hexadezimal mit 0x, z. B. 54 oder 0x36.
    public static uint ParseInput(string text)
    {
        uint value;
        bool ok = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)
            : uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        if (!ok) throw new ArgumentException("Ungültiger Eingang: " + text);
        return value;
    }

    public static string InputName(uint value)
    {
        switch (value)
        {
            case DisplayPort: return "DisplayPort";
            case UsbC: return "USB-C";
            case Hdmi1: return "HDMI 1";
            case Hdmi2: return "HDMI 2";
            default: return "Eingang " + value;
        }
    }

    static string FormatInput(uint value)
    {
        return "0x" + value.ToString("X2");
    }
}
