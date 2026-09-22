using System.Media;
using ScreenCropAutomation.Controls;
using ScreenCropAutomation.Native;
using ScreenCropAutomation.Services;

namespace ScreenCropAutomation;

/// <summary>
/// 主視窗：全域截圖、縮圖清單、批次套用裁切與一次複製全部圖片。
/// </summary>
public sealed class MainForm : Form
{
    private readonly List<Bitmap> _captures = [];
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

    public MainForm()
    {
        Text = "Screen Crop Automation — 截圖裁切小工具";
        MinimumSize = new Size(1100, 700);
        Size = new Size(1280, 800);
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

    /// <summary>
    /// 攔截 Win32 訊息，將 WM_HOTKEY 交給 GlobalHotKey 處理。
    /// </summary>
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        root.Controls.Add(BuildToolbar(), 0, 0);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6
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
            RowCount = 2
        };
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        _preview = new CropPictureBox { Dock = DockStyle.Fill };
        _preview.CropChanged += (_, _) => UpdateCropInfo();

        _cropInfoLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "請在預覽圖上拖曳滑鼠拉出紅色裁切框。"
        };

        previewPanel.Controls.Add(_preview, 0, 0);
        previewPanel.Controls.Add(_cropInfoLabel, 0, 1);

        _split.Panel1.Controls.Add(_listView);
        _split.Panel2.Controls.Add(previewPanel);

        root.Controls.Add(_split, 0, 1);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "就緒。按下 F1 擷取主螢幕。"
        };
        root.Controls.Add(_statusLabel, 0, 2);

        Controls.Add(root);
        Load += (_, _) =>
        {
            if (_split.Width > 0)
            {
                _split.SplitterDistance = Math.Max(220, (int)(_split.Width * 0.24));
            }
        };
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
            _captures.Add(bitmap);
            AddThumbnail(bitmap, _captures.Count - 1);
            SelectIndex(_captures.Count - 1);

            if (_soundCheck.Checked)
            {
                SystemSounds.Asterisk.Play();
            }

            _statusLabel.Text = $"已擷取第 {_captures.Count} 張（{_hotKey?.CurrentKey ?? Keys.F1}）。";
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
            UpdateCropInfo();
            return;
        }

        _preview.Image = _captures[index];
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
            AddThumbnail(_captures[i], i);
        }

        _listView.EndUpdate();
    }

    private void ClearCaptures(bool disposeImages)
    {
        _preview.Image = null;
        _listView.Items.Clear();

        if (disposeImages)
        {
            foreach (Bitmap bitmap in _captures)
            {
                bitmap.Dispose();
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
    /// 將目前紅框比例套用到暫存區每一張圖（只改記憶體，不存檔）。
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
            var cropped = new List<Bitmap>(_captures.Count);
            foreach (Bitmap source in _captures)
            {
                cropped.Add(ImageCropService.CropByNormalized(source, ratio));
            }

            int selected = Math.Max(0, GetSelectedIndex());
            _preview.Image = null;
            foreach (Bitmap source in _captures)
            {
                source.Dispose();
            }

            _captures.Clear();
            _captures.AddRange(cropped);
            _preview.ClearCrop();
            RebuildThumbnails();
            SelectIndex(Math.Min(selected, _captures.Count - 1));
            UpdateUiState();
            _statusLabel.Text = $"已將裁切套用到全部 {_captures.Count} 張（未存檔）。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"套用失敗：{ex.Message}", "錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 依目前紅框裁切「全部」暫存圖並複製。Windows 剪貼簿一次只能貼一張點陣圖，
    /// 因此會先清空暫存資料夾再放入本次全部檔案；若 Excel 已開啟則一次插入全部圖片。
    /// </summary>
    private void CopyCurrentCropToClipboard()
    {
        if (_captures.Count == 0)
        {
            _statusLabel.Text = "暫存區沒有截圖，無法複製。";
            return;
        }

        List<Bitmap>? generated = null;
        IReadOnlyList<Bitmap> toCopy = _captures;

        try
        {
            if (_preview.HasCrop)
            {
                RectangleF ratio = ImageCropService.ClampNormalized(_preview.NormalizedCrop);
                generated = [];
                foreach (Bitmap source in _captures)
                {
                    generated.Add(ImageCropService.CropByNormalized(source, ratio));
                }

                toCopy = generated;
            }

            _statusLabel.Text = MultiImageClipboardService.CopyAll(toCopy);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "複製失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (generated is not null)
            {
                foreach (Bitmap bitmap in generated)
                {
                    bitmap.Dispose();
                }
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

        Bitmap source = _captures[index];
        if (!_preview.HasCrop)
        {
            _cropInfoLabel.Text = $"原圖 {source.Width}×{source.Height}。在圖上拖曳以建立裁切框。";
            return;
        }

        RectangleF n = _preview.NormalizedCrop;
        Rectangle px = ImageCropService.ToPixelRectangle(source.Size, n);
        _cropInfoLabel.Text =
            $"比例 X={n.X:P1}  Y={n.Y:P1}  W={n.Width:P1}  H={n.Height:P1}　" +
            $"目前圖像素 ({px.X}, {px.Y}, {px.Width}×{px.Height})";
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
