// Der globale Hotkey Strg+Alt+K über ein unsichtbares Nachrichtenfenster. Strg+Alt ist auf deutschen Tastaturen
// AltGr, deshalb eine Taste ohne AltGr-Zeichen. Strg+Alt+M ist auf dem Privatlaptop schon belegt.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public sealed class Hotkey : IDisposable
{
    public const string Name = "Strg+Alt+K";

    const int Id = 1, WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    const int VK_RETURN = 0x0D, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_K = 0x4B;
    // Eine vorige Instanz oder der Explorer gibt den Hotkey eventuell erst verzögert frei.
    const int RegisterAttempts = 30, RegisterRetryDelayMs = 500;
    const int ReleaseTimeoutMs = 3000, ReleasePollMs = 20;

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    readonly MessageWindow window;

    public Hotkey(Action pressed)
    {
        window = new MessageWindow(pressed);
    }

    public bool Register()
    {
        for (int i = 0; i < RegisterAttempts; i++)
        {
            if (RegisterHotKey(window.Handle, Id, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_K)) return true;
            Thread.Sleep(RegisterRetryDelayMs);
        }
        return false;
    }

    public void Unregister()
    {
        UnregisterHotKey(window.Handle, Id);
    }

    public void Dispose()
    {
        Unregister();
        window.DestroyHandle();
    }

    // Vor dem Gerätewechsel aufrufen: Sonst bleiben Tasten auf diesem Laptop eventuell "gedrückt", weil die Tastatur
    // schon beim anderen ist, bevor sie das Loslassen meldet. Strg+Alt+K vom Hotkey, Enter vom Startmenü-Eintrag.
    public static void WaitForRelease()
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(ReleaseTimeoutMs);
        while (DateTime.UtcNow < deadline && (IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_K) || IsDown(VK_RETURN)))
            Thread.Sleep(ReleasePollMs);
    }

    static bool IsDown(int vk)
    {
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    sealed class MessageWindow : NativeWindow
    {
        static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
        readonly Action pressed;

        public MessageWindow(Action pressed)
        {
            this.pressed = pressed;
            var cp = new CreateParams();
            cp.Parent = HWND_MESSAGE;
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) pressed();
            else base.WndProc(ref m);
        }
    }
}
