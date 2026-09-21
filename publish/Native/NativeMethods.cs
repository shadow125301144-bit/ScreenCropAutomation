using System.Runtime.InteropServices;

namespace ScreenCropAutomation.Native;

/// <summary>
/// Win32 P/Invoke 宣告（全域快捷鍵）。
/// </summary>
internal static class NativeMethods
{
    /// <summary>熱鍵訊息識別碼。</summary>
    public const int WmHotKey = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
