using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using ZaldoPrinter.Common.Models;

namespace ZaldoPrinter.Common.Printing;

public sealed class EscPosBuilder
{
    private readonly Encoding _encoding;

    public EscPosBuilder()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _encoding = Encoding.GetEncoding(850);
    }

    public byte[] BuildReceipt(ReceiptPayload payload, PrinterProfile profile, bool applyCut, string? cutModeOverride, bool applyDrawer)
    {
        var bytes = new List<byte>(1024);

        bytes.AddRange(CmdInit());
        bytes.AddRange(CmdAlign(1));

        if (!string.IsNullOrWhiteSpace(payload.LogoBase64))
        {
            TryAppendLogo(bytes, payload.LogoBase64);
        }

        bytes.AddRange(CmdBold(true));
        AppendLine(bytes, payload.CompanyName);
        bytes.AddRange(CmdBold(false));

        if (!string.IsNullOrWhiteSpace(payload.CompanyNif))
        {
            AppendLine(bytes, payload.CompanyNif);
        }

        if (!string.IsNullOrWhiteSpace(payload.TerminalName))
        {
            AppendLine(bytes, payload.TerminalName);
        }

        if (!string.IsNullOrWhiteSpace(payload.PrintedAt))
        {
            AppendLine(bytes, payload.PrintedAt);
        }

        bytes.AddRange(CmdAlign(0));
        AppendLine(bytes, HorizontalRule());

        if (!string.IsNullOrWhiteSpace(payload.DocumentNumber))
        {
            bytes.AddRange(CmdBold(true));
            AppendLine(bytes, payload.DocumentNumber);
            bytes.AddRange(CmdBold(false));
        }

        if (!string.IsNullOrWhiteSpace(payload.OperatorName))
        {
            AppendLine(bytes, "Operador: " + payload.OperatorName);
        }

        if (!string.IsNullOrWhiteSpace(payload.Atcud))
        {
            AppendLine(bytes, "ATCUD: " + payload.Atcud);
        }

        AppendLine(bytes, HorizontalRule());

        foreach (var item in payload.Items)
        {
            AppendLine(bytes, item.Name);
            var left = string.Format(CultureInfo.InvariantCulture, "{0} x {1}", FormatQty(item.Qty), FormatMoney(item.UnitPrice));
            AppendLine(bytes, TwoCol(left, FormatMoney(item.LineTotal)));
        }

        AppendLine(bytes, HorizontalRule());
        AppendLine(bytes, TwoCol("Subtotal", FormatMoney(payload.Totals.Subtotal)));
        AppendLine(bytes, TwoCol("IVA", FormatMoney(payload.Totals.Tax)));
        bytes.AddRange(CmdBold(true));
        AppendLine(bytes, TwoCol("TOTAL", FormatMoney(payload.Totals.Total)));
        bytes.AddRange(CmdBold(false));

        if (payload.Payments.Count > 0)
        {
            AppendLine(bytes, HorizontalRule());
            foreach (var payment in payload.Payments)
            {
                AppendLine(bytes, TwoCol(payment.Label, FormatMoney(payment.Amount)));
            }
        }

        if (!string.IsNullOrWhiteSpace(payload.QrCode))
        {
            bytes.AddRange(CmdAlign(1));
            bytes.AddRange(QrCode(payload.QrCode));
            AppendLine(bytes, string.Empty);
            bytes.AddRange(CmdAlign(0));
        }

        AppendLine(bytes, string.Empty);
        bytes.AddRange(CmdAlign(1));
        AppendLine(bytes, "Obrigado pela preferencia");
        bytes.AddRange(CmdAlign(0));

        if (applyDrawer && profile.CashDrawer.Enabled)
        {
            bytes.AddRange(OpenDrawer(profile));
        }

        bytes.AddRange(CmdFeed(3));

        var cutEnabled = applyCut && profile.Cut.Enabled;
        if (cutEnabled)
        {
            var mode = string.IsNullOrWhiteSpace(cutModeOverride) ? profile.Cut.Mode : cutModeOverride;
            bytes.AddRange(Cut(mode));
        }
        return bytes.ToArray();
    }

    public byte[] BuildDrawer(PrinterProfile profile)
    {
        var bytes = new List<byte>(8);
        bytes.AddRange(CmdInit());
        bytes.AddRange(OpenDrawer(profile));
        return bytes.ToArray();
    }

    public byte[] BuildCut(PrinterProfile profile, string? modeOverride)
    {
        var bytes = new List<byte>(8);
        bytes.AddRange(CmdInit());
        bytes.AddRange(Cut(modeOverride ?? profile.Cut.Mode));
        return bytes.ToArray();
    }

    public byte[] BuildTestPrint(PrinterProfile profile, string title)
    {
        var payload = new ReceiptPayload
        {
            CompanyName = "ZALDO PRINTER",
            CompanyNif = "TESTE",
            TerminalName = profile.Name,
            OperatorName = "TESTE",
            DocumentNumber = "TEST-" + DateTimeOffset.Now.ToString("yyyyMMddHHmmss"),
            PrintedAt = DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm:ss"),
            Items =
            {
                new ReceiptItemPayload { Name = title, Qty = 1, UnitPrice = 1.00m, LineTotal = 1.00m },
            },
            Totals = new ReceiptTotalsPayload { Subtotal = 0.81m, Tax = 0.19m, Total = 1.00m },
            Payments =
            {
                new ReceiptPaymentPayload { Label = "Numerario", Amount = 1.00m },
            },
        };

        return BuildReceipt(payload, profile, applyCut: true, cutModeOverride: null, applyDrawer: true);
    }

    private static IEnumerable<byte> CmdInit() => new byte[] { 0x1B, 0x40 };

    private static IEnumerable<byte> CmdAlign(int mode) => new byte[] { 0x1B, 0x61, (byte)Math.Clamp(mode, 0, 2) };

    private static IEnumerable<byte> CmdBold(bool on) => new byte[] { 0x1B, 0x45, (byte)(on ? 1 : 0) };

    private static IEnumerable<byte> CmdFeed(int lines) => new byte[] { 0x1B, 0x64, (byte)Math.Clamp(lines, 0, 10) };

    private static string HorizontalRule() => new('-', 42);

    private static string FormatMoney(decimal value)
    {
        return value.ToString("N2", CultureInfo.GetCultureInfo("pt-PT")) + " EUR";
    }

    private static string FormatQty(decimal value)
    {
        if (decimal.Truncate(value) == value)
        {
            return value.ToString("0", CultureInfo.InvariantCulture);
        }

        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private string TwoCol(string left, string right, int width = 42)
    {
        var l = Sanitize(left);
        var r = Sanitize(right);
        if (r.Length == 0)
        {
            return TrimWidth(l, width);
        }

        if (l.Length + 1 + r.Length <= width)
        {
            return l + new string(' ', width - l.Length - r.Length) + r;
        }

        var keepLeft = Math.Max(0, width - r.Length - 1);
        return TrimWidth(l, keepLeft) + " " + TrimWidth(r, Math.Min(r.Length, width - 1));
    }

    private static string TrimWidth(string value, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= width)
        {
            return value;
        }

        if (width == 1)
        {
            return value[..1];
        }

        return value[..(width - 1)] + ".";
    }

    private string Sanitize(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var encoded = _encoding.GetBytes(text);
        return _encoding.GetString(encoded);
    }

    private void AppendLine(List<byte> target, string value)
    {
        target.AddRange(_encoding.GetBytes(Sanitize(value)));
        target.Add(0x0A);
    }

    private static IEnumerable<byte> OpenDrawer(PrinterProfile profile)
    {
        var pulse = profile.CashDrawer.KickPulse;
        return new byte[]
        {
            0x1B,
            0x70,
            (byte)Math.Clamp(pulse.M, 0, 1),
            (byte)Math.Clamp(pulse.T1, 0, 255),
            (byte)Math.Clamp(pulse.T2, 0, 255),
        };
    }

    private static IEnumerable<byte> Cut(string? mode)
    {
        if (string.Equals(mode, "full", StringComparison.OrdinalIgnoreCase))
        {
            return new byte[] { 0x1D, 0x56, 0x00 };
        }

        return new byte[] { 0x1D, 0x56, 0x01 };
    }

    private static IEnumerable<byte> QrCode(string value)
    {
        var payload = Encoding.UTF8.GetBytes(value.Length > 700 ? value[..700] : value);
        var storeLen = payload.Length + 3;
        var pL = (byte)(storeLen % 256);
        var pH = (byte)(storeLen / 256);

        var bytes = new List<byte>();
        bytes.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 });
        bytes.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x06 });
        bytes.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x30 });
        bytes.AddRange(new byte[] { 0x1D, 0x28, 0x6B, pL, pH, 0x31, 0x50, 0x30 });
        bytes.AddRange(payload);
        bytes.AddRange(new byte[] { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30 });
        return bytes;
    }

    private void TryAppendLogo(List<byte> target, string rawBase64)
    {
        try
        {
            var sourceBytes = DecodeBase64Image(rawBase64);
            if (sourceBytes is null || sourceBytes.Length == 0)
            {
                return;
            }

            using var stream = new MemoryStream(sourceBytes);
            using var source = new Bitmap(stream);
            var maxWidth = 384;
            var scale = source.Width > maxWidth
                ? (decimal)maxWidth / source.Width
                : 1m;

            var width = Math.Max(1, (int)Math.Round(source.Width * (double)scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * (double)scale));
            using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.DrawImage(source, 0, 0, width, height);
            }

            var bytes = BuildRaster(bitmap);
            if (bytes.Length == 0)
            {
                return;
            }

            target.AddRange(CmdAlign(1));
            target.AddRange(bytes);
            target.Add(0x0A);
            target.AddRange(CmdAlign(1));
        }
        catch
        {
        }
    }

    private static byte[]? DecodeBase64Image(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (value.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
        {
            var comma = value.IndexOf(',');
            if (comma >= 0 && comma + 1 < value.Length)
            {
                value = value[(comma + 1)..];
            }
        }

        try
        {
            return Convert.FromBase64String(value);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] BuildRaster(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        if (width <= 0 || height <= 0)
        {
            return Array.Empty<byte>();
        }

        var widthBytes = (width + 7) / 8;
        var output = new List<byte>(8 + (widthBytes * height))
        {
            0x1D,
            0x76,
            0x30,
            0x00,
            (byte)(widthBytes & 0xFF),
            (byte)((widthBytes >> 8) & 0xFF),
            (byte)(height & 0xFF),
            (byte)((height >> 8) & 0xFF),
        };

        for (var y = 0; y < height; y++)
        {
            for (var xb = 0; xb < widthBytes; xb++)
            {
                byte slice = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    var x = (xb * 8) + bit;
                    if (x >= width)
                    {
                        continue;
                    }

                    var pixel = bitmap.GetPixel(x, y);
                    var luma = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
                    if (luma < 150)
                    {
                        slice |= (byte)(0x80 >> bit);
                    }
                }
                output.Add(slice);
            }
        }

        return output.ToArray();
    }
}
