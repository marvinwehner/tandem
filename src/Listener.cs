// Hintergrundprozess (Autostart): reagiert sofort auf Strg+Alt+K und prüft regelmäßig auf Updates.
// Läuft nur einmal (Mutex). Installer und "Tandem.exe update" sprechen ihn über benannte Events an.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public sealed class Listener : ApplicationContext
{
    const string MutexName = @"Local\Tandem";
    const string StopEventName = @"Local\Tandem.Beenden";
    const string CheckEventName = @"Local\Tandem.Pruefen";
    const string ReadyEventName = @"Local\Tandem.Bereit"; // setzt eine neue Version, sobald sie Mutex und Hotkey hat

    const int HotkeyId = 1, WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000, VK_K = 0x4B;
    const int HotkeyAttempts = 30, HotkeyRetryDelayMs = 500;
    const int MutexTimeoutMs = 10000, HandOverTimeoutMs = 30000, TickMs = 30000;
    static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);
    static readonly TimeSpan PresenceWindow = TimeSpan.FromMinutes(2);

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    readonly Profile profile;
    readonly Mutex mutex;
    readonly HotkeyWindow window;
    readonly SynchronizationContext ui;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly EventWaitHandle stopEvent, checkEvent, readyEvent;
    readonly List<RegisteredWaitHandle> waits = new List<RegisteredWaitHandle>();

    bool ownsMutex = true;
    bool busy;              // Umschalten läuft, weiterer Hotkey würde sofort zurückschalten
    bool updating;          // Update wird geladen oder übergeben
    bool checking;          // Update-Prüfung läuft im Hintergrund
    bool asking;            // Update-Frage ist offen
    DateTime nextCheck = DateTime.UtcNow + FirstCheckDelay;
    AppVersion pending;     // gefundenes Update, wartet darauf, dass jemand an diesem Laptop arbeitet
    AppVersion failed;      // gescheitertes Update, bis zum Neustart nicht automatisch erneut anbieten
    string preparedExe;     // fertig gebautes Update, wartet auf das Ende des Umschaltens
    AppVersion preparedVersion;

    // Läuft bis zum Beenden. 0 = normal beendet, 1 = konnte nicht starten.
    public static int Run(Profile profile)
    {
        using (var mutex = new Mutex(false, MutexName))
        {
            if (!Acquire(mutex, MutexTimeoutMs))
            {
                Overlay.Run("Tandem läuft schon", null);
                return 1;
            }
            using (var listener = new Listener(profile, mutex))
            {
                if (!listener.RegisterHotkey())
                {
                    Log.Info("Strg+Alt+K ist belegt, Tandem beendet sich");
                    Overlay.Run("Strg+Alt+K ist von einem anderen Programm belegt", null);
                    return 1;
                }
                Log.Info("Tandem " + AppVersion.Current + " läuft: " + profile);
                listener.readyEvent.Set(); // eine alte Version wartet eventuell auf die Übergabe
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

    Listener(Profile profile, Mutex mutex)
    {
        this.profile = profile;
        this.mutex = mutex;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        ui = SynchronizationContext.Current;
        window = new HotkeyWindow(OnHotkey);
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
        UnregisterHotKey(window.Handle, HotkeyId);
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
            window.DestroyHandle();
            ReleaseMutex();
        }
        base.Dispose(disposing);
    }

    void OnHotkey()
    {
        if (busy || updating) return;
        busy = true;
        Overlay.Show("Umschalten …", () =>
        {
            string text = Switcher.Toggle(profile).Describe();
            Log.Info("Umgeschaltet: " + text.Replace("\n", ", "));
            return text;
        }, () =>
        {
            busy = false;
            if (preparedExe != null) HandOver();
        });
    }

    void OnTick()
    {
        if (busy || updating || checking || asking) return;
        if (pending != null)
        {
            if (UserIsPresent()) Offer();
            return;
        }
        // Entwicklungsstände nur auf Anfrage aktualisieren, sonst überschreibt ein Klick einen Teststand.
        if (AppVersion.Current.IsRelease && DateTime.UtcNow >= nextCheck) CheckForUpdate(false);
    }

    void CheckForUpdate(bool manual)
    {
        if (updating || checking || asking) return;
        checking = true;
        ThreadPool.QueueUserWorkItem(state =>
        {
            AppVersion latest = null;
            Exception error = null;
            try { latest = Updater.Latest(); }
            catch (Exception ex) { error = ex; }
            ui.Post(s => OnChecked(latest, error, manual), null);
        });
    }

    void OnChecked(AppVersion latest, Exception error, bool manual)
    {
        checking = false;
        if (error != null)
        {
            Log.Error("Update-Prüfung fehlgeschlagen", error);
            nextCheck = DateTime.UtcNow + RetryInterval;
            if (manual) Overlay.Show("Update-Prüfung fehlgeschlagen\nDetails in tandem.log", null, null);
            return;
        }
        nextCheck = DateTime.UtcNow + CheckInterval;
        Log.Info("Update-Prüfung: " + (latest == null ? "noch kein Release" : "neueste Version " + latest)
            + ", installiert " + AppVersion.Current);
        // Ein gescheitertes Update nur auf ausdrückliche Anfrage noch einmal versuchen.
        if (latest == null || !latest.IsNewerThan(AppVersion.Current) || (!manual && latest.SameAs(failed)))
        {
            if (manual) Overlay.Show("Tandem " + AppVersion.Current + " ist aktuell", null, null);
            return;
        }
        pending = latest;
        if (manual) Offer();
    }

    void Offer()
    {
        AppVersion version = pending;
        pending = null;
        asking = true;
        bool yes = Dialog.Ask("Tandem " + version + " ist verfügbar (installiert: " + AppVersion.Current + ")."
            + "\n\nJetzt aktualisieren? Das dauert nur ein paar Sekunden.");
        asking = false;
        if (yes) InstallUpdate(version);
        else Log.Info("Update auf " + version + " abgelehnt");
    }

    void InstallUpdate(AppVersion version)
    {
        updating = true;
        string exe = null;
        Overlay.Show("Update auf " + version + " wird geladen …", () =>
        {
            try
            {
                exe = Updater.Prepare(version);
                return "Update auf " + version + " wird installiert …";
            }
            catch (Exception ex)
            {
                Log.Error("Update auf " + version + " fehlgeschlagen", ex);
                return "Update fehlgeschlagen\nDetails in tandem.log";
            }
        }, () =>
        {
            if (exe == null)
            {
                failed = version;
                updating = false;
                return;
            }
            preparedExe = exe;
            preparedVersion = version;
            // Läuft noch ein Umschalten, übergibt dessen Ende. Ein Abbruch mittendrin ließe Monitor und Geräte auseinanderlaufen.
            if (!busy) HandOver();
        });
    }

    // Gibt Hotkey und Mutex frei, startet die neue Version und wartet, bis sie beides hat.
    // Kommt sie nicht rechtzeitig, macht diese Version weiter. So bleibt der Hotkey nie ohne Besitzer.
    void HandOver()
    {
        string exe = preparedExe;
        AppVersion version = preparedVersion;
        preparedExe = null;
        Log.Info("Übergabe an " + version);
        UnregisterHotKey(window.Handle, HotkeyId);
        ReleaseMutex();
        readyEvent.Reset();
        try { Installer.StartUpdate(exe, profile); }
        catch (Exception ex)
        {
            Log.Error("Neue Version ließ sich nicht starten", ex);
            TakeBack(version);
            return;
        }
        waits.Add(ThreadPool.RegisterWaitForSingleObject(readyEvent, (state, timedOut) => ui.Post(s =>
        {
            if (timedOut) TakeBack(version);
            else ExitThread();
        }, null), null, HandOverTimeoutMs, true));
    }

    void TakeBack(AppVersion version)
    {
        Log.Info("Übergabe an " + version + " gescheitert, " + AppVersion.Current + " läuft weiter");
        Installer.StopOtherInstances(0);
        Installer.RestoreBackup();
        ownsMutex = Acquire(mutex, MutexTimeoutMs);
        bool hotkey = RegisterHotkey();
        failed = version;
        updating = false;
        Dialog.Warn("Update auf " + version + " fehlgeschlagen, Tandem " + AppVersion.Current + " läuft weiter."
            + (hotkey ? "" : "\nStrg+Alt+K ist gerade belegt, bitte ab- und wieder anmelden.")
            + "\n\nDetails: " + Log.FilePath);
    }

    // Eine vorige Instanz oder der Explorer gibt den Hotkey eventuell erst verzögert frei.
    bool RegisterHotkey()
    {
        for (int i = 0; i < HotkeyAttempts; i++)
        {
            if (RegisterHotKey(window.Handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_K)) return true;
            Thread.Sleep(HotkeyRetryDelayMs);
        }
        return false;
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
        waits.Add(ThreadPool.RegisterWaitForSingleObject(handle, (state, timedOut) => ui.Post(s => action(), null), null, Timeout.Infinite, false));
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

    // Unsichtbares Nachrichtenfenster, das WM_HOTKEY empfängt.
    sealed class HotkeyWindow : NativeWindow
    {
        readonly Action onHotkey;

        public HotkeyWindow(Action onHotkey)
        {
            this.onHotkey = onHotkey;
            var cp = new CreateParams();
            cp.Parent = new IntPtr(-3); // HWND_MESSAGE
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) onHotkey();
            else base.WndProc(ref m);
        }
    }
}
