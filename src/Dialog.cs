// Rückfragen und Meldungen als MessageBox im Vordergrund, auch ohne eigenes Fenster (Hintergrundprozess).
// Fragen und Hinweise tragen das Tandem-Icon, damit man sofort sieht, woher sie kommen. Warnungen behalten
// das Warnsymbol von Windows, damit Fehler als Fehler auffallen.
using System;
using System.Runtime.InteropServices;

public static class Dialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MSGBOXPARAMS
    {
        public int cbSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpszText;
        public string lpszCaption;
        public uint dwStyle;
        public IntPtr lpszIcon;
        public UIntPtr dwContextHelpId;
        public IntPtr lpfnMsgBoxCallback;
        public uint dwLanguageId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxIndirectW")]
    static extern int MessageBoxIndirect(ref MSGBOXPARAMS parameters);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string moduleName);

    const uint MB_OK = 0x0, MB_YESNO = 0x4, MB_ICONWARNING = 0x30, MB_USERICON = 0x80;
    const uint MB_DEFBUTTON2 = 0x100, MB_SETFOREGROUND = 0x10000, MB_TOPMOST = 0x40000;
    const int IDYES = 6;
    const string Caption = "Tandem";

    // Ja/Nein mit "Nein" als Standard, damit ein Enter beim Tippen nichts auslöst.
    public static bool Ask(string text)
    {
        return Show(text, MB_YESNO | MB_DEFBUTTON2 | MB_USERICON) == IDYES;
    }

    public static void Inform(string text)
    {
        Show(text, MB_OK | MB_USERICON);
    }

    public static void Warn(string text)
    {
        Show(text, MB_OK | MB_ICONWARNING);
    }

    static int Show(string text, uint style)
    {
        var parameters = new MSGBOXPARAMS();
        parameters.cbSize = Marshal.SizeOf(parameters);
        parameters.hInstance = GetModuleHandle(null); // die exe selbst, dort liegt das Icon
        parameters.lpszText = text;
        parameters.lpszCaption = Caption;
        parameters.dwStyle = style | MB_SETFOREGROUND | MB_TOPMOST;
        parameters.lpszIcon = new IntPtr(AppIcon.Win32ResourceId);
        return MessageBoxIndirect(ref parameters);
    }
}
