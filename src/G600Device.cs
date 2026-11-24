using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace G600Studio;

internal sealed class G600Device(SafeFileHandle handle, int reportSize, string path) : IDisposable
{
    public const int ProfileCount = 3;
    public const int ProfileSize = 154;
    public string Path { get; } = path;

    public static G600Device Open()
    {
        HidD_GetHidGuid(out var guid);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            CheckConfig(CM_Get_Device_Interface_List_SizeW(out var size, ref guid, IntPtr.Zero, 0));
            var buffer = new char[size];
            var result = CM_Get_Device_Interface_ListW(ref guid, IntPtr.Zero, buffer, size, 0);
            if (result == 0x1a)
                continue;
            CheckConfig(result);

            var matches = new List<G600Device>();
            var failedOpen = 0;
            foreach (var candidate in new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!candidate.Contains("vid_046d&pid_c24a", StringComparison.OrdinalIgnoreCase) ||
                    !candidate.Contains("mi_01", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Query the collection with zero I/O access, then open only the vendor collection for reports.
                using var query = CreateFileW(candidate, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (query.IsInvalid || !HidD_GetPreparsedData(query, out var data))
                {
                    failedOpen = Marshal.GetLastWin32Error();
                    continue;
                }

                var caps = new byte[64]; // HIDP_CAPS, including the 17 reserved USHORTs.
                int capsStatus;
                try { capsStatus = HidP_GetCaps(data, caps); }
                finally { HidD_FreePreparsedData(data); }
                if (capsStatus != 0x00110000 || BitConverter.ToUInt16(caps, 0) != 0x0080 ||
                    BitConverter.ToUInt16(caps, 2) != 0xff80)
                    continue;

                var length = BitConverter.ToUInt16(caps, 8);
                if (length < ProfileSize)
                    continue;

                var device = CreateFileW(candidate, 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (device.IsInvalid)
                {
                    failedOpen = Marshal.GetLastWin32Error();
                    device.Dispose();
                    continue;
                }
                matches.Add(new G600Device(device, length, candidate));
            }

            if (matches.Count == 1)
                return matches[0];
            foreach (var match in matches)
                match.Dispose();
            if (matches.Count > 1)
                throw new IOException("G600 が複数台見つかりました。設定する1台だけを接続してください。");
            if (failedOpen != 0)
                throw new Win32Exception(failedOpen, $"G600 の設定用接続を開けません（Windows エラー {failedOpen}）。");
            throw new IOException("G600 の設定用接続が見つかりません。USB 接続を確認してください。");
        }
        throw new IOException("接続中の機器が変化しました。もう一度読み込んでください。");
    }

    public (int Profile, int DpiSlot, byte[] Raw) ReadActive()
    {
        var raw = Read(0xf0, 4);
        return (raw[1] >> 4, (raw[1] >> 1) & 3, raw);
    }

    public byte[] ReadProfile(int index) => Read(ProfileId(index), ProfileSize);

    public void WriteProfile(int index, byte[] report)
    {
        if (report.Length != ProfileSize || report[0] != ProfileId(index))
            throw new InvalidDataException("プロファイルの形式が一致しません。");
        try { Write(report); }
        finally
        {
            // The G600 programs this report in chunks. A GET during that
            // interval can interrupt the transfer and corrupt the profile.
            Thread.Sleep(2400);
        }
    }

    public void SelectProfile(int index)
    {
        _ = ProfileId(index);
        Write([0xf0, (byte)(0x80 | (index << 4)), 0, 0]);
    }

    public void SelectDpi(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 3);
        Write([0xf0, (byte)(0x40 | (index << 1)), 0, 0]);
    }

    private byte[] Read(byte id, int length)
    {
        var buffer = new byte[reportSize];
        buffer[0] = id;
        if (!HidD_GetFeature(handle, buffer, buffer.Length))
            throw ReportError("読み込み", id);
        if (buffer[0] != id)
            throw new IOException($"G600 から想定外の応答がありました（0x{buffer[0]:X2}）。");
        return buffer[..length];
    }

    private void Write(byte[] report)
    {
        var buffer = new byte[reportSize];
        report.CopyTo(buffer, 0);
        if (!HidD_SetFeature(handle, buffer, buffer.Length))
            throw ReportError("書き込み", buffer[0]);
    }

    internal static byte ProfileId(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ProfileCount);
        return (byte)(0xf3 + index);
    }

    private static Win32Exception ReportError(string action, byte id)
    {
        var error = Marshal.GetLastWin32Error();
        return new Win32Exception(error, $"G600 の{action}に失敗しました（レポート 0x{id:X2}、Windows エラー {error}）。");
    }

    private static void CheckConfig(int result)
    {
        if (result != 0)
            throw new IOException($"HID 接続の列挙に失敗しました（ConfigMgr 0x{result:X}）。");
    }

    public void Dispose() => handle.Dispose();

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out uint size, ref Guid guid, IntPtr device, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_Interface_ListW(ref Guid guid, IntPtr device, [Out] char[] list, uint length, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr data, [Out] byte[] caps);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetFeature(SafeFileHandle device, [In, Out] byte[] report, int length);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetFeature(SafeFileHandle device, [In] byte[] report, int length);
}
