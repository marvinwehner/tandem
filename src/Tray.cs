// Symbol im Infobereich der Taskleiste. Die helle Hälfte zeigt, welcher Laptop den Monitor hat (links
// Privatlaptop an DisplayPort, rechts Arbeitslaptop an USB-C). Das Menü meldet Klicks über Events,
// die Abläufe dahinter verdrahtet der Listener.
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

public enum TrayState { Unknown, MonitorLeft, MonitorRight }

// Inhalt der Info-Meldung, vom Listener beim Öffnen gefüllt.
public sealed class TrayInfo
{
    public Profile Profile { get; set; }
    public string Monitor { get; set; }
    public DateTime? MonitorReadAt { get; set; }
    public string LastSwitch { get; set; }
    public DateTime? LastSwitchAt { get; set; }
    public string LastCheck { get; set; }
    public DateTime? LastCheckAt { get; set; }
    public Installer.Autostart Autostart { get; set; }
    public string ExePath { get; set; }
    public string LogPath { get; set; }
}

public sealed class Tray : IDisposable
{
    const int TooltipMax = 63; // mehr erlaubt NotifyIcon nicht, sonst wirft es
    // WinForms öffnet das Menü nur per Rechtsklick. Für den Linksklick gibt es nur diese interne Methode.
    static readonly MethodInfo ShowContextMenuMethod =
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

    readonly NotifyIcon notifyIcon = new NotifyIcon();
    readonly ContextMenu menu;
    readonly MenuItem statusItem;
    TrayState? shownState;

    public event EventHandler SwitchRequested, UpdateCheckRequested, InfoRequested, LogRequested, ExitRequested;

    public Tray(string title)
    {
        var titleItem = new MenuItem(title);
        titleItem.Enabled = false;
        statusItem = new MenuItem();
        statusItem.Enabled = false;
        // Natives Windows-Menü: sieht aus wie Windows 11 und skaliert pro Bildschirm von selbst.
        menu = new ContextMenu(new[]
        {
            titleItem,
            statusItem,
            new MenuItem("-"),
            new MenuItem("Umschalten\t" + Hotkey.Name, (s, e) => Raise(SwitchRequested)),
            new MenuItem("Nach Updates suchen", (s, e) => Raise(UpdateCheckRequested)),
            new MenuItem("Info …", (s, e) => Raise(InfoRequested)),
            new MenuItem("Protokoll öffnen", (s, e) => Raise(LogRequested)),
            new MenuItem("-"),
            new MenuItem("Beenden", (s, e) => Raise(ExitRequested)),
        });
        notifyIcon.ContextMenu = menu;
        notifyIcon.MouseClick += OnMouseClick;
        SetMonitor(TrayState.Unknown, MonitorText(null, false, null));
    }

    public void Show()
    {
        notifyIcon.Visible = true;
    }

    public void Hide()
    {
        notifyIcon.Visible = false;
    }

    public void SetMonitor(TrayState state, string text)
    {
        statusItem.Text = text;
        notifyIcon.Text = Tooltip("Tandem " + AppVersion.Current + " – " + text);
        if (shownState == state) return;
        shownState = state;
        notifyIcon.Icon = IconFor(state);
    }

    public void Dispose()
    {
        notifyIcon.Visible = false; // sonst bleibt bis zum nächsten Darüberfahren ein totes Symbol stehen
        notifyIcon.Dispose();
        menu.Dispose();
    }

    // Links (Bernstein) ist der Privatlaptop an DisplayPort, rechts (Blau) der Arbeitslaptop an USB-C, auf beiden Laptops gleich.
    public static TrayState StateFor(uint? input)
    {
        if (input == Profile.Privat.Own) return TrayState.MonitorLeft;
        if (input == Profile.Arbeit.Own) return TrayState.MonitorRight;
        return TrayState.Unknown;
    }

    // Für Menü und Tooltip. known = schon einmal gelesen, input = null heißt nicht erreichbar.
    public static string MonitorText(uint? input, bool known, Profile here)
    {
        if (!known) return "Monitor noch nicht gelesen";
        if (input == null) return "Monitor nicht erreichbar";
        string where;
        if (input == Profile.Privat.Own) where = "Monitor beim " + Profile.Privat.Title;
        else if (input == Profile.Arbeit.Own) where = "Monitor beim " + Profile.Arbeit.Title;
        else where = "Monitor an " + Profile.InputName(input.Value);
        return here != null && input == here.Own ? where + " (hier)" : where;
    }

    public static string Tooltip(string text)
    {
        return text.Length <= TooltipMax ? text : text.Substring(0, TooltipMax - 1) + "…";
    }

    public static string InfoText(TrayInfo info)
    {
        Profile p = info.Profile;
        return "Tandem " + AppVersion.Current + "\n"
            + p.Title + ": Monitor an " + Profile.InputName(p.Own) + ", anderer Laptop an " + Profile.InputName(p.Other) + "\n\n"
            + info.Monitor + (info.MonitorReadAt.HasValue ? ", gelesen um " + Time(info.MonitorReadAt.Value) : "") + "\n"
            + "Letztes Umschalten: " + (info.LastSwitchAt.HasValue ? Time(info.LastSwitchAt.Value) + ", " + info.LastSwitch : "noch nicht") + "\n"
            + "Updates: " + (info.LastCheckAt.HasValue ? "zuletzt geprüft um " + Time(info.LastCheckAt.Value) + ", " + info.LastCheck : "noch nicht geprüft") + "\n"
            + "Autostart: " + AutostartText(info.Autostart) + "\n\n"
            + "Programm: " + info.ExePath + "\n"
            + "Protokoll: " + info.LogPath;
    }

    static string AutostartText(Installer.Autostart autostart)
    {
        switch (autostart)
        {
            case Installer.Autostart.Active: return "aktiv";
            case Installer.Autostart.Disabled: return "im Task-Manager deaktiviert";
            default: return "nicht eingerichtet";
        }
    }

    static string Time(DateTime time)
    {
        return time.ToString("HH:mm");
    }

    static Icon IconFor(TrayState state)
    {
        int size = SystemInformation.SmallIconSize.Width;
        string resource = state == TrayState.MonitorLeft ? AppIcon.MonitorLeft
            : state == TrayState.MonitorRight ? AppIcon.MonitorRight
            : AppIcon.Normal;
        return AppIcon.Get(resource, size) ?? AppIcon.Get(size) ?? SystemIcons.Application;
    }

    void OnMouseClick(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || ShowContextMenuMethod == null) return;
        try { ShowContextMenuMethod.Invoke(notifyIcon, null); }
        catch (Exception ex) { Log.Error("Menü ließ sich per Linksklick nicht öffnen", ex); }
    }

    void Raise(EventHandler handler)
    {
        if (handler != null) handler(this, EventArgs.Empty);
    }
}
