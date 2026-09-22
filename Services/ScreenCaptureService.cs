namespace ScreenCropAutomation.Services;

/// <summary>
/// 使用 <see cref="Graphics.CopyFromScreen"/> 擷取主螢幕全畫面。
/// </summary>
internal static class ScreenCaptureService
{
    /// <summary>
    /// 擷取目前主螢幕（含工作列）並回傳獨立的 Bitmap。呼叫端負責 Dispose。
    /// </summary>
    public static Bitmap CapturePrimaryScreen()
    {
        Screen screen = Screen.PrimaryScreen
            ?? throw new InvalidOperationException("找不到主螢幕。");

        Rectangle bounds = screen.Bounds;
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return bitmap;
    }
}
