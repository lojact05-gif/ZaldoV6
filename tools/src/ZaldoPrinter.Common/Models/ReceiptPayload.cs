using System.Text.Json.Serialization;

namespace ZaldoPrinter.Common.Models;

public sealed class ReceiptPayload
{
    [JsonPropertyName("company_name")]
    public string CompanyName { get; set; } = "ZALDO POS";

    [JsonPropertyName("company_nif")]
    public string CompanyNif { get; set; } = string.Empty;

    [JsonPropertyName("terminal_name")]
    public string TerminalName { get; set; } = string.Empty;

    [JsonPropertyName("operator_name")]
    public string OperatorName { get; set; } = string.Empty;

    [JsonPropertyName("document_number")]
    public string DocumentNumber { get; set; } = string.Empty;

    [JsonPropertyName("atcud")]
    public string Atcud { get; set; } = string.Empty;

    [JsonPropertyName("qrcode")]
    public string QrCode { get; set; } = string.Empty;

    [JsonPropertyName("logo_base64")]
    public string LogoBase64 { get; set; } = string.Empty;

    [JsonPropertyName("printed_at")]
    public string PrintedAt { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<ReceiptItemPayload> Items { get; set; } = new();

    [JsonPropertyName("payments")]
    public List<ReceiptPaymentPayload> Payments { get; set; } = new();

    [JsonPropertyName("totals")]
    public ReceiptTotalsPayload Totals { get; set; } = new();
}

public sealed class ReceiptItemPayload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("qty")]
    public decimal Qty { get; set; } = 1;

    [JsonPropertyName("unit_price")]
    public decimal UnitPrice { get; set; } = 0;

    [JsonPropertyName("line_total")]
    public decimal LineTotal { get; set; } = 0;
}

public sealed class ReceiptPaymentPayload
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; } = 0;
}

public sealed class ReceiptTotalsPayload
{
    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; set; } = 0;

    [JsonPropertyName("tax")]
    public decimal Tax { get; set; } = 0;

    [JsonPropertyName("total")]
    public decimal Total { get; set; } = 0;
}

public sealed class ReceiptPrintRequest
{
    [JsonPropertyName("receipt")]
    public ReceiptPayload Receipt { get; set; } = new();

    [JsonPropertyName("cut")]
    public bool? Cut { get; set; }

    [JsonPropertyName("cutMode")]
    public string? CutMode { get; set; }

    [JsonPropertyName("openDrawer")]
    public bool? OpenDrawer { get; set; }
}

public sealed class CutRequest
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}

public sealed class TestPrintRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
