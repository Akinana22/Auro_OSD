using System.Text;

namespace Auro_OSD.Logging;

public static class AppLogger
{
    private static readonly object Sync = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static bool Enabled { get; set; }

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Auro_OSD", "logs");

    public static string FilePath => Path.Combine(DirectoryPath, "autoswitch.log");

    private static string BackupPath => Path.Combine(DirectoryPath, "autoswitch.1.log");

    public static void Line(string message)
    {
        if (!Enabled) return;

        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                Roll();
                File.AppendAllText(FilePath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // never let logging break the app
        }
    }

    private static void Roll()
    {
        try
        {
            var info = new FileInfo(FilePath);
            if (!info.Exists || info.Length < MaxBytes) return;

            if (File.Exists(BackupPath)) File.Delete(BackupPath);
            File.Move(FilePath, BackupPath);
        }
        catch
        {
            // ignore
        }
    }
}
