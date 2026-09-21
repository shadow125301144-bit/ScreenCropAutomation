using System.Runtime.InteropServices;

namespace ScreenCropAutomation.Native;

/// <summary>
/// 取得正在執行的 COM 物件（.NET 8 沒有 Marshal.GetActiveObject）。
/// </summary>
internal static class ActiveComObject
{
    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string lpszProgID, out Guid pclsid);

    [DllImport("oleaut32.dll")]
    private static extern int GetActiveObject(
        ref Guid rclsid,
        IntPtr pvReserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    public static object? TryGet(string progId)
    {
        if (CLSIDFromProgID(progId, out Guid clsid) != 0)
        {
            return null;
        }

        if (GetActiveObject(ref clsid, IntPtr.Zero, out object obj) != 0)
        {
            return null;
        }

        return obj;
    }
}
