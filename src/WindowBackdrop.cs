using System.Runtime.InteropServices;

namespace G600Studio;

internal static class WindowBackdrop
{
    private const int UseImmersiveDarkMode = 20;
    private const int SystemBackdropType = 38;
    private const int NoBackdrop = 1;
    private const int DesktopAcrylic = 3;
    private const int ExtendedStyle = -20;
    private const int LayeredStyle = 0x00080000;
    private const uint LayeredAlpha = 2;
    private const uint RedrawChildrenNow = 0x0001 | 0x0004 | 0x0080 | 0x0100;

    private static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    public static bool Apply(nint window, bool enabled)
    {
        if (!IsSupported)
            return false;

        SetAttribute(window, UseImmersiveDarkMode, enabled ? 1 : 0);
        if (enabled && SetAttribute(window, SystemBackdropType, DesktopAcrylic) >= 0)
        {
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            if (DwmExtendFrameIntoClientArea(window, ref margins) >= 0)
                return true;
        }

        SetAttribute(window, SystemBackdropType, NoBackdrop);
        var normalFrame = new Margins();
        DwmExtendFrameIntoClientArea(window, ref normalFrame);
        return false;
    }

    public static void MakeOpaque(nint window)
    {
        if (!IsSupported)
            return;

        // A layered child supplies its own alpha instead of relying on GDI's frame pixels.
        var style = GetWindowLong(window, ExtendedStyle);
        SetWindowLong(window, ExtendedStyle, style | LayeredStyle);
        if (SetLayeredWindowAttributes(window, 0, 255, LayeredAlpha))
            RedrawWindow(window, 0, 0, RedrawChildrenNow);
        else
            SetWindowLong(window, ExtendedStyle, style);
    }

    private static int SetAttribute(nint window, int attribute, int value) =>
        DwmSetWindowAttribute(window, attribute, ref value, sizeof(int));

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint window, int index, int value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(nint window, nint updateRectangle, nint updateRegion, uint flags);
}
