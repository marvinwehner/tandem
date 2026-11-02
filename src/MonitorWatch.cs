// Liest Eingang und Bildschirmfläche des Monitors im Hintergrund: für das Tray-Symbol, das auch merken soll, wenn
// vom anderen Laptop oder am Monitor umgeschaltet wurde, und für den Rand beim Mittelklick. Läuft gerade ein anderer
// Zugriff (Umschalten), setzt die Abfrage eine Runde aus.
using System;
using System.Drawing;
using System.Threading;

// Ergebnis einer Abfrage. Werte statt Ddc.Monitor, denn dessen Handles sind nach der Abfrage ungültig.
public sealed class MonitorReading
{
    public MonitorReading(uint? input, Rectangle? bounds)
    {
        Input = input;
        Bounds = bounds;
    }

    // Eingang des ersten DDC-Monitors, null wenn keiner erreichbar ist
    public uint? Input { get; private set; }
    // Seine Fläche auf dem Windows-Desktop in physischen Pixeln, null wenn keiner erreichbar ist
    public Rectangle? Bounds { get; private set; }
}

public sealed class MonitorWatch
{
    readonly SynchronizationContext ui;
    readonly Action<MonitorReading> read; // auf dem UI-Thread
    bool running;
    bool again; // während einer Abfrage angefragt, z. B. nach geänderten Anzeigeeinstellungen

    public MonitorWatch(SynchronizationContext ui, Action<MonitorReading> read)
    {
        this.ui = ui;
        this.read = read;
    }

    // Nur vom UI-Thread aufrufen. DDC kann im Standby Sekunden hängen, deshalb nie auf dem UI-Thread lesen.
    public void Poll()
    {
        if (running)
        {
            again = true;
            return;
        }
        running = true;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            MonitorReading reading = null;
            try
            {
                using (Ddc ddc = Ddc.TryOpen())
                {
                    if (ddc != null)
                        reading = ddc.Monitors.Count > 0
                            ? new MonitorReading(ddc.Monitors[0].Input, ddc.Monitors[0].Bounds)
                            : new MonitorReading(null, null);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Monitor-Abfrage fehlgeschlagen", ex);
                reading = new MonitorReading(null, null);
            }
            ui.Post(__ =>
            {
                running = false;
                // Kam inzwischen eine neue Anfrage (z. B. geänderte Anzeige), ist dieses Ergebnis schon veraltet.
                if (reading != null && !again) read(reading);
                if (again)
                {
                    again = false;
                    Poll();
                }
            }, null);
        });
    }
}
