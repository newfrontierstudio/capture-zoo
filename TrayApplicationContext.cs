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
    private readonly HotkeyWindow ocrHotkey;
    private Keys ocrKey=Keys.F10;
    private readonly ToolStripMenuItem ocrShortcutMenu;
    private readonly ToolStripMenuItem ocrItem;
    private static string OcrKeyPath=>Path.Combine(Path.GetDirectoryName(HotkeySettingsPath)!,"ocr-hotkey.txt");
    private CancellationTokenSource? ocrCancellation;
    private readonly List<EditorForm> _editors = new();
    private Form? _activeCaptureWindow;
    private Keys _captureKey = Keys.F9;
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
        try{if(File.Exists(OcrKeyPath)&&Enum.TryParse<Keys>(File.ReadAllText(OcrKeyPath).Trim(),out var savedOcr)&&savedOcr>=Keys.F1&&savedOcr<=Keys.F12)ocrKey=savedOcr;}catch(Exception error){AppLog.Error(error);}
        if(_captureKey==ocrKey)_captureKey=ocrKey==Keys.F9?Keys.F10:Keys.F9;
        _captureItem = new ToolStripMenuItem(UiLanguage.F("今すぐキャプチャ ({0})",_captureKey));
        _captureItem.Text=UiLanguage.F("今すぐキャプチャ ({0})",_captureKey);
        var captureItem = _captureItem;
        _hotkeyMenu = new ToolStripMenuItem(UiLanguage.T("キャプチャのショートカット"));
        for (var number = 1; number <= 12; number++)
        {
            var key = (Keys)((int)Keys.F1 + number - 1);
            var item = new ToolStripMenuItem(key.ToString()) { Tag = key, Checked = key == _captureKey, Enabled = key != ocrKey };
            item.Click += (_, _) => ChangeHotkey(key);
            _hotkeyMenu.DropDownItems.Add(item);
        }
        captureItem.Click += (_, _) => RunCaptureFlow();

        _autoStartItem = new ToolStripMenuItem(UiLanguage.T("Windows起動時に自動起動"))
        {
            CheckOnClick = true,
            Checked = IsAutoStartEnabled()
        };
        _autoStartItem.CheckedChanged += (_, _) => SetAutoStart(_autoStartItem.Checked);

        var exitItem = new ToolStripMenuItem(UiLanguage.T("終了"));
        exitItem.Click += (_, _) => ExitThread();

        menu.Items.Add(captureItem);
        menu.Items.Add(_hotkeyMenu);
        ocrItem=new ToolStripMenuItem(UiLanguage.F("文字を読み取る ({0})",ocrKey));
        ocrShortcutMenu=new ToolStripMenuItem(UiLanguage.T("OCRのショートカット"));
        for(int number=1;number<=12;number++){var key=(Keys)((int)Keys.F1+number-1);var item=new ToolStripMenuItem(key.ToString()){Tag=key,Checked=key==ocrKey,Enabled=key!=_captureKey};item.Click+=(_,_)=>ChangeOcrKey(key);ocrShortcutMenu.DropDownItems.Add(item);}
        menu.Items.Add(ocrShortcutMenu);
        ocrItem.Click+=(_,_)=>RunOcrFlow();menu.Items.Add(ocrItem);
        menu.Items.Add(UiLanguage.T("キャプチャを中止して待機に戻る"), null, (_, _) => CancelCapture());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = UiLanguage.F("CaptureZoo ({0}でキャプチャ)",_captureKey),
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => RunCaptureFlow();
        var languageMenu=new ToolStripMenuItem("Language / 言語");menu.Items.Insert(menu.Items.Count-1,languageMenu);
        foreach(var value in new[]{"auto","ja","en"}){string choice=value;var item=new ToolStripMenuItem(choice=="ja"?"日本語":choice=="en"?"English":UiLanguage.T("自動（Windows）")){Tag=choice,Checked=UiLanguage.Selection==choice};languageMenu.DropDownItems.Add(item);
            item.Click+=(_,_)=>{try{UiLanguage.Set(choice);foreach(ToolStripItem entry in menu.Items)UiLanguage.Apply(entry);_captureItem.Text=UiLanguage.F("今すぐキャプチャ ({0})",_captureKey);ocrItem.Text=UiLanguage.F("文字を読み取る ({0})",ocrKey);_trayIcon.Text=UiLanguage.F("CaptureZoo ({0}でキャプチャ)",_captureKey);foreach(ToolStripMenuItem option in languageMenu.DropDownItems)option.Checked=(string)option.Tag! == choice;foreach(var editor in _editors)editor.ApplyLanguage();}catch(Exception error){MessageBox.Show(error.Message,"CaptureZoo");}};
        }


        _hotkeyWindow = new HotkeyWindow(HotkeyId, _captureKey);
        _ = _dispatcher.Handle;
        ocrHotkey=new HotkeyWindow(10,ocrKey);
        ocrHotkey.HotkeyPressed+=()=>_dispatcher.BeginInvoke((Action)RunOcrFlow);
        AppLog.Info($"OCR {ocrKey} registration: {ocrHotkey.IsRegistered}.");
        if(!ocrHotkey.IsRegistered)_trayIcon.ShowBalloonTip(4000,"CaptureZoo",UiLanguage.F("{0}を登録できません。アイコンの右クリックで別のキーを選んでください。",ocrKey),ToolTipIcon.Warning);
        _hotkeyWindow.HotkeyPressed += () =>
        {
            AppLog.Info($"{_captureKey} received.");
            _dispatcher.BeginInvoke((Action)RunCaptureFlow);
        };

        if(System.Diagnostics.Process.GetProcessesByName("CaptureZoo").Length > 1) _dispatcher.BeginInvoke((Action)RunCaptureFlow);

        AppLog.Info($"Tray daemon started. {_captureKey} registration: {_hotkeyWindow.IsRegistered}.");
        if(!_hotkeyWindow.IsRegistered && System.Diagnostics.Process.GetProcessesByName("CaptureZoo").Length <= 1)_trayIcon.ShowBalloonTip(5000,"CaptureZoo",UiLanguage.F("{0}を登録できません。アイコンの右クリックで別のキーを選んでください。",_captureKey),ToolTipIcon.Warning);
    }

    private void ChangeHotkey(Keys key)
    {
        if(key==ocrKey)return;
        if (key == _captureKey && _hotkeyWindow.IsRegistered) return;
        if (!_hotkeyWindow.TryChangeKey(key))
        {
            MessageBox.Show(UiLanguage.F("{0} は他のアプリで使用されています。別のキーを選んでください。",key),
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
            MessageBox.Show(UiLanguage.T("ショートカット設定を保存できませんでした。"), "CaptureZoo");
            return;
        }
        _captureKey = key;
        _captureItem.Text = UiLanguage.F("今すぐキャプチャ ({0})",key);
        _trayIcon.Text = UiLanguage.F("CaptureZoo ({0}でキャプチャ)",key);
        foreach (ToolStripMenuItem item in _hotkeyMenu.DropDownItems) item.Checked = (Keys)item.Tag! == key;
        RefreshShortcutMenus();
        AppLog.Info($"Capture hotkey changed to {key}.");
    }

    private void RefreshShortcutMenus()
    {
        foreach(ToolStripMenuItem item in _hotkeyMenu.DropDownItems){item.Checked=(Keys)item.Tag! == _captureKey;item.Enabled=(Keys)item.Tag! != ocrKey;}
        foreach(ToolStripMenuItem item in ocrShortcutMenu.DropDownItems){item.Checked=(Keys)item.Tag! == ocrKey;item.Enabled=(Keys)item.Tag! != _captureKey;}
        ocrItem.Text=UiLanguage.F("文字を読み取る ({0})",ocrKey);
    }
    private void ChangeOcrKey(Keys key)
    {
        if(key==_captureKey||(key==ocrKey&&ocrHotkey.IsRegistered))return;
        if(!ocrHotkey.TryChangeKey(key)){MessageBox.Show(UiLanguage.F("{0} は他のアプリで使用されています。別のキーを選んでください。",key),"CaptureZoo");return;}
        try{Directory.CreateDirectory(Path.GetDirectoryName(OcrKeyPath)!);File.WriteAllText(OcrKeyPath,key.ToString());}
        catch(Exception error){ocrHotkey.TryChangeKey(ocrKey);AppLog.Error(error);MessageBox.Show(UiLanguage.T("ショートカット設定を保存できませんでした。"),"CaptureZoo");return;}
        ocrKey=key;RefreshShortcutMenus();
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
            MessageBox.Show(UiLanguage.T("キャプチャに失敗しました。CaptureZoo.log を送ってください。"), "CaptureZoo");
        }
        finally
        {
            _activeCaptureWindow=null;
            _capturing = false;
            AppLog.Info($"Capture flow finished; waiting for {_captureKey}.");
        }
    }

    private async void RunOcrFlow()
    {
        if(_capturing){PresentCaptureWindow(_activeCaptureWindow);return;}
        ocrCancellation?.Cancel();var request=new CancellationTokenSource();ocrCancellation=request;
        _capturing=true;
        AppLog.Info("OCR F10 selection started.");
        try{
            using var desktop=DesktopSnapshot.Capture();
            using var overlay=new CaptureOverlay(desktop);
            _activeCaptureWindow=overlay;
            if(overlay.ShowDialog()!=DialogResult.OK||overlay.SelectedImage==null)return;
            _activeCaptureWindow=null;
            using var image=new Bitmap(overlay.SelectedImage);
            _trayIcon.ShowBalloonTip(2000,"CaptureZoo",UiLanguage.T("文字を読み取っています…"),ToolTipIcon.Info);
            await OcrModelStore.EnsureAsync(null,request.Token);
            var result=await OcrService.ReadAsync(image,request.Token);
            request.Token.ThrowIfCancellationRequested();
            if(string.IsNullOrWhiteSpace(result.Text)){_trayIcon.ShowBalloonTip(2500,"CaptureZoo",UiLanguage.T("文字が見つかりませんでした。文字の範囲を大きめに選んでお試しください。"),ToolTipIcon.Info);return;}
            for(int attempt=0;;attempt++){try{Clipboard.SetText(result.Text);break;}catch(System.Runtime.InteropServices.ExternalException) when(attempt<3){await Task.Delay(150,request.Token);}}
            _trayIcon.ShowBalloonTip(2000,"CaptureZoo",UiLanguage.T("テキストをコピーしました。"),ToolTipIcon.Info);
        }catch(OperationCanceledException){}
        catch(Exception error){AppLog.Error(error);_trayIcon.ShowBalloonTip(3000,"CaptureZoo",UiLanguage.T("読み取りできませんでした。接続または画像を確認して、もう一度お試しください。"),ToolTipIcon.Warning);}
        finally{_activeCaptureWindow=null;_capturing=false;if(ocrCancellation==request)ocrCancellation=null;request.Dispose();}
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
        ocrCancellation?.Cancel();
        
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
        
        ocrHotkey.Dispose();
        _hotkeyWindow.Dispose();
        _dispatcher.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _appIcon.Dispose();
        base.ExitThreadCore();
    }
}
