using System;
using System.IO;
using System.Text;

namespace SnapDescribe.App.Services;

public static class DiagnosticLogger
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SnapDescribe",
        "logs");

    public static void Log(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var builder = new StringBuilder();
            builder.Append('[').Append(timestamp).Append("] ").Append(message);

            if (exception is not null)
            {
                builder.AppendLine();
                builder.AppendLine(exception.ToString());
            }
            else
            {
                builder.AppendLine();
            }

            var filePath = Path.Combine(LogDirectory, $"snap-{DateTime.UtcNow:yyyyMMdd}.log");
            lock (SyncRoot)
            {
                File.AppendAllText(filePath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Swallow logging failures to avoid cascading crashes.
        }
    }
}
