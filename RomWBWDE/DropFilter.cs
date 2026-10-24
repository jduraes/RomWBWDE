using System.Runtime.InteropServices;

namespace RomWBWDE.Explorer;

internal static class DropFilter
{
    private const uint MsgfltAllow = 1;
    private const uint WmDropFiles = 0x0233;
    private const uint WmCopyData = 0x004A;
    private const uint WmCopyGlobalData = 0x0049;

    public static void Allow(IntPtr handle)
    {
        ChangeWindowMessageFilterEx(handle, WmDropFiles, MsgfltAllow, IntPtr.Zero);
        ChangeWindowMessageFilterEx(handle, WmCopyData, MsgfltAllow, IntPtr.Zero);
        ChangeWindowMessageFilterEx(handle, WmCopyGlobalData, MsgfltAllow, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr changeInfo);
}
