using System.Diagnostics;

namespace Dabudi.Infrastructure;

public sealed class AppLog(string directory)
{
    private readonly object _gate = new();
    public string DirectoryPath { get; } = directory;

    public void Write(string message, Exception? error = null)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = Path.Combine(DirectoryPath, "dabudi.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"
                    + (error == null ? "" : error + Environment.NewLine));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"dabudi log unavailable: {exception.Message}; {message}; {error}");
            }
        }
    }
}
