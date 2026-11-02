// Hintergrundprozess (Autostart): reagiert sofort auf Strg+Alt+K und prüft regelmäßig auf Updates.
// Läuft nur einmal (Mutex). Installer und "Tandem.exe update" sprechen ihn über benannte Events an.
using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

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
        hotkey = new Hotkey(OnHotkey);
        stopEvent = ListenFor(StopEventName, ExitThread);
        checkEvent = ListenFor(CheckEventName, () => CheckForUpdate(true));
        readyEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEventName);
        timer.Interval = TickMs;
        timer.Tick += (s, e) => OnTick();
        timer.Start();
    }

    protected override void ExitThreadCore()
    {
        timer.Stop();
        hotkey.Unregister();
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
            hotkey.Dispose();
            ReleaseMutex();
        }
        base.Dispose(disposing);
    }

    void OnHotkey()
    {
        if (busy) return;
        busy = true;
        try
        {
            Overlay.Show("Umschalten …", () =>
            {
                Hotkey.WaitForRelease();
                string text = Switcher.Toggle(profile).Describe();
                Log.Info("Umgeschaltet: " + text.Replace("\n", ", "));
                return text;
            }, OnSwitched);
        }
        catch (Exception ex)
        {
            busy = false;
            Log.Error("Hinweisfenster ließ sich nicht öffnen", ex);
        }
    }

    void OnSwitched()
    {
        busy = false;
        // Ein Update, das während des Umschaltens fertig wurde, übergibt erst jetzt.
        // Ein Abbruch mittendrin ließe Monitor und Geräte auseinanderlaufen.
        if (preparedExe != null) HandOver();
    }

    void OnTick()
    {
        if (state != UpdateState.Idle) return;
        if (pending != null)
        {
            if (UserIsPresent()) Offer();
        }
        else if (DateTime.UtcNow >= nextCheck) CheckForUpdate(false);
    }

    void CheckForUpdate(bool manual)
    {
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
        if (error != null)
        {
            Log.Error("Update-Prüfung fehlgeschlagen", error);
            nextCheck = DateTime.UtcNow + RetryInterval;
            if (manual) Overlay.Show("Update-Prüfung fehlgeschlagen\nDetails in tandem.log");
            return;
        }
        nextCheck = DateTime.UtcNow + CheckInterval;
        Log.Info("Update-Prüfung: " + (latest == null ? "noch kein Release" : "neueste Version " + latest)
            + ", installiert " + AppVersion.Current);
        if (!Updater.ShouldOffer(latest, AppVersion.Current, failed, manual))
        {
            if (manual) Overlay.Show("Tandem " + AppVersion.Current + " ist aktuell");
            return;
        }
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
        hotkey.Unregister();
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
