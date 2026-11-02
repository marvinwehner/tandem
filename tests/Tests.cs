// Tests für die reine Logik, ohne Test-Framework. test.cmd baut und startet sie.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

public static class Tests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll")] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr data);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    const uint LOAD_LIBRARY_AS_DATAFILE = 0x2;
    const int RT_GROUP_ICON = 14, RT_MANIFEST = 24, ManifestId = 1;

    static int failures;

    public static int Main()
    {
        Run("Version: Tag und Entwicklungsstand lesen", ParsesVersions);
        Run("Version: nur X.Y.Z vergleichen", ComparesVersions);
        Run("Update: Tag aus der GitHub-Weiterleitung", ReadsRedirect);
        Run("Update: wann eine Version angeboten wird", DecidesOffer);
        Run("Umschalten: Ziel-Eingang und Hinweistext", ChoosesTarget);
        Run("Profil: Rollen und eigene Eingänge", ParsesProfiles);
        Run("Kommandozeile: gültige und ungültige Aufrufe", ParsesCommands);
        Run("Logitech: PID und Adresse aus dem HID-Pfad", ParsesHidPaths);
        Run("Icon: alle Größen in der .ico und als Ressource", ContainsIcon);
        Run("Tray: Zustand, Texte und Info", DescribesTray);
        Run("Mittelklick: Rand erkennen und beschreiben", FindsEdge);
        Run("Mittelklick: was geschluckt wird und wann umgeschaltet", FiltersClicks);
        Run("Update: Quellcode bauen, heil und kaputt", BuildsSource);
        Console.WriteLine(failures == 0 ? "Alle Tests bestanden." : failures + " Test(s) fehlgeschlagen.");
        return failures == 0 ? 0 : 1;
    }

    static void ParsesVersions()
    {
        AppVersion v = AppVersion.Parse("v1.2.3");
        Equal(1, v.Major);
        Equal(2, v.Minor);
        Equal(3, v.Patch);
        Equal("", v.Suffix);
        Equal("1.2.3", v.ToString());
        Equal("v1.2.3", v.Tag);

        AppVersion dev = AppVersion.Parse("1.2.0-3-gabc1234");
        Equal("3-gabc1234", dev.Suffix);
        Equal("1.2.0-3-gabc1234", dev.ToString());
        Equal("dev", AppVersion.Parse("0.0.0-dev").Suffix);
        True(AppVersion.Parse("1.2.0").SameAs(AppVersion.Parse("v1.2.0")));
        False(AppVersion.Parse("1.2.0").SameAs(null));

        IsNull(AppVersion.TryParse("1.2"));
        IsNull(AppVersion.TryParse("abc"));
        IsNull(AppVersion.TryParse(null));
        Throws<FormatException>(() => AppVersion.Parse("x"));
    }

    static void ComparesVersions()
    {
        True(AppVersion.Parse("1.10.0").IsNewerThan(AppVersion.Parse("1.9.0")));
        True(AppVersion.Parse("2.0.0").IsNewerThan(AppVersion.Parse("1.99.99")));
        True(AppVersion.Parse("1.2.1").IsNewerThan(AppVersion.Parse("1.2.0-3-gabc")));
        True(AppVersion.Parse("0.0.1").IsNewerThan(AppVersion.Parse("0.0.0-dev")));
        // Ein Entwicklungsstand nach 1.2.0 bekommt 1.2.0 nicht angeboten.
        False(AppVersion.Parse("1.2.0").IsNewerThan(AppVersion.Parse("1.2.0-3-gabc")));
        False(AppVersion.Parse("1.2.0").IsNewerThan(AppVersion.Parse("1.2.0")));
        False(AppVersion.Parse("1.9.0").IsNewerThan(AppVersion.Parse("1.10.0")));
    }

    static void ReadsRedirect()
    {
        Equal("1.2.0", Updater.VersionFromRedirect("https://github.com/marvinwehner/tandem/releases/tag/v1.2.0").ToString());
        IsNull(Updater.VersionFromRedirect("https://github.com/marvinwehner/tandem/releases"));
        IsNull(Updater.VersionFromRedirect("https://github.com/marvinwehner/tandem/releases/tag/test"));
        IsNull(Updater.VersionFromRedirect(null));
    }

    static void DecidesOffer()
    {
        AppVersion installed = AppVersion.Parse("1.2.0");
        AppVersion newer = AppVersion.Parse("1.3.0");
        True(Updater.ShouldOffer(newer, installed, null, false));
        True(Updater.ShouldOffer(newer, AppVersion.Parse("1.2.0-3-gabc"), null, false));
        False(Updater.ShouldOffer(null, installed, null, true));
        False(Updater.ShouldOffer(installed, installed, null, true));
        False(Updater.ShouldOffer(AppVersion.Parse("1.1.9"), installed, null, true));
        // Ein gescheitertes Update nur auf ausdrückliche Anfrage noch einmal.
        False(Updater.ShouldOffer(newer, installed, newer, false));
        True(Updater.ShouldOffer(newer, installed, newer, true));
        True(Updater.ShouldOffer(AppVersion.Parse("1.3.1"), installed, newer, false));
    }

    static void ChoosesTarget()
    {
        Profile p = Profile.Privat;
        Equal(p.Other, Switcher.ChooseTarget(p, p.Own, 2));
        Equal(p.Other, Switcher.ChooseTarget(p, p.Other, 1)); // Geräte weg: Monitor fest zum anderen Laptop
        Equal(p.Other, Switcher.ChooseTarget(p, p.Own, 0));
        Equal(p.Own, Switcher.ChooseTarget(p, p.Other, 0));
        Equal(p.Own, Switcher.ChooseTarget(p, Profile.Hdmi1, 0));

        Equal("Monitor nicht erreichbar\nMaus + Tastatur bleiben hier", new SwitchResult(false, false, 0, 0).Describe());
        Equal("Monitor → USB-C\nMaus + Tastatur → anderer Laptop", new SwitchResult(true, true, Profile.UsbC, 2).Describe());
        Equal("Monitor → DisplayPort", new SwitchResult(true, true, Profile.DisplayPort, 0).Describe());
        Equal("Monitor ließ sich nicht umschalten", new SwitchResult(true, false, Profile.UsbC, 0).Describe());
    }

    static void ParsesProfiles()
    {
        Equal(Profile.Privat, Profile.Parse(new[] { "privat" }));
        Equal(Profile.Arbeit, Profile.Parse(new[] { "arbeit" }));
        Equal(0x0Fu, Profile.Privat.Own);
        Equal(54u, Profile.Privat.Other);
        Equal(54u, Profile.Arbeit.Own);
        Equal(0x0Fu, Profile.Arbeit.Other);
        Equal("privat", Profile.Privat.ToArguments());
        Equal(Side.Left, Profile.Arbeit.Position.Value);
        Equal(Side.Right, Profile.Arbeit.OtherSide.Value);
        Equal(Side.Right, Profile.Privat.Position.Value);
        Equal(Side.Left, Profile.Privat.OtherSide.Value);

        Profile custom = Profile.Parse(new[] { "0x11", "18" });
        Equal(0x11u, custom.Own);
        Equal(0x12u, custom.Other);
        Equal("0x11 0x12", custom.ToArguments());
        False(custom.Position.HasValue);
        False(custom.OtherSide.HasValue);

        Equal("USB-C", Profile.InputName(54));
        Equal("Eingang 99", Profile.InputName(99));
        Throws<ArgumentException>(() => Profile.Parse(new[] { "laptop" }));
        Throws<ArgumentException>(() => Profile.Parse(new[] { "privat", "arbeit" }));
        Throws<ArgumentException>(() => Profile.Parse(new string[0]));
    }

    static void ParsesCommands()
    {
        Command c = Command.Parse(new[] { "listen", "privat" });
        Equal("listen", c.Name);
        Equal(Profile.Privat, c.Profile);
        False(c.HandOver);

        c = Command.Parse(new[] { "install", "arbeit", "--handover" });
        Equal(Profile.Arbeit, c.Profile);
        True(c.HandOver);

        Equal(0x11u, Command.Parse(new[] { "set", "0x11" }).Value);
        Equal(54u, Command.Parse(new[] { "set", "54" }).Value);
        Equal("version", Command.Parse(new[] { "version" }).Name);

        Throws<ArgumentException>(() => Command.Parse(new string[0]));
        Throws<ArgumentException>(() => Command.Parse(new[] { "toggle" }));
        Throws<ArgumentException>(() => Command.Parse(new[] { "install" }));
        Throws<ArgumentException>(() => Command.Parse(new[] { "set" }));
        Throws<ArgumentException>(() => Command.Parse(new[] { "set", "abc" }));
        Throws<ArgumentException>(() => Command.Parse(new[] { "get", "x" }));
        Throws<ArgumentException>(() => Command.Parse(new[] { "update", "--handover" }));
        Throws<ArgumentException>(() => Command.Parse(new[] { "toggle", "privat", "--handover" }));
        Throws<ArgumentException>(() => Command.Parse(new[] { "foo" }));
    }

    static void ParsesHidPaths()
    {
        Logitech.HidPath p = Logitech.HidPath.Parse(
            @"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&02046d_pid&b359_rev&0010_a1b2c3d4e5f6&col04#c&1ea9b2e9&0&0003#{4d1e55b2-f16f-11cf-88cb-001111000030}");
        Equal("b359", p.Pid);
        Equal("a1b2c3d4e5f6", p.Address);

        p = Logitech.HidPath.Parse(@"\\?\HID#{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&02046D_PID&B031_REV&0011_A1B2C3D4E5F6&COL02#X");
        Equal("b031", p.Pid);

        IsNull(Logitech.HidPath.Parse(@"\\?\hid#vid_045e&pid_07a5&mi_01#7&1234#{4d1e55b2-f16f-11cf-88cb-001111000030}"));
        IsNull(Logitech.HidPath.Parse(@"\\?\hid#{00001812}_dev_vid&02046d_pid&b359_rev&0010_a1b2"));
        IsNull(Logitech.HidPath.Parse(@"\\?\hid#{00001812}_dev_vid&02046d_pid&b359&col01#x"));
    }

    static void ContainsIcon()
    {
        Equal("16 20 24 32 40 48 64 256", IcoSizes("tandem.ico"));
        Equal("16 20 24 32", IcoSizes("tray-links.ico"));
        Equal("16 20 24 32", IcoSizes("tray-rechts.ico"));

        Equal(32, AppIcon.SizeFor(1f));
        Equal(40, AppIcon.SizeFor(1.25f));
        Equal(48, AppIcon.SizeFor(1.5f));
        Equal(64, AppIcon.SizeFor(1.75f));
        Equal(64, AppIcon.SizeFor(3f));
        Equal(32, AppIcon.SizeFor(0.5f));
        foreach (int size in new[] { 32, 40, 48, 64 }) Equal(size, AppIcon.Get(size).Width);
        foreach (int size in new[] { 16, 20, 24, 32 })
        {
            Equal(size, AppIcon.Get(AppIcon.MonitorLeft, size).Width);
            Equal(size, AppIcon.Get(AppIcon.MonitorRight, size).Width);
        }
    }

    static void DescribesTray()
    {
        // Wie auf dem Schreibtisch: links Arbeitslaptop (USB-C), rechts Privatlaptop (DisplayPort).
        Equal(TrayState.MonitorLeft, Tray.StateFor(Profile.UsbC));
        Equal(TrayState.MonitorRight, Tray.StateFor(Profile.DisplayPort));
        Equal(TrayState.Unknown, Tray.StateFor(Profile.Hdmi1));
        Equal(TrayState.Unknown, Tray.StateFor(null));

        Equal("Monitor noch nicht gelesen", Tray.MonitorText(null, false, Profile.Privat));
        Equal("Monitor nicht erreichbar", Tray.MonitorText(null, true, Profile.Privat));
        Equal("Monitor beim Privatlaptop (hier)", Tray.MonitorText(Profile.DisplayPort, true, Profile.Privat));
        Equal("Monitor beim Arbeitslaptop", Tray.MonitorText(Profile.UsbC, true, Profile.Privat));
        Equal("Monitor beim Arbeitslaptop (hier)", Tray.MonitorText(Profile.UsbC, true, Profile.Arbeit));
        Equal("Monitor an HDMI 1", Tray.MonitorText(Profile.Hdmi1, true, Profile.Arbeit));

        Equal("kurz", Tray.Tooltip("kurz"));
        string tooltip = Tray.Tooltip("Tandem 1.2.0-12-gabcdef0 – Monitor beim Arbeitslaptop (hier) und noch mehr");
        Equal(63, tooltip.Length);
        True(tooltip.EndsWith("…"));

        string info = Tray.InfoText(new TrayInfo
        {
            Profile = Profile.Privat,
            Monitor = "Monitor beim Privatlaptop (hier)",
            MonitorReadAt = new DateTime(2026, 11, 2, 14, 32, 0),
            LastSwitch = "Monitor → USB-C, Maus + Tastatur → anderer Laptop",
            LastSwitchAt = new DateTime(2026, 11, 2, 14, 2, 0),
            Autostart = Installer.Autostart.Disabled,
            ExePath = @"C:\Tandem.exe",
            LogPath = @"C:\tandem.log",
        });
        True(info.Contains("Privatlaptop: Monitor an DisplayPort, anderer Laptop an USB-C"));
        True(info.Contains("Monitor beim Privatlaptop (hier), gelesen um 14:32"));
        True(info.Contains("Letztes Umschalten: 14:02, Monitor → USB-C, Maus + Tastatur → anderer Laptop"));
        True(info.Contains("Updates: noch nicht geprüft"));
        True(info.Contains("Autostart: im Task-Manager deaktiviert"));
        True(info.Contains(@"Programm: C:\Tandem.exe"));
    }

    static void FindsEdge()
    {
        // Wie auf dem Privatlaptop: Samsung links vom Laptop-Display.
        var samsung = new System.Drawing.Rectangle(-5120, 6, 5120, 1440);
        True(EdgeClick.IsOnEdge(new System.Drawing.Point(-5120, 500), samsung, Side.Left));
        True(EdgeClick.IsOnEdge(new System.Drawing.Point(-5119, 6), samsung, Side.Left));
        False(EdgeClick.IsOnEdge(new System.Drawing.Point(-5118, 500), samsung, Side.Left));
        False(EdgeClick.IsOnEdge(new System.Drawing.Point(-5120, 5), samsung, Side.Left));    // über dem Monitor
        False(EdgeClick.IsOnEdge(new System.Drawing.Point(-5120, 1446), samsung, Side.Left)); // unten exklusiv
        False(EdgeClick.IsOnEdge(new System.Drawing.Point(-1, 500), samsung, Side.Left));     // rechter Rand zählt nicht

        // Wie vermutlich auf dem Arbeitslaptop: Samsung rechts vom Laptop-Display, rechts exklusiv.
        var right = new System.Drawing.Rectangle(2560, 0, 5120, 1440);
        True(EdgeClick.IsOnEdge(new System.Drawing.Point(7679, 700), right, Side.Right));
        True(EdgeClick.IsOnEdge(new System.Drawing.Point(7678, 700), right, Side.Right));
        False(EdgeClick.IsOnEdge(new System.Drawing.Point(7677, 700), right, Side.Right));
        False(EdgeClick.IsOnEdge(new System.Drawing.Point(7680, 700), right, Side.Right));

        var desktop = new System.Drawing.Rectangle(-5120, 0, 7680, 1600);
        Equal("Mittelklick am linken Rand, x = -5120, außen am Desktop", EdgeClick.Describe(samsung, Side.Left, desktop));
        True(EdgeClick.Describe(samsung, Side.Right, desktop).Contains("innen"));
        Equal("Mittelklick am Rand aus", EdgeClick.Describe(null, Side.Left, desktop));
        Equal("Mittelklick am Rand aus", EdgeClick.Describe(samsung, null, desktop));
    }

    static void FiltersClicks()
    {
        bool switchNow;
        var filter = new EdgeClick.Filter();
        // Rand, Rand: beides schlucken, beim Loslassen umschalten
        True(filter.Swallow(true, true, out switchNow)); False(switchNow);
        True(filter.Swallow(false, true, out switchNow)); True(switchNow);
        // Rand, Mitte: beides schlucken, damit die Taste nicht hängt, aber nicht umschalten
        True(filter.Swallow(true, true, out switchNow));
        True(filter.Swallow(false, false, out switchNow)); False(switchNow);
        // Mitte, Rand: beides durchlassen
        False(filter.Swallow(true, false, out switchNow));
        False(filter.Swallow(false, true, out switchNow)); False(switchNow);
        // Mitte, Mitte: durchlassen
        False(filter.Swallow(true, false, out switchNow));
        False(filter.Swallow(false, false, out switchNow)); False(switchNow);
    }

    // Größen aus dem Verzeichnis einer .ico in src, z. B. "16 20 24 32".
    static string IcoSizes(string name)
    {
        string repo = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        using (var reader = new BinaryReader(File.OpenRead(Path.Combine(repo, "src", name))))
        {
            reader.ReadInt16();
            Equal((short)1, reader.ReadInt16()); // Typ Icon
            int count = reader.ReadInt16();
            var sizes = new string[count];
            for (int i = 0; i < count; i++)
            {
                byte width = reader.ReadByte();
                sizes[i] = width == 0 ? "256" : width.ToString();
                reader.ReadBytes(15);
            }
            return string.Join(" ", sizes);
        }
    }

    // Inhalt einer Win32-Ressource der exe, null wenn es sie nicht gibt.
    static byte[] Win32Resource(string exe, int id, int type)
    {
        IntPtr module = LoadLibraryEx(exe, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
        if (module == IntPtr.Zero) return null;
        try
        {
            IntPtr resource = FindResource(module, new IntPtr(id), new IntPtr(type));
            if (resource == IntPtr.Zero) return null;
            var bytes = new byte[SizeofResource(module, resource)];
            Marshal.Copy(LockResource(LoadResource(module, resource)), bytes, 0, bytes.Length);
            return bytes;
        }
        finally { FreeLibrary(module); }
    }

    // Baut eine Kopie des eigenen Quellcodes wie der Updater, einmal heil und einmal mit Syntaxfehler.
    static void BuildsSource()
    {
        string repo = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        string temp = Path.Combine(Path.GetTempPath(), "tandem-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(temp, "src"));
            foreach (string file in Directory.GetFiles(Path.Combine(repo, "src")))
                File.Copy(file, Path.Combine(temp, "src", Path.GetFileName(file)));

            string exe = Updater.Build(temp, AppVersion.Parse("9.8.7"), temp);
            Equal("9.8.7", FileVersionInfo.GetVersionInfo(exe).ProductVersion);
            True(Win32Resource(exe, AppIcon.Win32ResourceId, RT_GROUP_ICON) != null); // Dialog zeigt das Icon über diese ID
            byte[] manifest = Win32Resource(exe, ManifestId, RT_MANIFEST);
            True(manifest != null && System.Text.Encoding.UTF8.GetString(manifest).Contains("Microsoft.Windows.Common-Controls"));

            File.AppendAllText(Path.Combine(temp, @"src\Profile.cs"), "kaputt");
            Throws<InvalidOperationException>(() => Updater.Build(temp, AppVersion.Parse("9.8.8"), temp));
        }
        finally
        {
            try { Directory.Delete(temp, true); }
            catch (IOException) { }
        }
    }

    static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("ok      " + name);
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("FEHLER  " + name + ": " + ex.Message);
        }
    }

    static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual)) throw new Exception("erwartet „" + expected + "“, war „" + actual + "“");
    }

    static void True(bool value)
    {
        if (!value) throw new Exception("erwartet true");
    }

    static void False(bool value)
    {
        if (value) throw new Exception("erwartet false");
    }

    static void IsNull(object value)
    {
        if (value != null) throw new Exception("erwartet null, war „" + value + "“");
    }

    static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("erwartet " + typeof(T).Name);
    }
}
