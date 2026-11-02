// Liest den Monitoreingang im Hintergrund, damit das Tray-Symbol auch merkt, wenn vom anderen Laptop oder am
// Monitor umgeschaltet wurde. Läuft gerade ein anderer Zugriff (Umschalten), setzt die Abfrage eine Runde aus.
using System;
using System.Threading;

public sealed class MonitorWatch
{
    readonly SynchronizationContext ui;
    readonly Action<uint?> read; // auf dem UI-Thread: Eingang des ersten DDC-Monitors, null wenn nicht erreichbar
    bool running;

    public MonitorWatch(SynchronizationContext ui, Action<uint?> read)
    {
        this.ui = ui;
        this.read = read;
    }

    // Nur vom UI-Thread aufrufen. DDC kann im Standby Sekunden hängen, deshalb nie auf dem UI-Thread lesen.
    public void Poll()
    {
        if (running) return;
        running = true;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            bool skipped = false;
            uint? input = null;
            try
            {
                using (Ddc ddc = Ddc.TryOpen())
                {
                    if (ddc == null) skipped = true;
                    else if (ddc.Monitors.Count > 0) input = ddc.Monitors[0].Input;
                }
            }
            catch (Exception ex) { Log.Error("Monitor-Abfrage fehlgeschlagen", ex); }
            ui.Post(__ =>
            {
                running = false;
                if (!skipped) read(input);
            }, null);
        });
    }
}
