namespace ZaldoPrinter.Common.System;

public static class ProgramDataPaths
{
    public static string BasePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(root, "ZaldoPrinter");
    }

    public static string ConfigDirectory() => Path.Combine(BasePath(), "config");

    public static string LogsDirectory() => Path.Combine(BasePath(), "logs");

    public static string ConfigFile() => Path.Combine(ConfigDirectory(), "config.json");
}
