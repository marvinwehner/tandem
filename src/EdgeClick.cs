// Mittelklick am Rand des Monitors zur Seite des anderen Laptops schaltet um. Ein globaler Maus-Hook erkennt den
// Klick und schluckt ihn, damit das Programm unter der Maus nichts davon merkt (z. B. kein Autoscroll im Browser).
// Der Hook läuft auf einem eigenen Thread: Windows wartet bei jeder Mausbewegung auf ihn. Hinge er am UI-Thread,
// würde eine offene Meldung oder die Übergabe an eine neue Version die Maus im ganzen System ausbremsen.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

public sealed class EdgeClick : IDisposable
{
    // Breite des Rands in Pixeln. Liegt er außen am Desktop, bleibt die Maus dort ohnehin hängen.
    public const int EdgeWidth = 2;

    // Entscheidet je Mittelklick-Nachricht, ob sie geschluckt wird und ob umgeschaltet werden soll.
    // Entschieden wird beim Drücken: Wer das Drücken schluckt, muss auch das Loslassen schlucken, sonst bliebe
    // die mittlere Taste im ganzen System „gedrückt“. Umgeschaltet wird beim Loslassen, wenn es auch am Rand ist.
    public sealed class Filter
    {
        bool swallowing;

        public bool Swallow(bool down, bool onEdge, out bool switchNow)
        {
            switchNow = false;
            if (down)
            {
                swallowing = onEdge;
                return swallowing;
            }
            if (!swallowing) return false;
            swallowing = false;
            switchNow = onEdge;
            return true;
        }
    }

    // Rand als unveränderliches Objekt: Der UI-Thread tauscht es aus, der Hook-Thread liest es nur.
    sealed class Edge
    {
        public Edge(Rectangle monitor, Side side)
        {
            Monitor = monitor;
            Side = side;
        }

        public readonly Rectangle Monitor;
        public readonly Side Side;
    }

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    const int WH_MOUSE_LL = 14;
    const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
    const uint WM_QUIT = 0x0012, PM_NOREMOVE = 0x0000;
    const uint WM_REHOOK = 0x8001; // WM_APP + 1: Hook neu setzen
    const int StartTimeoutMs = 2000, StopTimeoutMs = 2000;

    readonly SynchronizationContext ui;
    readonly Action clicked; // auf dem UI-Thread
    volatile Edge edge;      // null = aus, Klicks laufen durch
    Worker worker;           // nur vom UI-Thread benutzt

    public EdgeClick(SynchronizationContext ui, Action clicked)
    {
        this.ui = ui;
        this.clicked = clicked;
    }

    // Startet den Hook-Thread und wartet, bis der Hook steht. false, wenn das nicht klappt.
    public bool Start()
    {
        if (worker != null) return true;
        worker = new Worker(this);
        return worker.Start();
    }

    public void Stop()
    {
        if (worker == null) return;
        worker.Stop();
        worker = null;
    }

    public void Dispose()
    {
        Stop();
    }

    // Neuer Rand, oder null zum Abschalten. Setzt den Hook dabei neu: Windows entfernt ihn still, wenn er einmal
    // zu lange braucht (z. B. nach dem Ruhezustand), und das ließe sich sonst nicht bemerken.
    public void SetEdge(Rectangle? monitor, Side? side)
    {
        edge = monitor.HasValue && side.HasValue ? new Edge(monitor.Value, side.Value) : null;
        if (worker != null) worker.Rehook();
    }

    // Liegt der Punkt in den äußersten EdgeWidth Pixeln dieser Seite des Monitors? Rechts und unten sind exklusiv.
    public static bool IsOnEdge(Point p, Rectangle monitor, Side side)
    {
        if (p.Y < monitor.Top || p.Y >= monitor.Bottom) return false;
        return side == Side.Left
            ? p.X >= monitor.Left && p.X < monitor.Left + EdgeWidth
            : p.X >= monitor.Right - EdgeWidth && p.X < monitor.Right;
    }

    // Für das Protokoll: wo der Rand liegt und ob er außen am Desktop ist (dann bleibt die Maus dort hängen).
    public static string Describe(Rectangle? monitor, Side? side, Rectangle desktop)
    {
        if (!monitor.HasValue || !side.HasValue) return "Mittelklick am Rand aus";
        Rectangle m = monitor.Value;
        bool left = side.Value == Side.Left;
        int x = left ? m.Left : m.Right - 1;
        bool outer = left ? m.Left == desktop.Left : m.Right == desktop.Right;
        return "Mittelklick am " + (left ? "linken" : "rechten") + " Rand, x = " + x
            + (outer ? ", außen am Desktop" : ", innen: Anordnung in den Anzeigeeinstellungen prüfen");
    }

    // Ein Hook-Thread mit allem, was nur ihm gehört. Hängt ein alter Thread beim Beenden, stört er so einen neuen nicht.
    sealed class Worker
    {
        readonly EdgeClick owner;
        readonly HookProc proc;   // im Feld halten, sonst sammelt der GC den Delegate ein, während Windows ihn noch aufruft
        readonly Filter filter = new Filter();
        // Bewusst nie freigegeben: Käme der Thread erst nach dem Timeout dazu, darf Set nicht auf ein freigegebenes Event treffen.
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        readonly Thread thread;
        volatile uint threadId;
        volatile bool installed;

        public Worker(EdgeClick owner)
        {
            this.owner = owner;
            proc = OnMouse;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "Tandem-Maus-Hook";
        }

        public bool Start()
        {
            thread.Start();
            if (!ready.WaitOne(StartTimeoutMs)) Log.Info("Maus-Hook startet nicht rechtzeitig");
            return installed;
        }

        public void Rehook()
        {
            if (threadId != 0) PostThreadMessage(threadId, WM_REHOOK, IntPtr.Zero, IntPtr.Zero);
        }

        public void Stop()
        {
            ready.WaitOne(StartTimeoutMs); // erst dann gibt es eine Nachrichtenschlange für WM_QUIT
            if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            if (!thread.Join(StopTimeoutMs)) Log.Info("Maus-Hook-Thread beendet sich nicht rechtzeitig");
        }

        void Run()
        {
            threadId = GetCurrentThreadId();
            MSG msg;
            PeekMessage(out msg, IntPtr.Zero, 0, 0, PM_NOREMOVE); // legt die Nachrichtenschlange an, sonst geht PostThreadMessage ins Leere
            IntPtr hook = Install();
            installed = hook != IntPtr.Zero;
            ready.Set();
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message != WM_REHOOK) continue;
                // Erst den neuen setzen, dann den alten entfernen: Dazwischen wird auf diesem Thread nichts verarbeitet,
                // es gibt also weder eine Lücke noch doppelte Aufrufe.
                IntPtr fresh = Install();
                if (fresh == IntPtr.Zero) continue;
                if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
                hook = fresh;
            }
            if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        }

        IntPtr Install()
        {
            IntPtr hook = SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) Log.Info("Maus-Hook ließ sich nicht setzen (Fehler " + Marshal.GetLastWin32Error() + ")");
            return hook;
        }

        // So kurz wie möglich: Windows wartet bei jeder Mausbewegung darauf. Kein Protokoll, nur ui.Post.
        IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                int message = wParam.ToInt32();
                if (nCode >= 0 && (message == WM_MBUTTONDOWN || message == WM_MBUTTONUP))
                {
                    Edge current = owner.edge;
                    // MSLLHOOKSTRUCT beginnt mit dem Punkt in physischen Pixeln.
                    var point = new Point(Marshal.ReadInt32(lParam), Marshal.ReadInt32(lParam, 4));
                    bool onEdge = current != null && IsOnEdge(point, current.Monitor, current.Side);
                    bool switchNow;
                    if (filter.Swallow(message == WM_MBUTTONDOWN, onEdge, out switchNow))
                    {
                        if (switchNow) owner.ui.Post(_ => owner.clicked(), null);
                        return new IntPtr(1);
                    }
                }
            }
            catch (Exception)
            {
                // Eine Ausnahme im nativen Rückruf würde den Prozess beenden. Dann lieber den Klick durchlassen.
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam); // das Handle ignoriert Windows
        }
    }
}
