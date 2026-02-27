using ZaldoPrinter.Common.Models;

namespace ZaldoPrinter.ConfigApp;

public sealed class MainForm : Form
{
    private readonly ServiceApiClient _api = new();
    private AppConfig _config = AppConfig.CreateDefault();
    private IReadOnlyList<InstalledPrinterInfo> _installedPrinters = Array.Empty<InstalledPrinterInfo>();

    private readonly Label _serviceStatus = new() { AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
    private readonly TextBox _tokenText = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly ListBox _installedList = new() { Dock = DockStyle.Fill };
    private readonly ListBox _lastPrintList = new() { Dock = DockStyle.Fill };
    private readonly ListBox _errorsList = new() { Dock = DockStyle.Fill };
    private readonly ListBox _profilesList = new() { Dock = DockStyle.Fill };

    private readonly TextBox _idText = new() { Dock = DockStyle.Fill };
    private readonly TextBox _nameText = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _modeCombo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _usbCombo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _ipText = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _portNumber = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 65535, Value = 9100 };

    private readonly CheckBox _enabledCheck = new() { Text = "Perfil ativo", AutoSize = true };
    private readonly CheckBox _drawerCheck = new() { Text = "Abrir gaveta", AutoSize = true };
    private readonly NumericUpDown _pulseM = new() { Minimum = 0, Maximum = 1, Value = 0 };
    private readonly NumericUpDown _pulseT1 = new() { Minimum = 0, Maximum = 255, Value = 25 };
    private readonly NumericUpDown _pulseT2 = new() { Minimum = 0, Maximum = 255, Value = 250 };
    private readonly CheckBox _cutCheck = new() { Text = "Corte automatico", AutoSize = true };
    private readonly ComboBox _cutModeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly Label _defaultLabel = new() { AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };

    private readonly Button _btnRefreshAll = new() { Text = "Atualizar status" };
    private readonly Button _btnRegenerateToken = new() { Text = "Gerar novo token (pareamento)" };
    private readonly Button _btnAddProfile = new() { Text = "Adicionar impressora (selecionada)" };
    private readonly Button _btnAddAllProfiles = new() { Text = "Adicionar todas do Windows" };
    private readonly Button _btnRemoveProfile = new() { Text = "Remover" };
    private readonly Button _btnSetDefault = new() { Text = "Definir padrao" };
    private readonly Button _btnSaveConfig = new() { Text = "Guardar configuracao" };

    private readonly Button _btnTestPrint = new() { Text = "Teste: imprimir" };
    private readonly Button _btnTestDrawer = new() { Text = "Teste: abrir gaveta" };
    private readonly Button _btnTestCut = new() { Text = "Teste: cortar papel" };
    private readonly ToolTip _tip = new() { AutomaticDelay = 250, AutoPopDelay = 12000, InitialDelay = 250, ReshowDelay = 150 };

    private bool _loadingProfile;
    public MainForm()
    {
        Text = "Zaldo Printer";
        Width = 1280;
        Height = 840;
        StartPosition = FormStartPosition.CenterScreen;

        _modeCombo.Items.AddRange(new object[] { "usb", "network" });
        _cutModeCombo.Items.AddRange(new object[] { "partial", "full" });
        _modeCombo.SelectedIndex = 0;
        _cutModeCombo.SelectedIndex = 0;

        BuildLayout();
        WireEvents();

        _tip.SetToolTip(_btnTestPrint, "Imprime um talão de teste no perfil selecionado.");
        _tip.SetToolTip(_btnTestDrawer, "Envia o pulso de gaveta (se suportado) no perfil selecionado.");
        _tip.SetToolTip(_btnTestCut, "Envia comando de corte (se suportado) no perfil selecionado.");
        _tip.SetToolTip(_btnRegenerateToken, "Gera um novo token. Depois, cole este token no POS para parear.");
        _tip.SetToolTip(_btnAddProfile, "Cria um perfil usando a impressora selecionada no Windows.");
        _tip.SetToolTip(_btnAddAllProfiles, "Cria um perfil para cada impressora instalada no Windows (útil para setup inicial).");
Shown += async (_, _) => await RefreshAllAsync();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));

        root.Controls.Add(BuildLeftPanel(), 0, 0);
        root.Controls.Add(BuildMiddlePanel(), 1, 0);
        root.Controls.Add(BuildEditorPanel(), 2, 0);

        Controls.Add(root);
    }

    private Control BuildLeftPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 13,
            ColumnCount = 1,
            Padding = new Padding(8),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label { Text = "Servico", AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) }, 0, 0);
        panel.Controls.Add(_serviceStatus, 0, 1);
        panel.Controls.Add(new Label { Text = "Token de pareamento", AutoSize = true, Margin = new Padding(0, 12, 0, 2) }, 0, 2);
        panel.Controls.Add(_tokenText, 0, 3);

        var tokenButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        tokenButtons.Controls.Add(_btnRegenerateToken);
        panel.Controls.Add(tokenButtons, 0, 4);

        panel.Controls.Add(new Label { Text = "Impressoras instaladas no Windows", AutoSize = true, Margin = new Padding(0, 12, 0, 2) }, 0, 5);
        panel.Controls.Add(_installedList, 0, 6);

        panel.Controls.Add(new Label { Text = "Ultima impressao por impressora", AutoSize = true, Margin = new Padding(0, 12, 0, 2) }, 0, 7);
        panel.Controls.Add(_lastPrintList, 0, 8);

        panel.Controls.Add(new Label { Text = "Erros recentes", AutoSize = true, Margin = new Padding(0, 12, 0, 2) }, 0, 9);
        panel.Controls.Add(_errorsList, 0, 10);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        bottom.Controls.Add(_btnRefreshAll);
        panel.Controls.Add(bottom, 0, 11);

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Modo USB usa o nome instalado no spooler. Modo Rede usa IP/porta 9100.",
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 8, 0, 0),
        }, 0, 12);

        return panel;
    }

    private Control BuildMiddlePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 8,
            ColumnCount = 1,
            Padding = new Padding(8),
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label { Text = "Perfis configurados", AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) }, 0, 0);
        panel.Controls.Add(_defaultLabel, 0, 1);
        panel.Controls.Add(_profilesList, 0, 2);

        var row1 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        row1.Controls.Add(_btnAddProfile);
        row1.Controls.Add(_btnAddAllProfiles);
        row1.Controls.Add(_btnRemoveProfile);
        row1.Controls.Add(_btnSetDefault);
        panel.Controls.Add(row1, 0, 3);

        panel.Controls.Add(new Label { Text = "Testes (perfil selecionado)", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 10, 0, 2) }, 0, 4);

        var row2 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        row2.Controls.Add(_btnTestPrint);
        row2.Controls.Add(_btnTestDrawer);
        row2.Controls.Add(_btnTestCut);
        panel.Controls.Add(row2, 0, 5);

        var row3 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        row3.Controls.Add(_btnSaveConfig);
        panel.Controls.Add(row3, 0, 6);

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Fila por impressora e retries sao controlados no servico.",
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 8, 0, 0),
        }, 0, 7);

        return panel;
    }

    private Control BuildEditorPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 14,
            Padding = new Padding(8),
            AutoScroll = true,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(panel, 0, "ID", _idText);
        AddRow(panel, 1, "Nome", _nameText);
        AddRow(panel, 2, "Modo", _modeCombo);
        AddRow(panel, 3, "USB - printerName", _usbCombo);
        AddRow(panel, 4, "Rede - IP", _ipText);
        AddRow(panel, 5, "Rede - Porta", _portNumber);

        panel.Controls.Add(_enabledCheck, 1, 6);
        panel.Controls.Add(_drawerCheck, 1, 7);

        var pulsePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        pulsePanel.Controls.Add(new Label { Text = "m" });
        pulsePanel.Controls.Add(_pulseM);
        pulsePanel.Controls.Add(new Label { Text = "t1" });
        pulsePanel.Controls.Add(_pulseT1);
        pulsePanel.Controls.Add(new Label { Text = "t2" });
        pulsePanel.Controls.Add(_pulseT2);
        AddRow(panel, 8, "Pulso gaveta", pulsePanel);

        panel.Controls.Add(_cutCheck, 1, 9);
        AddRow(panel, 10, "Modo corte", _cutModeCombo);

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Text = "Selecione uma impressora instalada para USB. Para Rede, informe IP e porta. O id pode ser personalizado, mas deve ser unico.",
            ForeColor = Color.DimGray,
        };
        panel.Controls.Add(help, 1, 11);

        return panel;
    }

    private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
    {
        if (panel.RowStyles.Count <= row)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        panel.Controls.Add(new Label
        {
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 2),
        }, 0, row);

        control.Margin = new Padding(0, 4, 0, 2);
        panel.Controls.Add(control, 1, row);
    }

    private void WireEvents()
    {
        _btnRefreshAll.Click += async (_, _) => await RefreshAllAsync();
        _btnRegenerateToken.Click += async (_, _) => await RegenerateTokenAsync();

        _btnAddProfile.Click += (_, _) => AddProfileFromSelection();
        _btnAddAllProfiles.Click += (_, _) => AddAllProfilesFromWindows();

        _installedList.SelectedIndexChanged += (_, _) => SyncInstalledSelectionToUsb();
        _btnRemoveProfile.Click += (_, _) => RemoveSelectedProfile();
        _btnSetDefault.Click += (_, _) => SetDefaultProfile();
        _btnSaveConfig.Click += async (_, _) => await SaveConfigAsync();

        _btnTestPrint.Click += async (_, _) => await RunPrinterActionAsync(id => _api.TestPrintAsync(id));
        _btnTestDrawer.Click += async (_, _) => await RunPrinterActionAsync(id => _api.TestDrawerAsync(id));
        _btnTestCut.Click += async (_, _) => await RunPrinterActionAsync(id => _api.TestCutAsync(id));

        _profilesList.SelectedIndexChanged += (_, _) => LoadSelectedProfileIntoEditor();
        _modeCombo.SelectedIndexChanged += (_, _) => ToggleModeFields();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            var health = await _api.HealthAsync();
            var pendingTotal = health.PendingByPrinter.Values.Sum();
            _serviceStatus.Text = health.Ok
                ? $"Online (127.0.0.1:16161) | Fila pendente: {pendingTotal}"
                : "Offline: " + health.Message;
            _serviceStatus.ForeColor = health.Ok ? Color.DarkGreen : Color.DarkRed;
            BindHealthDiagnostics(health);

            _config = await _api.GetConfigAsync();
            _api.SetToken(_config.PairingToken);
            _tokenText.Text = _config.PairingToken;

            _installedPrinters = await _api.GetInstalledPrintersAsync();
            BindInstalledPrinters();
            BindProfiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao carregar dados do servico:\n" + ex.Message, "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task SaveConfigAsync()
    {
        try
        {
            ApplyEditorToSelectedProfile();
            _config = await _api.SaveConfigAsync(_config);
            _api.SetToken(_config.PairingToken);
            _tokenText.Text = _config.PairingToken;
            BindProfiles();
            MessageBox.Show(this, "Configuracao guardada com sucesso.", "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao guardar configuracao:\n" + ex.Message, "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RegenerateTokenAsync()
    {
        if (MessageBox.Show(this, "Gerar novo token de pareamento?", "Zaldo Printer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            ApplyEditorToSelectedProfile();
            var newToken = await _api.RegenerateTokenAsync();
            if (string.IsNullOrWhiteSpace(newToken))
            {
                throw new InvalidOperationException("Token vazio retornado pelo servico.");
            }

            _config.PairingToken = newToken;
            _api.SetToken(newToken);
            _tokenText.Text = newToken;
            MessageBox.Show(this, "Novo token gerado.", "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao regenerar token:\n" + ex.Message, "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BindInstalledPrinters()
    {
        _installedList.Items.Clear();
        _usbCombo.Items.Clear();

        foreach (var printer in _installedPrinters)
        {
            var label = printer.IsDefault
                ? $"{printer.PrinterName} [Default]"
                : printer.PrinterName;
            _installedList.Items.Add(label);
            _usbCombo.Items.Add(printer.PrinterName);
        }

        if (_usbCombo.Items.Count > 0 && _usbCombo.SelectedIndex < 0)
        {
            _usbCombo.SelectedIndex = 0;
        }
    }

    private void BindHealthDiagnostics(ServiceHealthInfo health)
    {
        _lastPrintList.Items.Clear();
        _errorsList.Items.Clear();

        if (health.LastPrintByPrinter.Count == 0)
        {
            _lastPrintList.Items.Add("Sem impressões ainda.");
        }
        else
        {
            foreach (var entry in health.LastPrintByPrinter.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                var value = entry.Value;
                var status = value.Ok ? "OK" : "ERRO";
                var when = value.CompletedAt == default
                    ? "--"
                    : value.CompletedAt.ToLocalTime().ToString("dd/MM HH:mm:ss");
                var pending = health.PendingByPrinter.TryGetValue(entry.Key, out var queued) ? queued : 0;
                _lastPrintList.Items.Add($"{entry.Key} | {status} | {value.Operation} | {when} | fila={pending}");
            }
        }

        if (health.RecentErrors.Count == 0)
        {
            _errorsList.Items.Add("Sem erros recentes.");
        }
        else
        {
            foreach (var error in health.RecentErrors)
            {
                _errorsList.Items.Add(error);
            }
        }
    }

    private void BindProfiles()
    {
        _profilesList.Items.Clear();

        foreach (var profile in _config.Printers)
        {
            var suffix = string.Equals(profile.Id, _config.DefaultPrinterId, StringComparison.OrdinalIgnoreCase) ? " [PADRAO]" : string.Empty;
            _profilesList.Items.Add($"{profile.Name} ({profile.Id}){suffix}");
        }

        _defaultLabel.Text = string.IsNullOrWhiteSpace(_config.DefaultPrinterId)
            ? "Padrao: (nao definido)"
            : "Padrao: " + _config.DefaultPrinterId;

        if (_profilesList.Items.Count > 0)
        {
            if (_profilesList.SelectedIndex < 0)
            {
                _profilesList.SelectedIndex = 0;
            }
        }
        else
        {
            ClearEditor();
        }

        var hasProfile = _config.Printers.Count > 0;
        _btnTestPrint.Enabled = hasProfile;
        _btnTestDrawer.Enabled = hasProfile;
        _btnTestCut.Enabled = hasProfile;
    }

    private void AddProfileFromSelection()
    {
        var selectedInstalled = _usbCombo.SelectedItem?.ToString() ?? _installedPrinters.FirstOrDefault()?.PrinterName ?? "";
        var profile = new PrinterProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = selectedInstalled.Length > 0 ? selectedInstalled : "Nova Impressora",
            Enabled = true,
            Mode = "usb",
            Usb = new UsbSettings { PrinterName = selectedInstalled },
            Network = new NetworkSettings { Ip = string.Empty, Port = 9100 },
            CashDrawer = new CashDrawerSettings
            {
                Enabled = true,
                KickPulse = new KickPulseSettings { M = 0, T1 = 25, T2 = 250 },
            },
            Cut = new CutSettings { Enabled = true, Mode = "partial" },
        };

        _config.Printers.Add(profile);
        if (string.IsNullOrWhiteSpace(_config.DefaultPrinterId))
        {
            _config.DefaultPrinterId = profile.Id;
        }

        BindProfiles();
        _profilesList.SelectedIndex = _config.Printers.Count - 1;
    }

    // Cria um perfil (configurado) para cada impressora instalada no Windows.
    // Evita duplicar: se ja existir um perfil USB com o mesmo PrinterName, nao cria novamente.
    private void AddAllProfilesFromWindows()
    {
        if (_config is null)
        {
            MessageBox.Show(this, "Configuracao ainda nao carregada. Clique em Recarregar.", "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_installedPrinters is null || _installedPrinters.Count == 0)
        {
            MessageBox.Show(this, "Nenhuma impressora instalada foi encontrada no Windows.", "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var existingUsbNames = new HashSet<string>(
            _config.Printers
                .Where(p => string.Equals(p.Mode, "usb", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Usb?.PrinterName ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n)),
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var p in _installedPrinters)
        {
            var name = p.PrinterName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (existingUsbNames.Contains(name))
            {
                continue;
            }

            var profile = new PrinterProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Enabled = true,
                Mode = "usb",
                Usb = new UsbSettings { PrinterName = name },
                Network = new NetworkSettings { Ip = string.Empty, Port = 9100 },
                CashDrawer = new CashDrawerSettings
                {
                    Enabled = true,
                    KickPulse = new KickPulseSettings { M = 0, T1 = 25, T2 = 250 },
                },
                Cut = new CutSettings { Enabled = true, Mode = "partial" },
            };

            _config.Printers.Add(profile);
            existingUsbNames.Add(name);
            added++;

            if (string.IsNullOrWhiteSpace(_config.DefaultPrinterId))
            {
                _config.DefaultPrinterId = profile.Id;
            }
        }

        BindProfiles();

        MessageBox.Show(
            this,
            added > 0 ? $"Perfis adicionados: {added}." : "Nenhuma impressora nova para adicionar (todas ja possuem perfil).",
            "Zaldo Printer",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // Mantem o combo USB alinhado com a selecao da lista de impressoras instaladas.
    private void SyncInstalledSelectionToUsb()
    {
        var selected = _installedList.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        for (var i = 0; i < _usbCombo.Items.Count; i++)
        {
            if (string.Equals(_usbCombo.Items[i]?.ToString(), selected, StringComparison.OrdinalIgnoreCase))
            {
                _usbCombo.SelectedIndex = i;
                return;
            }
        }
    }

    private void RemoveSelectedProfile()
    {
        var index = _profilesList.SelectedIndex;
        if (index < 0 || index >= _config.Printers.Count)
        {
            return;
        }

        var removed = _config.Printers[index];
        _config.Printers.RemoveAt(index);

        if (string.Equals(_config.DefaultPrinterId, removed.Id, StringComparison.OrdinalIgnoreCase))
        {
            _config.DefaultPrinterId = _config.Printers.FirstOrDefault()?.Id ?? string.Empty;
        }

        BindProfiles();
    }

    private void SetDefaultProfile()
    {
        ApplyEditorToSelectedProfile();
        var index = _profilesList.SelectedIndex;
        if (index < 0 || index >= _config.Printers.Count)
        {
            return;
        }

        _config.DefaultPrinterId = _config.Printers[index].Id;
        BindProfiles();
    }

    private async Task RunPrinterActionAsync(Func<string, Task<(bool ok, string message)>> action)
    {
        ApplyEditorToSelectedProfile();
        var index = _profilesList.SelectedIndex;
        if (index < 0 || index >= _config.Printers.Count)
        {
            MessageBox.Show(this, "Selecione um perfil.", "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var printerId = _config.Printers[index].Id;
        var result = await action(printerId);
        if (result.ok)
        {
            MessageBox.Show(this, "Comando enviado com sucesso.", "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, "Falha no comando:\n" + result.message, "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadSelectedProfileIntoEditor()
    {
        if (_loadingProfile)
        {
            return;
        }

        ApplyEditorToSelectedProfile();

        var index = _profilesList.SelectedIndex;
        if (index < 0 || index >= _config.Printers.Count)
        {
            ClearEditor();
            return;
        }

        _loadingProfile = true;
        try
        {
            var profile = _config.Printers[index];
            _idText.Text = profile.Id;
            _nameText.Text = profile.Name;
            _modeCombo.SelectedItem = profile.ModeNormalized();
            _usbCombo.SelectedItem = profile.Usb.PrinterName;
            _ipText.Text = profile.Network.Ip;
            _portNumber.Value = Math.Clamp(profile.Network.Port <= 0 ? 9100 : profile.Network.Port, 1, 65535);
            _enabledCheck.Checked = profile.Enabled;
            _drawerCheck.Checked = profile.CashDrawer.Enabled;
            _pulseM.Value = Math.Clamp(profile.CashDrawer.KickPulse.M, 0, 1);
            _pulseT1.Value = Math.Clamp(profile.CashDrawer.KickPulse.T1, 0, 255);
            _pulseT2.Value = Math.Clamp(profile.CashDrawer.KickPulse.T2, 0, 255);
            _cutCheck.Checked = profile.Cut.Enabled;
            _cutModeCombo.SelectedItem = string.Equals(profile.Cut.Mode, "full", StringComparison.OrdinalIgnoreCase) ? "full" : "partial";
            ToggleModeFields();
        }
        finally
        {
            _loadingProfile = false;
        }
    }

    private void ApplyEditorToSelectedProfile()
    {
        if (_loadingProfile)
        {
            return;
        }

        var index = _profilesList.SelectedIndex;
        if (index < 0 || index >= _config.Printers.Count)
        {
            return;
        }

        var profile = _config.Printers[index];
        profile.Id = string.IsNullOrWhiteSpace(_idText.Text) ? profile.Id : _idText.Text.Trim();
        profile.Name = _nameText.Text?.Trim() ?? string.Empty;
        profile.Mode = (_modeCombo.SelectedItem?.ToString() ?? "usb").Trim().ToLowerInvariant();
        profile.Usb.PrinterName = _usbCombo.SelectedItem?.ToString() ?? string.Empty;
        profile.Network.Ip = _ipText.Text?.Trim() ?? string.Empty;
        profile.Network.Port = (int)_portNumber.Value;
        profile.Enabled = _enabledCheck.Checked;
        profile.CashDrawer.Enabled = _drawerCheck.Checked;
        profile.CashDrawer.KickPulse.M = (int)_pulseM.Value;
        profile.CashDrawer.KickPulse.T1 = (int)_pulseT1.Value;
        profile.CashDrawer.KickPulse.T2 = (int)_pulseT2.Value;
        profile.Cut.Enabled = _cutCheck.Checked;
        profile.Cut.Mode = (_cutModeCombo.SelectedItem?.ToString() ?? "partial").Trim().ToLowerInvariant();

        _config.Printers[index] = profile;
    }

    private void ToggleModeFields()
    {
        var mode = (_modeCombo.SelectedItem?.ToString() ?? "usb").Trim().ToLowerInvariant();
        var isUsb = mode == "usb";
        _usbCombo.Enabled = isUsb;
        _ipText.Enabled = !isUsb;
        _portNumber.Enabled = !isUsb;
    }

    private void ClearEditor()
    {
        _loadingProfile = true;
        try
        {
            _idText.Text = string.Empty;
            _nameText.Text = string.Empty;
            _modeCombo.SelectedIndex = 0;
            if (_usbCombo.Items.Count > 0)
            {
                _usbCombo.SelectedIndex = 0;
            }
            _ipText.Text = string.Empty;
            _portNumber.Value = 9100;
            _enabledCheck.Checked = true;
            _drawerCheck.Checked = true;
            _pulseM.Value = 0;
            _pulseT1.Value = 25;
            _pulseT2.Value = 250;
            _cutCheck.Checked = true;
            _cutModeCombo.SelectedIndex = 0;
            ToggleModeFields();
        }
        finally
        {
            _loadingProfile = false;
        }
    }
}
