using System.Drawing;
namespace CaptureZoo;
internal static class AppIcon
{
    public static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("CaptureZoo.capture.ico")!;
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}