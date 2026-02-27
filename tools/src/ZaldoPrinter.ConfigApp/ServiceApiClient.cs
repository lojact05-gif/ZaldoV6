using System.Net.Http.Json;
using System.Text.Json;
using ZaldoPrinter.Common.Models;

namespace ZaldoPrinter.ConfigApp;

internal sealed class ServiceApiClient
{
    private readonly HttpClient _http;
    private string _token = string.Empty;

    public ServiceApiClient()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:16161/"),
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    public void SetToken(string token)
    {
        _token = token?.Trim() ?? string.Empty;
    }

    public async Task<ServiceHealthInfo> HealthAsync()
    {
        try
        {
            var response = await _http.GetAsync("health");
            if (!response.IsSuccessStatusCode)
            {
                return new ServiceHealthInfo
                {
                    Ok = false,
                    Message = $"HTTP {(int)response.StatusCode}",
                };
            }

            var payload = await response.Content.ReadFromJsonAsync<ApiHealthEnvelope>();
            if (payload is null || !payload.Ok)
            {
                return new ServiceHealthInfo
                {
                    Ok = false,
                    Message = "Resposta inválida do serviço.",
                };
            }

            return new ServiceHealthInfo
            {
                Ok = true,
                Message = "Online",
                PendingByPrinter = payload.PendingByPrinter ?? new(),
                LastPrintByPrinter = payload.LastPrintByPrinter ?? new(),
                RecentErrors = payload.RecentErrors ?? new(),
            };
        }
        catch (Exception ex)
        {
            return new ServiceHealthInfo
            {
                Ok = false,
                Message = ex.Message,
            };
        }
    }

    public async Task<AppConfig> GetConfigAsync()
    {
        var result = await _http.GetFromJsonAsync<ApiConfigEnvelope>("config");
        return result?.Config ?? AppConfig.CreateDefault();
    }

    public async Task<IReadOnlyList<InstalledPrinterInfo>> GetInstalledPrintersAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "printers");
        AttachAuth(request);
        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiPrintersEnvelope>();
        return payload?.Printers ?? new List<InstalledPrinterInfo>();
    }

    public async Task<AppConfig> SaveConfigAsync(AppConfig config)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "config")
        {
            Content = JsonContent.Create(config),
        };
        AttachAuth(request);
        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiConfigEnvelope>();
        return payload?.Config ?? config;
    }

    public async Task<string> RegenerateTokenAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "token/regenerate")
        {
            Content = JsonContent.Create(new { }),
        };
        AttachAuth(request);
        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.TryGetProperty("pairingToken", out var node)
            ? node.GetString() ?? string.Empty
            : string.Empty;
    }

    public Task<(bool ok, string message)> TestPrintAsync(string printerId)
    {
        return ExecutePrintEndpointAsync("print/test", printerId, new { title = "Teste Zaldo Printer" });
    }

    public Task<(bool ok, string message)> TestDrawerAsync(string printerId)
    {
        return ExecutePrintEndpointAsync("print/cashdrawer", printerId, new { });
    }

    public Task<(bool ok, string message)> TestCutAsync(string printerId)
    {
        return ExecutePrintEndpointAsync("print/cut", printerId, new { mode = "partial" });
    }

    private async Task<(bool ok, string message)> ExecutePrintEndpointAsync(string endpoint, string printerId, object body)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(body),
            };
            AttachAuth(request);
            if (!string.IsNullOrWhiteSpace(printerId))
            {
                request.Headers.Add("X-PRINTER-ID", printerId);
            }

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                return (true, "OK");
            }

            return (false, json.Length == 0 ? $"HTTP {(int)response.StatusCode}" : json);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private void AttachAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_token))
        {
            request.Headers.Remove("X-ZALDO-TOKEN");
            request.Headers.Add("X-ZALDO-TOKEN", _token);
        }
    }

    private sealed class ApiConfigEnvelope
    {
        public bool Ok { get; set; }
        public AppConfig Config { get; set; } = AppConfig.CreateDefault();
    }

    private sealed class ApiPrintersEnvelope
    {
        public bool Ok { get; set; }
        public List<InstalledPrinterInfo> Printers { get; set; } = new();
    }

    private sealed class ApiHealthEnvelope
    {
        public bool Ok { get; set; }
        public Dictionary<string, int>? PendingByPrinter { get; set; }
        public Dictionary<string, ServiceHealthPrintInfo>? LastPrintByPrinter { get; set; }
        public List<string>? RecentErrors { get; set; }
    }
}

internal sealed class ServiceHealthInfo
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, int> PendingByPrinter { get; set; } = new();
    public Dictionary<string, ServiceHealthPrintInfo> LastPrintByPrinter { get; set; } = new();
    public List<string> RecentErrors { get; set; } = new();
}

internal sealed class ServiceHealthPrintInfo
{
    public bool Ok { get; set; }
    public string Message { get; set; } = string.Empty;
    public string PrinterId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public string Operation { get; set; } = string.Empty;
    public DateTimeOffset CompletedAt { get; set; }
}
