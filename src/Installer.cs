// Installiert Tandem für den aktuellen Benutzer, ohne Adminrechte: exe nach %LOCALAPPDATA%\Programs\Tandem,
// Autostart (Run-Schlüssel) und Startmenü-Eintrag. Ein Update nutzt dieselben Schritte. Meldungen zeigt der Aufrufer.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

public static class Installer
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Tandem");
    static readonly string ExePath = Path.Combine(Folder, "Tandem.exe");
    static readonly string BackupPath = ExePath + ".bak";
    static readonly string StartMenuLink = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Tandem umschalten.lnk");

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Hier merkt sich Windows, ob ein Autostart im Task-Manager deaktiviert wurde.
    const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string RunValue = "Tandem";
    const string ProcessName = "Tandem";
    const int KillTimeoutMs = 5000, FileAttempts = 5, FileRetryDelayMs = 500;

    // Installiert diese exe für die Rolle und startet den Hintergrundprozess. Eine laufende Version muss beendet sein
    // oder (bei der Übergabe) Hotkey und Mutex schon freigegeben haben. Liefert Hinweise für den User.
    public static List<string> Install(Profile profile)
    {
        var notes = new List<string>();
        CopySelf();
        SetAutostart(profile);
        Start(ExePath, "listen " + profile.ToArguments());
        // Der Startmenü-Eintrag ist entbehrlich, ein Fehler dort soll die Installation nicht aufhalten.
        try { CreateStartMenuLink(profile); }
        catch (Exception ex)
        {
            Log.Error("Startmenü-Eintrag ließ sich nicht anlegen", ex);
            notes.Add("Der Startmenü-Eintrag ließ sich nicht anlegen.");
        }
        if (GetAutostart() == Autostart.Disabled)
            notes.Add("Der Autostart von Tandem ist im Task-Manager deaktiviert. Dort unter „Autostart-Apps“ wieder aktivieren.");
        Log.Info("Tandem " + AppVersion.Current + " installiert: " + profile);
        return notes;
    }

    // Entfernt Autostart, Startmenü-Eintrag und den Installationsordner samt Protokoll. Liefert false, wenn diese exe
    // selbst im Ordner liegt: Den löscht dann RemoveFolderAfterExit.
    public static bool Uninstall()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            if (key != null) key.DeleteValue(RunValue, false);
        File.Delete(StartMenuLink);
        if (!Directory.Exists(Folder)) return true;
        if (IsInside(Assembly.GetExecutingAssembly().Location, Folder)) return false;
        Directory.Delete(Folder, true);
        return true;
    }

    // Die laufende exe kann ihren eigenen Ordner nicht löschen. Das erledigt cmd, kurz nachdem sie sich beendet hat.
    public static void RemoveFolderAfterExit()
    {
        var start = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q " + Quote(Folder));
        start.WorkingDirectory = Path.GetTempPath(); // sonst hielte cmd den Ordner selbst fest
        start.CreateNoWindow = true;
        start.UseShellExecute = false;
        Process.Start(start).Dispose();
    }

    // Startet die neu gebaute exe als Installer. Sie ersetzt die installierte exe und startet sie.
    public static void StartHandOver(string newExe, Profile profile)
    {
        Start(newExe, "install " + profile.ToArguments() + " --handover");
    }

    // Beendet andere Tandem-Prozesse aus dem Installationsordner (auch update\), nach graceMs notfalls hart.
    // Ein laufendes Umschalten aus dem Startmenü bekommt so Zeit, fertig zu werden. Der eigene Prozess bleibt.
    public static void StopOtherInstances(int graceMs)
    {
        int self = Process.GetCurrentProcess().Id;
        foreach (Process p in Process.GetProcessesByName(ProcessName))
        {
            using (p)
            {
                try
                {
                    if (p.Id == self || !IsInside(p.MainModule.FileName, Folder)) continue;
                    if (p.WaitForExit(graceMs)) continue;
                    p.Kill();
                    p.WaitForExit(KillTimeoutMs);
                }
                catch (InvalidOperationException) { } // schon beendet
                catch (Win32Exception ex) { Log.Error("Prozess " + p.Id + " übersprungen", ex); }
            }
        }
    }

    // Nach einer gescheiterten Übergabe: die vorige exe zurückholen, falls die neue sie schon ersetzt hat.
    public static void RestoreBackup()
    {
        string current = AppVersion.Current.ToString();
        if (VersionOf(BackupPath) != current || VersionOf(ExePath) == current) return;
        Retry(() => File.Delete(ExePath));
        Retry(() => File.Move(BackupPath, ExePath));
        Log.Info("Sicherung " + current + " wiederhergestellt");
    }

    // Kopiert diese exe in den Installationsordner, die bisherige wird zur Sicherung (.bak).
    // Umbenennen geht auch, während die bisherige noch läuft.
    static void CopySelf()
    {
        string self = Assembly.GetExecutingAssembly().Location;
        if (SamePath(self, ExePath)) return;
        Directory.CreateDirectory(Folder);
        string next = ExePath + ".new";
        Retry(() => File.Copy(self, next, true));
        if (File.Exists(ExePath))
        {
            Retry(() => File.Delete(BackupPath));
            Retry(() => File.Move(ExePath, BackupPath));
        }
        try { Retry(() => File.Move(next, ExePath)); }
        catch
        {
            // Ohne Tandem.exe zeigte der Autostart ins Leere.
            if (!File.Exists(ExePath) && File.Exists(BackupPath)) File.Move(BackupPath, ExePath);
            throw;
        }
    }

    static void SetAutostart(Profile profile)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            key.SetValue(RunValue, Quote(ExePath) + " listen " + profile.ToArguments());
    }

    public enum Autostart { Active, Disabled, Missing }

    // Missing ohne Run-Eintrag. Den Schalter im Task-Manager merkt sich Windows getrennt davon:
    // 02 (an) oder 03 (aus) im ersten Byte.
    public static Autostart GetAutostart()
    {
        using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey))
            if (run == null || run.GetValue(RunValue) == null) return Autostart.Missing;
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey))
        {
            byte[] state = key == null ? null : key.GetValue(RunValue) as byte[];
            return state != null && state.Length > 0 && (state[0] & 1) == 1 ? Autostart.Disabled : Autostart.Active;
        }
    }

    static void CreateStartMenuLink(Profile profile)
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        try
        {
            dynamic link = ((dynamic)shell).CreateShortcut(StartMenuLink);
            link.TargetPath = ExePath;
            link.Arguments = "toggle " + profile.ToArguments();
            link.WorkingDirectory = Folder;
            link.Description = "Monitor, Maus und Tastatur zum anderen Laptop umschalten";
            link.Save();
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    // Arbeitsverzeichnis immer der Installationsordner, sonst bliebe z. B. der update-Ordner gesperrt.
    static void Start(string exe, string arguments)
    {
        var start = new ProcessStartInfo(exe, arguments);
        start.WorkingDirectory = Folder;
        start.UseShellExecute = false;
        Process.Start(start).Dispose();
    }

    // Virenscanner sperren frisch kopierte Dateien manchmal kurz.
    static void Retry(Action action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) { if (attempt == FileAttempts) throw; }
            catch (UnauthorizedAccessException) { if (attempt == FileAttempts) throw; }
            Thread.Sleep(FileRetryDelayMs);
        }
    }

    static string VersionOf(string exe)
    {
        return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion : null;
    }

    static bool SamePath(string a, string b)
    {
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }

    static bool IsInside(string file, string folder)
    {
        return Path.GetFullPath(file).StartsWith(Path.GetFullPath(folder) + "\\", StringComparison.OrdinalIgnoreCase);
    }

    static string Quote(string path)
    {
        return "\"" + path + "\"";
    }
}
