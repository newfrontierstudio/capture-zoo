namespace CaptureZoo;

internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "CaptureZoo.log");

    public static void Info(string message)
    {
        try
        {
            lock (Gate) File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void Error(Exception error) => Info($"ERROR {error.GetType().Name}: {error.Message}{Environment.NewLine}{error.StackTrace}");
}
