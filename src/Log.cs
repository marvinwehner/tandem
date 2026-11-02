// Protokoll im Installationsordner (tandem.log), damit sich Fehler des Hintergrundprozesses nachvollziehen lassen.
// Schreibfehler werden verschluckt: Das Protokoll darf nie das Umschalten stören.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

public static class Log
{
    const long MaxBytes = 1024 * 1024;
    static readonly object gate = new object();
    static readonly int processId = Process.GetCurrentProcess().Id;

    public static string FilePath
    {
        get { return Path.Combine(Installer.Folder, "tandem.log"); }
    }

    public static void Info(string message)
    {
        Write("INFO  ", message);
    }

    public static void Error(string message, Exception ex)
    {
        Write("FEHLER", ex == null ? message : message + ": " + ex);
    }

    static void Write(string level, string message)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + processId + "] " + level + " " + message + Environment.NewLine;
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Installer.Folder);
                var file = new FileInfo(FilePath);
                // Nur die letzte Datei aufheben, damit das Protokoll nicht unbegrenzt wächst.
                if (file.Exists && file.Length > MaxBytes)
                {
                    File.Delete(FilePath + ".1");
                    File.Move(FilePath, FilePath + ".1");
                }
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
