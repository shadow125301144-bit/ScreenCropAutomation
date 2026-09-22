namespace ScreenCropAutomation.Models;

/// <summary>
/// 一張暫存截圖及其專屬標註（線條／矩形）。
/// </summary>
internal sealed class CaptureItem : IDisposable
{
    public CaptureItem(Bitmap bitmap)
    {
        Bitmap = bitmap;
    }

    public Bitmap Bitmap { get; set; }
    public List<ImageAnnotation> Annotations { get; } = [];

    public void Dispose() => Bitmap.Dispose();
}
