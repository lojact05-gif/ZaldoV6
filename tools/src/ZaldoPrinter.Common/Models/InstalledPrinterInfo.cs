using System.Text.Json.Serialization;

namespace ZaldoPrinter.Common.Models;

public sealed class InstalledPrinterInfo
{
    [JsonPropertyName("printerName")]
    public string PrinterName { get; set; } = string.Empty;

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Unknown";

    [JsonPropertyName("driverName")]
    public string DriverName { get; set; } = string.Empty;
}
