# Screen Crop Automation

適用 Windows 10/11 的 C# WinForms 截圖裁切小工具：用全域快捷鍵連續擷取主螢幕，在預覽圖上拉一次紅框，即可對全部暫存圖套用相同裁切，或一次複製全部裁切圖。

## 需求

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 / 11
- 若要一次插入多張圖到試算表，請先開啟 Excel 並選好儲存格

## 建置與執行

```powershell
cd D:\Cursor\workspaces\screen-crop-automation
dotnet build
dotnet run
```

## 使用方式

1. 啟動程式後會自動註冊全域快捷鍵（預設 **F1**，可在工具列改為 F1–F12）。
2. 切到要擷取的畫面，按下快捷鍵。每按一次就會把主螢幕全畫面加入左側縮圖暫存。
3. 在中央預覽圖拖曳滑鼠拉出紅色裁切框，可拖曳框身移動，或拖曳八個控制點調整大小。
4. **全部套用**：把目前紅框比例套用到暫存區每一張圖（只改記憶體預覽，不存檔）。
5. **複製目前的裁剪 (Ctrl+C)**：依目前紅框裁切「全部」暫存圖並複製。若 Excel 已開啟，會從作用中儲存格向下一次插入全部圖片。每次複製會先清空內部暫存檔再寫入本次張數，不會留下上次多出來的舊圖。

Windows 系統剪貼簿的「圖片」格式一次只能放一張，所以若沒開 Excel 就 Ctrl+V，通常只會貼到第一張。請先開 Excel 再按複製。

## 專案結構

| 路徑 | 說明 |
| --- | --- |
| `Program.cs` | 進入點與 DPI 設定 |
| `MainForm.cs` | 主視窗、快捷鍵訊息、批次套用 |
| `Native/GlobalHotKey.cs` | `RegisterHotKey` / `WM_HOTKEY` |
| `Native/ActiveComObject.cs` | 取得正在執行的 Excel |
| `Services/ScreenCaptureService.cs` | `CopyFromScreen` 全螢幕擷取 |
| `Services/ImageCropService.cs` | 比例裁切 |
| `Services/PictureBoxImageMapper.cs` | Zoom 模式座標轉換 |
| `Services/MultiImageClipboardService.cs` | 清空後複製全部、插入 Excel |
| `Controls/CropPictureBox.cs` | 可拖曳紅框預覽 |

裁切座標會把 PictureBox 的 Zoom 黑邊納入計算，再換成原始影像像素，避免預覽縮放造成偏移。
