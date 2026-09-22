namespace ScreenCropAutomation;

/// <summary>
/// 應用程式進入點。
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                ReportFatal(ex);
            }
        };

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            ReportFatal(ex);
        }
    }

    /// <summary>
    /// 寫入錯誤 log 並跳出訊息，避免 WinExe 沒有主控台時錯誤一閃即逝。
    /// </summary>
    private static void ReportFatal(Exception exception)
    {
        try
        {
            // 寫在 exe 所在目錄（與執行檔同一層），不要放到使用者 AppData。
            string folder = Path.GetDirectoryName(Environment.ProcessPath)
                ?? AppContext.BaseDirectory;
            string path = Path.Combine(folder, "error.log");
            string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(path, text);
            MessageBox.Show(
                $"程式發生錯誤：{exception.Message}{Environment.NewLine}{Environment.NewLine}詳細內容已寫入：{path}",
                "Screen Crop Automation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            MessageBox.Show(exception.ToString(), "Screen Crop Automation", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
