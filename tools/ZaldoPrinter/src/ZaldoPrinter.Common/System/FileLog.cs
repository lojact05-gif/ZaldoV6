namespace ZaldoPrinter.Common.System;

public sealed class FileLog
{
    private readonly object _sync = new();
    private readonly string _logDirectory;

    public FileLog()
    {
        _logDirectory = ProgramDataPaths.LogsDirectory();
        Directory.CreateDirectory(_logDirectory);
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message) => Write("WARN", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var file = Path.Combine(_logDirectory, $"zaldo-printer-{DateTimeOffset.Now:yyyyMMdd}.log");
        var line = $"[{timestamp}] {level} {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (_sync)
        {
            File.AppendAllText(file, line + Environment.NewLine, global::System.Text.Encoding.UTF8);
        }
    }
}
