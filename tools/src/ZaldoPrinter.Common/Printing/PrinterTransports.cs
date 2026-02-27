using System.Net.Sockets;
using ZaldoPrinter.Common.Models;
using ZaldoPrinter.Common.Spooler;

namespace ZaldoPrinter.Common.Printing;

public interface IPrinterTransport
{
    Task SendAsync(PrinterProfile profile, byte[] bytes, int timeoutMs, CancellationToken cancellationToken);
}

public sealed class NetworkPrinterTransport : IPrinterTransport
{
    public async Task SendAsync(PrinterProfile profile, byte[] bytes, int timeoutMs, CancellationToken cancellationToken)
    {
        var host = profile.Network?.Ip?.Trim() ?? string.Empty;
        var port = profile.Network?.Port <= 0 ? 9100 : profile.Network.Port;

        if (host.Length == 0)
        {
            throw new InvalidOperationException($"Printer '{profile.Name}' has no network host configured.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Math.Clamp(timeoutMs, 500, 15000));

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);

        await using var stream = client.GetStream();
        await stream.WriteAsync(bytes, cts.Token).ConfigureAwait(false);
        await stream.FlushAsync(cts.Token).ConfigureAwait(false);
    }
}

public sealed class UsbRawSpoolerTransport : IPrinterTransport
{
    public Task SendAsync(PrinterProfile profile, byte[] bytes, int timeoutMs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var printerName = profile.Usb?.PrinterName?.Trim() ?? string.Empty;
        if (printerName.Length == 0)
        {
            throw new InvalidOperationException($"Printer '{profile.Name}' has no spooler printer name configured.");
        }

        RawPrinterSpooler.WriteRaw(printerName, bytes, "Zaldo Printer Job");
        return Task.CompletedTask;
    }
}
