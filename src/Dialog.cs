// Rückfragen und Meldungen als MessageBox im Vordergrund, auch ohne eigenes Fenster (Hintergrundprozess).
using System;
using System.Runtime.InteropServices;

public static class Dialog
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    static extern int ShowMessageBox(IntPtr owner, string text, string caption, uint type);

    const uint MB_OK = 0x0, MB_YESNO = 0x4, MB_ICONQUESTION = 0x20, MB_ICONWARNING = 0x30, MB_ICONINFORMATION = 0x40;
    const uint MB_DEFBUTTON2 = 0x100, MB_SETFOREGROUND = 0x10000, MB_TOPMOST = 0x40000;
    const int IDYES = 6;
    const string Caption = "Tandem";

    // Ja/Nein mit "Nein" als Standard, damit ein Enter beim Tippen nichts auslöst.
    public static bool Ask(string text)
    {
        return ShowMessageBox(IntPtr.Zero, text, Caption, MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2 | MB_SETFOREGROUND | MB_TOPMOST) == IDYES;
    }

    public static void Inform(string text)
    {
        ShowMessageBox(IntPtr.Zero, text, Caption, MB_OK | MB_ICONINFORMATION | MB_SETFOREGROUND | MB_TOPMOST);
    }

    public static void Warn(string text)
    {
        ShowMessageBox(IntPtr.Zero, text, Caption, MB_OK | MB_ICONWARNING | MB_SETFOREGROUND | MB_TOPMOST);
    }
}
