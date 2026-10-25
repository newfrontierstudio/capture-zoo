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
    private readonly List<EditorForm> _editors = new();
    private Form? _activeCaptureWindow;
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
        menu.Items.Add("キャプチャを中止して待機に戻る", null, (_, _) => CancelCapture());
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

        if(System.Diagnostics.Process.GetProcessesByName("CaptureZoo").Length > 1) _dispatcher.BeginInvoke((Action)RunCaptureFlow);

        AppLog.Info($"Tray daemon started. {_captureKey} registration: {_hotkeyWindow.IsRegistered}.");
        if(!_hotkeyWindow.IsRegistered && System.Diagnostics.Process.GetProcessesByName("CaptureZoo").Length <= 1)_trayIcon.ShowBalloonTip(5000,"CaptureZoo",$"{_captureKey}を登録できません。アイコンの右クリックで別のキーを選んでください。",ToolTipIcon.Warning);
    }

    private void ChangeHotkey(Keys key)
    {
        if (key == _captureKey && _hotkeyWindow.IsRegistered) return;
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
        if (_capturing) { AppLog.Info("Capture already active; restoring its window."); PresentCaptureWindow(_activeCaptureWindow); return; }
        _capturing = true;
        AppLog.Info("Capture flow started.");
        try
        {
            Bitmap selected;
            using(var desktop = DesktopSnapshot.Capture())
            using (var overlay = new CaptureOverlay(desktop))
            {
                _activeCaptureWindow=overlay;
                var result=overlay.ShowDialog();
                AppLog.Info($"Selection closed: {result}.");
                if(result!=DialogResult.OK||overlay.SelectedImage is null)return;
                selected=new Bitmap(overlay.SelectedImage);
            }
            EditorForm createdEditor;
            using(selected)createdEditor=new EditorForm(selected);
            var editor=createdEditor;
            _editors.Add(editor);
            editor.FormClosed+=(_,_)=>{_editors.Remove(editor);editor.Dispose();};
            editor.Show();
            PresentCaptureWindow(editor);
        }
        catch (Exception error)
        {
            AppLog.Error(error);
            MessageBox.Show("キャプチャに失敗しました。CaptureZoo.log を送ってください。", "CaptureZoo");
        }
        finally
        {
            _activeCaptureWindow=null;
            _capturing = false;
            AppLog.Info($"Capture flow finished; waiting for {_captureKey}.");
        }
    }

    internal static bool PresentCaptureWindow(Form? form)
    {
        if(form is null||form.IsDisposed)return false;
        var dialog=form.OwnedForms.LastOrDefault(f=>f.Visible&&!f.IsDisposed)??form;
        if(dialog.WindowState==FormWindowState.Minimized)dialog.WindowState=FormWindowState.Normal;
        if(!Screen.AllScreens.Any(screen=>screen.WorkingArea.IntersectsWith(dialog.Bounds))){var area=Screen.PrimaryScreen!.WorkingArea;dialog.Location=new Point(area.Left+Math.Max(0,(area.Width-dialog.Width)/2),area.Top+Math.Max(0,(area.Height-dialog.Height)/2));}
        dialog.Show();dialog.BringToFront();dialog.Activate();return true;
    }

    private void CancelCapture()
    {
        if(_editors.LastOrDefault(e=>!e.IsDisposed) is EditorForm editor && !_capturing)editor.CancelCapture();
        else if(_activeCaptureWindow is {IsDisposed:false} form){form.DialogResult=DialogResult.Cancel;form.Close();}
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
        CancelCapture();
        foreach(var editor in _editors.ToArray())editor.CancelCapture();
        _hotkeyWindow.Dispose();
        _dispatcher.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _appIcon.Dispose();
        base.ExitThreadCore();
    }
}
