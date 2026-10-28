using System.Drawing;
using System.Drawing.Drawing2D;
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
        internal Func<bool>? RenderGpu;
        bool printing,gpuPainted;
        public CaptureCanvas(){SetStyle(ControlStyles.OptimizedDoubleBuffer,false);}
        protected override void OnPaintBackground(PaintEventArgs e){if(printing||!gpuPainted)base.OnPaintBackground(e);}
        protected override void OnPaint(PaintEventArgs e){if(!printing&&RenderGpu?.Invoke()==true){gpuPainted=true;return;}gpuPainted=false;e.Graphics.Clear(BackColor);base.OnPaint(e);}

        protected override void WndProc(ref Message m)
        {
            if(m.Msg is 0x0317 or 0x0318){printing=true;try{base.WndProc(ref m);}finally{printing=false;}return;}
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
    private PointF? _last;
    private readonly SmoothStroke _smoothStroke=new();
    private readonly GpuCanvasRenderer _gpuRenderer=new();
    private readonly List<SmoothStroke.Segment> _strokeSegments=[];
    private long _gpuImageRevision;
    private int _gpuStrokeId;
    private Graphics? _strokeGraphics;
    private Pen? _strokePen;
    private readonly Action<Image> _initialCopy;
    private bool _initialCopyAttempted;
    private bool _strokeChanged;
    private readonly List<Bitmap> _undoHistory = new();
    private readonly List<Bitmap> _redoHistory = new();
    private bool _cancelled;
    private readonly ContextMenuStrip _canvasMenu=new();
    private bool _ocrSelecting;
    private Point _ocrOrigin,_ocrCurrent;
    private Rectangle? _ocrSelection;
    internal Action<Bitmap>? OcrRequestOverride;

    public Image? FinalImage { get; private set; }

    public EditorForm(Bitmap image, Action<Image>? initialCopy = null)
    {
        _image = new Bitmap(image);
        _initialCopy = initialCopy ?? ClipboardWriter.CopyForSharing;
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
                AccessibleName = UiLanguage.F("色 {0}",index+1)
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
                AccessibleName = UiLanguage.F("ブラシサイズ {0}",size)
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
        var ocr=new Button{Text="OCR",Width=54,Height=SwatchSize,ForeColor=Color.White,BackColor=Color.FromArgb(70,73,82),FlatStyle=FlatStyle.Flat,Margin=new Padding(12,0,0,0),AccessibleName=UiLanguage.T("文字を読み取る（OCR）")};ocr.Click+=(_,_)=>ReadText();_toolbar.Controls.Add(ocr);
        _canvasMenu.Items.Add(UiLanguage.T("文字を読み取る（OCR）"),null,(_,_)=>ReadText());_canvas.Disposed+=(_,_)=>_canvasMenu.Dispose();
        HighlightSelectedBrushSize();

        _toolbarHost.Controls.Add(_toolbar);

        Controls.Add(_canvas);
        Controls.Add(_toolbarHost);
        _canvas.Image = _image;
        _canvas.MouseDown += CanvasMouseDown;
        _canvas.MouseMove += CanvasMouseMove;
        _canvas.MouseUp += (sender, e) => { if(e.Button==MouseButtons.Right){FinishOcrSelection(e.Location);return;}if(e.Button==MouseButtons.Left)CanvasMouseMove(sender,e);EndStroke(); };
        _canvas.Paint += PaintStrokePreview;
        _canvas.Paint += PaintOcrSelection;
        _canvas.RenderGpu=()=>_gpuRenderer.Draw(_canvas.Handle,_canvas.ClientSize,_strokeChanged&&_undoHistory.Count>0?_undoHistory[^1]:_image,_gpuImageRevision,_gpuStrokeId,_strokeSegments,_smoothStroke.Preview,_penColor,_penWidth,_canvas.BackColor,_ocrSelection);
        _canvas.MouseCaptureChanged += (_, _) => { if (!_canvas.Capture){CancelOcrSelection();EndStroke();} };
        _canvas.CanvasDoubleClick += (_, _) => CopyAndClose();

        _toolbarHost.Resize += (_, _) => CenterToolbar();
        _toolbar.SizeChanged += (_, _) => CenterToolbar();
        Load += (_, _) => CenterToolbar();
    }

    internal void ApplyLanguage(){UiLanguage.Apply(this);for(int i=0;i<_swatchButtons.Count;i++)_swatchButtons[i].AccessibleName=UiLanguage.F("色 {0}",i+1);for(int i=0;i<_brushSizeButtons.Count;i++)_brushSizeButtons[i].AccessibleName=UiLanguage.F("ブラシサイズ {0}",_preferences.BrushSizes[i]);foreach(ToolStripItem item in _canvasMenu.Items)UiLanguage.Apply(item);foreach(Form owned in OwnedForms)UiLanguage.Apply(owned);}
    private void ReadText(Rectangle? bounds=null){EndStroke();using var image=bounds is {} region?_image.Clone(region,System.Drawing.Imaging.PixelFormat.Format32bppArgb):new Bitmap(_image);if(OcrRequestOverride!=null){OcrRequestOverride(image);return;}using var dialog=new OcrDialog(image);dialog.ShowDialog(this);}
    private void CancelOcrSelection(){if(!_ocrSelecting&&_ocrSelection==null)return;_ocrSelecting=false;_ocrSelection=null;_canvas.Invalidate();}
    private void FinishOcrSelection(Point end){if(!_ocrSelecting)return;var origin=_ocrOrigin;var region=OcrSelection.Bounds(origin,end,_canvas.ClientSize,_image.Size);bool drag=OcrSelection.Dragged(origin,end);CancelOcrSelection();_canvas.Capture=false;if(drag){if(region is {} rect&&rect.Width>=2&&rect.Height>=2)ReadText(rect);}else _canvasMenu.Show(_canvas,end);}
    private void PaintOcrSelection(object? sender,PaintEventArgs e){if(_ocrSelection is not {} rect)return;var box=_canvas.ClientRectangle;float scale=Math.Min((float)box.Width/_image.Width,(float)box.Height/_image.Height);var display=new RectangleF((box.Width-_image.Width*scale)/2+rect.Left*scale,(box.Height-_image.Height*scale)/2+rect.Top*scale,rect.Width*scale,rect.Height*scale);using var fill=new SolidBrush(Color.FromArgb(40,0,160,220));using var pen=new Pen(Color.FromArgb(0,140,210),1);e.Graphics.FillRectangle(fill,display);e.Graphics.DrawRectangle(pen,display.X,display.Y,display.Width,display.Height);}
    private void SelectColor(int index)
    {
        _penColor = Color.FromArgb(_preferences.PaletteColors[index]);
        _preferences.SelectedPaletteIndex = index;
        HighlightSelected();
        _preferences.Save();
    }

    private void EditColor(int index)
    {
        int original=_preferences.PaletteColors[index];Color originalPen=_penColor;
        bool committed=false;
        using var dialog = new ColorPickerDialog(Color.FromArgb(original));
        dialog.ColorChanged+=color=>{_swatchButtons[index].BackColor=color;if(_preferences.SelectedPaletteIndex==index)_penColor=color;HighlightSelected();};
        try{if(dialog.ShowDialog(this)!=DialogResult.OK)return;_preferences.PaletteColors[index]=dialog.SelectedColor.ToArgb();_swatchButtons[index].BackColor=dialog.SelectedColor;SelectColor(index);committed=true;}
        finally{if(!committed){_swatchButtons[index].BackColor=Color.FromArgb(original);_penColor=originalPen;HighlightSelected();}}
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
        _brushSizeButtons[index].AccessibleName = UiLanguage.F("ブラシサイズ {0}",selected.Value);
        SelectBrushSize(index);
    }

    private int? PromptBrushSize(int currentSize)
    {
        using var dialog = new Form { Text = UiLanguage.T("ブラシサイズ"), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(300, 105), MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false };
        var input = new NumericUpDown { Minimum = 1, Maximum = 100, Value = currentSize, Location = new Point(18, 18), Width = 264 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(70, 61), Width = 80, Height = 31 };
        var cancel = new Button { Text = UiLanguage.T("キャンセル"), DialogResult = DialogResult.Cancel, Location = new Point(158, 61), Width = 100, Height = 31 };
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
        if(keyData==Keys.Escape&&_ocrSelecting){CancelOcrSelection();_canvas.Capture=false;return true;}
        if(keyData==(Keys.Control|Keys.Shift|Keys.O)){CancelOcrSelection();_canvas.Capture=false;ReadText();return true;}
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
            // 編集結果は追加コピーしない。開いた時点の画像はコピー済み。
            FinishStroke(false);
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

    internal void CancelCapture(){FinishStroke(false);_cancelled=true;DialogResult=DialogResult.Cancel;Close();}
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing must not replace another editor's clipboard image.
        FinishStroke(false);
        base.OnFormClosing(e);
    }
    private void CopyAndClose()
    {
        if (_cancelled || IsDisposed) return;
        FinishStroke(false);CopyCurrentImage();Close();
    }

    private void CanvasMouseDown(object? sender, MouseEventArgs e)
    {
        if(e.Button==MouseButtons.Right){EndStroke();_ocrOrigin=_ocrCurrent=e.Location;_ocrSelecting=true;_ocrSelection=null;_canvas.Capture=true;return;}
        if (e.Button != MouseButtons.Left) return;
        CancelOcrSelection();EndStroke();
        _last = ImagePoint(e.Location);
        var box=_canvas.ClientRectangle;float scale=Math.Max(.0001f,Math.Min((float)box.Width/_image.Width,(float)box.Height/_image.Height));
        _smoothStroke.ApproximationTolerance=2.25f/scale;
        _smoothStroke.Start(_last.Value);_strokeSegments.Clear();_gpuStrokeId++;
        _canvas.Capture=true;
    }

    private void CanvasMouseMove(object? sender, MouseEventArgs e)
    {
        if(_ocrSelecting){_ocrCurrent=e.Location;_ocrSelection=OcrSelection.Bounds(_ocrOrigin,_ocrCurrent,_canvas.ClientSize,_image.Size);_canvas.Invalidate();return;}
        if (_last is null || e.Button != MouseButtons.Left) return;
        var next = ImagePoint(e.Location);
        if(next==_last.Value)return;
        if(_strokeGraphics==null){PushHistory(_undoHistory,new Bitmap(_image));ClearHistory(_redoHistory);_strokeGraphics=Graphics.FromImage(_image);_strokeGraphics.SmoothingMode=SmoothingMode.AntiAlias;_strokeGraphics.PixelOffsetMode=PixelOffsetMode.HighQuality;_strokePen=new Pen(_penColor,_penWidth){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};}
        var before=_smoothStroke.Preview;
        var committed=_smoothStroke.Add(next);if(committed.HasValue)_strokeSegments.Add(committed.Value);_last=next;_strokeChanged=true;
        RectangleF dirty=new(next,SizeF.Empty);
        foreach(var segment in new[]{before,committed,_smoothStroke.Preview})if(segment.HasValue)dirty=RectangleF.Union(dirty,segment.Value.Bounds);
        InvalidateStroke(dirty);
    }

    private void InvalidateStroke(RectangleF dirty)
    {
        var box=_canvas.ClientRectangle;float scale=Math.Min((float)box.Width/_image.Width,(float)box.Height/_image.Height);
        dirty.Inflate(_penWidth/2f+3,_penWidth/2f+3);
        var bounds=Rectangle.FromLTRB((int)Math.Floor((box.Width-_image.Width*scale)/2+dirty.Left*scale),(int)Math.Floor((box.Height-_image.Height*scale)/2+dirty.Top*scale),(int)Math.Ceiling((box.Width-_image.Width*scale)/2+dirty.Right*scale),(int)Math.Ceiling((box.Height-_image.Height*scale)/2+dirty.Bottom*scale));
        _canvas.Invalidate(Rectangle.Intersect(bounds,box));
    }
    private void PaintStrokePreview(object? sender,PaintEventArgs e)
    {
        if(_strokePen==null)return;
        var box=_canvas.ClientRectangle;float scale=Math.Min((float)box.Width/_image.Width,(float)box.Height/_image.Height);var state=e.Graphics.Save();
        try{e.Graphics.TranslateTransform((box.Width-_image.Width*scale)/2,(box.Height-_image.Height*scale)/2);e.Graphics.ScaleTransform(scale,scale);e.Graphics.SetClip(new Rectangle(0,0,_image.Width,_image.Height),CombineMode.Intersect);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;using var path=ActiveStrokePath();if(path.PointCount>0)e.Graphics.DrawPath(_strokePen,path);}finally{e.Graphics.Restore(state);}
    }

    private GraphicsPath ActiveStrokePath()
    {
        var path=new GraphicsPath();foreach(var s in _strokeSegments)path.AddBezier(s.Start,s.C1,s.C2,s.End);if(_smoothStroke.Preview is {} tail)path.AddBezier(tail.Start,tail.C1,tail.C2,tail.End);return path;
    }
    private void EndStroke() => FinishStroke(true);
    private void FinishStroke(bool copy)
    {
        bool changed=_strokeChanged;
        if(changed&&_strokeGraphics!=null&&_strokePen!=null){if(_last.HasValue&&_smoothStroke.Add(_last.Value,true) is {} final)_strokeSegments.Add(final);using var path=ActiveStrokePath();if(path.PointCount>0)_strokeGraphics.DrawPath(_strokePen,path);}
        _smoothStroke.Clear();_strokeSegments.Clear();_strokeChanged=false;_last=null;
        _strokePen?.Dispose();_strokePen=null;_strokeGraphics?.Dispose();_strokeGraphics=null;
        if(changed){_gpuImageRevision++;_canvas.Invalidate();}
        if(_canvas.Capture)_canvas.Capture=false;
        if(changed && copy) CopyCurrentImage();
    }
    private void CopyCurrentImage()
    {
        string warning=UiLanguage.T(" — コピー失敗（描画またはダブルクリックで再試行）");
        try{_initialCopy(_image);Text=Text.Replace(warning, "");}
        catch(Exception error){AppLog.Error(error);if(!Text.EndsWith(warning))Text+=warning;}
    }

    private PointF ImagePoint(Point point)
    {
        var box = _canvas.ClientRectangle;
        var scale = Math.Max(.0001f,Math.Min((float)box.Width / _image.Width, (float)box.Height / _image.Height));
        return new PointF(Math.Clamp((point.X-(box.Width-_image.Width*scale)/2)/scale,0,_image.Width-1f),Math.Clamp((point.Y-(box.Height-_image.Height*scale)/2)/scale,0,_image.Height-1f));
    }

    protected override void OnShown(EventArgs e)
    {
        if(!_initialCopyAttempted){_initialCopyAttempted=true;CopyCurrentImage();}
        base.OnShown(e);
    }

    private void RestoreUndo()
    {
        EndStroke();
        if (_undoHistory.Count == 0) return;
        PushHistory(_redoHistory, new Bitmap(_image));
        RestoreLast(_undoHistory);
    }

    private void RestoreRedo()
    {
        EndStroke();
        if (_redoHistory.Count == 0) return;
        PushHistory(_undoHistory, new Bitmap(_image));
        RestoreLast(_redoHistory);
    }

    private void RestoreLast(List<Bitmap> history)
    {
        var lastIndex = history.Count - 1;
        var snapshot = history[lastIndex];
        history.RemoveAt(lastIndex);
        using (var graphics = Graphics.FromImage(_image)) graphics.DrawImageUnscaled(snapshot, 0, 0);
        snapshot.Dispose();
        _gpuImageRevision++;_canvas.Invalidate();
        CopyCurrentImage();
    }

    private static void PushHistory(List<Bitmap> history, Bitmap snapshot)
    {
        history.Add(snapshot);
        if(history.Count<=HistoryLimit)return;
        history[0].Dispose();history.RemoveAt(0);
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
            FinishStroke(false);
            _gpuRenderer.Dispose();
            ClearHistory(_undoHistory);
            ClearHistory(_redoHistory);
            _image.Dispose();
        }
        base.Dispose(disposing);
    }
}
