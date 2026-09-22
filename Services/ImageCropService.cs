namespace ScreenCropAutomation.Services;

/// <summary>
/// 以「相對比例」對任意解析度點陣圖做矩形裁切。
/// </summary>
internal static class ImageCropService
{
    /// <summary>
    /// 依正規化矩形（0~1）裁切。會自動夾在影像範圍內。
    /// </summary>
    public static Bitmap CropByNormalized(Bitmap source, RectangleF normalized)
    {
        Rectangle pixel = ToPixelRectangle(source.Size, normalized);
        if (pixel.Width <= 0 || pixel.Height <= 0)
        {
            throw new InvalidOperationException("裁切區域無效，請先在預覽圖上拉出紅框。");
        }

        var cropped = new Bitmap(pixel.Width, pixel.Height);
        using Graphics graphics = Graphics.FromImage(cropped);
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, pixel.Width, pixel.Height),
            pixel,
            GraphicsUnit.Pixel);
        return cropped;
    }

    /// <summary>
    /// 將 0~1 比例矩形轉成來源影像的像素矩形。
    /// </summary>
    public static Rectangle ToPixelRectangle(Size imageSize, RectangleF normalized)
    {
        RectangleF clamped = ClampNormalized(normalized);

        int x = (int)Math.Round(clamped.X * imageSize.Width);
        int y = (int)Math.Round(clamped.Y * imageSize.Height);
        int right = (int)Math.Round((clamped.X + clamped.Width) * imageSize.Width);
        int bottom = (int)Math.Round((clamped.Y + clamped.Height) * imageSize.Height);

        x = Math.Clamp(x, 0, Math.Max(0, imageSize.Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, imageSize.Height - 1));
        right = Math.Clamp(right, x + 1, imageSize.Width);
        bottom = Math.Clamp(bottom, y + 1, imageSize.Height);

        return Rectangle.FromLTRB(x, y, right, bottom);
    }

    public static RectangleF ClampNormalized(RectangleF rect)
    {
        float x = Math.Clamp(rect.X, 0f, 1f);
        float y = Math.Clamp(rect.Y, 0f, 1f);
        float right = Math.Clamp(rect.Right, 0f, 1f);
        float bottom = Math.Clamp(rect.Bottom, 0f, 1f);

        if (right < x)
        {
            (x, right) = (right, x);
        }

        if (bottom < y)
        {
            (y, bottom) = (bottom, y);
        }

        return RectangleF.FromLTRB(x, y, Math.Max(x + 0.001f, right), Math.Max(y + 0.001f, bottom));
    }
}
