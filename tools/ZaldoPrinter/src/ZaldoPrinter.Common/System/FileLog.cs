namespace ZaldoPrinter.Common.System;

public sealed class FileLog
{
    private readonly object _sync = new();
    private string _logDirectory;

    public FileLog(string? preferredLogDirectory = null)
    {
        _logDirectory = ProgramDataPaths.ResolveWritableLogsDirectory(preferredLogDirectory);
    }

    public string LogDirectory => _logDirectory;

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
            try
            {
                File.AppendAllText(file, line + Environment.NewLine, global::System.Text.Encoding.UTF8);
                return;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                // Fallback de execução em clientes sem ACL válida no ProgramData.
                if (!TrySwitchToLocalAppDataDirectory())
                {
                    throw;
                }
            }

            var fallbackFile = Path.Combine(_logDirectory, $"zaldo-printer-{DateTimeOffset.Now:yyyyMMdd}.log");
            File.AppendAllText(fallbackFile, line + Environment.NewLine, global::System.Text.Encoding.UTF8);
        }
    }

    private bool TrySwitchToLocalAppDataDirectory()
    {
        var fallback = ProgramDataPaths.LocalLogsDirectory();
        if (string.Equals(_logDirectory, fallback, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!ProgramDataPaths.TryEnsureWritableDirectory(fallback, out _))
        {
            return false;
        }

        _logDirectory = fallback;
        return true;
    }
}
