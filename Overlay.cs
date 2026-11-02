// Kleines Hinweisfenster (ähnlich der Lautstärke-Anzeige), damit man sofort sieht, dass der Hotkey erkannt wurde.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public static class Overlay
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point pt, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    // Skalierung des Bildschirms (1.25 = 125 %). Laptop-Panel und Samsung sind unterschiedlich skaliert.
    static float ScaleOf(Screen screen)
    {
        uint dpiX, dpiY;
        IntPtr monitor = MonitorFromPoint(new Point(screen.Bounds.Left + 1, screen.Bounds.Top + 1), 2 /* NEAREST */);
        return GetDpiForMonitor(monitor, 0, out dpiX, out dpiY) == 0 ? dpiX / 96f : 1f;
    }

    class OsdForm : Form
    {
        readonly Screen screen;
        readonly float scale;
        readonly Label label = new Label();

        public OsdForm(string text, Screen screen)
        {
            this.screen = screen;
            scale = ScaleOf(screen);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(40, 40, 40);
            Opacity = 0.95;
            ResizeRedraw = true; // sonst bleiben beim Textwechsel Reste des alten Rahmens stehen
            label.AutoSize = true;
            label.ForeColor = Color.White;
            label.Font = new Font("Segoe UI", 19f * scale, GraphicsUnit.Pixel); // 14 pt bei 100 %
            Controls.Add(label);
            SetText(text);
        }

        public void SetText(string text)
        {
            int padX = (int)(28 * scale), padY = (int)(18 * scale);
            label.Text = text;
            Size s = label.PreferredSize;
            ClientSize = new Size(s.Width + 2 * padX, s.Height + 2 * padY);
            label.Location = new Point(padX, padY);
            Rectangle a = screen.WorkingArea;
            Location = new Point(a.Left + (a.Width - Width) / 2, a.Bottom - Height - a.Height / 10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(110, 110, 110)))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        // Fokus nicht stehlen und nicht in Alt+Tab auftauchen.
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x80 /* WS_EX_TOOLWINDOW */ | 0x8 /* WS_EX_TOPMOST */;
                return cp;
            }
        }
    }

    // Zeigt <text> auf allen Bildschirmen, führt <work> im Hintergrund aus, zeigt dessen Ergebnis kurz an und schließt.
    public static void Run(string text, Func<string> work)
    {
        // Per-Monitor-DPI (V2), damit der Text auf beiden Bildschirmen scharf und gleich groß ist.
        if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware();
        var forms = new List<OsdForm>();
        foreach (Screen screen in Screen.AllScreens)
        {
            var f = new OsdForm(text, screen);
            f.Show();
            forms.Add(f);
        }
        var context = new ApplicationContext();
        var worker = new Thread(() =>
        {
            string result;
            try { result = work(); }
            catch (Exception ex) { result = "Fehler: " + ex.Message; }
            forms[0].BeginInvoke((Action)(() =>
            {
                foreach (var f in forms) f.SetText(result);
                var timer = new System.Windows.Forms.Timer();
                timer.Interval = 1500;
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    foreach (var f in forms) f.Close(); // im Hintergrundprozess läuft das Programm danach weiter
                    context.ExitThread();
                };
                timer.Start();
            }));
        });
        worker.IsBackground = true;
        worker.Start();
        Application.Run(context);
    }
}
