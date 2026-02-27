using System.Globalization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
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
        var lineWidth = 42;

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
        if (payload.CompanyLines.Count > 0)
        {
            foreach (var line in payload.CompanyLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    AppendLine(bytes, line);
                }
            }
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
        AppendLine(bytes, HorizontalRule(lineWidth));

        var documentHeadline = string.Join(" ", new[]
        {
            payload.DocumentLabel?.Trim() ?? string.Empty,
            payload.DocumentNumber?.Trim() ?? string.Empty,
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

        if (!string.IsNullOrWhiteSpace(documentHeadline))
        {
            bytes.AddRange(CmdBold(true));
            AppendLine(bytes, documentHeadline);
            bytes.AddRange(CmdBold(false));
        }
        else if (!string.IsNullOrWhiteSpace(payload.DocumentNumber))
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
        if (!string.IsNullOrWhiteSpace(payload.HashValue))
        {
            AppendLine(bytes, "Hash: " + TrimWidth(payload.HashValue, lineWidth));
        }
        if (!string.IsNullOrWhiteSpace(payload.CustomerLine))
        {
            AppendLine(bytes, "Cliente: " + payload.CustomerLine);
        }
        if (!string.IsNullOrWhiteSpace(payload.TableLine))
        {
            AppendLine(bytes, payload.TableLine);
        }

        AppendLine(bytes, HorizontalRule(lineWidth));

        foreach (var item in payload.Items)
        {
            AppendLine(bytes, item.Name);
            var left = string.Format(CultureInfo.InvariantCulture, "{0} x {1}", FormatQty(item.Qty), FormatMoney(item.UnitPrice));
            AppendLine(bytes, TwoCol(left, FormatMoney(item.LineTotal), lineWidth));
        }

        AppendLine(bytes, HorizontalRule(lineWidth));
        AppendLine(bytes, TwoCol("Subtotal", FormatMoney(payload.Totals.Subtotal), lineWidth));
        AppendLine(bytes, TwoCol("IVA", FormatMoney(payload.Totals.Tax), lineWidth));
        bytes.AddRange(CmdBold(true));
        AppendLine(bytes, TwoCol("TOTAL", FormatMoney(payload.Totals.Total), lineWidth));
        bytes.AddRange(CmdBold(false));

        if (payload.TaxLines.Count > 0)
        {
            AppendLine(bytes, HorizontalRule(lineWidth));
            bytes.AddRange(CmdBold(true));
            AppendLine(bytes, "Impostos");
            bytes.AddRange(CmdBold(false));
            foreach (var taxRow in payload.TaxLines)
            {
                if (string.IsNullOrWhiteSpace(taxRow.Label))
                {
                    continue;
                }
                AppendLine(bytes, taxRow.Label);
                AppendLine(bytes, TwoCol("Base " + FormatMoney(taxRow.Base), FormatMoney(taxRow.Tax), lineWidth));
            }
        }

        if (payload.Payments.Count > 0)
        {
            AppendLine(bytes, HorizontalRule(lineWidth));
            foreach (var payment in payload.Payments)
            {
                AppendLine(bytes, TwoCol(payment.Label, FormatMoney(payment.Amount), lineWidth));
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

        var requestedFeed = payload.FinalFeedLines <= 0 ? 8 : payload.FinalFeedLines;
        var feedLines = Math.Clamp(requestedFeed, 6, 14);
        bytes.AddRange(CmdFeed(feedLines));
        bytes.Add(0x0A);
        bytes.Add(0x0A);

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

    public byte[] BuildFiscalPdfPages(
        IReadOnlyList<string> pages,
        PrinterProfile profile,
        bool applyCut,
        string? cutModeOverride,
        bool applyDrawer,
        int? feedLines,
        int? widthDots,
        int? segmentHeight)
    {
        if (pages is null || pages.Count == 0)
        {
            throw new InvalidOperationException("No fiscal pages were provided.");
        }

        var bytes = new List<byte>(4096);
        bytes.AddRange(CmdInit());
        bytes.AddRange(CmdAlign(1));

        var defaultWidth = profile.ModeNormalized() == "network" ? 576 : 384;
        var targetWidth = NormalizePaperWidthDots(widthDots ?? defaultWidth, defaultWidth);
        var targetSegmentHeight = Math.Clamp(segmentHeight ?? 1200, 420, 2200);
        var printedPages = 0;
        foreach (var raw in pages)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var chunks = BuildRasterChunksFromBase64(raw, targetWidth, targetSegmentHeight);
            if (chunks.Count == 0)
            {
                continue;
            }
            foreach (var raster in chunks)
            {
                bytes.AddRange(raster);
                bytes.Add(0x0A);
            }
            bytes.Add(0x0A);
            printedPages += chunks.Count;
        }

        if (printedPages <= 0)
        {
            throw new InvalidOperationException("Unable to convert fiscal PDF pages to ESC/POS raster.");
        }

        bytes.AddRange(CmdAlign(0));
        if (applyDrawer && profile.CashDrawer.Enabled)
        {
            bytes.AddRange(OpenDrawer(profile));
        }

        var safeFeed = Math.Clamp(feedLines ?? 6, 4, 6);
        bytes.AddRange(CmdFeed(safeFeed));
        bytes.Add(0x0A);
        bytes.Add(0x0A);

        var cutEnabled = applyCut && profile.Cut.Enabled;
        if (cutEnabled)
        {
            var mode = string.IsNullOrWhiteSpace(cutModeOverride) ? profile.Cut.Mode : cutModeOverride;
            bytes.AddRange(Cut(mode));
        }

        return bytes.ToArray();
    }

    private static IEnumerable<byte> CmdInit() => new byte[] { 0x1B, 0x40 };

    private static IEnumerable<byte> CmdAlign(int mode) => new byte[] { 0x1B, 0x61, (byte)Math.Clamp(mode, 0, 2) };

    private static IEnumerable<byte> CmdBold(bool on) => new byte[] { 0x1B, 0x45, (byte)(on ? 1 : 0) };

    private static IEnumerable<byte> CmdFeed(int lines) => new byte[] { 0x1B, 0x64, (byte)Math.Clamp(lines, 0, 10) };

    private static string HorizontalRule(int width) => new('-', Math.Clamp(width, 24, 64));

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
            const int maxWidth = 176;
            const int maxHeight = 68;

            var widthScale = source.Width > maxWidth
                ? (decimal)maxWidth / source.Width
                : 1m;
            var heightScale = source.Height > maxHeight
                ? (decimal)maxHeight / source.Height
                : 1m;
            var scale = Math.Min(widthScale, heightScale);
            if (scale <= 0)
            {
                scale = 1m;
            }

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

    private static List<byte[]> BuildRasterChunksFromBase64(string rawBase64, int maxWidth, int segmentHeight)
    {
        var chunks = new List<byte[]>();
        var sourceBytes = DecodeBase64Image(rawBase64);
        if (sourceBytes is null || sourceBytes.Length == 0)
        {
            return chunks;
        }

        using var stream = new MemoryStream(sourceBytes);
        using var source = new Bitmap(stream);
        if (source.Width <= 0 || source.Height <= 0)
        {
            return chunks;
        }

        var widthLimit = Math.Max(120, Math.Min(640, maxWidth));
        var safeSegment = Math.Clamp(segmentHeight, 420, 2200);
        var scale = (decimal)widthLimit / Math.Max(1, source.Width);
        if (scale <= 0)
        {
            scale = 1m;
        }
        var width = widthLimit;
        var height = Math.Max(1, (int)Math.Round(source.Height * (double)scale));
        using var scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.Clear(Color.White);
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, width, height);
        }

        for (var y = 0; y < height; y += safeSegment)
        {
            var chunkHeight = Math.Min(safeSegment, height - y);
            using var chunk = new Bitmap(width, chunkHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(chunk))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(
                    scaled,
                    new Rectangle(0, 0, width, chunkHeight),
                    new Rectangle(0, y, width, chunkHeight),
                    GraphicsUnit.Pixel
                );
            }
            var raster = BuildRaster(chunk);
            if (raster.Length > 0)
            {
                chunks.Add(raster);
            }
        }
        return chunks;
    }

    private static int NormalizePaperWidthDots(int value, int fallback)
    {
        var allowed = new[] { 384, 512, 576, 640 };
        var target = value > 0 ? value : fallback;
        if (allowed.Contains(target))
        {
            return target;
        }

        var nearest = fallback;
        var bestDelta = int.MaxValue;
        foreach (var candidate in allowed)
        {
            var delta = Math.Abs(candidate - target);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                nearest = candidate;
            }
        }
        return nearest;
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

        var luma = new double[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                luma[(y * width) + x] = ((pixel.R * 299) + (pixel.G * 587) + (pixel.B * 114)) / 1000.0;
            }
        }

        const double threshold = 162.0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var idx = (y * width) + x;
                var oldPixel = luma[idx];
                if (oldPixel < 0) oldPixel = 0;
                if (oldPixel > 255) oldPixel = 255;
                var newPixel = oldPixel < threshold ? 0.0 : 255.0;
                luma[idx] = newPixel;
                var error = oldPixel - newPixel;

                if (x + 1 < width)
                {
                    luma[idx + 1] += error * (7.0 / 16.0);
                }
                if (y + 1 < height)
                {
                    if (x > 0)
                    {
                        luma[idx + width - 1] += error * (3.0 / 16.0);
                    }
                    luma[idx + width] += error * (5.0 / 16.0);
                    if (x + 1 < width)
                    {
                        luma[idx + width + 1] += error * (1.0 / 16.0);
                    }
                }
            }
        }

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
                    var idx = (y * width) + x;
                    if (luma[idx] < 128.0)
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
