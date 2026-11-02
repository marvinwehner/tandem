// Tandem: zwei Laptops an einem Monitor. Strg+Alt+K schickt Monitor, Maus und Tastatur zum anderen Laptop.
// Einstieg: prüft die Kommandozeile, ruft den passenden Ablauf auf und zeigt die Meldungen.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

public static class Program
{
    // Wie lange der Installer auf den Hintergrundprozess wartet: Der wartet selbst bis zu 10 s auf den Mutex
    // und 15 s auf den Hotkey.
    const int ListenerStartTimeoutMs = 30000;
    const int StopGraceMs = 10000;

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
                using (Ddc ddc = Ddc.Open())
                    foreach (Ddc.Monitor m in ddc.Monitors)
                        Console.WriteLine(m.Name + ": Eingang = " + m.Input + " (0x" + m.Input.ToString("X2") + "), Bereich "
                            + m.Bounds.X + "," + m.Bounds.Y + " " + m.Bounds.Width + "x" + m.Bounds.Height);
                return 0;
            case "set":
                using (Ddc ddc = Ddc.Open())
                    return ddc.SetInput(command.Value) ? 0 : 1;
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
                return command.HandOver ? CompleteHandOver(command.Profile) : Install(command.Profile);
            case "uninstall":
                return Uninstall();
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
            Hotkey.WaitForRelease();
            SwitchResult result = Switcher.Toggle(profile);
            monitorSet = result.MonitorSet;
            return result.Describe();
        });
        return monitorSet ? 0 : 1;
    }

    static void PrintDevices()
    {
        List<Logitech.DeviceStatus> devices = Logitech.ListDevices();
        if (devices.Count == 0) Console.WriteLine("Keine Logitech-Geräte mit diesem PC verbunden.");
        foreach (Logitech.DeviceStatus d in devices)
            Console.WriteLine(d.Name + " (" + d.Address + "): "
                + (d.Channel == 0 ? "Easy-Switch nicht erreichbar" : "Kanal " + d.Channel + " von " + d.ChannelCount));
    }

    static int Install(Profile profile)
    {
        StopBackgroundProcess();
        using (EventWaitHandle ready = Listener.CreateReadySignal())
        {
            List<string> notes = Installer.Install(profile);
            bool running = ready.WaitOne(ListenerStartTimeoutMs);
            string text = running
                ? "Tandem " + AppVersion.Current + " ist installiert: " + profile + ".\n\n"
                    + Hotkey.Name + " schickt Monitor, Maus und Tastatur zum anderen Laptop."
                : "Tandem " + AppVersion.Current + " ist installiert, läuft aber nicht. Vermutlich ist " + Hotkey.Name
                    + " von einem anderen Programm belegt, z. B. vom alten MonitorSwitch.\n\nDetails: " + Log.FilePath;
            foreach (string note in notes) text += "\n\n" + note;
            if (running) Dialog.Inform(text);
            else Dialog.Warn(text);
            return running ? 0 : 1;
        }
    }

    // Von der alten Version gestartet: Die wartet auf das Signal des neuen Hintergrundprozesses und beendet sich dann.
    static int CompleteHandOver(Profile profile)
    {
        Installer.Install(profile);
        Overlay.Run("Tandem " + AppVersion.Current + " ist installiert");
        return 0;
    }

    static int Uninstall()
    {
        StopBackgroundProcess();
        bool removed = Installer.Uninstall();
        Dialog.Inform("Tandem ist deinstalliert.");
        // Erst nach dem Dialog: cmd löscht den Ordner nach ca. 2 s, dann muss diese exe schon beendet sein.
        if (!removed) Installer.RemoveFolderAfterExit();
        return 0;
    }

    static void StopBackgroundProcess()
    {
        Listener.RequestStop();
        Installer.StopOtherInstances(StopGraceMs);
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
        "Tandem.exe listen <rolle>  –  im Hintergrund auf " + Hotkey.Name + " und Mittelklick am Rand warten (Autostart)\n" +
        "Tandem.exe toggle <rolle>  –  einmal umschalten\n" +
        "Tandem.exe get  –  aktuellen Monitoreingang anzeigen\n" +
        "Tandem.exe set <wert>  –  Monitoreingang setzen (dezimal oder 0x..)\n" +
        "Tandem.exe devices  –  verbundene Logitech-Geräte und ihren Kanal anzeigen\n" +
        "Tandem.exe version  –  Version anzeigen\n" +
        "Tandem.exe install <rolle>  –  für diesen Laptop installieren\n" +
        "Tandem.exe uninstall  –  deinstallieren\n" +
        "Tandem.exe update  –  sofort nach Updates suchen\n\n" +
        "<rolle>: privat (Monitor an DisplayPort), arbeit (Monitor an USB-C) oder <eigener> <anderer> Eingang";

    const string HandOverFlag = "--handover";

    Command() { }

    public string Name { get; private set; }
    public Profile Profile { get; private set; }
    public uint Value { get; private set; }
    // Nur bei install: Die alte Version läuft noch und wartet auf die Übergabe (siehe Listener).
    public bool HandOver { get; private set; }

    public static Command Parse(string[] args)
    {
        if (args.Length == 0) throw new ArgumentException("Kein Kommando angegeben.");
        var command = new Command();
        command.Name = args[0];
        List<string> rest = args.Skip(1).ToList();
        command.HandOver = rest.Remove(HandOverFlag);
        if (command.HandOver && command.Name != "install") throw new ArgumentException(HandOverFlag + " gibt es nur bei install.");

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
