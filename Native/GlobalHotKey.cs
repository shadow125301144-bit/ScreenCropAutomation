namespace ScreenCropAutomation.Native;

/// <summary>
/// 以 <c>RegisterHotKey</c> 將快捷鍵綁定到指定視窗，並在 <c>WM_HOTKEY</c> 時觸發事件。
/// </summary>
internal sealed class GlobalHotKey : IDisposable
{
    public const int DefaultHotKeyId = 0xC001;

    private readonly IntPtr _windowHandle;
    private readonly int _id;
    private bool _registered;
    private bool _disposed;

    public GlobalHotKey(IntPtr windowHandle, int id = DefaultHotKeyId)
    {
        _windowHandle = windowHandle;
        _id = id;
    }

    /// <summary>目前成功註冊的按鍵（例如 F1）。</summary>
    public Keys CurrentKey { get; private set; } = Keys.F1;

    /// <summary>按下已註冊的全域快捷鍵時引發。</summary>
    public event EventHandler? Pressed;

    /// <summary>
    /// 註冊或改註冊快捷鍵。失敗時回傳 false，並寫入 <paramref name="errorMessage"/>。
    /// </summary>
    public bool TryRegister(Keys key, out string errorMessage)
    {
        Unregister();

        // fsModifiers = 0 表示不需 Ctrl/Alt/Shift；vk 為虛擬鍵碼。
        bool ok = NativeMethods.RegisterHotKey(_windowHandle, _id, 0, (uint)key);
        if (!ok)
        {
            int code = MarshalGetLastWin32Error();
            errorMessage = $"無法註冊全域快捷鍵 {key}（Win32 錯誤 {code}）。可能已被其他程式佔用。";
            return false;
        }

        _registered = true;
        CurrentKey = key;
        errorMessage = string.Empty;
        return true;
    }

    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_windowHandle, _id);
        _registered = false;
    }

    /// <summary>
    /// 由 Form.WndProc 轉送訊息。若為本熱鍵則處理並回傳 true。
    /// </summary>
    public bool ProcessMessage(ref Message message)
    {
        if (message.Msg != NativeMethods.WmHotKey)
        {
            return false;
        }

        if (message.WParam.ToInt32() != _id)
        {
            return false;
        }

        Pressed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Unregister();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static int MarshalGetLastWin32Error()
        => System.Runtime.InteropServices.Marshal.GetLastWin32Error();
}
