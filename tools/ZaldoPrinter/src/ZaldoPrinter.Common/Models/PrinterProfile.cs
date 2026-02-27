using System.Text.Json.Serialization;

namespace ZaldoPrinter.Common.Models;

public sealed class PrinterProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "usb";

    [JsonPropertyName("usb")]
    public UsbSettings Usb { get; set; } = new();

    [JsonPropertyName("network")]
    public NetworkSettings Network { get; set; } = new();

    [JsonPropertyName("cashDrawer")]
    public CashDrawerSettings CashDrawer { get; set; } = new();

    [JsonPropertyName("cut")]
    public CutSettings Cut { get; set; } = new();

    public string ModeNormalized()
    {
        return string.Equals(Mode, "network", StringComparison.OrdinalIgnoreCase) ? "network" : "usb";
    }
}

public sealed class UsbSettings
{
    [JsonPropertyName("printerName")]
    public string PrinterName { get; set; } = string.Empty;
}

public sealed class NetworkSettings
{
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; } = 9100;
}

public sealed class CashDrawerSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("kickPulse")]
    public KickPulseSettings KickPulse { get; set; } = new();
}

public sealed class KickPulseSettings
{
    [JsonPropertyName("m")]
    public int M { get; set; } = 0;

    [JsonPropertyName("t1")]
    public int T1 { get; set; } = 25;

    [JsonPropertyName("t2")]
    public int T2 { get; set; } = 250;
}

public sealed class CutSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "partial";
}
