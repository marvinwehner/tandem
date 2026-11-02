// Tandem: zwei Laptops an einem Monitor. Strg+Alt+K schickt Monitor, Maus und Tastatur zum anderen Laptop.
// Einstieg: prüft die Kommandozeile und ruft den passenden Ablauf auf.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Overlay.InitDpi();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => Log.Error("Unerwarteter Fehler", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("Unerwarteter Fehler", e.ExceptionObject as Exception);

        Command command;
        try { command = Command.Parse(args); }
        catch (ArgumentException ex)
        {
            Console.WriteLine(ex.Message + "\n\n" + Command.Usage);
            Dialog.Warn(ex.Message + "\n\n" + Command.Usage);
            return 2;
        }
        try { return Run(command); }
        catch (Exception ex)
        {
            Log.Error(command.Name, ex);
            Dialog.Warn("Fehler bei „" + command.Name + "“: " + ex.Message);
            return 1;
        }
    }

    static int Run(Command command)
    {
        switch (command.Name)
        {
            case "get":
                foreach (Ddc.MonitorInput m in Ddc.ReadInputs())
                    Console.WriteLine(m.Name + ": Eingang = " + m.Value + " (0x" + m.Value.ToString("X2") + ")");
                return 0;
            case "set":
                return Ddc.SetInput(command.Value) ? 0 : 1;
            case "toggle":
                return Toggle(command.Profile);
            case "listen":
                return Listener.Run(command.Profile);
            case "devices":
                PrintDevices();
                return 0;
            case "version":
                Console.WriteLine(AppVersion.Current);
                return 0;
            case "install":
                Installer.Install(command.Profile, command.Update);
                return 0;
            case "uninstall":
                Installer.Uninstall();
                return 0;
            case "update":
                return RequestUpdate();
            default:
                throw new InvalidOperationException("Kommando ohne Ablauf: " + command.Name);
        }
    }

    // Hinweis sofort zeigen, weil das Umschalten von Monitor und Geräten ein paar Sekunden dauert.
    static int Toggle(Profile profile)
    {
        bool monitorSet = false;
        Overlay.Run("Umschalten …", () =>
        {
            SwitchResult result = Switcher.Toggle(profile);
            monitorSet = result.MonitorSet;
            return result.Describe();
        });
        return monitorSet ? 0 : 1;
    }

    static void PrintDevices()
    {
        List<Logitech.DeviceStatus> devices = Logitech.Describe();
        if (devices.Count == 0) Console.WriteLine("Keine Logitech-Geräte mit diesem PC verbunden.");
        foreach (Logitech.DeviceStatus d in devices)
            Console.WriteLine(d.Name + " (" + d.Address + "): "
                + (d.Channel == 0 ? "Easy-Switch nicht erreichbar" : "Kanal " + d.Channel + " von " + d.ChannelCount));
    }

    static int RequestUpdate()
    {
        if (Listener.RequestUpdateCheck()) return 0;
        Dialog.Inform("Tandem läuft gerade nicht im Hintergrund. Es startet nach dem nächsten Anmelden oder mit Installieren.cmd.");
        return 1;
    }
}

// Ein Aufruf von der Kommandozeile, vollständig geprüft, bevor etwas passiert.
public sealed class Command
{
    public const string Usage =
        "Tandem.exe listen <rolle>  –  im Hintergrund auf Strg+Alt+K warten (Autostart)\n" +
        "Tandem.exe toggle <rolle>  –  einmal umschalten\n" +
        "Tandem.exe get  –  aktuellen Monitoreingang anzeigen\n" +
        "Tandem.exe set <wert>  –  Monitoreingang setzen (dezimal oder 0x..)\n" +
        "Tandem.exe devices  –  verbundene Logitech-Geräte und ihren Kanal anzeigen\n" +
        "Tandem.exe version  –  Version anzeigen\n" +
        "Tandem.exe install <rolle>  –  für diesen Laptop installieren\n" +
        "Tandem.exe uninstall  –  deinstallieren\n" +
        "Tandem.exe update  –  sofort nach Updates suchen\n\n" +
        "<rolle>: privat (Monitor an DisplayPort), arbeit (Monitor an USB-C) oder <eigener> <anderer> Eingang";

    Command() { }

    public string Name { get; private set; }
    public Profile Profile { get; private set; }
    public uint Value { get; private set; }
    // Nur bei install: Die alte Version läuft noch und wartet auf die Übergabe.
    public bool Update { get; private set; }

    public static Command Parse(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("Kein Kommando angegeben.");
        var command = new Command();
        command.Name = args[0];
        List<string> rest = args.Skip(1).ToList();
        command.Update = rest.Remove("--update");
        if (command.Update && command.Name != "install") throw new ArgumentException("--update gibt es nur bei install.");

        switch (command.Name)
        {
            case "get":
            case "devices":
            case "version":
            case "uninstall":
            case "update":
                if (rest.Count > 0) throw new ArgumentException("Zu viele Argumente für " + command.Name + ".");
                break;
            case "set":
                if (rest.Count != 1) throw new ArgumentException("set braucht genau einen Wert.");
                command.Value = Profile.ParseInput(rest[0]);
                break;
            case "toggle":
            case "listen":
            case "install":
                command.Profile = Profile.Parse(rest);
                break;
            default:
                throw new ArgumentException("Unbekanntes Kommando: " + command.Name);
        }
        return command;
    }
}
