using ZaldoPrinter.Common.Models;

namespace ZaldoPrinter.ConfigApp;

internal sealed class AddPrinterDialog : Form
{
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _usbPrinter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _ip = new() { Dock = DockStyle.Fill, PlaceholderText = "192.168.1.50" };
    private readonly NumericUpDown _port = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 65535, Value = 9100 };

    private readonly Button _ok = new() { Text = "Adicionar", DialogResult = DialogResult.OK };
    private readonly Button _cancel = new() { Text = "Cancelar", DialogResult = DialogResult.Cancel };

    public AddPrinterDialog(IReadOnlyList<InstalledPrinterInfo> installed)
    {
        Text = "Adicionar impressora";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 520;
        Height = 290;

        _mode.Items.AddRange(new object[] { "USB (Windows)", "Rede (TCP/IP 9100)" });
        _mode.SelectedIndex = 0;

        foreach (var p in installed)
        {
            _usbPrinter.Items.Add(p.PrinterName);
        }
        if (_usbPrinter.Items.Count > 0)
        {
            _usbPrinter.SelectedIndex = 0;
            _name.Text = _usbPrinter.SelectedItem?.ToString() ?? string.Empty;
        }

        _usbPrinter.SelectedIndexChanged += (_, _) =>
        {
            if (_mode.SelectedIndex == 0)
            {
                _name.Text = _usbPrinter.SelectedItem?.ToString() ?? string.Empty;
            }
        };

        _mode.SelectedIndexChanged += (_, _) => ApplyMode();

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(grid, 0, "Modo", _mode);
        AddRow(grid, 1, "Nome no POS", _name);
        AddRow(grid, 2, "Impressora (USB)", _usbPrinter);
        AddRow(grid, 3, "IP (Rede)", _ip);
        AddRow(grid, 4, "Porta (Rede)", _port);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        buttons.Controls.Add(_ok);
        buttons.Controls.Add(_cancel);
        grid.Controls.Add(buttons, 1, 5);

        Controls.Add(grid);

        AcceptButton = _ok;
        CancelButton = _cancel;

        ApplyMode();
    }

    public bool TryBuildProfile(out PrinterProfile profile, out string error)
    {
        error = string.Empty;
        profile = new PrinterProfile();

        var mode = _mode.SelectedIndex == 1 ? "network" : "usb";
        var name = (_name.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Informe o nome.";
            return false;
        }

        if (mode == "usb")
        {
            var printerName = _usbPrinter.SelectedItem?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(printerName))
            {
                error = "Selecione uma impressora instalada no Windows.";
                return false;
            }

            profile = CreateUsbProfile(name, printerName);
            return true;
        }

        var ip = (_ip.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(ip))
        {
            error = "Informe o IP da impressora (modo rede).";
            return false;
        }

        profile = CreateNetworkProfile(name, ip, (int)_port.Value);
        return true;
    }

    private void ApplyMode()
    {
        var isUsb = _mode.SelectedIndex == 0;
        _usbPrinter.Enabled = isUsb;
        _ip.Enabled = !isUsb;
        _port.Enabled = !isUsb;
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 8, 0, 2) }, 0, row);
        control.Margin = new Padding(0, 4, 0, 2);
        panel.Controls.Add(control, 1, row);
    }

    private static PrinterProfile CreateUsbProfile(string name, string printerName)
    {
        return new PrinterProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Enabled = true,
            Mode = "usb",
            Usb = new UsbSettings { PrinterName = printerName },
            Network = new NetworkSettings { Ip = string.Empty, Port = 9100 },
            CashDrawer = new CashDrawerSettings
            {
                Enabled = true,
                KickPulse = new KickPulseSettings { M = 0, T1 = 25, T2 = 250 },
            },
            Cut = new CutSettings { Enabled = true, Mode = "partial" },
        };
    }

    private static PrinterProfile CreateNetworkProfile(string name, string ip, int port)
    {
        return new PrinterProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Enabled = true,
            Mode = "network",
            Usb = new UsbSettings { PrinterName = string.Empty },
            Network = new NetworkSettings { Ip = ip, Port = port <= 0 ? 9100 : port },
            CashDrawer = new CashDrawerSettings
            {
                Enabled = false,
                KickPulse = new KickPulseSettings { M = 0, T1 = 25, T2 = 250 },
            },
            Cut = new CutSettings { Enabled = false, Mode = "partial" },
        };
    }
}
