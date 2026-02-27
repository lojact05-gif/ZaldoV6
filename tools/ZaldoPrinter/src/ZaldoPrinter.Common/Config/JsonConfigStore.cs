using System.Text.Json;
using ZaldoPrinter.Common.Models;
using ZaldoPrinter.Common.System;

namespace ZaldoPrinter.Common.Config;

public sealed class JsonConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly string _configFile;
    private AppConfig _current;

    public JsonConfigStore()
    {
        _configFile = ProgramDataPaths.ConfigFile();
        Directory.CreateDirectory(Path.GetDirectoryName(_configFile)!);
        _current = LoadOrCreate();
    }

    public AppConfig Get()
    {
        _lock.EnterReadLock();
        try
        {
            return DeepClone(_current);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public AppConfig Save(AppConfig config)
    {
        Normalize(config);

        _lock.EnterWriteLock();
        try
        {
            _current = DeepClone(config);
            WriteFile(_current);
            return DeepClone(_current);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public string RegenerateToken()
    {
        _lock.EnterWriteLock();
        try
        {
            _current.PairingToken = TokenGenerator.Generate();
            WriteFile(_current);
            return _current.PairingToken;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private AppConfig LoadOrCreate()
    {
        if (!File.Exists(_configFile))
        {
            var created = AppConfig.CreateDefault();
            created.PairingToken = TokenGenerator.Generate();
            WriteFile(created);
            return created;
        }

        try
        {
            var json = File.ReadAllText(_configFile);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? AppConfig.CreateDefault();
            Normalize(loaded);
            if (string.IsNullOrWhiteSpace(loaded.PairingToken))
            {
                loaded.PairingToken = TokenGenerator.Generate();
                WriteFile(loaded);
            }
            return loaded;
        }
        catch
        {
            var fallback = AppConfig.CreateDefault();
            fallback.PairingToken = TokenGenerator.Generate();
            WriteFile(fallback);
            return fallback;
        }
    }

    private void Normalize(AppConfig config)
    {
        config.RequestTimeoutMs = Math.Clamp(config.RequestTimeoutMs, 500, 15000);
        config.RetryCount = Math.Clamp(config.RetryCount, 0, 5);

        config.AllowedOrigins ??= new();
        config.Printers ??= new();

        foreach (var profile in config.Printers)
        {
            profile.Id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id.Trim();
            profile.Name = profile.Name?.Trim() ?? string.Empty;
            profile.Mode = profile.ModeNormalized();
            profile.Usb ??= new();
            profile.Network ??= new();
            profile.CashDrawer ??= new();
            profile.CashDrawer.KickPulse ??= new();
            profile.Cut ??= new();
            profile.Network.Port = Math.Clamp(profile.Network.Port <= 0 ? 9100 : profile.Network.Port, 1, 65535);
            profile.CashDrawer.KickPulse.M = Math.Clamp(profile.CashDrawer.KickPulse.M, 0, 1);
            profile.CashDrawer.KickPulse.T1 = Math.Clamp(profile.CashDrawer.KickPulse.T1, 0, 255);
            profile.CashDrawer.KickPulse.T2 = Math.Clamp(profile.CashDrawer.KickPulse.T2, 0, 255);
            if (!string.Equals(profile.Cut.Mode, "full", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(profile.Cut.Mode, "partial", StringComparison.OrdinalIgnoreCase))
            {
                profile.Cut.Mode = "partial";
            }
        }

        if (config.Printers.All(p => !string.Equals(p.Id, config.DefaultPrinterId, StringComparison.OrdinalIgnoreCase)))
        {
            config.DefaultPrinterId = config.Printers.FirstOrDefault()?.Id ?? string.Empty;
        }
    }

    private void WriteFile(AppConfig config)
    {
        var tmp = _configFile + ".tmp";
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(tmp, json, global::System.Text.Encoding.UTF8);
        File.Copy(tmp, _configFile, true);
        File.Delete(tmp);
    }

    private static AppConfig DeepClone(AppConfig source)
    {
        var json = JsonSerializer.Serialize(source, JsonOptions);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? AppConfig.CreateDefault();
    }
}
