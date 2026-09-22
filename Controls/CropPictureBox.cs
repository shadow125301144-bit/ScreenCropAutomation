using ScreenCropAutomation.Models;
using ScreenCropAutomation.Services;

namespace ScreenCropAutomation.Controls;

internal enum EditorTool
{
    Crop,
    Rectangle,
    Line
}

/// <summary>
/// 可在 Zoom 預覽圖上調整裁切紅框，或以小畫家方式畫矩形／直線。
/// 標註與裁切分開：畫上去的線只屬於目前這張圖。
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
        ResizeW,
        Draw
    }

    private DragMode _mode = DragMode.None;
    private Point _dragStartClient;
    private RectangleF _dragStartCrop;
    private PointF _createStartNormalized;
    private PointF _drawStartNormalized;
    private PointF _drawEndNormalized;
    private bool _drawingPreview;

    public CropPictureBox()
    {
        SizeMode = PictureBoxSizeMode.Zoom;
        BackColor = Color.FromArgb(32, 32, 36);
        TabStop = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public EditorTool Tool { get; set; } = EditorTool.Crop;

    public Color DrawColor { get; set; } = Color.Red;

    public float DrawThicknessPx { get; set; } = 3f;

    public List<ImageAnnotation> Annotations { get; private set; } = [];

    /// <summary>相對於原始影像寬高的裁切矩形（X%, Y%, W%, H% 以 0~1 表示）。</summary>
    public RectangleF NormalizedCrop { get; private set; }

    public bool HasCrop => NormalizedCrop.Width >= MinNormalizedSize && NormalizedCrop.Height >= MinNormalizedSize;

    public event EventHandler? CropChanged;

    public event EventHandler? AnnotationsChanged;

    public void SetAnnotations(List<ImageAnnotation> annotations)
    {
        Annotations = annotations;
        Invalidate();
    }

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

    public void UndoLastAnnotation()
    {
        if (Annotations.Count == 0)
        {
            return;
        }

        Annotations.RemoveAt(Annotations.Count - 1);
        Invalidate();
        AnnotationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearAnnotations()
    {
        if (Annotations.Count == 0)
        {
            return;
        }

        Annotations.Clear();
        Invalidate();
        AnnotationsChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        base.OnPaint(pe);
        if (Image is null)
        {
            return;
        }

        Rectangle dest = PictureBoxImageMapper.GetImageDisplayRectangle(this);
        if (dest.Width <= 0 || dest.Height <= 0)
        {
            return;
        }

        float scale = dest.Width / (float)Image.Width;
        pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        foreach (ImageAnnotation annotation in Annotations)
        {
            DrawAnnotation(pe.Graphics, annotation, dest, scale);
        }

        if (_drawingPreview)
        {
            var preview = new ImageAnnotation
            {
                Kind = Tool == EditorTool.Line ? AnnotationKind.Line : AnnotationKind.Rectangle,
                Start = _drawStartNormalized,
                End = _drawEndNormalized,
                Color = DrawColor,
                ThicknessPx = DrawThicknessPx
            };
            DrawAnnotation(pe.Graphics, preview, dest, scale);
        }

        if (!HasCrop)
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

        if (Tool is EditorTool.Line or EditorTool.Rectangle)
        {
            _mode = DragMode.Draw;
            _drawStartNormalized = PictureBoxImageMapper.ClientToNormalized(this, e.Location);
            _drawEndNormalized = _drawStartNormalized;
            _drawingPreview = true;
            Capture = true;
            Invalidate();
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
            Cursor = Tool == EditorTool.Crop ? CursorForMode(HitTest(e.Location)) : Cursors.Cross;
            return;
        }

        if (_mode == DragMode.Draw)
        {
            _drawEndNormalized = PictureBoxImageMapper.ClientToNormalized(this, e.Location);
            Invalidate();
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

        if (_mode == DragMode.Draw)
        {
            _drawEndNormalized = PictureBoxImageMapper.ClientToNormalized(this, e.Location);
            float dx = _drawEndNormalized.X - _drawStartNormalized.X;
            float dy = _drawEndNormalized.Y - _drawStartNormalized.Y;
            if (Math.Sqrt((dx * dx) + (dy * dy)) >= 0.004)
            {
                Annotations.Add(new ImageAnnotation
                {
                    Kind = Tool == EditorTool.Line ? AnnotationKind.Line : AnnotationKind.Rectangle,
                    Start = _drawStartNormalized,
                    End = _drawEndNormalized,
                    Color = DrawColor,
                    ThicknessPx = DrawThicknessPx
                });
                AnnotationsChanged?.Invoke(this, EventArgs.Empty);
            }

            _drawingPreview = false;
            _mode = DragMode.None;
            Capture = false;
            Invalidate();
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

    private void DrawAnnotation(Graphics graphics, ImageAnnotation annotation, Rectangle dest, float scale)
    {
        PointF a = NormalizedToClientPoint(annotation.Start, dest);
        PointF b = NormalizedToClientPoint(annotation.End, dest);
        using var pen = ImageAnnotationService.CreatePen(annotation.Color, Math.Max(1f, annotation.ThicknessPx * scale));
        if (annotation.Kind == AnnotationKind.Line)
        {
            graphics.DrawLine(pen, a, b);
        }
        else
        {
            float x = Math.Min(a.X, b.X);
            float y = Math.Min(a.Y, b.Y);
            float w = Math.Max(1f, Math.Abs(a.X - b.X));
            float h = Math.Max(1f, Math.Abs(a.Y - b.Y));
            graphics.DrawRectangle(pen, x, y, w, h);
        }
    }

    private static PointF NormalizedToClientPoint(PointF normalized, Rectangle dest)
        => new(dest.X + normalized.X * dest.Width, dest.Y + normalized.Y * dest.Height);

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
