using ScreenCropAutomation.Models;

namespace ScreenCropAutomation.Services;

/// <summary>
/// 將標註畫進點陣圖，或在裁切後把座標換到新圖空間。
/// </summary>
internal static class ImageAnnotationService
{
    public static void Draw(Graphics graphics, Size imageSize, IReadOnlyList<ImageAnnotation> annotations)
    {
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        foreach (ImageAnnotation annotation in annotations)
        {
            using var pen = CreatePen(annotation.Color, annotation.ThicknessPx);
            PointF a = ToPixel(annotation.Start, imageSize);
            PointF b = ToPixel(annotation.End, imageSize);
            if (annotation.Kind == AnnotationKind.Line)
            {
                graphics.DrawLine(pen, a, b);
            }
            else
            {
                graphics.DrawRectangle(pen, ToRectangle(a, b));
            }
        }
    }

    public static Bitmap CloneWithAnnotations(Bitmap source, IReadOnlyList<ImageAnnotation> annotations)
    {
        var copy = new Bitmap(source.Width, source.Height);
        using Graphics graphics = Graphics.FromImage(copy);
        graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        Draw(graphics, source.Size, annotations);
        return copy;
    }

    /// <summary>
    /// 裁切後只保留落在裁切區內的標註，並換成新圖的相對座標。
    /// </summary>
    public static List<ImageAnnotation> RemapToCrop(
        IReadOnlyList<ImageAnnotation> annotations,
        RectangleF crop)
    {
        RectangleF c = ImageCropService.ClampNormalized(crop);
        var result = new List<ImageAnnotation>();

        foreach (ImageAnnotation annotation in annotations)
        {
            PointF start = RemapPoint(annotation.Start, c);
            PointF end = RemapPoint(annotation.End, c);
            bool startInside = IsInside(start);
            bool endInside = IsInside(end);
            if (!startInside && !endInside)
            {
                continue;
            }

            result.Add(new ImageAnnotation
            {
                Kind = annotation.Kind,
                Start = Clamp01(start),
                End = Clamp01(end),
                Color = annotation.Color,
                ThicknessPx = annotation.ThicknessPx
            });
        }

        return result;
    }

    public static Pen CreatePen(Color color, float thicknessPx)
    {
        return new Pen(color, Math.Max(1f, thicknessPx))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
    }

    private static PointF ToPixel(PointF normalized, Size imageSize)
        => new(normalized.X * imageSize.Width, normalized.Y * imageSize.Height);

    private static RectangleF ToRectangle(PointF a, PointF b)
    {
        float x = Math.Min(a.X, b.X);
        float y = Math.Min(a.Y, b.Y);
        float w = Math.Abs(a.X - b.X);
        float h = Math.Abs(a.Y - b.Y);
        return new RectangleF(x, y, Math.Max(1f, w), Math.Max(1f, h));
    }

    private static PointF RemapPoint(PointF p, RectangleF crop)
        => new((p.X - crop.X) / crop.Width, (p.Y - crop.Y) / crop.Height);

    private static bool IsInside(PointF p)
        => p.X >= -0.02f && p.X <= 1.02f && p.Y >= -0.02f && p.Y <= 1.02f;

    private static PointF Clamp01(PointF p)
        => new(Math.Clamp(p.X, 0f, 1f), Math.Clamp(p.Y, 0f, 1f));
}
