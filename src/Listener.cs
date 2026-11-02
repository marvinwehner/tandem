// Hintergrundprozess (Autostart): reagiert sofort auf Strg+Alt+K und den Mittelklick am Rand, zeigt das Tray-Symbol
// und prüft regelmäßig auf Updates. Läuft nur einmal (Mutex). Installer und "Tandem.exe update" sprechen ihn über benannte Events an.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

public sealed class Listener : ApplicationContext
{
    const string MutexName = @"Local\Tandem";
    const string StopEventName = @"Local\Tandem.Beenden";
    const string CheckEventName = @"Local\Tandem.Pruefen";
    // Setzt ein Hintergrundprozess, sobald er Mutex und Hotkey hat. Darauf warten Installer und alte Version.
    const string ReadyEventName = @"Local\Tandem.Bereit";

    // Beim Start kann eine vorige Instanz noch dabei sein, sich zu beenden (Installer, Update).
    const int MutexTimeoutMs = 10000;
    const int HandOverTimeoutMs = 30000;
    // Ein Umschalten aus dem Startmenü bekommt Zeit, fertig zu werden, bevor hängende Prozesse beendet werden.
    const int StopGraceMs = 5000;
    // Kurzer Takt, damit eine offene Update-Frage bald erscheint, wenn jemand an den Laptop zurückkommt.
    // Fällig ist eine Prüfung nach der Uhrzeit (nextCheck), das übersteht auch den Ruhezustand.
    const int TickMs = 30000;
    // Nach einer Anzeigeänderung bis zu 6 Nachfragen im Abstand von 5 s, bis DDC wieder antwortet.
    const int EdgeRetryMs = 5000, EdgeRetryCount = 6;
    static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1); // Netz ist direkt nach der Anmeldung oft noch nicht da
    static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);
    static readonly TimeSpan PresenceWindow = TimeSpan.FromMinutes(2);

    enum UpdateState { Idle, Checking, Asking, Updating }

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    readonly Profile profile;
    readonly Mutex mutex;
    readonly Hotkey hotkey;
    readonly SynchronizationContext ui;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly EventWaitHandle stopEvent, checkEvent, readyEvent;
    readonly List<RegisteredWaitHandle> waits = new List<RegisteredWaitHandle>();

    bool ownsMutex = true;
    bool busy;                      // Umschalten läuft, ein weiterer Hotkey würde sofort zurückschalten
    UpdateState state = UpdateState.Idle;
    bool manualCheckRequested;      // "Tandem.exe update" kam während einer automatischen Prüfung
    DateTime nextCheck = DateTime.UtcNow + FirstCheckDelay;
    AppVersion pending;             // gefundenes Update, wartet darauf, dass jemand an diesem Laptop arbeitet
    AppVersion failed;              // gescheitertes Update, bis zum Neustart nicht automatisch erneut anbieten
    string preparedExe;             // fertig gebautes Update, wartet auf das Ende des Umschaltens
    AppVersion preparedVersion;

    readonly Tray tray;
    readonly MonitorWatch watch;
    bool quitting;                  // Beenden ist angefragt, wartet eventuell auf das Ende des Umschaltens
    bool quitWithNotice;
    bool exiting;
    bool infoOpen;
    // Für Tray-Symbol und Info
    bool monitorKnown;
    uint? monitorInput;
    DateTime? monitorReadAt;
    string lastSwitch, lastCheck;
    DateTime? lastSwitchAt, lastCheckAt;

    readonly EdgeClick edgeClick;
    Rectangle? edgeMonitor;         // Bereich des Monitors für den Mittelklick-Rand, null = unbekannt
    int edgeRetries;                // kurze Nachfragen nach einer Anzeigeänderung, bis der Rand wieder bekannt ist
    readonly System.Windows.Forms.Timer edgeRetry = new System.Windows.Forms.Timer();

    // Läuft bis zum Beenden. 0 = normal beendet, 1 = konnte nicht starten.
    public static int Run(Profile profile)
    {
        using (var mutex = new Mutex(false, MutexName))
        {
            if (!Acquire(mutex, MutexTimeoutMs))
            {
                Overlay.Run("Tandem läuft schon");
                return 1;
            }
            using (var listener = new Listener(profile, mutex))
            {
                if (!listener.hotkey.Register())
                {
                    Log.Info(Hotkey.Name + " ist belegt, Tandem beendet sich");
                    Overlay.Run(Hotkey.Name + " ist von einem anderen Programm belegt");
                    return 1;
                }
                Log.Info("Tandem " + AppVersion.Current + " läuft: " + profile);
                if (!listener.edgeClick.Start()) Log.Info("Mittelklick am Rand ist nicht verfügbar");
                listener.tray.Show();
                listener.watch.Poll();
                listener.readyEvent.Set();
                Updater.CleanUp();
                Application.Run(listener);
            }
        }
        return 0;
    }

    // Bittet einen laufenden Hintergrundprozess, sich zu beenden. false, wenn keiner läuft.
    public static bool RequestStop()
    {
        return Signal(StopEventName);
    }

    // Bittet den laufenden Hintergrundprozess, sofort nach Updates zu suchen. false, wenn keiner läuft.
    public static bool RequestUpdateCheck()
    {
        return Signal(CheckEventName);
    }

    // Für den Installer: wird gesetzt, sobald der danach gestartete Hintergrundprozess läuft.
    // Vor dem Start öffnen, damit das Signal nicht verloren geht. Nicht bei einer Übergabe, da wartet die alte Version.
    public static EventWaitHandle CreateReadySignal()
    {
        var ready = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEventName);
        ready.Reset();
        return ready;
    }

    Listener(Profile profile, Mutex mutex)
    {
        this.profile = profile;
        this.mutex = mutex;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        ui = SynchronizationContext.Current;
        hotkey = new Hotkey(Toggle);
        stopEvent = ListenFor(StopEventName, () => Quit(false));
        checkEvent = ListenFor(CheckEventName, () => CheckForUpdate(true));
        readyEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEventName);
        watch = new MonitorWatch(ui, OnReading);
        edgeClick = new EdgeClick(ui, Toggle);
        // Nach SetSynchronizationContext abonnieren, dann kommt das Event auf dem UI-Thread an.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        edgeRetry.Interval = EdgeRetryMs;
        edgeRetry.Tick += (s, e) =>
        {
            edgeRetry.Stop();
            watch.Poll();
        };
        tray = new Tray("Tandem " + AppVersion.Current + " · " + profile.Title);
        tray.SwitchRequested += (s, e) => Toggle();
        tray.UpdateCheckRequested += (s, e) => CheckForUpdate(true);
        tray.InfoRequested += (s, e) => ShowInfo();
        tray.LogRequested += (s, e) => OpenLog();
        tray.ExitRequested += (s, e) => Quit(true);
        timer.Interval = TickMs;
        timer.Tick += (s, e) => OnTick();
        timer.Start();
    }

    protected override void ExitThreadCore()
    {
        timer.Stop();
        tray.Hide();
        hotkey.Unregister();
        edgeClick.Stop();
        ReleaseMutex();
        Log.Info("Tandem " + AppVersion.Current + " beendet");
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (RegisteredWaitHandle wait in waits) wait.Unregister(null);
            timer.Dispose();
            stopEvent.Dispose();
            checkEvent.Dispose();
            readyEvent.Dispose();
            tray.Dispose();
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; // statisches Event, sonst bleibt der Listener daran hängen
            edgeClick.Dispose();
            edgeRetry.Dispose();
            hotkey.Dispose();
            ReleaseMutex();
        }
        base.Dispose(disposing);
    }

    // Per Hotkey oder Tray-Menü.
    void Toggle()
    {
        if (busy || quitting) return;
        busy = true;
        SwitchResult result = null;
        try
        {
            Overlay.Show("Umschalten …", () =>
            {
                Hotkey.WaitForRelease();
                result = Switcher.Toggle(profile);
                string text = result.Describe();
                Log.Info("Umgeschaltet: " + text.Replace("\n", ", "));
                return text;
            }, () => OnSwitched(result));
        }
        catch (Exception ex)
        {
            busy = false;
            Log.Error("Hinweisfenster ließ sich nicht öffnen", ex);
        }
    }

    void OnSwitched(SwitchResult result)
    {
        busy = false;
        if (result != null)
        {
            lastSwitch = result.Describe().Replace("\n", ", ");
            lastSwitchAt = DateTime.Now;
            if (result.MonitorSet) ShowMonitor(result.Target);
            else if (!result.MonitorFound) ShowMonitor(null);
        }
        // Eine Abfrage während des Umschaltens setzt aus. War der Rand da gerade unbekannt, jetzt nachholen.
        if (!edgeMonitor.HasValue && !quitting) watch.Poll();
        // Beenden oder ein Update, das während des Umschaltens kam, erst jetzt.
        // Ein Abbruch mittendrin ließe Monitor und Geräte auseinanderlaufen.
        if (quitting) Quit(quitWithNotice);
        else if (preparedExe != null) HandOver();
    }

    // Aus dem Tray-Menü mit Hinweis, auf Bitte des Installers ohne. Läuft gerade ein Umschalten, erst danach.
    void Quit(bool notice)
    {
        quitting = true;
        quitWithNotice |= notice;
        if (busy || exiting) return;
        exiting = true;
        tray.Hide();
        Log.Info(notice ? "Über das Tray-Menü beendet" : "Auf Anfrage beendet");
        if (quitWithNotice) Overlay.Show("Tandem beendet\n" + Hotkey.Name + " wirkt wieder nach der nächsten Anmeldung", null, ExitThread);
        else ExitThread();
    }

    // Ergebnis der Abfrage im Hintergrund: Eingang fürs Tray, Fläche für den Mittelklick-Rand.
    void OnReading(MonitorReading reading)
    {
        ShowMonitor(reading.Input);
        // Antwortet nur DDC nicht (Standby), bleibt der letzte Rand gültig. Sonst ginge der Klick nach dem Aufwachen
        // bis zur nächsten Abfrage an das Programm unter der Maus. UpdateEdge setzt dabei auch den Hook neu.
        UpdateEdge(reading.Bounds.HasValue ? reading.Bounds : edgeMonitor);
        // Nach einer Anzeigeänderung antwortet DDC oft erst nach ein paar Sekunden: dann bald noch einmal fragen.
        if (!edgeMonitor.HasValue && edgeRetries > 0)
        {
            edgeRetries--;
            edgeRetry.Start();
        }
    }

    // Anzeige umgestellt (Auflösung, Anordnung, Monitor ab- oder angesteckt, Aufwachen): Rand erst wieder nach
    // neuer Abfrage. Bis dahin laufen Mittelklicks durch.
    void OnDisplaySettingsChanged(object sender, EventArgs e)
    {
        UpdateEdge(null);
        edgeRetries = EdgeRetryCount;
        watch.Poll();
    }

    void UpdateEdge(Rectangle? monitor)
    {
        Side? side = profile.OtherSide;
        edgeClick.SetEdge(monitor, side); // setzt dabei auch den Hook neu
        if (monitor == edgeMonitor) return;
        edgeMonitor = monitor;
        Log.Info(EdgeClick.Describe(monitor, side, SystemInformation.VirtualScreen));
    }

    // Vom eigenen Umschalten oder von der Abfrage im Hintergrund. null = Monitor nicht erreichbar.
    void ShowMonitor(uint? input)
    {
        bool changed = !monitorKnown || monitorInput != input;
        monitorKnown = true;
        monitorInput = input;
        monitorReadAt = DateTime.Now;
        string text = Tray.MonitorText(input, true, profile);
        tray.SetMonitor(Tray.StateFor(input), text);
        if (changed) Log.Info(text);
    }

    void ShowInfo()
    {
        if (infoOpen) return; // das Menü bleibt bedienbar, während die Meldung offen ist
        infoOpen = true;
        try
        {
            Dialog.Inform(Tray.InfoText(new TrayInfo
            {
                Profile = profile,
                Monitor = Tray.MonitorText(monitorInput, monitorKnown, profile),
                MonitorReadAt = monitorReadAt,
                LastSwitch = lastSwitch,
                LastSwitchAt = lastSwitchAt,
                LastCheck = lastCheck,
                LastCheckAt = lastCheckAt,
                Autostart = Installer.GetAutostart(),
                ExePath = Application.ExecutablePath,
                LogPath = Log.FilePath,
            }));
        }
        finally { infoOpen = false; }
    }

    // Im Standard-Editor.
    void OpenLog()
    {
        // Start liefert null, wenn ein laufender Editor die Datei übernimmt (z. B. Notepad unter Windows 11).
        try { using (Process.Start(Log.FilePath)) { } }
        catch (Exception ex)
        {
            Log.Error("Protokoll ließ sich nicht öffnen", ex);
            Dialog.Warn("Das Protokoll ließ sich nicht öffnen:\n" + Log.FilePath);
        }
    }

    void OnTick()
    {
        if (quitting) return;
        // Nur wenn hier jemand arbeitet: Wer vom anderen Laptop zurückschaltet, bringt die Geräte mit.
        // So läuft über Nacht kein DDC-Verkehr, und während des Umschaltens fragt niemand dazwischen.
        if (!busy && UserIsPresent()) watch.Poll();
        if (state != UpdateState.Idle) return;
        if (pending != null)
        {
            if (UserIsPresent()) Offer();
        }
        else if (DateTime.UtcNow >= nextCheck) CheckForUpdate(false);
    }

    void CheckForUpdate(bool manual)
    {
        if (quitting) return;
        if (state == UpdateState.Checking)
        {
            manualCheckRequested |= manual;
            return;
        }
        if (state != UpdateState.Idle) return; // Frage oder Update sind schon sichtbar
        state = UpdateState.Checking;
        manualCheckRequested = manual;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            AppVersion latest = null;
            Exception error = null;
            try { latest = Updater.Latest(); }
            catch (Exception ex) { error = ex; }
            ui.Post(__ => OnChecked(latest, error), null);
        });
    }

    void OnChecked(AppVersion latest, Exception error)
    {
        bool manual = manualCheckRequested;
        state = UpdateState.Idle;
        manualCheckRequested = false;
        lastCheckAt = DateTime.Now;
        if (error != null)
        {
            Log.Error("Update-Prüfung fehlgeschlagen", error);
            lastCheck = "fehlgeschlagen";
            nextCheck = DateTime.UtcNow + RetryInterval;
            if (manual) Overlay.Show("Update-Prüfung fehlgeschlagen\nDetails in tandem.log");
            return;
        }
        nextCheck = DateTime.UtcNow + CheckInterval;
        Log.Info("Update-Prüfung: " + (latest == null ? "noch kein Release" : "neueste Version " + latest)
            + ", installiert " + AppVersion.Current);
        if (!Updater.ShouldOffer(latest, AppVersion.Current, failed, manual))
        {
            lastCheck = latest == null ? "noch kein Release" : "aktuell";
            if (manual) Overlay.Show("Tandem " + AppVersion.Current + " ist aktuell");
            return;
        }
        lastCheck = "Version " + latest + " verfügbar";
        if (quitting) return; // keine Frage mehr, die der Beenden-Hinweis gleich wieder schließt
        pending = latest;
        if (manual) Offer();
    }

    void Offer()
    {
        AppVersion version = pending;
        pending = null;
        state = UpdateState.Asking;
        bool yes;
        try
        {
            yes = Dialog.Ask("Tandem " + version + " ist verfügbar (installiert: " + AppVersion.Current + ")."
                + "\n\nJetzt aktualisieren? Das dauert nur ein paar Sekunden.");
        }
        finally { state = UpdateState.Idle; }
        if (yes) InstallUpdate(version);
        else Log.Info("Update auf " + version + " abgelehnt");
    }

    void InstallUpdate(AppVersion version)
    {
        state = UpdateState.Updating;
        string exe = null;
        bool offline = false;
        try
        {
            Overlay.Show("Update auf " + version + " wird geladen …", () =>
            {
                try
                {
                    exe = Updater.Prepare(version);
                    return "Update auf " + version + " wird installiert …";
                }
                catch (WebException ex)
                {
                    offline = true;
                    Log.Error("Update auf " + version + " ließ sich nicht laden", ex);
                    return "Update ließ sich nicht laden\nNeuer Versuch in einer Stunde";
                }
                catch (Exception ex)
                {
                    Log.Error("Update auf " + version + " fehlgeschlagen", ex);
                    return "Update fehlgeschlagen\nDetails in tandem.log";
                }
            }, () => OnPrepared(version, exe, offline));
        }
        catch (Exception ex)
        {
            state = UpdateState.Idle;
            Log.Error("Hinweisfenster ließ sich nicht öffnen", ex);
        }
    }

    void OnPrepared(AppVersion version, string exe, bool offline)
    {
        // Nach „Beenden“ keine neue Version mehr starten. Den gebauten Stand räumt Updater.CleanUp beim nächsten Start weg.
        if (quitting)
        {
            state = UpdateState.Idle;
            return;
        }
        if (exe == null)
        {
            // Ein Netzfehler ist bald vorbei, ein Build-Fehler nicht: dann diese Version nicht wieder automatisch anbieten.
            if (offline) nextCheck = DateTime.UtcNow + RetryInterval;
            else failed = version;
            state = UpdateState.Idle;
            return;
        }
        preparedExe = exe;
        preparedVersion = version;
        if (!busy) HandOver();
    }

    // Gibt Hotkey und Mutex frei, startet die neue Version und wartet, bis sie beides hat.
    // Kommt sie nicht rechtzeitig, macht diese Version weiter. So bleibt der Hotkey nie ohne Besitzer.
    void HandOver()
    {
        string exe = preparedExe;
        AppVersion version = preparedVersion;
        preparedExe = null;
        Log.Info("Übergabe an " + version);
        tray.Hide(); // die neue Version zeigt ihr eigenes Symbol
        hotkey.Unregister();
        edgeClick.Stop();
        ReleaseMutex();
        readyEvent.Reset();
        try { Installer.StartHandOver(exe, profile); }
        catch (Exception ex)
        {
            Log.Error("Neue Version ließ sich nicht starten", ex);
            TakeBack(version);
            return;
        }
        waits.Add(ThreadPool.RegisterWaitForSingleObject(readyEvent, (_, timedOut) => ui.Post(__ =>
        {
            if (timedOut) TakeBack(version);
            else ExitThread();
        }, null), null, HandOverTimeoutMs, true));
    }

    void TakeBack(AppVersion version)
    {
        Log.Info("Übergabe an " + version + " gescheitert, " + AppVersion.Current + " läuft weiter");
        try
        {
            Installer.StopOtherInstances(StopGraceMs);
            Installer.RestoreBackup();
        }
        catch (Exception ex) { Log.Error("Aufräumen nach gescheiterter Übergabe fehlgeschlagen", ex); }
        ownsMutex = Acquire(mutex, MutexTimeoutMs);
        bool registered = hotkey.Register();
        edgeClick.Start();
        tray.Show();
        failed = version;
        state = UpdateState.Idle;
        Dialog.Warn("Update auf " + version + " fehlgeschlagen, Tandem " + AppVersion.Current + " läuft weiter."
            + (registered ? "" : "\n" + Hotkey.Name + " ist gerade belegt, bitte ab- und wieder anmelden.")
            + "\n\nDetails: " + Log.FilePath);
    }

    void ReleaseMutex()
    {
        if (!ownsMutex) return;
        mutex.ReleaseMutex();
        ownsMutex = false;
    }

    // Ruft action auf dem UI-Thread auf, sobald ein anderer Prozess das Event setzt.
    EventWaitHandle ListenFor(string name, Action action)
    {
        var handle = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        waits.Add(ThreadPool.RegisterWaitForSingleObject(handle, (_, timedOut) => ui.Post(__ => action(), null), null, Timeout.Infinite, false));
        return handle;
    }

    static bool Signal(string name)
    {
        EventWaitHandle handle;
        if (!EventWaitHandle.TryOpenExisting(name, out handle)) return false;
        using (handle) handle.Set();
        return true;
    }

    // Ein abgestürzter Vorgänger hinterlässt einen verwaisten Mutex, der gilt als frei.
    static bool Acquire(Mutex mutex, int timeoutMs)
    {
        try { return mutex.WaitOne(timeoutMs); }
        catch (AbandonedMutexException) { return true; }
    }

    // Arbeitet gerade jemand an diesem Laptop? Sonst zeigt der Monitor vermutlich den anderen und niemand sieht die Frage.
    static bool UserIsPresent()
    {
        var info = new LASTINPUTINFO();
        info.cbSize = (uint)Marshal.SizeOf(info);
        if (!GetLastInputInfo(ref info)) return true;
        return unchecked((uint)Environment.TickCount - info.dwTime) < PresenceWindow.TotalMilliseconds;
    }
}
