using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CaptureZoo;

internal sealed class CaptureOverlay : Form
{
    private const int DragThreshold = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximumCount);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpShowWindow = 0x0040;

    private readonly Bitmap _desktop;
    private readonly Rectangle _virtualScreen;
    private readonly System.Windows.Forms.Timer _singleClickTimer;
    private Point? _start;
    private Point _pendingClickLocation;
    private Rectangle _pendingClickWindow;
    private string _pendingClickWindowTitle = string.Empty;
    private Rectangle _selection;
    private Rectangle _hoveredWindow;
    private string _hoveredWindowTitle = string.Empty;

    public Bitmap? SelectedImage { get; private set; }
    public Rectangle SelectedBoundsInVirtualScreen => new(_selection.X + _virtualScreen.Left, _selection.Y + _virtualScreen.Top, _selection.Width, _selection.Height);

    public CaptureOverlay(Bitmap desktop)
    {
        _desktop = desktop;
        _virtualScreen = SystemInformation.VirtualScreen;
        Text = "CaptureZoo - 範囲選択";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = _virtualScreen;
        TopMost = true;
        DoubleBuffered = true;
        ShowInTaskbar = true;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        BackColor = Color.Black;

        _singleClickTimer = new System.Windows.Forms.Timer { Interval = SystemInformation.DoubleClickTime };
        _singleClickTimer.Tick += (_, _) => CompletePendingSingleClick();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // F10 から起動しても、全モニター上の全アプリより手前へ確実に表示する。
        SetWindowPos(Handle, HwndTopmost, _virtualScreen.Left, _virtualScreen.Top, _virtualScreen.Width, _virtualScreen.Height, SwpShowWindow);
        TopMost = true;
        BringToFront();
        Activate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(_desktop, 0, 0);
        using var dim = new SolidBrush(Color.FromArgb(115, Color.Black));
        e.Graphics.FillRectangle(dim, ClientRectangle);

        var isFreeSelection = _selection.Width > 1 && _selection.Height > 1;
        var bounds = isFreeSelection ? _selection : _hoveredWindow;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        e.Graphics.DrawImage(_desktop, bounds, bounds, GraphicsUnit.Pixel);
        using var pen = new Pen(isFreeSelection ? Color.DeepPink : Color.FromArgb(84, 216, 255), 2)
        {
            DashStyle = isFreeSelection ? DashStyle.Solid : DashStyle.Dash
        };
        e.Graphics.DrawRectangle(pen, bounds);

        var description = isFreeSelection
            ? $"{bounds.Width} × {bounds.Height}"
            : string.IsNullOrWhiteSpace(_hoveredWindowTitle)
                ? $"ウィンドウ  {bounds.Width} × {bounds.Height}"
                : $"{_hoveredWindowTitle}  {bounds.Width} × {bounds.Height}";
        DrawLabel(e.Graphics, bounds, description);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        // 単クリックを保留している間のもう一度のクリックは、キャプチャではなく
        // 選択オーバーレイのキャンセルとして扱う。
        if (_singleClickTimer.Enabled && IsWithinDoubleClickArea(e.Location, _pendingClickLocation))
        {
            _singleClickTimer.Stop();
            _start = null;
            _selection = Rectangle.Empty;
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        _start = e.Location;
        _selection = Rectangle.Empty;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_start is not null)
        {
            var dragBounds = Normalize(_start.Value, e.Location);
            if (dragBounds.Width >= DragThreshold || dragBounds.Height >= DragThreshold)
                _selection = dragBounds;
        }
        else
        {
            UpdateHoveredWindow(e.Location);
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _start is null) return;

        var dragBounds = Normalize(_start.Value, e.Location);
        _start = null;
        if (dragBounds.Width >= DragThreshold || dragBounds.Height >= DragThreshold)
        {
            _selection = dragBounds;
            CompleteSelection();
            return;
        }

        UpdateHoveredWindow(e.Location);
        _pendingClickLocation = e.Location;
        _pendingClickWindow = _hoveredWindow;
        _pendingClickWindowTitle = _hoveredWindowTitle;
        _singleClickTimer.Stop();
        _singleClickTimer.Start();
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    private void UpdateHoveredWindow(Point overlayPoint)
    {
        var desktopPoint = new Point(overlayPoint.X + _virtualScreen.Left, overlayPoint.Y + _virtualScreen.Top);
        var candidate = FindTopLevelWindowAt(desktopPoint);
        _hoveredWindow = candidate.Bounds;
        _hoveredWindowTitle = candidate.Title;
    }

    private (Rectangle Bounds, string Title) FindTopLevelWindowAt(Point desktopPoint)
    {
        var result = (Bounds: Rectangle.Empty, Title: string.Empty);
        EnumWindows((handle, _) =>
        {
            if (handle == Handle || !IsWindowVisible(handle) || !GetWindowRect(handle, out var native)) return true;
            var screenBounds = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
            if (screenBounds.Width < 4 || screenBounds.Height < 4 || !screenBounds.Contains(desktopPoint)) return true;

            var localBounds = Rectangle.Intersect(
                new Rectangle(screenBounds.Left - _virtualScreen.Left, screenBounds.Top - _virtualScreen.Top, screenBounds.Width, screenBounds.Height),
                ClientRectangle);
            if (localBounds.Width < 4 || localBounds.Height < 4) return true;

            var title = new StringBuilder(512);
            GetWindowText(handle, title, title.Capacity);
            result = (localBounds, title.ToString());
            return false;
        }, IntPtr.Zero);
        return result;
    }

    private void CompleteSelection()
    {
        if (_selection.Width < 3 || _selection.Height < 3) { Invalidate(); return; }
        SelectedImage = new Bitmap(_selection.Width, _selection.Height);
        using var graphics = Graphics.FromImage(SelectedImage);
        graphics.DrawImage(_desktop, new Rectangle(0, 0, _selection.Width, _selection.Height), _selection, GraphicsUnit.Pixel);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CompletePendingSingleClick()
    {
        _singleClickTimer.Stop();
        if (IsDisposed || Disposing) return;

        _hoveredWindow = _pendingClickWindow;
        _hoveredWindowTitle = _pendingClickWindowTitle;
        if (_pendingClickWindow.Width <= 1 || _pendingClickWindow.Height <= 1)
        {
            Invalidate();
            return;
        }

        _selection = _pendingClickWindow;
        CompleteSelection();
    }

    private static bool IsWithinDoubleClickArea(Point current, Point original)
    {
        var size = SystemInformation.DoubleClickSize;
        return Math.Abs(current.X - original.X) <= size.Width / 2
            && Math.Abs(current.Y - original.Y) <= size.Height / 2;
    }

    private void DrawLabel(Graphics graphics, Rectangle bounds, string text)
    {
        var size = TextRenderer.MeasureText(text, Font);
        var top = Math.Max(0, bounds.Top - size.Height - 5);
        using var textBack = new SolidBrush(Color.FromArgb(230, 24, 24, 28));
        graphics.FillRectangle(textBack, bounds.Left, top, Math.Min(size.Width + 8, ClientSize.Width - bounds.Left), size.Height + 4);
        TextRenderer.DrawText(graphics, text, Font, new Point(bounds.Left + 4, top + 2), Color.White, TextFormatFlags.EndEllipsis);
    }

    private static Rectangle Normalize(Point a, Point b) => Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
}
