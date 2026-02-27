using ZaldoPrinter.Common.Models;

namespace ZaldoPrinter.ConfigApp;

public sealed class MainForm : Form
{
    private readonly ServiceApiClient _api = new();
    private AppConfig _config = AppConfig.CreateDefault();
    private IReadOnlyList<InstalledPrinterInfo> _installedPrinters = Array.Empty<InstalledPrinterInfo>();

    private readonly Label _serviceStatus = new() { AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };

    private readonly TextBox _tokenText = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Button _btnCopyToken = new() { Text = "Copiar token" };
    private readonly Button _btnRegenerateToken = new() { Text = "Gerar novo token" };

    private readonly ListBox _installedList = new() { Dock = DockStyle.Fill };
    private readonly ListBox _lastPrintList = new() { Dock = DockStyle.Fill };
    private readonly ListBox _errorsList = new() { Dock = DockStyle.Fill };

    private readonly ListBox _profilesList = new() { Dock = DockStyle.Fill };
    private readonly Label _defaultLabel = new() { AutoSize = true, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };

    private readonly Button _btnRefreshAll = new() { Text = "Atualizar" };
    private readonly Button _btnAddProfile = new() { Text = "Adicionar impressora" };
    private readonly Button _btnRemoveProfile = new() { Text = "Remover" };
    private readonly Button _btnSetDefault = new() { Text = "Definir como padrão" };
    private readonly Button _btnSaveConfig = new() { Text = "Guardar configurações" };

    private readonly Button _btnTestPrint = new() { Text = "Testar impressão (talão)" };
    private readonly Button _btnTestDrawer = new() { Text = "Testar gaveta" };
    private readonly Button _btnTestCut = new() { Text = "Testar corte" };

    // Editor
    private readonly TextBox _idText = new() { Dock = DockStyle.Fill };
    private readonly TextBox _nameText = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _modeCombo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _usbCombo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _ipText = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _portNumber = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 65535, Value = 9100 };

    private readonly CheckBox _enabledCheck = new() { Text = "Perfil ativo", AutoSize = true };
    private readonly CheckBox _drawerCheck = new() { Text = "Abrir gaveta ao imprimir", AutoSize = true };
    private readonly NumericUpDown _pulseM = new() { Minimum = 0, Maximum = 1, Value = 0 };
    private readonly NumericUpDown _pulseT1 = new() { Minimum = 0, Maximum = 255, Value = 25 };
    private readonly NumericUpDown _pulseT2 = new() { Minimum = 0, Maximum = 255, Value = 250 };

    private readonly CheckBox _cutCheck = new() { Text = "Cortar no fim do talão", AutoSize = true };
    private readonly ComboBox _cutModeCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private bool _loadingProfile;
    private NotifyIcon? _trayIcon;
    private bool _allowExit;
    private bool _trayHintShown;

    public MainForm()
    {
        Text = "Zaldo Printer Config";
        Width = 1140;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 620);

        _modeCombo.Items.AddRange(new object[] { "usb", "network" });
        _cutModeCombo.Items.AddRange(new object[] { "partial", "full" });
        _modeCombo.SelectedIndex = 0;
        _cutModeCombo.SelectedIndex = 0;

        BuildLayout();
        InitializeTrayIcon();
        WireEvents();

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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        root.Controls.Add(BuildLeftPanel(), 0, 0);
        root.Controls.Add(BuildMiddlePanel(), 1, 0);
        root.Controls.Add(BuildEditorPanel(), 2, 0);

        Controls.Add(root);
    }

    private Control BuildLeftPanel()
    {
        var box = new GroupBox { Text = "Serviço e diagnósticos", Dock = DockStyle.Fill, Padding = new Padding(10) };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 10,
            ColumnCount = 1,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            AutoSize = true,
            Text = "Estado do serviço (API local)",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        };

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(_serviceStatus, 0, 1);

        var tokenRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        tokenRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tokenRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tokenRow.Controls.Add(_tokenText, 0, 0);
        tokenRow.Controls.Add(_btnCopyToken, 1, 0);

        panel.Controls.Add(new Label { Text = "Token de pareamento (colar no POS)", AutoSize = true, Margin = new Padding(0, 10, 0, 2) }, 0, 2);
        panel.Controls.Add(tokenRow, 0, 3);

        var tokenButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        tokenButtons.Controls.Add(_btnRegenerateToken);
        tokenButtons.Controls.Add(_btnRefreshAll);
        panel.Controls.Add(tokenButtons, 0, 4);

        panel.Controls.Add(new Label { Text = "Impressoras instaladas no Windows", AutoSize = true, Margin = new Padding(0, 12, 0, 2) }, 0, 5);
        panel.Controls.Add(_installedList, 0, 6);

        panel.Controls.Add(new Label { Text = "Última operação por impressora", AutoSize = true, Margin = new Padding(0, 12, 0, 2) }, 0, 7);
        panel.Controls.Add(_lastPrintList, 0, 8);

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Dica: o serviço escuta em http://127.0.0.1:16161/health",
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 10, 0, 0),
        }, 0, 9);

        box.Controls.Add(panel);
        return box;
    }

    private Control BuildMiddlePanel()
    {
        var box = new GroupBox { Text = "Impressoras configuradas", Dock = DockStyle.Fill, Padding = new Padding(10) };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 8,
            ColumnCount = 1,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(_defaultLabel, 0, 0);
        panel.Controls.Add(_profilesList, 0, 1);

        var row1 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        row1.Controls.Add(_btnAddProfile);
        row1.Controls.Add(_btnRemoveProfile);
        row1.Controls.Add(_btnSetDefault);
        panel.Controls.Add(row1, 0, 2);

        panel.Controls.Add(new Label { Text = "Testes rápidos (para o perfil selecionado)", AutoSize = true, Margin = new Padding(0, 10, 0, 2) }, 0, 3);

        var row2 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        row2.Controls.Add(_btnTestPrint);
        row2.Controls.Add(_btnTestDrawer);
        row2.Controls.Add(_btnTestCut);
        panel.Controls.Add(row2, 0, 4);

        panel.Controls.Add(new Label { Text = "Erros recentes", AutoSize = true, Margin = new Padding(0, 12, 0, 2) }, 0, 5);
        panel.Controls.Add(_errorsList, 0, 6);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        footer.Controls.Add(_btnSaveConfig);
        panel.Controls.Add(footer, 0, 7);

        box.Controls.Add(panel);
        return box;
    }

    private Control BuildEditorPanel()
    {
        var box = new GroupBox { Text = "Detalhes do perfil", Dock = DockStyle.Fill, Padding = new Padding(10) };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 18,
            AutoScroll = true,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(panel, 0, "ID", _idText);
        AddRow(panel, 1, "Nome", _nameText);
        AddRow(panel, 2, "Modo", _modeCombo);
        AddRow(panel, 3, "USB - nome instalado", _usbCombo);
        AddRow(panel, 4, "Rede - IP", _ipText);
        AddRow(panel, 5, "Rede - Porta", _portNumber);

        panel.Controls.Add(_enabledCheck, 1, 6);

        panel.Controls.Add(new Label { Text = "Gaveta (kick) ", AutoSize = true, Margin = new Padding(0, 12, 0, 2) }, 0, 7);
        panel.Controls.Add(_drawerCheck, 1, 7);

        var pulsePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        pulsePanel.Controls.Add(new Label { Text = "m" });
        pulsePanel.Controls.Add(_pulseM);
        pulsePanel.Controls.Add(new Label { Text = "t1" });
        pulsePanel.Controls.Add(_pulseT1);
        pulsePanel.Controls.Add(new Label { Text = "t2" });
        pulsePanel.Controls.Add(_pulseT2);
        AddRow(panel, 8, "Pulso", pulsePanel);

        panel.Controls.Add(new Label { Text = "Corte", AutoSize = true, Margin = new Padding(0, 12, 0, 2) }, 0, 9);
        panel.Controls.Add(_cutCheck, 1, 9);
        AddRow(panel, 10, "Modo de corte", _cutModeCombo);

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Text = "No POS você vai escolher qual perfil imprime o quê. Aqui você define como cada perfil se liga à impressora (USB ou Rede).",
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 14, 0, 0),
        };
        panel.Controls.Add(help, 1, 12);

        box.Controls.Add(panel);
        return box;
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
        _btnCopyToken.Click += (_, _) => CopyToken();

        _btnAddProfile.Click += (_, _) => AddProfileViaDialog();
        _btnRemoveProfile.Click += (_, _) => RemoveSelectedProfile();
        _btnSetDefault.Click += (_, _) => SetDefaultProfile();
        _btnSaveConfig.Click += async (_, _) => await SaveConfigAsync();

        _btnTestPrint.Click += async (_, _) => await RunPrinterActionAsync("Teste de impressão (talão)", id => _api.TestPrintAsync(id));
        _btnTestDrawer.Click += async (_, _) => await RunPrinterActionAsync("Teste de gaveta", id => _api.TestDrawerAsync(id));
        _btnTestCut.Click += async (_, _) => await RunPrinterActionAsync("Teste de corte", id => _api.TestCutAsync(id));

        _profilesList.SelectedIndexChanged += (_, _) => LoadSelectedProfileIntoEditor();
        _modeCombo.SelectedIndexChanged += (_, _) => ToggleModeFields();
        Resize += (_, _) => HandleMinimizeToTray();
        FormClosing += HandleFormClosingToTray;
    }

    private void InitializeTrayIcon()
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Abrir", null, (_, _) => RestoreFromTray());
        contextMenu.Items.Add("Sair", null, (_, _) =>
        {
            _allowExit = true;
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
            }
            Close();
        });

        _trayIcon = new NotifyIcon
        {
            Text = "Zaldo Printer",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = contextMenu,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void HandleMinimizeToTray()
    {
        if (WindowState != FormWindowState.Minimized)
        {
            return;
        }
        HideToTray();
    }

    private void HandleFormClosingToTray(object? sender, FormClosingEventArgs e)
    {
        if (_allowExit)
        {
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
            }
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();
        ShowInTaskbar = false;
        if (_trayIcon is not null && !_trayHintShown)
        {
            _trayHintShown = true;
            _trayIcon.BalloonTipTitle = "Zaldo Printer";
            _trayIcon.BalloonTipText = "Configuração fechada. O serviço continua ativo em segundo plano.";
            _trayIcon.BalloonTipIcon = ToolTipIcon.Info;
            _trayIcon.ShowBalloonTip(3000);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void CopyToken()
    {
        try
        {
            var token = _tokenText.Text?.Trim() ?? string.Empty;
            if (token.Length == 0)
            {
                MessageBox.Show(this, "Token vazio.", "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Clipboard.SetText(token);
            MessageBox.Show(this, "Token copiado para a área de transferência.", "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao copiar token:\n" + ex.Message, "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            var health = await _api.HealthAsync();
            var pendingTotal = health.PendingByPrinter.Values.Sum();
            _serviceStatus.Text = health.Ok
                ? $"Online (127.0.0.1:16161) · Fila pendente: {pendingTotal}"
                : "Offline: " + health.Message;
            _serviceStatus.ForeColor = health.Ok ? Color.DarkGreen : Color.DarkRed;
            BindHealthDiagnostics(health);

            _config = await _api.GetConfigAsync();
            _api.SetToken(_config.PairingToken);
            _tokenText.Text = _config.PairingToken;

            _installedPrinters = await _api.GetInstalledPrintersAsync();
            BindInstalledPrinters();
            BindProfiles();
            UpdateButtonsState();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao carregar dados do serviço:\n" + ex.Message, "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateButtonsState()
    {
        var hasSelection = _profilesList.SelectedIndex >= 0 && _profilesList.SelectedIndex < _config.Printers.Count;
        _btnRemoveProfile.Enabled = hasSelection;
        _btnSetDefault.Enabled = hasSelection;
        _btnTestPrint.Enabled = hasSelection;
        _btnTestDrawer.Enabled = hasSelection;
        _btnTestCut.Enabled = hasSelection;
        _btnSaveConfig.Enabled = true;
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
            UpdateButtonsState();
            MessageBox.Show(this, "Configuração guardada com sucesso.", "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Falha ao guardar configuração:\n" + ex.Message, "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RegenerateTokenAsync()
    {
        if (MessageBox.Show(this, "Gerar novo token de pareamento?\n\nDepois atualize o POS com o novo token.", "Zaldo Printer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            ApplyEditorToSelectedProfile();
            var newToken = await _api.RegenerateTokenAsync();
            if (string.IsNullOrWhiteSpace(newToken))
            {
                throw new InvalidOperationException("Token vazio retornado pelo serviço.");
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
                ? $"{printer.PrinterName} [Padrão do Windows]"
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
                _lastPrintList.Items.Add($"{entry.Key} · {status} · {value.Operation} · {when} · fila={pending}");
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
            var suffix = string.Equals(profile.Id, _config.DefaultPrinterId, StringComparison.OrdinalIgnoreCase) ? " [PADRÃO]" : string.Empty;
            _profilesList.Items.Add($"{profile.Name} ({profile.Id}){suffix}");
        }

        _defaultLabel.Text = string.IsNullOrWhiteSpace(_config.DefaultPrinterId)
            ? "Padrão: (não definido)"
            : "Padrão: " + _config.DefaultPrinterId;

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
    }

    private void AddProfileViaDialog()
    {
        using var dlg = new AddPrinterDialog(_installedPrinters);
        if (dlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!dlg.TryBuildProfile(out var profile, out var error))
        {
            MessageBox.Show(this, error, "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _config.Printers.Add(profile);
        if (string.IsNullOrWhiteSpace(_config.DefaultPrinterId))
        {
            _config.DefaultPrinterId = profile.Id;
        }

        BindProfiles();
        _profilesList.SelectedIndex = _config.Printers.Count - 1;
        UpdateButtonsState();
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
        UpdateButtonsState();
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
        UpdateButtonsState();
    }

    private async Task RunPrinterActionAsync(string title, Func<string, Task<(bool ok, string message)>> action)
    {
        ApplyEditorToSelectedProfile();
        var index = _profilesList.SelectedIndex;
        if (index < 0 || index >= _config.Printers.Count)
        {
            MessageBox.Show(this, "Selecione uma impressora.", "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var profile = _config.Printers[index];
        var printerId = profile.Id;

        var result = await action(printerId);
        if (result.ok)
        {
            MessageBox.Show(this, $"{title} enviado para '{profile.Name}'.", "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, $"Falha no comando ({title}):\n{result.message}", "Zaldo Printer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        await RefreshAllAsync();
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
            UpdateButtonsState();
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
            UpdateButtonsState();
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

        if (isUsb)
        {
            _drawerCheck.Enabled = true;
            _cutCheck.Enabled = true;
        }
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }
        base.Dispose(disposing);
    }
}
