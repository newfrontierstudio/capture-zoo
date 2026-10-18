using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CaptureZoo;

internal sealed class EditorForm : Form
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // Button の DoubleClick は環境によって通知されないため、Windows メッセージを直接拾う。
    private sealed class EditorOptionButton : Button
    {
        public event EventHandler? OptionDoubleClick;

        protected override void WndProc(ref Message m)
        {
            const int WmLButtonDblClk = 0x0203;
            var isDoubleClick = m.Msg == WmLButtonDblClk;
            base.WndProc(ref m);
            if (isDoubleClick)
                OptionDoubleClick?.Invoke(this, EventArgs.Empty);
        }
    }

    // PictureBox の DoubleClick は環境差があるため、Windows メッセージで確実に拾う。
    private sealed class CaptureCanvas : PictureBox
    {
        public event EventHandler? CanvasDoubleClick;

        protected override void WndProc(ref Message m)
        {
            const int WmLButtonDblClk = 0x0203;
            var isDoubleClick = m.Msg == WmLButtonDblClk;
            base.WndProc(ref m);
            if (isDoubleClick)
                CanvasDoubleClick?.Invoke(this, EventArgs.Empty);
        }
    }

    private const int SwatchSize = 30;
    private const int HistoryLimit = 20;

    // 表示倍率を保ち、キャプチャ画像を歪めない。
    private readonly CaptureCanvas _canvas = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
    private readonly Bitmap _image;
    private readonly EditorPreferences _preferences;
    private readonly List<Button> _swatchButtons = new();
    private readonly List<Button> _brushSizeButtons = new();
    private readonly Panel _toolbarHost;
    private readonly FlowLayoutPanel _toolbar;
    private Color _penColor;
    private int _penWidth;
    private Point? _last;
    private readonly List<Bitmap> _undoHistory = new();
    private readonly List<Bitmap> _redoHistory = new();
    private bool _cancelled;

    public Image? FinalImage { get; private set; }

    public EditorForm(Bitmap image)
    {
        _image = new Bitmap(image);
        _preferences = EditorPreferences.Load();
        _penColor = Color.FromArgb(_preferences.PaletteColors[_preferences.SelectedPaletteIndex]);
        _penWidth = _preferences.BrushSizes[_preferences.SelectedBrushSizeIndex];
        Text = "CaptureZoo Ver."+Application.ProductVersion.Split('+')[0];
        Icon = AppIcon.Load();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        BackColor = Color.FromArgb(30, 31, 36);
        Font = new Font("Arial", 9F);
        KeyPreview = true;

        _toolbarHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = Color.FromArgb(40, 42, 49)
        };
        SizeToCapturedImage();

        _toolbar = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        for (var index = 0; index < _preferences.PaletteColors.Count; index++)
        {
            var currentIndex = index;
            var swatchColor = Color.FromArgb(_preferences.PaletteColors[index]);
            var swatch = new EditorOptionButton
            {
                Width = SwatchSize,
                Height = SwatchSize,
                BackColor = swatchColor,
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Margin = new Padding(0, 0, 4, 0),
                Tag = index,
                AccessibleName = $"色 {index + 1}"
            };
            swatch.FlatAppearance.BorderColor = Color.White;
            swatch.FlatAppearance.BorderSize = 0;
            swatch.Click += (_, _) => SelectColor(currentIndex);
            swatch.OptionDoubleClick += (_, _) => EditColor(currentIndex);
            _swatchButtons.Add(swatch);
            _toolbar.Controls.Add(swatch);
        }
        HighlightSelected();

        var brushSizeHost = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(16, 0, 0, 0)
        };
        for (var index = 0; index < _preferences.BrushSizes.Count; index++)
        {
            var currentIndex = index;
            var size = _preferences.BrushSizes[index];
            var button = new EditorOptionButton
            {
                Width = 48,
                Height = SwatchSize,
                Text = size.ToString(),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(70, 73, 82),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Margin = new Padding(0, 0, 4, 0),
                Tag = index,
                AccessibleName = $"ブラシサイズ {size}"
            };
            button.FlatAppearance.BorderColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            // 押下時に選択することで、標準 Button の Click 通知に依存せず即時に切り替える。
            button.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) SelectBrushSize(currentIndex);
            };
            button.OptionDoubleClick += (_, _) => EditBrushSize(currentIndex);
            _brushSizeButtons.Add(button);
            brushSizeHost.Controls.Add(button);
        }
        _toolbar.Controls.Add(brushSizeHost);
        HighlightSelectedBrushSize();

        _toolbarHost.Controls.Add(_toolbar);

        Controls.Add(_canvas);
        Controls.Add(_toolbarHost);
        _canvas.Image = _image;
        _canvas.MouseDown += CanvasMouseDown;
        _canvas.MouseMove += CanvasMouseMove;
        _canvas.MouseUp += (_, _) => _last = null;
        _canvas.CanvasDoubleClick += (_, _) => CopyAndClose();

        _toolbarHost.Resize += (_, _) => CenterToolbar();
        _toolbar.SizeChanged += (_, _) => CenterToolbar();
        Load += (_, _) => CenterToolbar();
    }

    private void SelectColor(int index)
    {
        _penColor = Color.FromArgb(_preferences.PaletteColors[index]);
        _preferences.SelectedPaletteIndex = index;
        HighlightSelected();
        _preferences.Save();
    }

    private void EditColor(int index)
    {
        using var dialog = new ColorDialog { Color = Color.FromArgb(_preferences.PaletteColors[index]), FullOpen = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _preferences.PaletteColors[index] = dialog.Color.ToArgb();
        _swatchButtons[index].BackColor = dialog.Color;
        SelectColor(index);
    }

    private void SelectBrushSize(int index)
    {
        _penWidth = _preferences.BrushSizes[index];
        _preferences.SelectedBrushSizeIndex = index;
        HighlightSelectedBrushSize();
        _preferences.Save();
    }

    private void EditBrushSize(int index)
    {
        var selected = PromptBrushSize(_preferences.BrushSizes[index]);
        if (selected is null) return;
        _preferences.BrushSizes[index] = selected.Value;
        _brushSizeButtons[index].Text = selected.Value.ToString();
        _brushSizeButtons[index].AccessibleName = $"ブラシサイズ {selected.Value}";
        SelectBrushSize(index);
    }

    private int? PromptBrushSize(int currentSize)
    {
        using var dialog = new Form { Text = "ブラシサイズ", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(300, 105), MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false };
        var input = new NumericUpDown { Minimum = 1, Maximum = 100, Value = currentSize, Location = new Point(18, 18), Width = 264 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(70, 61), Width = 80, Height = 31 };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Location = new Point(158, 61), Width = 100, Height = 31 };
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.Controls.AddRange([input, ok, cancel]);
        return dialog.ShowDialog(this) == DialogResult.OK ? (int)input.Value : null;
    }

    private void HighlightSelected()
    {
        foreach (var swatch in _swatchButtons)
        {
            var isSelected = swatch.BackColor == _penColor;
            swatch.FlatAppearance.BorderSize = isSelected ? 2 : 0;
        }
    }

    private void HighlightSelectedBrushSize()
    {
        foreach (var button in _brushSizeButtons)
            button.FlatAppearance.BorderSize = button.Text == _penWidth.ToString() ? 2 : 0;
    }

    private void SizeToCapturedImage()
    {
        // キャンバスの表示領域を画像と同じ縦横比にして、Zoom 時の黒い余白をなくす。
        var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        var maxCanvasWidth = Math.Max(1, workArea.Width - 80);
        var maxCanvasHeight = Math.Max(1, workArea.Height - _toolbarHost.Height - 100);
        var scale = Math.Min(1F, Math.Min((float)maxCanvasWidth / _image.Width, (float)maxCanvasHeight / _image.Height));
        var canvasWidth = Math.Max(1, (int)Math.Floor(_image.Width * scale));
        var canvasHeight = Math.Max(1, (int)Math.Floor(_image.Height * scale));
        ClientSize = new Size(canvasWidth, canvasHeight + _toolbarHost.Height);
    }

    private void CenterToolbar()
    {
        _toolbar.Location = new Point(
            Math.Max(0, (_toolbarHost.Width - _toolbar.Width) / 2),
            Math.Max(0, (_toolbarHost.Height - _toolbar.Height) / 2));
    }

    protected override void WndProc(ref Message m)
    {
        const int WmSizing = 0x0214;
        if (m.Msg == WmSizing && m.LParam != IntPtr.Zero)
            KeepImageAspectRatio(m.WParam, m.LParam);
        base.WndProc(ref m);
    }

    private void KeepImageAspectRatio(IntPtr edge, IntPtr rectPointer)
    {
        const int Top = 3;
        const int Bottom = 6;
        var rect = Marshal.PtrToStructure<NativeRect>(rectPointer);
        var frameWidth = Width - ClientSize.Width;
        var frameHeight = Height - ClientSize.Height;
        var imageRatio = (float)_image.Width / _image.Height;
        var edgeValue = edge.ToInt32();

        if (edgeValue is Top or Bottom)
        {
            var canvasHeight = Math.Max(1, rect.Bottom - rect.Top - frameHeight - _toolbarHost.Height);
            var outerWidth = Math.Max(frameWidth + 1, (int)Math.Round(canvasHeight * imageRatio) + frameWidth);
            rect.Right = rect.Left + outerWidth;
        }
        else
        {
            var canvasWidth = Math.Max(1, rect.Right - rect.Left - frameWidth);
            var outerHeight = Math.Max(frameHeight + _toolbarHost.Height + 1, (int)Math.Round(canvasWidth / imageRatio) + _toolbarHost.Height + frameHeight);
            rect.Bottom = rect.Top + outerHeight;
        }
        Marshal.StructureToPtr(rect, rectPointer, false);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z))
        {
            RestoreUndo();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
        {
            RestoreRedo();
            return true;
        }
        if (keyData == Keys.Escape)
        {
            // 何もコピーせずにキャンセル終了。
            _cancelled = true;
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // フォーカスを持つ子コントロールが Ctrl+Z を入力キーとして扱っても取り消せるようにする。
        if (e.Control && e.KeyCode == Keys.Z)
        {
            if (e.Shift) RestoreRedo();
            else RestoreUndo();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (e.Control && e.KeyCode == Keys.Y)
        {
            RestoreRedo();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }

    internal void CancelCapture(){_cancelled=true;DialogResult=DialogResult.Cancel;Close();}

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Xボタン・Alt+F4など、Esc以外の方法で閉じられた場合はここでコピーを確定する。
        if (!_cancelled)
        {
            FinalImage ??= new Bitmap(_image);
            DialogResult = DialogResult.OK;
        }
        base.OnFormClosing(e);
    }

    private void CopyAndClose()
    {
        if (_cancelled || IsDisposed) return;
        _last = null;
        FinalImage = new Bitmap(_image);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CanvasMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        PushHistory(_undoHistory, new Bitmap(_image));
        ClearHistory(_redoHistory);
        _last = ImagePoint(e.Location);
    }

    private void CanvasMouseMove(object? sender, MouseEventArgs e)
    {
        if (_last is null || e.Button != MouseButtons.Left) return;
        var next = ImagePoint(e.Location);
        using var graphics = Graphics.FromImage(_image);
        using var pen = new Pen(_penColor, _penWidth) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        graphics.DrawLine(pen, _last.Value, next);
        _last = next;
        _canvas.Invalidate();
    }

    private Point ImagePoint(Point point)
    {
        var box = _canvas.ClientRectangle;
        var scale = Math.Min((float)box.Width / _image.Width, (float)box.Height / _image.Height);
        var width = _image.Width * scale;
        var height = _image.Height * scale;
        var left = (box.Width - width) / 2;
        var top = (box.Height - height) / 2;
        return new Point(
            Math.Clamp((int)((point.X - left) / scale), 0, _image.Width - 1),
            Math.Clamp((int)((point.Y - top) / scale), 0, _image.Height - 1));
    }

    private void RestoreUndo()
    {
        if (_undoHistory.Count == 0) return;
        PushHistory(_redoHistory, new Bitmap(_image));
        RestoreLast(_undoHistory);
    }

    private void RestoreRedo()
    {
        if (_redoHistory.Count == 0) return;
        PushHistory(_undoHistory, new Bitmap(_image));
        RestoreLast(_redoHistory);
    }

    private void RestoreLast(List<Bitmap> history)
    {
        var lastIndex = history.Count - 1;
        var snapshot = history[lastIndex];
        history.RemoveAt(lastIndex);
        using var graphics = Graphics.FromImage(_image);
        graphics.DrawImageUnscaled(snapshot, 0, 0);
        snapshot.Dispose();
        _canvas.Invalidate();
    }

    private static void PushHistory(List<Bitmap> history, Bitmap snapshot)
    {
        history.Add(snapshot);
        if (history.Count <= HistoryLimit) return;
        history[0].Dispose();
        history.RemoveAt(0);
    }

    private static void ClearHistory(List<Bitmap> history)
    {
        foreach (var snapshot in history) snapshot.Dispose();
        history.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearHistory(_undoHistory);
            ClearHistory(_redoHistory);
            _image.Dispose();
        }
        base.Dispose(disposing);
    }
}
