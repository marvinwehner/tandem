// Updates von GitHub. Ein Release ist ein Tag vX.Y.Z im öffentlichen Repo. Geladen wird der Quellcode, gebaut
// wird mit dem csc des .NET Framework wie bei der Installation. Die Übergabe an die neue Version steuert der Listener.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public static class Updater
{
    const string Repo = "marvinwehner/tandem";
    const int TimeoutMs = 15000, BuildTimeoutMs = 120000;
    static readonly string WorkFolder = Path.Combine(Installer.Folder, "update");

    static Updater()
    {
        // Mit dem Framework-csc gebaute Programme sprechen sonst nur SSL3/TLS 1.0, das lehnt GitHub ab.
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        // Firmen-Proxys verlangen oft die Windows-Anmeldung.
        if (WebRequest.DefaultWebProxy != null) WebRequest.DefaultWebProxy.Credentials = CredentialCache.DefaultCredentials;
    }

    // Neueste Version laut GitHub, null, solange es kein Release gibt. Wirft bei Netzwerkfehlern.
    public static AppVersion Latest()
    {
        HttpWebRequest request = CreateRequest("https://github.com/" + Repo + "/releases/latest");
        request.Method = "HEAD";
        request.AllowAutoRedirect = false;
        using (var response = (HttpWebResponse)request.GetResponse())
            return VersionFromRedirect(response.Headers[HttpResponseHeader.Location]);
    }

    // GitHub leitet /releases/latest auf /releases/tag/<tag> weiter, ohne Release auf /releases.
    public static AppVersion VersionFromRedirect(string location)
    {
        Match m = Regex.Match(location ?? "", "/releases/tag/([^/?#]+)$");
        return m.Success ? AppVersion.TryParse(Uri.UnescapeDataString(m.Groups[1].Value)) : null;
    }

    // Lädt die Version herunter, baut sie und liefert den Pfad der neuen exe. Wirft bei Fehlern.
    public static string Prepare(AppVersion version)
    {
        string folder = Path.Combine(WorkFolder, version.ToString());
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        string source = Path.Combine(folder, "quelle");
        Directory.CreateDirectory(source);
        string zip = Path.Combine(folder, "quelle.zip");
        Download("https://github.com/" + Repo + "/archive/refs/tags/" + version.Tag + ".zip", zip);
        ZipFile.ExtractToDirectory(zip, source);
        // Das Archiv enthält genau einen Ordner, z. B. tandem-1.2.0.
        string[] roots = Directory.GetDirectories(source);
        if (roots.Length != 1) throw new InvalidDataException("Unerwarteter Aufbau des Archivs von " + version.Tag);
        return Build(roots[0], version, folder);
    }

    // Baut den Quellcode unter root als outFolder\Tandem.exe und prüft, dass die exe die erwartete Version trägt.
    // csc direkt statt build.cmd: Firmenrichtlinien sperren Skripte eher als den Compiler.
    public static string Build(string root, AppVersion version, string outFolder)
    {
        File.WriteAllText(Path.Combine(root, @"src\BuildInfo.cs"), BuildInfo(version));
        string exe = Path.Combine(outFolder, "Tandem.exe");
        var start = new ProcessStartInfo(
            Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "csc.exe"),
            "/nologo /target:winexe /out:\"" + exe + "\" @src\\tandem.rsp");
        start.WorkingDirectory = root; // Pfade in tandem.rsp sind relativ zum Repo-Ordner
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        using (Process csc = Process.Start(start))
        {
            Task<string> output = csc.StandardOutput.ReadToEndAsync();
            if (!csc.WaitForExit(BuildTimeoutMs))
            {
                csc.Kill();
                throw new TimeoutException("Build von " + version + " dauert zu lange");
            }
            if (csc.ExitCode != 0) throw new InvalidOperationException("Build von " + version + " fehlgeschlagen:\n" + output.Result);
        }
        string built = FileVersionInfo.GetVersionInfo(exe).ProductVersion;
        if (built != version.ToString()) throw new InvalidOperationException("Gebaut wurde " + built + " statt " + version);
        return exe;
    }

    // Inhalt von src\BuildInfo.cs. build.cmd schreibt dieselbe Zeile.
    public static string BuildInfo(AppVersion version)
    {
        return "[assembly: System.Reflection.AssemblyInformationalVersion(\"" + version + "\")]\r\n";
    }

    // Reste früherer Updates entfernen, soweit möglich (eine gerade laufende exe ist gesperrt).
    public static void CleanUp()
    {
        try
        {
            if (Directory.Exists(WorkFolder)) Directory.Delete(WorkFolder, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static void Download(string url, string file)
    {
        using (WebResponse response = CreateRequest(url).GetResponse())
        using (Stream input = response.GetResponseStream())
        using (FileStream output = File.Create(file))
            input.CopyTo(output);
    }

    static HttpWebRequest CreateRequest(string url)
    {
        HttpWebRequest request = WebRequest.CreateHttp(url);
        request.UserAgent = "Tandem/" + AppVersion.Current; // ohne User-Agent lehnt GitHub ab
        request.Timeout = TimeoutMs;
        request.ReadWriteTimeout = TimeoutMs;
        return request;
    }
}
