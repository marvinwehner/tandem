// Logitech-Geräte (Easy-Switch) per Bluetooth und HID++ 2.0 auf den anderen Kanal schicken
// (Feature 0x1814 CHANGE_HOST). Funktioniert nur für Geräte, die gerade mit diesem PC verbunden sind.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

public static class Logitech
{
    public sealed class DeviceStatus
    {
        public DeviceStatus(string name, string address, int channel, int channelCount)
        {
            Name = name;
            Address = address;
            Channel = channel;
            ChannelCount = channelCount;
        }

        public string Name { get; private set; }
        public string Address { get; private set; }
        // 1-basiert, 0 = Easy-Switch nicht erreichbar
        public int Channel { get; private set; }
        public int ChannelCount { get; private set; }
    }

    // PID und Bluetooth-Adresse aus dem HID-Pfad, z. B.
    // \\?\hid#{00001812-...}_dev_vid&02046d_pid&b359_rev&0010_<bt-adresse>&col04#...
    public sealed class HidPath
    {
        const string VendorMarker = "vid&02046d_pid&", RevisionMarker = "_rev&";
        const int PidLength = 4, RevisionLength = 4, AddressLength = 12;

        HidPath(string pid, string address)
        {
            Pid = pid;
            Address = address;
        }

        public string Pid { get; private set; }
        public string Address { get; private set; }

        // null, wenn der Pfad zu keinem Logitech-Bluetooth-Gerät gehört.
        public static HidPath Parse(string path)
        {
            string p = path.ToLowerInvariant();
            int v = p.IndexOf(VendorMarker, StringComparison.Ordinal);
            if (v < 0) return null;
            int r = p.IndexOf(RevisionMarker, v, StringComparison.Ordinal);
            // Nach "_rev&" folgen die Revision, ein "_" und die Bluetooth-Adresse.
            int a = r + RevisionMarker.Length + RevisionLength + 1;
            if (r < 0 || p.Length < a + AddressLength) return null;
            return new HidPath(p.Substring(v + VendorMarker.Length, PidLength), p.Substring(a, AddressLength));
        }
    }

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

    const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    const uint OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

    const ushort HidppUsagePage = 0xFF43;
    const byte LongReportId = 0x11;      // HID++ Long Report, 20 Byte
    const int LongReportLength = 20;
    const byte DeviceIndex = 0xFF;       // Gerät direkt per Bluetooth, ohne Empfänger dazwischen
    const byte ErrorFeatureIndex = 0xFF; // Antwort mit Feature-Index 0xFF = HID++-Fehlermeldung
    const byte SoftwareId = 0x0A;        // unterscheidet unsere Antworten von denen anderer Programme
    const int PayloadOffset = 4;         // Report-ID, Device-Index, Feature-Index, Funktion, dann die Daten
    const int ResponseTimeoutMs = 1000;

    // Features und ihre Funktionen
    const byte RootIndex = 0x00, RootGetFeature = 0;
    const ushort DeviceName = 0x0005;
    const byte DeviceNameGetCount = 0, DeviceNameGetName = 1;
    const ushort ChangeHost = 0x1814;
    const byte ChangeHostGetInfo = 0, ChangeHostSetCurrent = 1;

    // Easy-Switch-Zustand eines Geräts. Current ist 0-basiert.
    sealed class HostInfo
    {
        public HostInfo(byte featureIndex, int count, int current)
        {
            FeatureIndex = featureIndex;
            Count = count;
            Current = current;
        }

        public byte FeatureIndex { get; private set; }
        public int Count { get; private set; }
        public int Current { get; private set; }
    }

    sealed class Device : IDisposable
    {
        readonly FileStream stream;
        readonly byte[] input = new byte[LongReportLength];
        // Ein abgelaufenes ReadAsync lässt sich nicht abbrechen. Es bleibt für die nächste Anfrage stehen,
        // ein zweites Lesen daneben würde Antworten verschlucken.
        Task<int> pending;

        public Device(HidPath path, SafeFileHandle handle)
        {
            Path = path;
            stream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
        }

        public HidPath Path { get; private set; }

        public void Dispose()
        {
            stream.Dispose();
        }

        public void Send(byte featureIndex, byte function, params byte[] args)
        {
            var b = new byte[LongReportLength];
            b[0] = LongReportId;
            b[1] = DeviceIndex;
            b[2] = featureIndex;
            b[3] = (byte)((function << 4) | SoftwareId);
            Array.Copy(args, 0, b, PayloadOffset, args.Length);
            stream.Write(b, 0, b.Length);
        }

        // Sendet eine Anfrage und wartet bis zu 1 s auf die passende Antwort (null bei Fehler oder Timeout).
        public byte[] Request(byte featureIndex, byte function, params byte[] args)
        {
            byte fn = (byte)((function << 4) | SoftwareId);
            var deadline = DateTime.UtcNow.AddMilliseconds(ResponseTimeoutMs);
            try
            {
                Send(featureIndex, function, args);
                while (true)
                {
                    int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (left <= 0) return null;
                    if (pending == null) pending = stream.ReadAsync(input, 0, input.Length);
                    if (!pending.Wait(left)) return null;
                    pending = null;
                    if (input[0] != LongReportId) continue;
                    if (input[2] == featureIndex && input[3] == fn) return (byte[])input.Clone();
                    if (input[2] == ErrorFeatureIndex && input[3] == featureIndex && input[PayloadOffset] == fn) return null;
                }
            }
            // Gerät weg oder Verbindung abgebrochen: wie keine Antwort behandeln.
            catch (IOException) { return null; }
            catch (AggregateException) { return null; }
            catch (ObjectDisposedException) { return null; }
        }

        // Index des Features auf diesem Gerät, 0 wenn es das Feature nicht gibt.
        public byte FeatureIndex(ushort feature)
        {
            byte[] r = Request(RootIndex, RootGetFeature, (byte)(feature >> 8), (byte)feature);
            return r == null ? (byte)0 : r[PayloadOffset];
        }

        public string Name()
        {
            byte index = FeatureIndex(DeviceName);
            byte[] count = index == 0 ? null : Request(index, DeviceNameGetCount);
            var sb = new StringBuilder();
            int length = count == null ? 0 : count[PayloadOffset];
            while (sb.Length < length)
            {
                byte[] part = Request(index, DeviceNameGetName, (byte)sb.Length);
                if (part == null) break;
                for (int i = PayloadOffset; i < LongReportLength && sb.Length < length; i++) sb.Append((char)part[i]);
            }
            return sb.Length > 0 ? sb.ToString() : "Logitech-Gerät " + Path.Pid;
        }

        // null, wenn das Gerät CHANGE_HOST nicht beantwortet.
        public HostInfo GetHostInfo()
        {
            byte index = FeatureIndex(ChangeHost);
            byte[] r = index == 0 ? null : Request(index, ChangeHostGetInfo);
            return r == null ? null : new HostInfo(index, r[PayloadOffset], r[PayloadOffset + 1]);
        }
    }

    public static List<DeviceStatus> ListDevices()
    {
        var result = new List<DeviceStatus>();
        ForEachDevice(d =>
        {
            HostInfo info = d.GetHostInfo();
            result.Add(info == null
                ? new DeviceStatus(d.Name(), d.Path.Address, 0, 0)
                : new DeviceStatus(d.Name(), d.Path.Address, info.Current + 1, info.Count));
        });
        return result;
    }

    // Schickt jedes verbundene Gerät auf den jeweils anderen der Kanäle 1 und 2. Liefert die Anzahl der Geräte.
    public static int SwitchAway()
    {
        int switched = 0;
        ForEachDevice(d =>
        {
            HostInfo info = d.GetHostInfo();
            if (info == null) return;
            // Keine Antwort abwarten: Das Gerät ist sofort beim anderen Laptop.
            try { d.Send(info.FeatureIndex, ChangeHostSetCurrent, (byte)(info.Current == 0 ? 1 : 0)); switched++; }
            catch (IOException) { }
        });
        return switched;
    }

    static void ForEachDevice(Action<Device> action)
    {
        List<Device> devices = Find();
        try
        {
            foreach (Device d in devices) action(d);
        }
        finally
        {
            foreach (Device d in devices) d.Dispose();
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
                HidPath id = path == null ? null : HidPath.Parse(path);
                if (id == null || seen.Contains(id.Address)) continue;

                SafeFileHandle handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (handle.IsInvalid) continue;
                // Jedes Gerät hat mehrere HID-Schnittstellen, HID++ spricht nur die mit eigener Usage Page.
                if (!IsHidpp(handle)) { handle.Dispose(); continue; }
                seen.Add(id.Address);
                result.Add(new Device(id, handle));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    static bool IsHidpp(SafeFileHandle handle)
    {
        IntPtr data;
        if (!HidD_GetPreparsedData(handle, out data)) return false;
        try
        {
            HIDP_CAPS caps;
            HidP_GetCaps(data, out caps);
            return caps.UsagePage == HidppUsagePage && caps.OutputReportByteLength == LongReportLength;
        }
        finally { HidD_FreePreparsedData(data); }
    }

    static string GetPath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data)
    {
        int size;
        SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out size, IntPtr.Zero);
        if (size == 0) return null;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W: cbSize (4 Byte, Wert je nach Packing), danach der Pfad
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, size, out size, IntPtr.Zero)) return null;
            return Marshal.PtrToStringUni(IntPtr.Add(buffer, sizeof(int)));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
