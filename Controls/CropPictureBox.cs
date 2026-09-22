using ScreenCropAutomation.Services;

namespace ScreenCropAutomation.Controls;

/// <summary>
/// 可在 Zoom 預覽圖上拉出、拖曳與調整紅框的 PictureBox。
/// 內部以影像相對比例（0~1）儲存裁切區，避免縮放誤差。
/// </summary>
internal sealed class CropPictureBox : PictureBox
{
    private const int HandleSize = 8;
    private const float MinNormalizedSize = 0.01f;

    private enum DragMode
    {
        None,
        Create,
        Move,
        ResizeNw,
        ResizeN,
        ResizeNe,
        ResizeE,
        ResizeSe,
        ResizeS,
        ResizeSw,
        ResizeW
    }

    private DragMode _mode = DragMode.None;
    private Point _dragStartClient;
    private RectangleF _dragStartCrop;
    private PointF _createStartNormalized;

    public CropPictureBox()
    {
        SizeMode = PictureBoxSizeMode.Zoom;
        BackColor = Color.FromArgb(32, 32, 36);
        TabStop = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>相對於原始影像寬高的裁切矩形（X%, Y%, W%, H% 以 0~1 表示）。</summary>
    public RectangleF NormalizedCrop { get; private set; }

    public bool HasCrop => NormalizedCrop.Width >= MinNormalizedSize && NormalizedCrop.Height >= MinNormalizedSize;

    public event EventHandler? CropChanged;

    public void SetNormalizedCrop(RectangleF crop)
    {
        NormalizedCrop = ImageCropService.ClampNormalized(crop);
        Invalidate();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearCrop()
    {
        NormalizedCrop = RectangleF.Empty;
        Invalidate();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        base.OnPaint(pe);

        if (Image is null || !HasCrop)
        {
            return;
        }

        Rectangle clientCrop = PictureBoxImageMapper.NormalizedToClient(this, NormalizedCrop);
        using var fill = new SolidBrush(Color.FromArgb(40, 220, 40, 40));
        using var border = new Pen(Color.FromArgb(230, 220, 40, 40), 2f);
        pe.Graphics.FillRectangle(fill, clientCrop);
        pe.Graphics.DrawRectangle(border, clientCrop);

        foreach (Rectangle handle in GetHandleRectangles(clientCrop))
        {
            pe.Graphics.FillRectangle(Brushes.White, handle);
            pe.Graphics.DrawRectangle(Pens.Red, handle);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Image is null)
        {
            return;
        }

        _dragStartClient = e.Location;
        _dragStartCrop = NormalizedCrop;
        _mode = HitTest(e.Location);

        if (_mode == DragMode.None)
        {
            _mode = DragMode.Create;
            _createStartNormalized = PictureBoxImageMapper.ClientToNormalized(this, e.Location);
            NormalizedCrop = new RectangleF(_createStartNormalized, SizeF.Empty);
        }

        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_mode == DragMode.None)
        {
            Cursor = CursorForMode(HitTest(e.Location));
            return;
        }

        ApplyDrag(e.Location);
        Invalidate();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_mode == DragMode.None)
        {
            return;
        }

        if (NormalizedCrop.Width < MinNormalizedSize || NormalizedCrop.Height < MinNormalizedSize)
        {
            ClearCrop();
        }
        else
        {
            NormalizedCrop = ImageCropService.ClampNormalized(NormalizedCrop);
        }

        _mode = DragMode.None;
        Capture = false;
        Invalidate();
        CropChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyDrag(Point currentClient)
    {
        if (_mode == DragMode.Create)
        {
            PointF current = PictureBoxImageMapper.ClientToNormalized(this, currentClient);
            float x = Math.Min(_createStartNormalized.X, current.X);
            float y = Math.Min(_createStartNormalized.Y, current.Y);
            float w = Math.Abs(current.X - _createStartNormalized.X);
            float h = Math.Abs(current.Y - _createStartNormalized.Y);
            NormalizedCrop = ImageCropService.ClampNormalized(new RectangleF(x, y, w, h));
            return;
        }

        Rectangle dest = PictureBoxImageMapper.GetImageDisplayRectangle(this);
        if (dest.Width <= 0 || dest.Height <= 0)
        {
            return;
        }

        float dx = (currentClient.X - _dragStartClient.X) / (float)dest.Width;
        float dy = (currentClient.Y - _dragStartClient.Y) / (float)dest.Height;
        RectangleF crop = _dragStartCrop;

        switch (_mode)
        {
            case DragMode.Move:
                crop.X += dx;
                crop.Y += dy;
                crop.X = Math.Clamp(crop.X, 0f, 1f - crop.Width);
                crop.Y = Math.Clamp(crop.Y, 0f, 1f - crop.Height);
                break;
            case DragMode.ResizeE:
                crop.Width += dx;
                break;
            case DragMode.ResizeW:
                crop.X += dx;
                crop.Width -= dx;
                break;
            case DragMode.ResizeS:
                crop.Height += dy;
                break;
            case DragMode.ResizeN:
                crop.Y += dy;
                crop.Height -= dy;
                break;
            case DragMode.ResizeSe:
                crop.Width += dx;
                crop.Height += dy;
                break;
            case DragMode.ResizeNw:
                crop.X += dx;
                crop.Y += dy;
                crop.Width -= dx;
                crop.Height -= dy;
                break;
            case DragMode.ResizeNe:
                crop.Y += dy;
                crop.Width += dx;
                crop.Height -= dy;
                break;
            case DragMode.ResizeSw:
                crop.X += dx;
                crop.Width -= dx;
                crop.Height += dy;
                break;
        }

        if (crop.Width < 0)
        {
            crop.X += crop.Width;
            crop.Width = Math.Abs(crop.Width);
        }

        if (crop.Height < 0)
        {
            crop.Y += crop.Height;
            crop.Height = Math.Abs(crop.Height);
        }

        NormalizedCrop = ImageCropService.ClampNormalized(crop);
    }

    private DragMode HitTest(Point client)
    {
        if (!HasCrop)
        {
            return DragMode.None;
        }

        Rectangle rect = PictureBoxImageMapper.NormalizedToClient(this, NormalizedCrop);
        Rectangle[] handles = GetHandleRectangles(rect);
        DragMode[] modes =
        [
            DragMode.ResizeNw, DragMode.ResizeN, DragMode.ResizeNe,
            DragMode.ResizeW, DragMode.ResizeE,
            DragMode.ResizeSw, DragMode.ResizeS, DragMode.ResizeSe
        ];

        for (int i = 0; i < handles.Length; i++)
        {
            if (handles[i].Contains(client))
            {
                return modes[i];
            }
        }

        return rect.Contains(client) ? DragMode.Move : DragMode.None;
    }

    private static Rectangle[] GetHandleRectangles(Rectangle crop)
    {
        return
        [
            HandleAt(crop.Left, crop.Top),
            HandleAt(crop.Left + crop.Width / 2, crop.Top),
            HandleAt(crop.Right, crop.Top),
            HandleAt(crop.Left, crop.Top + crop.Height / 2),
            HandleAt(crop.Right, crop.Top + crop.Height / 2),
            HandleAt(crop.Left, crop.Bottom),
            HandleAt(crop.Left + crop.Width / 2, crop.Bottom),
            HandleAt(crop.Right, crop.Bottom)
        ];
    }

    private static Rectangle HandleAt(int cx, int cy)
        => new(cx - HandleSize / 2, cy - HandleSize / 2, HandleSize, HandleSize);

    private static Cursor CursorForMode(DragMode mode) => mode switch
    {
        DragMode.Move => Cursors.SizeAll,
        DragMode.ResizeN or DragMode.ResizeS => Cursors.SizeNS,
        DragMode.ResizeE or DragMode.ResizeW => Cursors.SizeWE,
        DragMode.ResizeNw or DragMode.ResizeSe => Cursors.SizeNWSE,
        DragMode.ResizeNe or DragMode.ResizeSw => Cursors.SizeNESW,
        _ => Cursors.Cross
    };
}
