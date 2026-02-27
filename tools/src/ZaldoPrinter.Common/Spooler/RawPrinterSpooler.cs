using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ZaldoPrinter.Common.Spooler;

public static class RawPrinterSpooler
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DOC_INFO_1
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDocName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pOutputFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDataType;
    }

    [DllImport("winspool.Drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, ref DOC_INFO_1 pDocInfo);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, out int dwWritten);

    public static void WriteRaw(string printerName, byte[] bytes, string documentName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            throw new ArgumentException("Printer name is required.", nameof(printerName));
        }

        if (bytes.Length == 0)
        {
            return;
        }

        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero) || handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenPrinter failed.");
        }

        try
        {
            var info = new DOC_INFO_1
            {
                pDocName = string.IsNullOrWhiteSpace(documentName) ? "Zaldo Printer" : documentName,
                pOutputFile = string.Empty,
                pDataType = "RAW",
            };

            if (!StartDocPrinter(handle, 1, ref info))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "StartDocPrinter failed.");
            }

            try
            {
                if (!StartPagePrinter(handle))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "StartPagePrinter failed.");
                }

                try
                {
                    if (!WritePrinter(handle, bytes, bytes.Length, out var written))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "WritePrinter failed.");
                    }

                    if (written != bytes.Length)
                    {
                        throw new IOException($"Partial write to printer spooler ({written}/{bytes.Length}).");
                    }
                }
                finally
                {
                    EndPagePrinter(handle);
                }
            }
            finally
            {
                EndDocPrinter(handle);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }
}
