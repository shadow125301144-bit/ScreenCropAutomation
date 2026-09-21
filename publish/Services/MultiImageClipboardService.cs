using System.Collections.Specialized;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ScreenCropAutomation.Native;

namespace ScreenCropAutomation.Services;

/// <summary>
/// 將多張裁切圖一次複製：先清空暫存資料夾再寫入，避免上次 6 張、這次 4 張時殘留舊檔。
/// 若 Excel 已開啟，會在作用中儲存格向下插入全部圖片。
/// </summary>
internal static class MultiImageClipboardService
{
    public static string ClipboardFolder { get; } = Path.Combine(
        Path.GetTempPath(),
        "ScreenCropAutomation",
        "clipboard");

    /// <summary>
    /// 複製所有影像。回傳狀態說明文字。
    /// </summary>
    public static string CopyAll(IReadOnlyList<Bitmap> images)
    {
        if (images.Count == 0)
        {
            throw new InvalidOperationException("沒有可複製的圖片。");
        }

        ResetFolder(ClipboardFolder);

        var paths = new StringCollection();
        for (int i = 0; i < images.Count; i++)
        {
            string path = Path.Combine(ClipboardFolder, $"crop_{(i + 1):00}.png");
            images[i].Save(path, ImageFormat.Png);
            paths.Add(path);
        }

        var data = new DataObject();
        data.SetFileDropList(paths);
        data.SetImage(images[0]);
        Clipboard.SetDataObject(data, copy: true);

        if (TryInsertIntoExcel(paths, out string excelMessage))
        {
            return $"已複製 {images.Count} 張裁切圖，並{excelMessage}";
        }

        if (!string.IsNullOrWhiteSpace(excelMessage))
        {
            return $"已複製 {images.Count} 張裁切圖。{excelMessage}";
        }

        return $"已複製 {images.Count} 張裁切圖到剪貼簿。請先開啟 Excel 並選取儲存格後再按複製，即可一次插入全部圖片。";
    }

    public static void CleanupFolder()
    {
        try
        {
            if (Directory.Exists(ClipboardFolder))
            {
                Directory.Delete(ClipboardFolder, recursive: true);
            }
        }
        catch
        {
            // 暫存檔可能被 Excel 暫時鎖定，關閉時失敗可忽略。
        }
    }

    private static void ResetFolder(string folder)
    {
        Directory.CreateDirectory(folder);

        foreach (string file in Directory.GetFiles(folder))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // 略過被 Excel 暫時鎖定的檔案。
            }
        }
    }

    private static bool TryInsertIntoExcel(StringCollection paths, out string message)
    {
        object? com = ActiveComObject.TryGet("Excel.Application");
        if (com is null)
        {
            message = string.Empty;
            return false;
        }

        object? sheet = null;
        object? cell = null;
        var pictures = new List<object>();

        try
        {
            dynamic excel = com;
            sheet = excel.ActiveSheet;
            cell = excel.ActiveCell;
            if (sheet is null || cell is null)
            {
                message = string.Empty;
                return false;
            }

            dynamic activeSheet = sheet;
            dynamic activeCell = cell;
            double left = Convert.ToDouble(activeCell.Left);
            double top = Convert.ToDouble(activeCell.Top);
            const int msoFalse = 0;
            const int msoTrue = -1;

            foreach (string? path in paths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                using var bmp = new Bitmap(path);
                float dpiX = bmp.HorizontalResolution <= 0 ? 96f : bmp.HorizontalResolution;
                float dpiY = bmp.VerticalResolution <= 0 ? 96f : bmp.VerticalResolution;
                float width = bmp.Width * 72f / dpiX;
                float height = bmp.Height * 72f / dpiY;

                object pic = activeSheet.Shapes.AddPicture(
                    path,
                    msoFalse,
                    msoTrue,
                    left,
                    top,
                    width,
                    height);
                pictures.Add(pic);
                top += height + 6;
            }

            excel.Visible = true;
            message = $"插入 {pictures.Count} 張到 Excel 作用中工作表。";
            return pictures.Count > 0;
        }
        catch (Exception ex)
        {
            message = $"Excel 插入失敗（{ex.Message}），但仍已複製檔案到剪貼簿。";
            return false;
        }
        finally
        {
            foreach (object pic in pictures)
            {
                TryRelease(pic);
            }

            TryRelease(cell);
            TryRelease(sheet);
            TryRelease(com);
        }
    }

    private static void TryRelease(object? com)
    {
        if (com is null)
        {
            return;
        }

        try
        {
            Marshal.ReleaseComObject(com);
        }
        catch
        {
            // ignore
        }
    }
}
