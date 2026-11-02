// Programmversion X.Y.Z, bei Entwicklungsständen mit Zusatz (1.2.0-3-gabc1234, 0.0.0-dev).
// Die Version kommt aus dem Git-Tag vX.Y.Z: build.cmd und der Updater schreiben sie in src\BuildInfo.cs.
using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

public sealed class AppVersion
{
    // Muss vor Current stehen, statische Felder werden in dieser Reihenfolge initialisiert.
    static readonly Regex Pattern = new Regex("^v?([0-9]+)\\.([0-9]+)\\.([0-9]+)(?:-(.+))?$");

    public static readonly AppVersion Current = FromAssembly();

    AppVersion(int major, int minor, int patch, string suffix)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Suffix = suffix;
    }

    public int Major { get; private set; }
    public int Minor { get; private set; }
    public int Patch { get; private set; }
    // leer bei Releases
    public string Suffix { get; private set; }

    public string Tag { get { return "v" + this; } }

    public static AppVersion Parse(string text)
    {
        AppVersion version = TryParse(text);
        if (version == null) throw new FormatException("Keine gültige Version: " + text);
        return version;
    }

    // null, wenn text keine Version ist.
    public static AppVersion TryParse(string text)
    {
        Match m = Pattern.Match(text == null ? "" : text.Trim());
        int major, minor, patch;
        if (!m.Success
            || !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out major)
            || !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minor)
            || !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch))
            return null;
        return new AppVersion(major, minor, patch, m.Groups[4].Value);
    }

    // Vergleicht nur X.Y.Z: Ein Entwicklungsstand nach 1.2.0 gilt wie 1.2.0.
    public bool IsNewerThan(AppVersion other)
    {
        if (Major != other.Major) return Major > other.Major;
        if (Minor != other.Minor) return Minor > other.Minor;
        return Patch > other.Patch;
    }

    public bool SameAs(AppVersion other)
    {
        return other != null && ToString() == other.ToString();
    }

    public override string ToString()
    {
        string number = Major + "." + Minor + "." + Patch;
        return Suffix.Length == 0 ? number : number + "-" + Suffix;
    }

    static AppVersion FromAssembly()
    {
        var attribute = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
            typeof(AppVersion).Assembly, typeof(AssemblyInformationalVersionAttribute));
        AppVersion version = attribute == null ? null : TryParse(attribute.InformationalVersion);
        return version ?? new AppVersion(0, 0, 0, "dev");
    }
}
