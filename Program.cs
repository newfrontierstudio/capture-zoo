using System.Drawing;
using System.Windows.Forms;

namespace CaptureZoo;

internal static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Contains("--self-test")){Environment.ExitCode=CaptureSelfTest.Run();return;}
        // 二重起動防止。すでにトレイに常駐していれば、そちらを使ってもらう。
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Global\\CaptureZoo_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "CaptureZoo はすでに起動しています(タスクトレイを確認してください)。",
                "CaptureZoo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            AppLog.Info("Application started.");
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            ApplicationConfiguration.Initialize();
            // メインウィンドウは持たず、タスクトレイに常駐してF10を待ち受ける。
            Application.Run(new TrayApplicationContext());
        }
        catch (Exception error)
        {
            AppLog.Error(error);
            MessageBox.Show("起動に失敗しました。CaptureZoo.log を送ってください。", "CaptureZoo");
        }
        finally
        {
            _singleInstanceMutex.ReleaseMutex();
        }
    }
}

internal static class DesktopSnapshot
{
    public static Bitmap Capture()
    {
        var area = SystemInformation.VirtualScreen;
        // On an HDR display GDI can return a black desktop. Prefer the same FP16
        // capture and tone-mapping path used for the final shared image.
        if (HdrDesktopCapture.TryCapture(area, out var hdrImage)) { AppLog.Info("Overlay snapshot: HDR DirectX path."); return hdrImage; }
        AppLog.Info("Overlay snapshot: GDI fallback path.");
        var image = new Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(image);
        graphics.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
        return image;
    }
}

internal static class ClipboardWriter
{
    public static void CopyForSharing(Image source)
    {
        // LINE accepts PNG reliably. Re-encoding through JPEG first applies the fixed 80% quality.
        using var jpegBytes = new MemoryStream();
        var encoder = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.MimeType == "image/jpeg");
        using var parameters = new System.Drawing.Imaging.EncoderParameters(1);
        parameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);
        source.Save(jpegBytes, encoder, parameters);
        jpegBytes.Position = 0;
        using var jpeg = Image.FromStream(jpegBytes);
        using var copied = new Bitmap(jpeg);
        Clipboard.SetImage(copied);
    }
}
