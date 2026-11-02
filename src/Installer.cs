// Installiert Tandem für den aktuellen Benutzer, ohne Adminrechte: exe nach %LOCALAPPDATA%\Programs\Tandem,
// Autostart (Run-Schlüssel) und Startmenü-Eintrag. Ein Update nutzt dieselben Schritte.
using System;
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
    public static readonly string ExePath = Path.Combine(Folder, "Tandem.exe");
    static readonly string BackupPath = ExePath + ".bak";
    static readonly string StartMenuLink = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Tandem umschalten.lnk");

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Hier merkt sich Windows, ob ein Autostart im Task-Manager deaktiviert wurde.
    const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string RunValue = "Tandem";
    const string ProcessName = "Tandem";
    const int StopTimeoutMs = 10000, FileAttempts = 5, FileRetryDelayMs = 500;

    // Installiert diese exe für die Rolle. Bei einem Update wartet die alte Version auf die Übergabe
    // (siehe Listener) und wird deshalb nicht beendet.
    public static void Install(Profile profile, bool update)
    {
        if (!update) StopRunning();
        CopySelf();
        SetAutostart(profile);
        CreateStartMenuLink(profile);
        Start(ExePath, "listen " + profile.ToArguments());
        Log.Info("Tandem " + AppVersion.Current + " installiert: " + profile + (update ? " (Update)" : ""));

        if (update)
        {
            Overlay.Run("Tandem " + AppVersion.Current + " ist installiert", null);
            return;
        }
        string text = "Tandem " + AppVersion.Current + " ist installiert: " + profile + "."
            + "\n\nStrg+Alt+K schickt Monitor, Maus und Tastatur zum anderen Laptop.";
        if (IsAutostartDisabled())
            text += "\n\nAchtung: Der Autostart von Tandem ist im Task-Manager deaktiviert. Dort unter „Autostart-Apps“ wieder aktivieren.";
        Dialog.Inform(text);
    }

    // Entfernt Autostart, Startmenü-Eintrag und den Installationsordner samt Protokoll.
    public static void Uninstall()
    {
        StopRunning();
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            if (key != null) key.DeleteValue(RunValue, false);
        File.Delete(StartMenuLink);
        Dialog.Inform("Tandem ist deinstalliert.");
        RemoveFolder();
    }

    // Startet die neu gebaute exe als Installer. Sie ersetzt die installierte exe und startet sie.
    public static void StartUpdate(string newExe, Profile profile)
    {
        Start(newExe, "install " + profile.ToArguments() + " --update");
    }

    // Beendet alle anderen Tandem-Prozesse, nach graceMs notfalls hart. Ein laufendes Umschalten
    // (Startmenü) bekommt so Zeit, fertig zu werden. Der eigene Prozess bleibt.
    public static void StopOtherInstances(int graceMs)
    {
        int self = Process.GetCurrentProcess().Id;
        foreach (Process p in Process.GetProcessesByName(ProcessName))
        {
            using (p)
            {
                if (p.Id == self) continue;
                try
                {
                    if (p.WaitForExit(graceMs)) continue;
                    p.Kill();
                    p.WaitForExit(StopTimeoutMs);
                }
                catch (InvalidOperationException) { } // schon beendet
                catch (Win32Exception ex) { Log.Error("Prozess " + p.Id + " ließ sich nicht beenden", ex); }
            }
        }
    }

    // Nach einem gescheiterten Update: die vorige exe zurückholen, falls die neue sie schon ersetzt hat.
    public static void RestoreBackup()
    {
        string current = AppVersion.Current.ToString();
        if (VersionOf(BackupPath) != current || VersionOf(ExePath) == current) return;
        try
        {
            Retry(() => File.Delete(ExePath));
            Retry(() => File.Move(BackupPath, ExePath));
            Log.Info("Sicherung " + current + " wiederhergestellt");
        }
        catch (Exception ex) { Log.Error("Sicherung ließ sich nicht wiederherstellen", ex); }
    }

    static void StopRunning()
    {
        Listener.RequestStop();
        StopOtherInstances(StopTimeoutMs);
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
        Retry(() => File.Move(next, ExePath));
    }

    static void SetAutostart(Profile profile)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            key.SetValue(RunValue, Quote(ExePath) + " listen " + profile.ToArguments());
    }

    // Der Task-Manager schreibt 02 (an) oder 03 (aus) ins erste Byte.
    static bool IsAutostartDisabled()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey))
        {
            byte[] state = key == null ? null : key.GetValue(RunValue) as byte[];
            return state != null && state.Length > 0 && (state[0] & 1) == 1;
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

    static void RemoveFolder()
    {
        if (!Directory.Exists(Folder)) return;
        if (!IsInside(Assembly.GetExecutingAssembly().Location, Folder))
        {
            Directory.Delete(Folder, true);
            return;
        }
        // Die laufende exe kann ihren eigenen Ordner nicht löschen. Das erledigt cmd, kurz nachdem sie beendet ist.
        var start = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q " + Quote(Folder));
        start.CreateNoWindow = true;
        start.UseShellExecute = false;
        Process.Start(start).Dispose();
    }

    // Arbeitsverzeichnis immer der Installationsordner, sonst bleibt z. B. der update-Ordner gesperrt.
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
