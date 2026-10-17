using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CaptureZoo;

/// <summary>
/// メインウィンドウを持たず、タスクトレイにだけ常駐するアプリケーションコンテキスト。
/// F10のグローバルホットキー、またはトレイアイコンのメニュー/ダブルクリックから
/// これまでと同じキャプチャ→編集→クリップボードコピーのフローを起動する。
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const int HotkeyId = 1;
    private const string AutoStartValueName = "CaptureZoo";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly ToolStripMenuItem _autoStartItem;
    private bool _capturing;
    private Keys _captureKey = Keys.F10;
    private readonly ToolStripMenuItem _captureItem;
    private readonly ToolStripMenuItem _hotkeyMenu;
    private readonly Icon _appIcon = AppIcon.Load();
    private static readonly string HotkeySettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CaptureZoo", "hotkey.txt");
    private readonly Control _dispatcher = new();

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();

        try
        {
            if (File.Exists(HotkeySettingsPath) && Enum.TryParse<Keys>(File.ReadAllText(HotkeySettingsPath).Trim(), out var savedKey)
                && savedKey >= Keys.F1 && savedKey <= Keys.F12) _captureKey = savedKey;
        }
        catch (Exception error) { AppLog.Error(error); }
        _captureItem = new ToolStripMenuItem($"今すぐキャプチャ ({_captureKey})");
        var captureItem = _captureItem;
        _hotkeyMenu = new ToolStripMenuItem("ショートカットキー");
        for (var number = 1; number <= 12; number++)
        {
            var key = (Keys)((int)Keys.F1 + number - 1);
            var item = new ToolStripMenuItem(key.ToString()) { Tag = key, Checked = key == _captureKey };
            item.Click += (_, _) => ChangeHotkey(key);
            _hotkeyMenu.DropDownItems.Add(item);
        }
        captureItem.Click += (_, _) => RunCaptureFlow();

        _autoStartItem = new ToolStripMenuItem("Windows起動時に自動起動")
        {
            CheckOnClick = true,
            Checked = IsAutoStartEnabled()
        };
        _autoStartItem.CheckedChanged += (_, _) => SetAutoStart(_autoStartItem.Checked);

        var exitItem = new ToolStripMenuItem("終了");
        exitItem.Click += (_, _) => ExitThread();

        menu.Items.Add(captureItem);
        menu.Items.Add(_hotkeyMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = $"CaptureZoo ({_captureKey}でキャプチャ)",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => RunCaptureFlow();

        _hotkeyWindow = new HotkeyWindow(HotkeyId, _captureKey);
        _ = _dispatcher.Handle;
        _hotkeyWindow.HotkeyPressed += () =>
        {
            AppLog.Info($"{_captureKey} received.");
            _dispatcher.BeginInvoke((Action)RunCaptureFlow);
        };

        AppLog.Info($"Tray daemon started. {_captureKey} registered as global hotkey.");
    }

    private void ChangeHotkey(Keys key)
    {
        if (key == _captureKey) return;
        if (!_hotkeyWindow.TryChangeKey(key))
        {
            MessageBox.Show($"{key} は他のアプリで使用されています。別のキーを選んでください。",
                "CaptureZoo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var previous = _captureKey;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HotkeySettingsPath)!);
            File.WriteAllText(HotkeySettingsPath, key.ToString());
        }
        catch (Exception error)
        {
            _hotkeyWindow.TryChangeKey(previous);
            AppLog.Error(error);
            MessageBox.Show("ショートカット設定を保存できませんでした。", "CaptureZoo");
            return;
        }
        _captureKey = key;
        _captureItem.Text = $"今すぐキャプチャ ({key})";
        _trayIcon.Text = $"CaptureZoo ({key}でキャプチャ)";
        foreach (ToolStripMenuItem item in _hotkeyMenu.DropDownItems) item.Checked = (Keys)item.Tag! == key;
        AppLog.Info($"Capture hotkey changed to {key}.");
    }

    private void RunCaptureFlow()
    {
        if (_capturing) return; // 連打やダブルクリックでの多重起動を防ぐ
        _capturing = true;
        AppLog.Info("Capture flow started.");
        try
        {
            using var desktop = DesktopSnapshot.Capture();
            using var overlay = new CaptureOverlay(desktop);
            var result = overlay.ShowDialog();
            if (result != DialogResult.OK || overlay.SelectedImage is null) return;

            using var selected = HdrDesktopCapture.TryCapture(overlay.SelectedBoundsInVirtualScreen, out var hdrImage)
                ? hdrImage
                : overlay.SelectedImage;
            using var editor = new EditorForm(selected);
            if (editor.ShowDialog() == DialogResult.OK) ClipboardWriter.CopyForSharing(editor.FinalImage!);
        }
        catch (Exception error)
        {
            AppLog.Error(error);
            MessageBox.Show("キャプチャに失敗しました。CaptureZoo.log を送ってください。", "CaptureZoo");
        }
        finally
        {
            _capturing = false;
            AppLog.Info($"Capture flow finished; waiting for {_captureKey}.");
        }
    }

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(AutoStartValueName) is not null;
    }

    private static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null) return;

        if (enabled)
        {
            key.SetValue(AutoStartValueName, $"\"{Application.ExecutablePath}\"");
        }
        else
        {
            key.DeleteValue(AutoStartValueName, throwOnMissingValue: false);
        }
    }

    protected override void ExitThreadCore()
    {
        _hotkeyWindow.Dispose();
        _dispatcher.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _appIcon.Dispose();
        base.ExitThreadCore();
    }
}
