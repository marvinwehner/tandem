// Zeichnet das Tandem-Icon „Geteilter Bildschirm“ und schreibt es als .ico. Aufruf über tools\icon.cmd.
// Dazu zwei Varianten fürs Tray-Symbol, bei denen eine Hälfte gedimmt ist: Die helle Hälfte zeigt, welcher
// Laptop gerade den Monitor hat (links Privatlaptop, rechts Arbeitslaptop).
// Entwurf und Begründung: design/icon-board.html. Bis 48 px sind die Formen von Hand aufs Pixelraster gesetzt,
// darüber kommen sie aus dem 64er-Raster des Entwurfs. Bis 64 px als 32-Bit-BMP, 256 px als PNG:
// So lesen csc, Explorer und System.Drawing.Icon alle Einträge.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconGenerator
{
    static readonly Color Dark = Color.FromArgb(0x28, 0x28, 0x28);  // wie das Hinweisfenster
    static readonly Color Frame = Color.FromArgb(0x6E, 0x6E, 0x6E); // Rahmen des Hinweisfensters
    static readonly Color Amber = Color.FromArgb(0xF5, 0xA5, 0x24); // Privatlaptop, DisplayPort
    static readonly Color Blue = Color.FromArgb(0x3D, 0x7B, 0xF5);  // Arbeitslaptop, USB-C
    const float DimmedShare = 0.25f; // gedimmte Hälfte: 25 % Farbe über dem dunklen Rahmen
    const int MaxTraySize = 32;       // das Tray-Symbol ist höchstens 32 px groß (200 %)

    // Maße einer Größe in Pixeln. Rechtecke als { links, oben, rechts, unten }.
    sealed class Layout
    {
        public int Size;
        public float[] Bezel;
        public float BezelRadius;
        public int Border;
        public float[] PaneLeft, PaneRight;
        public float PaneRadius;
        public float[] Neck, Foot;
        public float FootRadius;
        // Ab 32 px hat der Fuß einen Rahmen wie der Bildschirm, darunter ist er einfach grau.
        public bool OutlinedStand;
    }

    static readonly Layout[] Layouts =
    {
        Make(16, new float[] { 0, 1, 16, 12 }, 2, 1, new float[] { 2, 3, 7, 10 }, new float[] { 9, 3, 14, 10 }, 1,
            new float[] { 7, 12, 9, 13 }, new float[] { 4, 13, 12, 15 }, 1, false),
        Make(20, new float[] { 0, 1, 20, 15 }, 2.5f, 1, new float[] { 2, 3, 9, 13 }, new float[] { 11, 3, 18, 13 }, 1,
            new float[] { 9, 15, 11, 17 }, new float[] { 5, 17, 15, 19 }, 1, false),
        Make(24, new float[] { 0, 2, 24, 19 }, 3, 1, new float[] { 3, 5, 11, 16 }, new float[] { 13, 5, 21, 16 }, 1.5f,
            new float[] { 11, 19, 13, 21 }, new float[] { 6, 21, 18, 23 }, 1, false),
        Make(32, new float[] { 1, 3, 31, 24 }, 4, 1, new float[] { 4, 6, 15, 21 }, new float[] { 17, 6, 28, 21 }, 2,
            new float[] { 13, 23, 19, 27 }, new float[] { 7, 26, 25, 30 }, 2, true),
        Make(40, new float[] { 2, 4, 38, 30 }, 5, 2, new float[] { 7, 9, 19, 25 }, new float[] { 21, 9, 33, 25 }, 2,
            new float[] { 16, 28, 24, 34 }, new float[] { 9, 32, 31, 38 }, 3, true),
        Make(48, new float[] { 2, 5, 46, 36 }, 6, 2, new float[] { 7, 10, 23, 31 }, new float[] { 25, 10, 41, 31 }, 2,
            new float[] { 20, 34, 28, 41 }, new float[] { 11, 39, 37, 45 }, 3, true),
        Scaled(64, 1),
        Scaled(256, 4),
    };

    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Aufruf: IconGenerator.exe <zielordner>");
            return 2;
        }
        Write(Path.Combine(args[0], "tandem.ico"), Layouts, false, false);
        var trayLayouts = Array.FindAll(Layouts, l => l.Size <= MaxTraySize);
        Write(Path.Combine(args[0], "tray-links.ico"), trayLayouts, false, true);
        Write(Path.Combine(args[0], "tray-rechts.ico"), trayLayouts, true, false);
        return 0;
    }

    static void Write(string file, Layout[] layouts, bool dimLeft, bool dimRight)
    {
        var images = new List<Bitmap>();
        foreach (Layout layout in layouts) images.Add(Draw(layout, dimLeft, dimRight));
        WriteIco(file, images);
        foreach (Bitmap image in images) image.Dispose();
        Console.WriteLine(file + " geschrieben (" + layouts.Length + " Größen)");
    }

    static Layout Make(int size, float[] bezel, float bezelRadius, int border, float[] paneLeft, float[] paneRight,
        float paneRadius, float[] neck, float[] foot, float footRadius, bool outlinedStand)
    {
        var l = new Layout();
        l.Size = size;
        l.Bezel = bezel;
        l.BezelRadius = bezelRadius;
        l.Border = border;
        l.PaneLeft = paneLeft;
        l.PaneRight = paneRight;
        l.PaneRadius = paneRadius;
        l.Neck = neck;
        l.Foot = foot;
        l.FootRadius = footRadius;
        l.OutlinedStand = outlinedStand;
        return l;
    }

    // Der Entwurf im 64er-Raster, mit factor vergrößert.
    static Layout Scaled(int size, float factor)
    {
        return Make(size,
            Scale(new float[] { 4, 7, 60, 47 }, factor), 7 * factor, (int)(2 * factor),
            Scale(new float[] { 10, 13, 31, 41 }, factor), Scale(new float[] { 33, 13, 54, 41 }, factor), 2.5f * factor,
            Scale(new float[] { 27, 44, 37, 53 }, factor), Scale(new float[] { 15, 51, 49, 58 }, factor), 3.5f * factor, true);
    }

    static float[] Scale(float[] r, float factor)
    {
        return new[] { r[0] * factor, r[1] * factor, r[2] * factor, r[3] * factor };
    }

    static Bitmap Draw(Layout l, bool dimLeft, bool dimRight)
    {
        var bitmap = new Bitmap(l.Size, l.Size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // Fuß zuerst, der Bildschirm verdeckt das obere Ende des Halses.
            Fill(g, Frame, l.Neck, 0);
            Fill(g, Frame, l.Foot, l.FootRadius);
            if (l.OutlinedStand)
            {
                int b = l.Border;
                Fill(g, Dark, new[] { l.Neck[0] + b, l.Neck[1], l.Neck[2] - b, l.Neck[3] }, 0);
                Fill(g, Dark, Inset(l.Foot, b), l.FootRadius - b);
            }

            Fill(g, Frame, l.Bezel, l.BezelRadius);
            Fill(g, Dark, Inset(l.Bezel, l.Border), l.BezelRadius - l.Border);
            Fill(g, dimLeft ? Dim(Amber) : Amber, l.PaneLeft, l.PaneRadius);
            Fill(g, dimRight ? Dim(Blue) : Blue, l.PaneRight, l.PaneRadius);
        }
        return bitmap;
    }

    static Color Dim(Color c)
    {
        return Color.FromArgb(
            (int)(Dark.R + (c.R - Dark.R) * DimmedShare),
            (int)(Dark.G + (c.G - Dark.G) * DimmedShare),
            (int)(Dark.B + (c.B - Dark.B) * DimmedShare));
    }

    static float[] Inset(float[] r, float by)
    {
        return new[] { r[0] + by, r[1] + by, r[2] - by, r[3] - by };
    }

    static void Fill(Graphics g, Color color, float[] r, float radius)
    {
        using (var brush = new SolidBrush(color))
        using (GraphicsPath path = RoundedRect(r, radius))
            g.FillPath(brush, path);
    }

    static GraphicsPath RoundedRect(float[] r, float radius)
    {
        var path = new GraphicsPath();
        float width = r[2] - r[0], height = r[3] - r[1];
        float d = Math.Min(2 * radius, Math.Min(width, height));
        if (d <= 0)
        {
            path.AddRectangle(new RectangleF(r[0], r[1], width, height));
            return path;
        }
        path.AddArc(r[0], r[1], d, d, 180, 90);
        path.AddArc(r[2] - d, r[1], d, d, 270, 90);
        path.AddArc(r[2] - d, r[3] - d, d, d, 0, 90);
        path.AddArc(r[0], r[3] - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ICO-Datei: Kopf, ein Verzeichniseintrag je Größe, dann die Bilddaten.
    static void WriteIco(string file, List<Bitmap> images)
    {
        var data = new List<byte[]>();
        foreach (Bitmap image in images) data.Add(image.Width >= 256 ? Png(image) : Dib(image));

        using (var w = new BinaryWriter(File.Create(file)))
        {
            w.Write((short)0);            // reserviert
            w.Write((short)1);            // Typ: Icon
            w.Write((short)images.Count);
            int offset = 6 + 16 * images.Count;
            for (int i = 0; i < images.Count; i++)
            {
                int size = images[i].Width;
                w.Write((byte)(size >= 256 ? 0 : size)); // 0 steht für 256
                w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)0);         // keine Palette
                w.Write((byte)0);         // reserviert
                w.Write((short)1);        // Ebenen
                w.Write((short)32);       // Bit pro Pixel
                w.Write(data[i].Length);
                w.Write(offset);
                offset += data[i].Length;
            }
            foreach (byte[] bytes in data) w.Write(bytes);
        }
    }

    static byte[] Png(Bitmap image)
    {
        using (var stream = new MemoryStream())
        {
            image.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    // 32-Bit-DIB wie in klassischen Icons: BITMAPINFOHEADER mit doppelter Höhe, Farben von unten nach oben,
    // danach die 1-Bit-Maske (gesetzt = durchsichtig) für Programme ohne Alphakanal.
    static byte[] Dib(Bitmap image)
    {
        int n = image.Width;
        int maskStride = (n + 31) / 32 * 4;
        using (var stream = new MemoryStream())
        using (var w = new BinaryWriter(stream))
        {
            w.Write(40);                  // Größe des Headers
            w.Write(n);
            w.Write(2 * n);               // Farben und Maske
            w.Write((short)1);
            w.Write((short)32);
            w.Write(0);                   // unkomprimiert
            w.Write(n * n * 4 + maskStride * n);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            w.Write(0);
            for (int y = n - 1; y >= 0; y--)
            {
                for (int x = 0; x < n; x++)
                {
                    Color c = image.GetPixel(x, y);
                    w.Write(c.B);
                    w.Write(c.G);
                    w.Write(c.R);
                    w.Write(c.A);
                }
            }
            for (int y = n - 1; y >= 0; y--)
            {
                var row = new byte[maskStride];
                for (int x = 0; x < n; x++)
                    if (image.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                w.Write(row);
            }
            return stream.ToArray();
        }
    }
}
