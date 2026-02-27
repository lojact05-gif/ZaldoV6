using ZaldoPrinter.Common.Models;

namespace ZaldoPrinter.Common.Printing;

public static class PrinterProfileResolver
{
    public static PrinterProfile Resolve(AppConfig config, string? requestedPrinterId)
    {
        var targetId = (requestedPrinterId ?? string.Empty).Trim();
        if (targetId.Length == 0)
        {
            targetId = config.DefaultPrinterId?.Trim() ?? string.Empty;
        }

        if (targetId.Length == 0)
        {
            var first = config.Printers.FirstOrDefault(p => p.Enabled);
            if (first is not null)
            {
                return first;
            }
            throw new InvalidOperationException("No enabled printer profile configured.");
        }

        var profile = config.Printers.FirstOrDefault(p => string.Equals(p.Id, targetId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            throw new InvalidOperationException($"Printer profile '{targetId}' not found.");
        }

        if (!profile.Enabled)
        {
            throw new InvalidOperationException($"Printer profile '{profile.Name}' is disabled.");
        }

        return profile;
    }
}
