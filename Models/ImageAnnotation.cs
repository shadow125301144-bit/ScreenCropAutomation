namespace ScreenCropAutomation.Models;

internal enum AnnotationKind
{
    Line,
    Rectangle
}

/// <summary>
/// 單張圖上的標註。座標為相對影像的 0~1，不會隨「全部套用」複製到其他圖。
/// </summary>
internal sealed class ImageAnnotation
{
    public required AnnotationKind Kind { get; init; }
    public required PointF Start { get; init; }
    public required PointF End { get; init; }
    public required Color Color { get; init; }
    public required float ThicknessPx { get; init; }
}
