using System.Media;
using ScreenCropAutomation.Controls;
using ScreenCropAutomation.Models;
using ScreenCropAutomation.Native;
using ScreenCropAutomation.Services;

namespace ScreenCropAutomation;

/// <summary>
/// 主視窗：全域截圖、裁切、單張標註（線／框）與批次套用裁切。
/// 「全部套用」只套用裁切比例，不會把某一張的畫線／畫框套到其他圖。
/// </summary>
public sealed class MainForm : Form
{
    private static readonly Color[] Palette =
    [
        Color.Black, Color.White, Color.Red, Color.Orange, Color.Gold,
        Color.LimeGreen, Color.DeepSkyBlue, Color.Blue, Color.Magenta
    ];

    private readonly List<CaptureItem> _captures = [];
    private readonly ImageList _thumbnails = new()
    {
        ColorDepth = ColorDepth.Depth32Bit,
        ImageSize = new Size(160, 90),
        TransparentColor = Color.Transparent
    };

    private GlobalHotKey? _hotKey;

    private SplitContainer _split = null!;
    private ListView _listView = null!;
    private CropPictureBox _preview = null!;
    private ComboBox _hotKeyCombo = null!;
    private CheckBox _soundCheck = null!;
    private Label _statusLabel = null!;
    private Label _cropInfoLabel = null!;
    private Button _applyAllButton = null!;
    private Button _copyButton = null!;
    private RadioButton _cropTool = null!;
    private RadioButton _rectTool = null!;
    private RadioButton _lineTool = null!;
    private ComboBox _thicknessCombo = null!;
    private Panel _colorSwatch = null!;

    public MainForm()
    {
        Text = "Screen Crop Automation — 截圖裁切小工具";
        MinimumSize = new Size(980, 760);
        Size = new Size(1280, 860);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Font = new Font("Microsoft JhengHei UI", 9f);

        BuildLayout();
        UpdateUiState();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hotKey = new GlobalHotKey(Handle);
        _hotKey.Pressed += (_, _) => BeginInvoke(CaptureScreen);
        RegisterSelectedHotKey();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _hotKey?.Dispose();
        ClearCaptures(disposeImages: true);
        _thumbnails.Dispose();
        MultiImageClipboardService.CleanupFolder();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (_hotKey is not null && _hotKey.ProcessMessage(ref m))
        {
            return;
        }

        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.C))
        {
            CopyCurrentCropToClipboard();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Z))
        {
            _preview.UndoLastAnnotation();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        root.Controls.Add(BuildToolbar(), 0, 0);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            // 建構當下尚未有實際寬度，MinSize 過大會直接讓程式開不起來。
            Panel1MinSize = 50,
            Panel2MinSize = 50
        };

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.LargeIcon,
            LargeImageList = _thumbnails,
            HideSelection = false,
            MultiSelect = false,
            BorderStyle = BorderStyle.FixedSingle
        };
        _listView.SelectedIndexChanged += (_, _) => ShowSelectedCapture();

        var previewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        _preview = new CropPictureBox
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(480, 320)
        };
        _preview.CropChanged += (_, _) => UpdateCropInfo();
        _preview.AnnotationsChanged += (_, _) => UpdateCropInfo();

        _cropInfoLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "請在預覽圖上拖曳滑鼠拉出紅色裁切框。"
        };

        previewPanel.Controls.Add(_preview, 0, 0);
        previewPanel.Controls.Add(BuildPaintBar(), 0, 1);
        previewPanel.Controls.Add(_cropInfoLabel, 0, 2);

        _split.Panel1.Controls.Add(_listView);
        _split.Panel2.Controls.Add(previewPanel);

        root.Controls.Add(_split, 0, 1);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "就緒。按下快捷鍵擷取主螢幕。"
        };
        root.Controls.Add(_statusLabel, 0, 2);

        Controls.Add(root);
        Load += (_, _) =>
        {
            ApplySplitterLayout(initialize: true);
        };
        SizeChanged += (_, _) => ApplySplitterLayout(initialize: false);
    }

    /// <summary>
    /// 視窗變窄時優先縮小左側縮圖清單，保住右側裁切區與底部工具列寬度。
    /// </summary>
    private void ApplySplitterLayout(bool initialize)
    {
        if (!_split.IsHandleCreated || _split.Width < 80)
        {
            return;
        }

        const int preferredLeft = 168;
        const int minLeft = 120;
        const int minRight = 560;
        int available = _split.Width - _split.SplitterWidth;
        int rightFloor = minRight;
        if (available < minLeft + minRight)
        {
            rightFloor = Math.Max(360, available - minLeft);
        }

        _split.Panel1MinSize = 50;
        _split.Panel2MinSize = 50;

        int maxLeft = Math.Max(minLeft, available - rightFloor);
        int distance = initialize
            ? Math.Min(preferredLeft, maxLeft)
            : Math.Min(_split.SplitterDistance, maxLeft);
        distance = Math.Clamp(distance, minLeft, Math.Max(minLeft, available - 50));

        try
        {
            if (_split.SplitterDistance != distance)
            {
                _split.SplitterDistance = distance;
            }

            _split.Panel1MinSize = minLeft;
            int remaining = available - _split.SplitterDistance;
            if (remaining > 50)
            {
                _split.Panel2MinSize = Math.Min(rightFloor, remaining);
            }
        }
        catch (InvalidOperationException)
        {
            // 縮放過程中 SplitContainer 尚未完成配置時略過。
        }
    }

    private Control BuildPaintBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            AutoScroll = false,
            Padding = new Padding(0, 2, 0, 2)
        };

        bar.Controls.Add(MakeLabel("工具："));
        _cropTool = MakeToolRadio("裁切", true);
        _rectTool = MakeToolRadio("畫矩形", false);
        _lineTool = MakeToolRadio("畫直線", false);
        _cropTool.CheckedChanged += (_, _) => SetTool(EditorTool.Crop, _cropTool);
        _rectTool.CheckedChanged += (_, _) => SetTool(EditorTool.Rectangle, _rectTool);
        _lineTool.CheckedChanged += (_, _) => SetTool(EditorTool.Line, _lineTool);
        bar.Controls.Add(_cropTool);
        bar.Controls.Add(_rectTool);
        bar.Controls.Add(_lineTool);

        bar.Controls.Add(MakeLabel("顏色："));
        foreach (Color color in Palette)
        {
            Color picked = color;
            var chip = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Padding(1, 6, 1, 2),
                BackColor = picked,
                FlatStyle = FlatStyle.Flat,
                TabStop = false
            };
            chip.FlatAppearance.BorderColor = Color.DimGray;
            chip.Click += (_, _) => SetDrawColor(picked);
            bar.Controls.Add(chip);
        }

        var moreColor = MakeButton("其他…", () =>
        {
            using var dialog = new ColorDialog { Color = _preview.DrawColor, FullOpen = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                SetDrawColor(dialog.Color);
            }
        });
        moreColor.Margin = new Padding(4, 4, 6, 2);
        bar.Controls.Add(moreColor);

        _colorSwatch = new Panel
        {
            Width = 28,
            Height = 22,
            Margin = new Padding(0, 8, 10, 2),
            BackColor = _preview.DrawColor,
            BorderStyle = BorderStyle.FixedSingle
        };
        bar.Controls.Add(_colorSwatch);

        bar.Controls.Add(MakeLabel("粗細："));
        _thicknessCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 72,
            Margin = new Padding(0, 6, 8, 2)
        };
        foreach (int px in new[] { 1, 2, 3, 5, 8, 12, 18 })
        {
            _thicknessCombo.Items.Add($"{px} px");
        }

        _thicknessCombo.SelectedIndex = 2;
        _thicknessCombo.SelectedIndexChanged += (_, _) =>
        {
            string token = (_thicknessCombo.SelectedItem?.ToString() ?? "3 px").Split(' ')[0];
            _preview.DrawThicknessPx = int.Parse(token);
        };
        bar.Controls.Add(_thicknessCombo);

        bar.Controls.Add(MakeButton("復原標註", () => _preview.UndoLastAnnotation()));
        bar.Controls.Add(MakeButton("清除此圖標註", () => _preview.ClearAnnotations()));
        return bar;
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(4, 8, 2, 0)
    };

    private static RadioButton MakeToolRadio(string text, bool isChecked) => new()
    {
        Text = text,
        AutoSize = true,
        Appearance = Appearance.Button,
        Checked = isChecked,
        Margin = new Padding(0, 4, 4, 2),
        Padding = new Padding(6, 3, 6, 3),
        FlatStyle = FlatStyle.System
    };

    private void SetTool(EditorTool tool, RadioButton source)
    {
        if (!source.Checked)
        {
            return;
        }

        _preview.Tool = tool;
        _statusLabel.Text = tool switch
        {
            EditorTool.Rectangle => "畫矩形：在目前這張圖上拖曳即可。標註不會套用到其他圖。",
            EditorTool.Line => "畫直線：在目前這張圖上拖出任意方向的線。標註不會套用到其他圖。",
            _ => "裁切：拖曳紅框。全部套用只會套用這個裁切區。"
        };
    }

    private void SetDrawColor(Color color)
    {
        _preview.DrawColor = color;
        _colorSwatch.BackColor = color;
    }

    private Control BuildToolbar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true
        };

        bar.Controls.Add(new Label
        {
            Text = "全域快捷鍵：",
            AutoSize = true,
            Margin = new Padding(0, 10, 4, 0)
        });

        _hotKeyCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 80,
            Margin = new Padding(0, 6, 12, 0)
        };
        for (int i = 2; i <= 12; i++)
        {
            _hotKeyCombo.Items.Add($"F{i}");
        }

        _hotKeyCombo.SelectedIndex = 0;
        _hotKeyCombo.SelectedIndexChanged += (_, _) => RegisterSelectedHotKey();
        bar.Controls.Add(_hotKeyCombo);

        _soundCheck = new CheckBox
        {
            Text = "截圖提示音",
            Checked = true,
            AutoSize = true,
            Margin = new Padding(0, 8, 12, 0)
        };
        bar.Controls.Add(_soundCheck);

        var deleteButton = MakeButton("刪除選取", DeleteSelected);
        var clearButton = MakeButton("清空暫存", () =>
        {
            if (_captures.Count == 0)
            {
                return;
            }

            if (MessageBox.Show(this, "確定清空所有暫存截圖？", "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes)
            {
                return;
            }

            ClearCaptures(disposeImages: true);
            UpdateUiState();
        });

        _applyAllButton = MakeButton("全部套用", ApplyCropToAll);
        _copyButton = MakeButton("複製目前的裁剪到Excel", CopyCurrentCropToClipboard);

        bar.Controls.Add(deleteButton);
        bar.Controls.Add(clearButton);
        bar.Controls.Add(_applyAllButton);
        bar.Controls.Add(_copyButton);

        return bar;
    }

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 4, 8, 0),
            Padding = new Padding(8, 4, 8, 4),
            UseVisualStyleBackColor = true
        };
        button.Click += (_, _) => click();
        return button;
    }

    private void RegisterSelectedHotKey()
    {
        if (_hotKey is null || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        string token = _hotKeyCombo.SelectedItem?.ToString() ?? "F2";
        Keys key = (Keys)Enum.Parse(typeof(Keys), token);
        if (!_hotKey.TryRegister(key, out string error))
        {
            MessageBox.Show(this, error, "快捷鍵註冊失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _statusLabel.Text = error;
            return;
        }

        _statusLabel.Text = $"已註冊全域快捷鍵 {key}。在任何視窗按下即可擷取主螢幕。";
    }

    private void CaptureScreen()
    {
        try
        {
            Bitmap bitmap = ScreenCaptureService.CapturePrimaryScreen();
            _captures.Add(new CaptureItem(bitmap));
            AddThumbnail(bitmap, _captures.Count - 1);
            SelectIndex(_captures.Count - 1);

            if (_soundCheck.Checked)
            {
                SystemSounds.Asterisk.Play();
            }

            _statusLabel.Text = $"已擷取第 {_captures.Count} 張（{_hotKey?.CurrentKey ?? Keys.F2}）。";
            UpdateUiState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"截圖失敗：{ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddThumbnail(Bitmap source, int index)
    {
        using var thumb = new Bitmap(_thumbnails.ImageSize.Width, _thumbnails.ImageSize.Height);
        using (Graphics g = Graphics.FromImage(thumb))
        {
            g.Clear(Color.FromArgb(24, 24, 28));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            float scale = Math.Min(
                thumb.Width / (float)source.Width,
                thumb.Height / (float)source.Height);
            int w = Math.Max(1, (int)(source.Width * scale));
            int h = Math.Max(1, (int)(source.Height * scale));
            int x = (thumb.Width - w) / 2;
            int y = (thumb.Height - h) / 2;
            g.DrawImage(source, new Rectangle(x, y, w, h));
        }

        _thumbnails.Images.Add((Bitmap)thumb.Clone());
        _listView.Items.Add(new ListViewItem($"#{index + 1}") { ImageIndex = index });
    }

    private void ShowSelectedCapture()
    {
        int index = GetSelectedIndex();
        if (index < 0)
        {
            _preview.Image = null;
            _preview.SetAnnotations([]);
            UpdateCropInfo();
            return;
        }

        CaptureItem item = _captures[index];
        _preview.Image = item.Bitmap;
        _preview.SetAnnotations(item.Annotations);
        UpdateCropInfo();
    }

    private void DeleteSelected()
    {
        int index = GetSelectedIndex();
        if (index < 0)
        {
            return;
        }

        _preview.Image = null;
        _preview.SetAnnotations([]);
        _captures[index].Dispose();
        _captures.RemoveAt(index);
        RebuildThumbnails();
        if (_captures.Count > 0)
        {
            SelectIndex(Math.Min(index, _captures.Count - 1));
        }
        else
        {
            _preview.Image = null;
        }

        UpdateUiState();
    }

    private void RebuildThumbnails()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();
        foreach (Image image in _thumbnails.Images)
        {
            image.Dispose();
        }

        _thumbnails.Images.Clear();
        for (int i = 0; i < _captures.Count; i++)
        {
            AddThumbnail(_captures[i].Bitmap, i);
        }

        _listView.EndUpdate();
    }

    private void ClearCaptures(bool disposeImages)
    {
        _preview.Image = null;
        _preview.SetAnnotations([]);
        _listView.Items.Clear();

        if (disposeImages)
        {
            foreach (CaptureItem item in _captures)
            {
                item.Dispose();
            }
        }

        _captures.Clear();
        foreach (Image image in _thumbnails.Images)
        {
            image.Dispose();
        }

        _thumbnails.Images.Clear();
    }

    /// <summary>
    /// 只把目前紅框比例套用到每一張圖。各圖自己的線條／矩形會跟著裁切座標轉換，不會互相複製。
    /// </summary>
    private void ApplyCropToAll()
    {
        if (_captures.Count == 0)
        {
            MessageBox.Show(this, "暫存區沒有截圖。", "無法套用", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_preview.HasCrop)
        {
            MessageBox.Show(this, "請先在預覽圖上設定裁切紅框。", "無法套用", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            RectangleF ratio = ImageCropService.ClampNormalized(_preview.NormalizedCrop);
            int selected = Math.Max(0, GetSelectedIndex());
            _preview.Image = null;
            _preview.SetAnnotations([]);

            foreach (CaptureItem item in _captures)
            {
                Bitmap cropped = ImageCropService.CropByNormalized(item.Bitmap, ratio);
                List<ImageAnnotation> remapped = ImageAnnotationService.RemapToCrop(item.Annotations, ratio);
                item.Bitmap.Dispose();
                item.Bitmap = cropped;
                item.Annotations.Clear();
                item.Annotations.AddRange(remapped);
            }

            _preview.ClearCrop();
            RebuildThumbnails();
            SelectIndex(Math.Min(selected, _captures.Count - 1));
            UpdateUiState();
            _statusLabel.Text = $"已將裁切套用到全部 {_captures.Count} 張。標註仍各圖獨立，未互相套用。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"套用失敗：{ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 複製時：各圖先依共用裁切區裁切，再畫上「該圖自己的」標註。
    /// </summary>
    private void CopyCurrentCropToClipboard()
    {
        if (_captures.Count == 0)
        {
            _statusLabel.Text = "暫存區沒有截圖，無法複製。";
            return;
        }

        var generated = new List<Bitmap>();
        try
        {
            RectangleF? crop = _preview.HasCrop
                ? ImageCropService.ClampNormalized(_preview.NormalizedCrop)
                : null;

            foreach (CaptureItem item in _captures)
            {
                if (crop is { } ratio)
                {
                    using Bitmap cropped = ImageCropService.CropByNormalized(item.Bitmap, ratio);
                    List<ImageAnnotation> remapped = ImageAnnotationService.RemapToCrop(item.Annotations, ratio);
                    generated.Add(ImageAnnotationService.CloneWithAnnotations(cropped, remapped));
                }
                else
                {
                    generated.Add(ImageAnnotationService.CloneWithAnnotations(item.Bitmap, item.Annotations));
                }
            }

            _statusLabel.Text = MultiImageClipboardService.CopyAll(generated);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "複製失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            foreach (Bitmap bitmap in generated)
            {
                bitmap.Dispose();
            }
        }
    }

    private void UpdateCropInfo()
    {
        int index = GetSelectedIndex();
        if (index < 0)
        {
            _cropInfoLabel.Text = "尚未選取截圖。";
            return;
        }

        CaptureItem item = _captures[index];
        Bitmap source = item.Bitmap;
        string marks = item.Annotations.Count == 0
            ? "此圖尚無標註"
            : $"此圖標註 {item.Annotations.Count} 筆（僅屬於這張）";

        if (!_preview.HasCrop)
        {
            _cropInfoLabel.Text = $"原圖 {source.Width}×{source.Height}。{marks}。切換「裁切」可拉紅框。";
            return;
        }

        RectangleF n = _preview.NormalizedCrop;
        Rectangle px = ImageCropService.ToPixelRectangle(source.Size, n);
        _cropInfoLabel.Text =
            $"裁切 X={n.X:P1} Y={n.Y:P1} W={n.Width:P1} H={n.Height:P1}　" +
            $"像素 ({px.X}, {px.Y}, {px.Width}×{px.Height})　{marks}";
    }

    private void UpdateUiState()
    {
        bool hasItems = _captures.Count > 0;
        _applyAllButton.Enabled = hasItems;
        _copyButton.Enabled = hasItems;
        Text = $"Screen Crop Automation — 暫存 {_captures.Count} 張";
        UpdateCropInfo();
    }

    private int GetSelectedIndex()
        => _listView.SelectedIndices.Count > 0 ? _listView.SelectedIndices[0] : -1;

    private void SelectIndex(int index)
    {
        if (index < 0 || index >= _listView.Items.Count)
        {
            return;
        }

        _listView.SelectedIndices.Clear();
        _listView.Items[index].Selected = true;
        _listView.Items[index].EnsureVisible();
        _listView.Focus();
    }
}
