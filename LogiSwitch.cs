// Schaltet per Bluetooth verbundene Logitech-Geräte (Easy-Switch) per HID++ 2.0 auf einen anderen Kanal
// (Feature 0x1814 CHANGE_HOST). Funktioniert nur für Geräte, die gerade mit diesem PC verbunden sind.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

public static class LogiSwitch
{
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 27)] public ushort[] Rest;
    }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
    [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    const uint OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
    const ushort HIDPP_USAGE_PAGE = 0xFF43;
    const byte SW_ID = 0x0A;

    class Device
    {
        public string Pid, Address;
        public FileStream Stream;
        Task<int> pending;
        readonly byte[] input = new byte[20];

        public void Send(byte feature, byte function, params byte[] args)
        {
            var b = new byte[20];
            b[0] = 0x11; b[1] = 0xFF; b[2] = feature; b[3] = (byte)((function << 4) | SW_ID);
            Array.Copy(args, 0, b, 4, args.Length);
            Stream.Write(b, 0, b.Length);
        }

        // Sendet eine Anfrage und wartet bis zu 1 s auf die passende Antwort (null bei Fehler oder Timeout).
        public byte[] Request(byte feature, byte function, params byte[] args)
        {
            byte fn = (byte)((function << 4) | SW_ID);
            var deadline = DateTime.UtcNow.AddSeconds(1);
            try
            {
                Send(feature, function, args);
                while (true)
                {
                    int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (left <= 0) return null;
                    if (pending == null) pending = Stream.ReadAsync(input, 0, input.Length);
                    if (!pending.Wait(left)) return null;
                    pending = null;
                    if (input[0] != 0x11) continue;
                    if (input[2] == feature && input[3] == fn) return (byte[])input.Clone();
                    if (input[2] == 0xFF && input[3] == feature && input[4] == fn) return null; // HID++-Fehlermeldung
                }
            }
            catch (Exception) { return null; }
        }

        public byte FeatureIndex(ushort feature)
        {
            var r = Request(0x00, 0, (byte)(feature >> 8), (byte)feature);
            return r == null ? (byte)0 : r[4];
        }

        public string Name()
        {
            byte idx = FeatureIndex(0x0005); // DEVICE_NAME
            var count = idx == 0 ? null : Request(idx, 0);
            var sb = new StringBuilder();
            while (count != null && sb.Length < count[4])
            {
                var part = Request(idx, 1, (byte)sb.Length);
                if (part == null) break;
                for (int i = 4; i < 20 && sb.Length < count[4]; i++) sb.Append((char)part[i]);
            }
            return sb.Length > 0 ? sb.ToString() : "Logitech-Gerät " + Pid;
        }
    }

    // Alle verbundenen Logitech-Bluetooth-Geräte mit HID++-Schnittstelle, jedes Gerät einmal.
    static List<Device> Find()
    {
        var result = new List<Device>();
        var seen = new HashSet<string>();
        Guid guid;
        HidD_GetHidGuid(out guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA();
            data.cbSize = Marshal.SizeOf(data);
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                string path = GetPath(set, ref data);
                if (path == null) continue;
                // Pfad z. B. \\?\hid#{00001812-...}_dev_vid&02046d_pid&b359_rev&0010_<bt-adresse>&col04#...
                string p = path.ToLowerInvariant();
                int v = p.IndexOf("vid&02046d_pid&");
                int r = p.IndexOf("_rev&", Math.Max(v, 0));
                if (v < 0 || r < 0 || p.Length < r + 22) continue;

                var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h.IsInvalid) continue;
                bool hidpp = false;
                IntPtr pre;
                if (HidD_GetPreparsedData(h, out pre))
                {
                    HIDP_CAPS caps;
                    HidP_GetCaps(pre, out caps);
                    hidpp = caps.UsagePage == HIDPP_USAGE_PAGE && caps.OutputReportByteLength == 20;
                    HidD_FreePreparsedData(pre);
                }
                string address = p.Substring(r + 10, 12);
                if (!hidpp || !seen.Add(address)) { h.Dispose(); continue; }

                var d = new Device();
                d.Pid = p.Substring(v + 15, 4);
                d.Address = address;
                d.Stream = new FileStream(h, FileAccess.ReadWrite, 1, true);
                result.Add(d);
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    static string GetPath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data)
    {
        int size;
        SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out size, IntPtr.Zero);
        if (size == 0) return null;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // cbSize von SP_DEVICE_INTERFACE_DETAIL_DATA_W
            if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buf, size, out size, IntPtr.Zero)) return null;
            return Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public static void PrintInfo()
    {
        var devices = Find();
        if (devices.Count == 0) Console.WriteLine("Keine Logitech-Geräte mit diesem PC verbunden.");
        foreach (var d in devices)
        {
            byte idx = d.FeatureIndex(0x1814);
            var info = idx == 0 ? null : d.Request(idx, 0);
            Console.WriteLine(d.Name() + " (" + d.Address + "): "
                + (info == null ? "Easy-Switch nicht erreichbar" : "Kanal " + (info[5] + 1) + " von " + info[4]));
            d.Stream.Dispose();
        }
    }

    // Schickt jedes verbundene Gerät auf den jeweils anderen der Kanäle 1 und 2.
    public static int SwitchAway()
    {
        int switched = 0;
        foreach (var d in Find())
        {
            byte idx = d.FeatureIndex(0x1814);
            var info = idx == 0 ? null : d.Request(idx, 0);
            if (info != null)
            {
                try { d.Send(idx, 1, (byte)(info[5] == 0 ? 1 : 0)); switched++; }
                catch (IOException) { }
            }
            d.Stream.Dispose();
        }
        return switched;
    }

    // Wartet, bis der Hotkey losgelassen ist. Sonst bleiben Strg/Alt auf diesem PC eventuell "gedrückt",
    // weil die Tastatur schon beim anderen PC ist, bevor sie das Loslassen meldet.
    public static void WaitForKeysReleased()
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            bool anyDown = false;
            for (int vk = 0x08; vk <= 0xFE && !anyDown; vk++)
                if ((GetAsyncKeyState(vk) & 0x8000) != 0) anyDown = true;
            if (!anyDown) return;
            Thread.Sleep(20);
        }
    }
}
