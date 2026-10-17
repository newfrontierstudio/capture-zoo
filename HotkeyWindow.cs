using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CaptureZoo;

/// <summary>
/// 画面に見えないメッセージ専用ウィンドウ(HWND_MESSAGE)を1つ作り、
/// Win32 の RegisterHotKey でOSレベルのグローバルホットキーを受け取る。
/// 通常の Form を使わないのは、タスクバー/Alt+Tabに何も表示させたくないため。
/// </summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HWND_MESSAGE = -3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private int _id;

    public event Action? HotkeyPressed;

    /// <param name="id">このプロセス内で一意なホットキーID</param>
    /// <param name="key">割り当てるキー(例: Keys.F10)</param>
    /// <param name="modifiers">MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8 の組み合わせ。0なら単独キー。</param>
    public HotkeyWindow(int id, Keys key, uint modifiers = 0)
    {
        _id = id;
        CreateHandle(new CreateParams { Parent = (IntPtr)HWND_MESSAGE });

        if (!RegisterHotKey(Handle, _id, modifiers, (uint)key))
        {
            // よくある原因: 他のアプリがすでに同じキーを登録している。
            AppLog.Error(new InvalidOperationException(
                $"RegisterHotKey に失敗しました (Win32 error {Marshal.GetLastWin32Error()})。" +
                "他のアプリが同じキーを使っている可能性があります。"));
        }
    }

    public bool TryChangeKey(Keys key)
    {
        if (key < Keys.F1 || key > Keys.F12) return false;
        var nextId = _id == 1 ? 2 : 1;
        if (!RegisterHotKey(Handle, nextId, 0x4000, (uint)key)) return false;
        UnregisterHotKey(Handle, _id);
        _id = nextId;
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == _id)
        {
            HotkeyPressed?.Invoke();
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, _id);
        DestroyHandle();
    }
}
