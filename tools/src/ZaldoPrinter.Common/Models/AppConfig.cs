using System.Text.Json.Serialization;

namespace ZaldoPrinter.Common.Models;

public sealed class AppConfig
{
    [JsonPropertyName("pairingToken")]
    public string PairingToken { get; set; } = string.Empty;

    [JsonPropertyName("defaultPrinterId")]
    public string DefaultPrinterId { get; set; } = string.Empty;

    [JsonPropertyName("allowedOrigins")]
    public List<string> AllowedOrigins { get; set; } = new();

    [JsonPropertyName("requestTimeoutMs")]
    public int RequestTimeoutMs { get; set; } = 3000;

    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 1;

    [JsonPropertyName("printers")]
    public List<PrinterProfile> Printers { get; set; } = new();

    public static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            PairingToken = string.Empty,
            DefaultPrinterId = string.Empty,
            RequestTimeoutMs = 3000,
            RetryCount = 1,
            AllowedOrigins = new List<string>
            {
                "https://zaldo.pt",
                "https://www.zaldo.pt",
                "http://localhost",
                "http://127.0.0.1",
            },
            Printers = new List<PrinterProfile>(),
        };
    }
}
