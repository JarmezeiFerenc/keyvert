using System.Text;

namespace Keyvert.Services;

/// <summary>Small thread-safe file log that keeps the current and one previous file of up to 1 MB.</summary>
public sealed class AppLog
{
    private const long MaxBytes = 1024 * 1024;
    private readonly object _lock = new();

    public AppLog(string directory)
    {
        Directory.CreateDirectory(directory);
        FilePath = Path.Combine(directory, "app.log");
    }

    public string FilePath { get; }

    public void Info(string message) => Write("INFO ", message);

    public void Warn(string message) => Write("WARN ", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    private void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
        lock (_lock)
        {
            try
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.log"), overwrite: true);

                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
