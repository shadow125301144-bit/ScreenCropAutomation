namespace ScreenCropAutomation.Services;

/// <summary>
/// 將 PictureBox（SizeMode = Zoom）客戶座標與影像座標互相轉換。
/// Zoom 模式會維持長寬比並在多餘空間留黑邊，必須先算出實際繪製矩形。
/// </summary>
internal static class PictureBoxImageMapper
{
    /// <summary>
    /// 取得影像在 PictureBox 內實際被繪製的矩形（含置中偏移）。
    /// </summary>
    public static Rectangle GetImageDisplayRectangle(PictureBox box)
    {
        if (box.Image is null || box.ClientSize.Width <= 0 || box.ClientSize.Height <= 0)
        {
            return Rectangle.Empty;
        }

        Image image = box.Image;
        float scale = Math.Min(
            box.ClientSize.Width / (float)image.Width,
            box.ClientSize.Height / (float)image.Height);

        int width = Math.Max(1, (int)Math.Round(image.Width * scale));
        int height = Math.Max(1, (int)Math.Round(image.Height * scale));
        int x = (box.ClientSize.Width - width) / 2;
        int y = (box.ClientSize.Height - height) / 2;
        return new Rectangle(x, y, width, height);
    }

    /// <summary>
    /// 客戶座標 → 影像正規化座標（0~1）。超出繪製區會被夾住。
    /// </summary>
    public static PointF ClientToNormalized(PictureBox box, Point client)
    {
        Rectangle dest = GetImageDisplayRectangle(box);
        if (dest.Width <= 0 || dest.Height <= 0)
        {
            return PointF.Empty;
        }

        float nx = (client.X - dest.X) / (float)dest.Width;
        float ny = (client.Y - dest.Y) / (float)dest.Height;
        return new PointF(Math.Clamp(nx, 0f, 1f), Math.Clamp(ny, 0f, 1f));
    }

    /// <summary>
    /// 正規化矩形 → PictureBox 客戶座標矩形，供繪製紅框使用。
    /// </summary>
    public static Rectangle NormalizedToClient(PictureBox box, RectangleF normalized)
    {
        Rectangle dest = GetImageDisplayRectangle(box);
        if (dest.Width <= 0 || dest.Height <= 0)
        {
            return Rectangle.Empty;
        }

        RectangleF clamped = ImageCropService.ClampNormalized(normalized);
        int x = dest.X + (int)Math.Round(clamped.X * dest.Width);
        int y = dest.Y + (int)Math.Round(clamped.Y * dest.Height);
        int w = Math.Max(1, (int)Math.Round(clamped.Width * dest.Width));
        int h = Math.Max(1, (int)Math.Round(clamped.Height * dest.Height));
        return new Rectangle(x, y, w, h);
    }
}
