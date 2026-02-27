namespace ZaldoPrinter.Common.System;

public static class ProgramDataPaths
{
    public static string BasePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(root, "ZaldoPrinter");
    }

    public static string LocalBasePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "ZaldoPrinter");
    }

    public static string ConfigDirectory() => Path.Combine(BasePath(), "config");

    public static string LogsDirectory() => Path.Combine(BasePath(), "log");

    public static string LocalLogsDirectory() => Path.Combine(LocalBasePath(), "log");

    public static string ConfigFile() => Path.Combine(ConfigDirectory(), "config.json");

    public static bool TryEnsureWritableDirectory(string path, out string? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(path);
            var probePath = Path.Combine(path, ".probe-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var fs = new FileStream(
                       probePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read,
                       8,
                       FileOptions.DeleteOnClose))
            {
                fs.WriteByte(0x5A);
                fs.Flush();
            }

            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string ResolveWritableLogsDirectory(string? preferredDirectory = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredDirectory))
        {
            candidates.Add(preferredDirectory.Trim());
        }

        candidates.Add(LogsDirectory());
        candidates.Add(LocalLogsDirectory());

        var lastError = string.Empty;
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryEnsureWritableDirectory(candidate, out var error))
            {
                return candidate;
            }
            lastError = string.IsNullOrWhiteSpace(error) ? "unknown error" : error.Trim();
        }

        throw new UnauthorizedAccessException(
            "Unable to prepare writable log directory for ZaldoPrinter. Last error: " + lastError
        );
    }
}
