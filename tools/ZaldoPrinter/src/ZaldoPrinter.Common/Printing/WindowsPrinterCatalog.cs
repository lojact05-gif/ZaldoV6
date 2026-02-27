using System.Drawing.Printing;
using System.Management;
using ZaldoPrinter.Common.Models;

namespace ZaldoPrinter.Common.Printing;

public sealed class WindowsPrinterCatalog
{
    public IReadOnlyList<InstalledPrinterInfo> EnumerateInstalledPrinters()
    {
        var defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultPrinter = new PrinterSettings().PrinterName;
        if (!string.IsNullOrWhiteSpace(defaultPrinter))
        {
            defaults.Add(defaultPrinter.Trim());
        }

        var statusByName = new Dictionary<string, (string status, string driver)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverName, WorkOffline, PrinterStatus FROM Win32_Printer");
            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                var name = (obj["Name"]?.ToString() ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                var driver = (obj["DriverName"]?.ToString() ?? string.Empty).Trim();
                var workOffline = ParseBool(obj["WorkOffline"]);
                var printerStatus = ParseInt(obj["PrinterStatus"]);
                var status = workOffline ? "Offline" : MapStatus(printerStatus);
                statusByName[name] = (status, driver);
            }
        }
        catch
        {
        }

        var result = new List<InstalledPrinterInfo>();
        foreach (string item in PrinterSettings.InstalledPrinters)
        {
            var name = item?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                continue;
            }

            var metadata = statusByName.TryGetValue(name, out var tuple)
                ? tuple
                : ("Unknown", string.Empty);

            result.Add(new InstalledPrinterInfo
            {
                PrinterName = name,
                IsDefault = defaults.Contains(name),
                Status = metadata.Item1,
                DriverName = metadata.Item2,
            });
        }

        result.Sort((a, b) =>
        {
            var rankA = a.IsDefault ? 0 : 1;
            var rankB = b.IsDefault ? 0 : 1;
            if (rankA != rankB)
            {
                return rankA.CompareTo(rankB);
            }
            return string.Compare(a.PrinterName, b.PrinterName, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    private static bool ParseBool(object? value)
    {
        if (value is bool b)
        {
            return b;
        }

        if (value is int i)
        {
            return i != 0;
        }

        if (value is string s && bool.TryParse(s, out var parsed))
        {
            return parsed;
        }

        return false;
    }

    private static int ParseInt(object? value)
    {
        if (value is int i)
        {
            return i;
        }

        if (value is short sh)
        {
            return sh;
        }

        if (value is string s && int.TryParse(s, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static string MapStatus(int status)
    {
        return status switch
        {
            1 => "Other",
            2 => "Unknown",
            3 => "Idle",
            4 => "Printing",
            5 => "Warmup",
            6 => "Stopped",
            7 => "Offline",
            _ => "Unknown",
        };
    }
}
