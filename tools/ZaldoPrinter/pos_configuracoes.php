<?php
declare(strict_types=1);

require_once __DIR__ . '/config/db.php';
require_once __DIR__ . '/config/permissoes.php';
require_once __DIR__ . '/modules/moloni_pos/services/SchemaService.php';
require_once __DIR__ . '/modules/moloni_pos/services/PosRepository.php';

if (!isset($_SESSION['loggedin']) || $_SESSION['loggedin'] !== true) {
    header('Location: /pos_login.php?next=%2Fmoloni_pos.php');
    exit;
}

$empresaId = (int)($_SESSION['empresa_id'] ?? 0);
if ($empresaId <= 0) {
    header('Location: /dashboard.php');
    exit;
}

$isAdmin = ((int)($_SESSION['is_admin'] ?? 0) === 1);
$canConfig = $isAdmin || (
    function_exists('tem_permissao')
    && (tem_permissao('config_pos') || tem_permissao('config_empresa'))
);
$canViewProducts = $isAdmin || (function_exists('tem_permissao') && tem_permissao('ver_produtos'));
$canManageProducts = $isAdmin || (function_exists('tem_permissao') && tem_permissao('gerir_produtos'));
if (!$canConfig) {
    header('Location: /dashboard.php?erro=sem_permissao');
    exit;
}

$schema = new MoloniPosSchemaService($pdo);
$schema->ensure();
$repo = new MoloniPosRepository($pdo);
$terminal = $repo->resolveTerminal($empresaId);
$categories = $repo->listCategories($empresaId);
$transportOptions = z_pos_transport_options();

$printerConfig = z_pos_default_printer_config();
$printerConfigRaw = trim((string)($terminal['printer_config_json'] ?? ''));
if ($printerConfigRaw !== '') {
    $decoded = json_decode($printerConfigRaw, true);
    if (is_array($decoded)) {
        $printerConfig = z_pos_normalize_printer_config($decoded);
    }
}

$flashOk = '';
$flashErr = '';

function z_pos_normalize_sort_mode(string $raw): string
{
    $value = strtoupper(trim($raw));
    $allowed = ['FAVORITES_FIRST', 'ALPHABETICAL', 'BEST_SELLERS', 'PRICE_ASC', 'PRICE_DESC'];
    return in_array($value, $allowed, true) ? $value : 'BEST_SELLERS';
}

function z_pos_sort_options(): array
{
    return [
        'FAVORITES_FIRST' => 'Favoritos Primeiro',
        'ALPHABETICAL' => 'Alfabética',
        'BEST_SELLERS' => '+ Vendidos',
        'PRICE_ASC' => '+ Baratos',
        'PRICE_DESC' => '+ Caros',
    ];
}

function z_pos_transport_options(): array
{
    return [
        'NETWORK' => 'Rede (TCP/IP 9100)',
        'USB' => 'USB (servidor)',
        'BLUETOOTH' => 'Bluetooth',
    ];
}

function z_pos_normalize_transport(string $raw): string
{
    $value = strtoupper(trim($raw));
    return in_array($value, ['NETWORK', 'USB', 'BLUETOOTH'], true) ? $value : 'NETWORK';
}

/**
 * @param mixed $value
 */
function z_pos_bool($value, bool $fallback = false): bool
{
    if (is_bool($value)) {
        return $value;
    }
    $raw = strtolower(trim((string)$value));
    if (in_array($raw, ['1', 'true', 'on', 'yes', 'sim'], true)) {
        return true;
    }
    if (in_array($raw, ['0', 'false', 'off', 'no', 'nao', 'não'], true)) {
        return false;
    }
    return $fallback;
}

function z_pos_safe_substr(string $value, int $start, int $length): string
{
    if (function_exists('mb_substr')) {
        return (string)mb_substr($value, $start, $length);
    }
    return substr($value, $start, $length);
}

/**
 * @param mixed $raw
 * @return array<int,int>
 */
function z_pos_category_ids($raw): array
{
    $items = $raw;
    if (is_string($items)) {
        $items = preg_split('/[;,\\s]+/', $items) ?: [];
    }
    if (!is_array($items)) {
        return [];
    }
    $out = [];
    foreach ($items as $value) {
        if (!is_numeric((string)$value)) {
            continue;
        }
        $id = (int)$value;
        if ($id > 0) {
            $out[] = $id;
        }
    }
    $out = array_values(array_unique($out));
    sort($out);
    return $out;
}

/**
 * @param array<string,mixed> $raw
 * @return array<string,mixed>
 */
function z_pos_normalize_printer_node(array $raw, bool $primary): array
{
    $idRaw = trim((string)($raw['id'] ?? ($primary ? 'PRINCIPAL' : 'AUX')));
    $id = strtoupper(preg_replace('/[^A-Z0-9_]+/', '_', $idRaw) ?? '');
    $id = trim($id, '_');
    if ($id === '') {
        $id = $primary ? 'PRINCIPAL' : 'AUX';
    }

    $name = trim((string)($raw['name'] ?? ($primary ? 'Principal' : 'Auxiliar')));
    if ($name === '') {
        $name = $primary ? 'Principal' : 'Auxiliar';
    }

    $routeMode = strtoupper(trim((string)($raw['route_mode'] ?? 'ALL')));
    if (!in_array($routeMode, ['ALL', 'CATEGORIES'], true)) {
        $routeMode = 'ALL';
    }

    return [
        'id' => $id,
        'name' => z_pos_safe_substr($name, 0, 90),
        'enabled' => z_pos_bool($raw['enabled'] ?? true, true),
        'print_receipt' => z_pos_bool($raw['print_receipt'] ?? true, true),
        'open_drawer' => z_pos_bool($raw['open_drawer'] ?? ($primary ? true : false), $primary),
        'cut_paper' => z_pos_bool($raw['cut_paper'] ?? ($primary ? true : false), $primary),
        'route_mode' => $routeMode,
        'category_ids' => z_pos_category_ids($raw['category_ids'] ?? []),
        'transport' => z_pos_normalize_transport((string)($raw['transport'] ?? 'NETWORK')),
        'host' => trim((string)($raw['host'] ?? '')),
        'port' => max(1, min(65535, (int)($raw['port'] ?? 9100))),
        'usb_device' => trim((string)($raw['usb_device'] ?? '')),
        'bluetooth_address' => trim((string)($raw['bluetooth_address'] ?? '')),
        'bluetooth_port' => max(1, min(65535, (int)($raw['bluetooth_port'] ?? 1))),
        'timeout_ms' => max(800, min(9000, (int)($raw['timeout_ms'] ?? 2500))),
        'zp_profile_id' => z_pos_safe_substr(trim((string)($raw['zp_profile_id'] ?? '')), 0, 64),
    ];
}

/**
 * @param array<string,mixed> $raw
 * @return array<string,mixed>
 */
function z_pos_normalize_printer_config(array $raw): array
{
    $primary = z_pos_normalize_printer_node(
        isset($raw['primary']) && is_array($raw['primary']) ? $raw['primary'] : [],
        true
    );

    $copies = [];
    if (isset($raw['copies']) && is_array($raw['copies'])) {
        foreach ($raw['copies'] as $row) {
            if (!is_array($row)) {
                continue;
            }
            $copy = z_pos_normalize_printer_node($row, false);
            $name = trim((string)($copy['name'] ?? ''));
            $host = trim((string)($copy['host'] ?? ''));
            $usb = trim((string)($copy['usb_device'] ?? ''));
            $bt = trim((string)($copy['bluetooth_address'] ?? ''));
            if ($name === '' && $host === '' && $usb === '' && $bt === '') {
                continue;
            }
            $copies[] = $copy;
        }
    }

    return [
        'enabled' => z_pos_bool($raw['enabled'] ?? false, false),
        'primary' => $primary,
        'copies' => $copies,
        // Serviço local do Zaldo Printer (por terminal/empresa)
        'service_url' => trim((string)($raw['service_url'] ?? $raw['printer_service_url'] ?? '')),
        'service_token' => trim((string)($raw['service_token'] ?? $raw['printer_service_token'] ?? '')),
    ];
}

/**
 * @return array<string,mixed>
 */
function z_pos_default_printer_config(): array
{
    return [
        'enabled' => false,
        'primary' => [
            'id' => 'PRINCIPAL',
            'name' => 'Principal',
            'enabled' => true,
            'print_receipt' => true,
            'open_drawer' => true,
            'cut_paper' => true,
            'route_mode' => 'ALL',
            'category_ids' => [],
            'transport' => 'NETWORK',
            'host' => '',
            'port' => 9100,
            'usb_device' => '',
            'bluetooth_address' => '',
            'bluetooth_port' => 1,
            'timeout_ms' => 2500,
        ],
        'copies' => [],
    ];
}

function z_pos_is_private_ipv4(string $ip): bool
{
    if (strpos($ip, '.') === false) {
        return false;
    }
    $parts = array_map('intval', explode('.', $ip));
    if (count($parts) !== 4) {
        return false;
    }
    if ($parts[0] === 10 || $parts[0] === 127) {
        return true;
    }
    if ($parts[0] === 192 && $parts[1] === 168) {
        return true;
    }
    return ($parts[0] === 172 && $parts[1] >= 16 && $parts[1] <= 31);
}

/**
 * @return array<int,string>
 */
function z_pos_detect_server_ips(): array
{
    $out = [];
    $push = static function ($raw) use (&$out): void {
        $value = trim((string)$raw);
        if ($value === '') {
            return;
        }
        $chunks = explode(',', $value);
        foreach ($chunks as $chunk) {
            $ip = trim((string)$chunk);
            if ($ip === '') {
                continue;
            }
            $valid = filter_var($ip, FILTER_VALIDATE_IP, FILTER_FLAG_IPV4);
            if ($valid === false) {
                continue;
            }
            if (!in_array($ip, $out, true)) {
                $out[] = $ip;
            }
        }
    };

    $push($_SERVER['SERVER_ADDR'] ?? '');
    $push($_SERVER['LOCAL_ADDR'] ?? '');

    $hostIp = gethostbyname((string)gethostname());
    $push($hostIp);

    usort($out, static function (string $a, string $b): int {
        $rank = static function (string $ip): int {
            if ($ip === '127.0.0.1') {
                return 2;
            }
            return z_pos_is_private_ipv4($ip) ? 0 : 1;
        };
        $ra = $rank($a);
        $rb = $rank($b);
        if ($ra === $rb) {
            return strcmp($a, $b);
        }
        return $ra <=> $rb;
    });

    return $out;
}

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $acao = trim((string)($_POST['acao'] ?? ''));
    if ($acao === 'save_terminal') {
        try {
            $terminalId = (int)($terminal['id'] ?? 0);
            if ($terminalId <= 0) {
                throw new RuntimeException('Terminal não encontrado para esta empresa.');
            }

            $nome = trim((string)($_POST['terminal_name'] ?? ''));
            if ($nome === '') {
                $nome = 'Terminal';
            }
            $nome = z_pos_safe_substr($nome, 0, 120);
            $teclado = isset($_POST['use_virtual_keyboard']) ? 1 : 0;
            $sortMode = z_pos_normalize_sort_mode((string)($_POST['product_sort_mode'] ?? 'BEST_SELLERS'));

            $st = $pdo->prepare('UPDATE pos_terminais SET nome = ?, teclado_virtual = ?, product_sort_mode = ? WHERE id = ? AND empresa_id = ? LIMIT 1');
            $st->execute([$nome, $teclado, $sortMode, $terminalId, $empresaId]);

            $_SESSION['moloni_pos_use_virtual_keyboard'] = $teclado;
            $terminal = $repo->resolveTerminal($empresaId);
            $printerConfigRaw = trim((string)($terminal['printer_config_json'] ?? ''));
            if ($printerConfigRaw !== '') {
                $decoded = json_decode($printerConfigRaw, true);
                if (is_array($decoded)) {
                    $printerConfig = z_pos_normalize_printer_config($decoded);
                }
            }
            $flashOk = 'Configuração do terminal atualizada.';
        } catch (Throwable $e) {
            $flashErr = trim((string)$e->getMessage());
            if ($flashErr === '') {
                $flashErr = 'Falha ao atualizar o terminal.';
            }
        }
    } elseif ($acao === 'save_printers') {
        try {
            $terminalId = (int)($terminal['id'] ?? 0);
            if ($terminalId <= 0) {
                throw new RuntimeException('Terminal não encontrado para esta empresa.');
            }

            $primaryInput = [
                'id' => (string)($_POST['primary_id'] ?? 'PRINCIPAL'),
                'name' => (string)($_POST['primary_name'] ?? 'Principal'),
                'enabled' => (string)($_POST['primary_enabled'] ?? '0'),
                'print_receipt' => (string)($_POST['primary_print_receipt'] ?? '0'),
                'open_drawer' => (string)($_POST['primary_open_drawer'] ?? '0'),
                'cut_paper' => (string)($_POST['primary_cut_paper'] ?? '0'),
                'route_mode' => 'ALL',
                'category_ids' => [],
                'transport' => (string)($_POST['primary_transport'] ?? 'NETWORK'),
                'host' => (string)($_POST['primary_host'] ?? ''),
                'port' => (string)($_POST['primary_port'] ?? '9100'),
                'usb_device' => (string)($_POST['primary_usb_device'] ?? ''),
                'bluetooth_address' => (string)($_POST['primary_bluetooth_address'] ?? ''),
                'bluetooth_port' => (string)($_POST['primary_bluetooth_port'] ?? '1'),
                'timeout_ms' => (string)($_POST['primary_timeout_ms'] ?? '2500'),
                'zp_profile_id' => (string)($_POST['primary_zp_profile_id'] ?? ''),
            ];

            $copiesInput = isset($_POST['copies']) && is_array($_POST['copies']) ? $_POST['copies'] : [];
            $copies = [];
            foreach ($copiesInput as $row) {
                if (!is_array($row)) {
                    continue;
                }
                $copies[] = [
                    'id' => (string)($row['id'] ?? ''),
                    'name' => (string)($row['name'] ?? ''),
                    'enabled' => (string)($row['enabled'] ?? '0'),
                    'print_receipt' => (string)($row['print_receipt'] ?? '0'),
                    'open_drawer' => '0',
                    'cut_paper' => '0',
                    'route_mode' => (string)($row['route_mode'] ?? 'ALL'),
                    'category_ids' => (string)($row['category_ids'] ?? ''),
                    'transport' => (string)($row['transport'] ?? 'NETWORK'),
                    'host' => (string)($row['host'] ?? ''),
                    'port' => (string)($row['port'] ?? '9100'),
                    'usb_device' => (string)($row['usb_device'] ?? ''),
                    'bluetooth_address' => (string)($row['bluetooth_address'] ?? ''),
                    'bluetooth_port' => (string)($row['bluetooth_port'] ?? '1'),
                    'timeout_ms' => (string)($row['timeout_ms'] ?? '2500'),
                    'zp_profile_id' => (string)($row['zp_profile_id'] ?? ''),
                ];
            }

            $newConfig = z_pos_normalize_printer_config([
                'enabled' => (string)($_POST['printer_enabled'] ?? '0'),
                'primary' => $primaryInput,
                'copies' => $copies,
                // Pairing do Zaldo Printer (gerado no app do Windows)
                'service_url' => (string)($_POST['printer_service_url'] ?? ''),
                'service_token' => (string)($_POST['printer_service_token'] ?? ''),
            ]);

            $json = json_encode($newConfig, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
            if (!is_string($json) || $json === '') {
                throw new RuntimeException('Falha ao serializar a configuração de impressoras.');
            }

            $st = $pdo->prepare('UPDATE pos_terminais SET printer_config_json = ? WHERE id = ? AND empresa_id = ? LIMIT 1');
            $st->execute([$json, $terminalId, $empresaId]);

            $terminal = $repo->resolveTerminal($empresaId);
            $printerConfig = $newConfig;
            $flashOk = 'Configuração de impressoras atualizada.';
        } catch (Throwable $e) {
            $flashErr = trim((string)$e->getMessage());
            if ($flashErr === '') {
                $flashErr = 'Falha ao salvar impressoras.';
            }
        }
    }
}

$empresaNome = trim((string)($_SESSION['username'] ?? 'Empresa'));

function h(string $value): string
{
    return htmlspecialchars($value, ENT_QUOTES, 'UTF-8');
}

function z_is_pos_origin(): bool
{
    $from = strtolower(trim((string)($_GET['from'] ?? '')));
    if ($from === 'pos') {
        return true;
    }
    $ref = strtolower((string)($_SERVER['HTTP_REFERER'] ?? ''));
    if ($ref === '') {
        return false;
    }
    return strpos($ref, '/moloni_pos.php') !== false
        || strpos($ref, '/pos_configuracoes.php?from=pos') !== false
        || strpos($ref, '/pos_pagamentos.php?from=pos') !== false
        || strpos($ref, '/cargos.php?from=pos') !== false;
}

function z_with_from_pos(string $url, bool $fromPos): string
{
    if (!$fromPos) {
        return $url;
    }
    $sep = strpos($url, '?') === false ? '?' : '&';
    return $url . $sep . 'from=pos';
}

$fromPos = z_is_pos_origin();
$backUrl = $fromPos ? '/moloni_pos.php' : '/dashboard.php';
$backLabel = $fromPos ? 'Voltar ao POS' : 'Voltar ao Dashboard';
$sortOptions = z_pos_sort_options();
$currentSortMode = z_pos_normalize_sort_mode((string)($terminal['product_sort_mode'] ?? 'BEST_SELLERS'));
$primaryPrinter = isset($printerConfig['primary']) && is_array($printerConfig['primary'])
    ? $printerConfig['primary']
    : z_pos_default_printer_config()['primary'];
$copyPrinters = isset($printerConfig['copies']) && is_array($printerConfig['copies']) ? $printerConfig['copies'] : [];
if ($copyPrinters === []) {
    $copyPrinters[] = z_pos_normalize_printer_node([
        'id' => 'COZINHA',
        'name' => 'Cozinha',
        'enabled' => false,
        'print_receipt' => true,
        'route_mode' => 'CATEGORIES',
        'category_ids' => [],
        'transport' => 'NETWORK',
        'host' => '',
        'port' => 9100,
        'usb_device' => '',
        'bluetooth_address' => '',
        'bluetooth_port' => 1,
        'timeout_ms' => 2500,
    ], false);
}
$categoryHintList = [];
foreach ($categories as $catRow) {
    if (!is_array($catRow)) {
        continue;
    }
    $catId = isset($catRow['id']) && is_numeric((string)$catRow['id']) ? (int)$catRow['id'] : 0;
    $catName = trim((string)($catRow['nome'] ?? ''));
    if ($catId > 0 && $catName !== '') {
        $categoryHintList[] = $catId . ' = ' . $catName;
    }
}
$categoryHintText = $categoryHintList !== []
    ? implode(' | ', $categoryHintList)
    : 'Sem categorias ativas para esta empresa.';
$serverIps = z_pos_detect_server_ips();
$bridgeUrl = trim((string)(getenv('POS_PRINTER_BRIDGE_URL') ?: ''));

// Primeiro: configuração por terminal (multi-empresa). Fallback: ENV (compatibilidade)
$printerServiceUrl = trim((string)($printerConfig['service_url'] ?? ''));
$printerServiceToken = trim((string)($printerConfig['service_token'] ?? ''));

if ($printerServiceUrl === '') {
    $printerServiceUrl = trim((string)(getenv('POS_ZALDO_PRINTER_URL') ?: ''));
}
if ($printerServiceToken === '') {
    $printerServiceToken = trim((string)(getenv('POS_ZALDO_PRINTER_TOKEN') ?: getenv('ZALDO_PRINTER_TOKEN') ?: ''));
}

$printerServiceUrlCurrent = $printerServiceUrl !== '' ? $printerServiceUrl : 'http://127.0.0.1:16161';
?>
<!DOCTYPE html>
<html lang="pt">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Configurações POS</title>
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css">
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css">
    <link rel="stylesheet" href="/assets/zaldo-brand.css?v=20260205">
    <style>
        :root {
            --z-bg: #f3f6fb;
            --z-card: #ffffff;
            --z-line: #d6e0ee;
            --z-ink: #13233a;
            --z-muted: #5f728e;
            --z-primary: #0f72c7;
            --z-primary-soft: #e9f4ff;
            --z-shadow: 0 10px 24px rgba(15, 35, 62, .08);
        }
        * { box-sizing: border-box; }
        body {
            margin: 0;
            background: linear-gradient(180deg, #f8fbff 0%, var(--z-bg) 35%, var(--z-bg) 100%);
            color: var(--z-ink);
        }
        .pos-wrap {
            max-width: 1220px;
            margin: 22px auto 28px;
            padding: 0 14px;
        }
        .z-shell {
            border: 1px solid var(--z-line);
            border-radius: 16px;
            background: var(--z-card);
            box-shadow: var(--z-shadow);
        }
        .z-header {
            padding: 14px 16px;
            display: flex;
            align-items: flex-start;
            justify-content: space-between;
            gap: 12px;
            border-bottom: 1px solid #e4ebf5;
        }
        .z-crumb {
            margin: 0 0 6px;
            font-size: .72rem;
            color: #6c819d;
            text-transform: uppercase;
            letter-spacing: .04em;
            font-weight: 800;
        }
        .z-title {
            margin: 0;
            font-size: 1.28rem;
            font-weight: 800;
            line-height: 1.1;
            color: #112540;
        }
        .z-subtitle {
            margin: 4px 0 0;
            font-size: .82rem;
            color: #5e738f;
            font-weight: 600;
        }
        .z-header-actions {
            display: flex;
            gap: 8px;
            flex-wrap: wrap;
            justify-content: flex-end;
        }
        .z-btn {
            border-radius: 10px;
            border: 1px solid #c9d8eb;
            background: #fff;
            color: #173153;
            text-decoration: none;
            font-size: .78rem;
            font-weight: 800;
            padding: 8px 11px;
            display: inline-flex;
            align-items: center;
            gap: 6px;
            line-height: 1;
        }
        .z-btn:hover { color: #173153; background: #f7fbff; }
        .z-btn-primary {
            border-color: #8ec0ef;
            background: #edf6ff;
            color: #085d9f;
        }
        .z-flash {
            margin: 10px 0 0;
            border-radius: 10px;
            padding: 9px 11px;
            font-size: .78rem;
            font-weight: 700;
        }
        .z-flash.ok { background: #e9fbe9; border: 1px solid #b7e4b7; color: #165f32; }
        .z-flash.err { background: #fff0f0; border: 1px solid #f2b7b7; color: #8e1d1d; }
        .z-body { padding: 14px; }
        .z-tabs {
            border-bottom: 1px solid #dbe6f3;
            gap: 6px;
            flex-wrap: nowrap;
            overflow-x: auto;
            white-space: nowrap;
            padding-bottom: 6px;
        }
        .z-tabs .nav-link {
            border: 1px solid #d8e3f1;
            border-radius: 10px;
            color: #395577;
            font-weight: 700;
            font-size: .78rem;
            padding: 8px 12px;
            background: #f8fbff;
        }
        .z-tabs .nav-link.active {
            border-color: #8ec0ef;
            background: #ecf5ff;
            color: #0f72c7;
        }
        .z-pane { padding-top: 12px; }
        .z-card {
            border: 1px solid #d9e3f1;
            border-radius: 12px;
            background: #fff;
            box-shadow: 0 4px 14px rgba(15, 35, 62, .05);
            padding: 12px;
        }
        .z-card + .z-card { margin-top: 10px; }
        .z-card-title {
            margin: 0;
            font-size: .96rem;
            font-weight: 800;
            color: #112540;
        }
        .z-card-sub {
            margin: 4px 0 0;
            font-size: .75rem;
            color: #67809d;
            font-weight: 600;
        }
        .z-kv {
            margin-top: 8px;
            font-size: .72rem;
            color: #6e829c;
            font-weight: 700;
        }
        .z-label {
            display: block;
            margin-bottom: 5px;
            font-size: .7rem;
            text-transform: uppercase;
            letter-spacing: .03em;
            color: #667e9b;
            font-weight: 800;
        }
        .z-compact .form-control,
        .z-compact .form-select {
            min-height: 38px;
            font-size: .82rem;
            border-color: #cfdced;
            border-radius: 9px;
        }
        .z-compact .form-control:focus,
        .z-compact .form-select:focus {
            border-color: #8ec0ef;
            box-shadow: 0 0 0 3px rgba(15,114,199,.12);
        }
        .z-check {
            border: 1px solid #d7e2f1;
            border-radius: 9px;
            min-height: 38px;
            padding: 8px 10px;
            background: #fbfdff;
            display: flex;
            align-items: center;
            gap: 8px;
            font-size: .8rem;
            font-weight: 700;
        }
        .z-note {
            margin-top: 6px;
            font-size: .72rem;
            color: #667e9b;
            line-height: 1.35;
        }
        .z-save-btn {
            border: 1px solid #0f72c7;
            background: #0f72c7;
            color: #fff;
            font-size: .78rem;
            font-weight: 800;
            border-radius: 9px;
            min-height: 38px;
            padding: 0 14px;
        }
        .z-save-btn:hover { background: #0d66b2; border-color: #0d66b2; color: #fff; }
        .z-link-btn {
            border: 1px solid #d1deef;
            background: #fff;
            color: #2a4a70;
            border-radius: 9px;
            font-size: .75rem;
            font-weight: 700;
            padding: 7px 10px;
            text-decoration: none;
            display: inline-flex;
            gap: 5px;
            align-items: center;
        }
        .z-link-btn:hover { color: #2a4a70; background: #f8fbff; }
        .accordion-item {
            border: 1px solid #d8e4f3;
            border-radius: 10px !important;
            overflow: hidden;
            margin-bottom: 8px;
        }
        .accordion-button {
            font-size: .82rem;
            font-weight: 800;
            color: #183556;
            background: #f8fbff;
            padding: 10px 12px;
        }
        .accordion-button:not(.collapsed) {
            color: #0d66b2;
            background: #edf5ff;
            box-shadow: inset 0 -1px 0 #d8e4f3;
        }
        .accordion-button:focus { box-shadow: none; }
        .z-status {
            border: 1px solid #d8e4f3;
            border-radius: 10px;
            padding: 10px;
            font-size: .78rem;
            font-weight: 700;
            background: #f8fbff;
            color: #3a5779;
        }
        .z-status.ok {
            border-color: #9dd7af;
            background: #eefdf2;
            color: #126536;
        }
        .z-status.err {
            border-color: #f3b0b0;
            background: #fff0f0;
            color: #9f1f1f;
        }
        .z-badge-warn {
            margin-top: 8px;
            border: 1px solid #f7d087;
            background: #fff8e8;
            color: #8a5b00;
            border-radius: 9px;
            font-size: .75rem;
            font-weight: 700;
            padding: 8px 10px;
        }
        .z-advanced-toggle {
            margin: 10px 0 6px;
            display: flex;
            align-items: center;
            gap: 8px;
            flex-wrap: wrap;
        }
        .z-advanced-btn {
            border: 1px solid #cddcf0;
            background: #fff;
            color: #1f4068;
            border-radius: 9px;
            font-size: .76rem;
            font-weight: 800;
            padding: 7px 11px;
        }
        .z-advanced-open .simple-only { display: none !important; }
        .copies-wrap {
            display: grid;
            gap: 8px;
            margin-top: 8px;
        }
        .copy-row {
            border: 1px solid #d8e3f1;
            border-radius: 10px;
            background: #fbfdff;
            padding: 10px;
        }
        .copy-row-grid {
            display: grid;
            gap: 8px;
            grid-template-columns: repeat(8, minmax(0, 1fr));
        }
        .copy-row-grid.secondary {
            margin-top: 8px;
            grid-template-columns: 130px 140px minmax(0,1fr) 120px 120px 1fr;
        }
        .copy-actions {
            margin-top: 8px;
            display: flex;
            gap: 6px;
            flex-wrap: wrap;
        }
        .small-btn {
            border: 1px solid #cfdced;
            background: #fff;
            color: #1d3a61;
            border-radius: 8px;
            font-size: .73rem;
            font-weight: 700;
            padding: 6px 9px;
            line-height: 1;
        }
        .small-btn:hover { background: #f8fbff; }
        .small-btn.danger {
            border-color: #f0b9b9;
            color: #9f1f1f;
            background: #fff3f3;
        }
        .z-diagnostic-grid {
            display: grid;
            gap: 10px;
            grid-template-columns: repeat(2, minmax(0, 1fr));
        }
        .z-ip-list { display: grid; gap: 7px; }
        .z-ip-row {
            border: 1px solid #d8e4f3;
            border-radius: 9px;
            background: #f8fbff;
            padding: 6px 8px;
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 6px;
        }
        .z-ip {
            font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
            font-size: .78rem;
            font-weight: 800;
            color: #122843;
        }
        .z-shortcuts {
            display: grid;
            grid-template-columns: repeat(3, minmax(0, 1fr));
            gap: 8px;
        }
        .z-tile {
            border: 1px solid #d8e4f3;
            border-radius: 10px;
            background: #fff;
            padding: 11px;
            text-decoration: none;
            color: #12304f;
            display: block;
        }
        .z-tile:hover {
            color: #12304f;
            background: #f8fbff;
            box-shadow: 0 6px 16px rgba(15,35,62,.08);
        }
        .z-tile strong {
            display: block;
            font-size: .82rem;
            margin-bottom: 3px;
        }
        .z-tile span {
            display: block;
            font-size: .72rem;
            color: #5f7490;
            line-height: 1.3;
        }
        .z-help-text {
            font-size: .74rem;
            color: #5e7591;
            line-height: 1.35;
        }
        .z-modal-list {
            margin: 0;
            padding-left: 18px;
            font-size: .82rem;
            color: #334b69;
            line-height: 1.45;
        }
        .z-modal-list li { margin-bottom: 5px; }
        @media (max-width: 1024px) {
            .z-header {
                flex-direction: column;
                align-items: stretch;
            }
            .z-header-actions {
                justify-content: flex-start;
            }
            .copy-row-grid,
            .copy-row-grid.secondary,
            .z-shortcuts,
            .z-diagnostic-grid {
                grid-template-columns: 1fr;
            }
        }
    
        .z-input-row{
            display:flex;
            gap:10px;
            align-items:center;
        }
        .z-mini-btn{
            border:1px solid var(--z-line);
            background: var(--z-card);
            color: var(--z-ink);
            padding: 10px 12px;
            border-radius: 12px;
            font-weight: 700;
            min-width: 92px;
            box-shadow: 0 2px 10px rgba(15,35,62,.06);
        }
        .z-mini-btn:active{ transform: translateY(1px); }
        .z-hint{
            display:block;
            margin-top:6px;
            color: var(--z-muted);
            font-size: .9rem;
            line-height: 1.3;
        }
        @media (max-width: 520px){
            .z-input-row{ flex-direction:column; align-items:stretch; }
            .z-mini-btn{ width:100%; }
        }

    </style>
</head>
<body>
    <?php
        $contaUrl = z_with_from_pos('/config_conta.php', $fromPos);
        $dashboardUrl = '/dashboard.php';
        $tokenMasked = $printerServiceToken !== ''
            ? substr($printerServiceToken, 0, 4) . str_repeat('•', max(4, strlen($printerServiceToken) - 7)) . substr($printerServiceToken, -3)
            : 'Nao definido';
    ?>
    <div class="pos-wrap">
        <div class="z-shell">
            <div class="z-header">
                <div>
                    <p class="z-crumb mb-0">Dashboard <span class="mx-1">&gt;</span> Configuracoes <span class="mx-1">&gt;</span> POS</p>
                    <h1 class="z-title">Configurações do POS</h1>
                    <p class="z-subtitle"><?php echo h($empresaNome); ?> · Gestão completa do terminal de vendas</p>
                </div>
                <div class="z-header-actions">
                    <a class="z-btn z-btn-primary" href="<?php echo h($contaUrl); ?>">
                        <i class="bi bi-shield-lock"></i>
                        Conta &amp; Assinatura
                    </a>
                    <a class="z-btn" href="<?php echo h($dashboardUrl); ?>">
                        <i class="bi bi-arrow-left"></i>
                        Voltar ao Dashboard
                    </a>
                </div>
            </div>

            <div class="z-body z-compact" id="printerConfigPane">
                <?php if ($flashOk !== ''): ?>
                    <div class="z-flash ok"><?php echo h($flashOk); ?></div>
                <?php endif; ?>
                <?php if ($flashErr !== ''): ?>
                    <div class="z-flash err"><?php echo h($flashErr); ?></div>
                <?php endif; ?>

                <ul class="nav nav-tabs z-tabs" id="posConfigTabs" role="tablist">
                    <li class="nav-item" role="presentation">
                        <button class="nav-link active" data-bs-toggle="tab" data-bs-target="#tab-terminal" type="button" role="tab">Terminal</button>
                    </li>
                    <li class="nav-item" role="presentation">
                        <button class="nav-link" data-bs-toggle="tab" data-bs-target="#tab-impressao" type="button" role="tab">Impressão</button>
                    </li>
                    <li class="nav-item" role="presentation">
                        <button class="nav-link" data-bs-toggle="tab" data-bs-target="#tab-pagamentos" type="button" role="tab">Pagamentos</button>
                    </li>
                    <li class="nav-item" role="presentation">
                        <button class="nav-link" data-bs-toggle="tab" data-bs-target="#tab-produtos" type="button" role="tab">Produtos</button>
                    </li>
                    <li class="nav-item" role="presentation">
                        <button class="nav-link" data-bs-toggle="tab" data-bs-target="#tab-operadores" type="button" role="tab">Operadores &amp; Permissões</button>
                    </li>
                    <li class="nav-item" role="presentation">
                        <button class="nav-link" data-bs-toggle="tab" data-bs-target="#tab-atalhos" type="button" role="tab">Atalhos</button>
                    </li>
                </ul>

                <div class="tab-content">
                    <div class="tab-pane fade show active z-pane" id="tab-terminal" role="tabpanel">
                        <div class="z-card">
                            <h3 class="z-card-title">Terminal</h3>
                            <p class="z-card-sub">Nome do terminal, ordenação da grelha e comportamento do teclado touch.</p>
                            <form method="post" class="row g-3 mt-1">
                                <input type="hidden" name="acao" value="save_terminal">
                                <div class="col-lg-5">
                                    <label class="z-label" for="terminal_name">Nome do terminal</label>
                                    <input id="terminal_name" type="text" name="terminal_name" class="form-control" value="<?php echo h((string)($terminal['name'] ?? 'Terminal')); ?>" maxlength="120">
                                </div>
                                <div class="col-lg-4">
                                    <label class="z-label" for="product_sort_mode">Ordenação dos produtos</label>
                                    <select id="product_sort_mode" name="product_sort_mode" class="form-select">
                                        <?php foreach ($sortOptions as $value => $label): ?>
                                            <option value="<?php echo h($value); ?>" <?php echo $currentSortMode === $value ? 'selected' : ''; ?>>
                                                <?php echo h($label); ?>
                                            </option>
                                        <?php endforeach; ?>
                                    </select>
                                </div>
                                <div class="col-lg-3">
                                    <label class="z-label">Teclado touch</label>
                                    <div class="z-check">
                                        <input id="use_virtual_keyboard" type="checkbox" name="use_virtual_keyboard" value="1" <?php echo !empty($terminal['use_virtual_keyboard']) ? 'checked' : ''; ?>>
                                        <label class="m-0" for="use_virtual_keyboard">Ativar teclado virtual</label>
                                    </div>
                                </div>
                                <div class="col-12 d-flex justify-content-end">
                                    <button type="submit" class="z-save-btn">Guardar terminal</button>
                                </div>
                            </form>
                            <div class="z-kv">Terminal ID: <?php echo (int)($terminal['id'] ?? 0); ?></div>
                        </div>
                    </div>

                    <div class="tab-pane fade z-pane" id="tab-impressao" role="tabpanel">
                        <div class="z-card">
                            <h3 class="z-card-title">Impressão</h3>
                            <p class="z-card-sub">Fluxo simples por defeito. Modo avançado apenas quando necessário.</p>

                            <form method="post" id="printersForm" class="mt-2">
                                <input type="hidden" name="acao" value="save_printers">
                                <input type="hidden" name="primary_id" value="<?php echo h((string)($primaryPrinter['id'] ?? 'PRINCIPAL')); ?>">

                                <div class="accordion" id="printAccordion">
                                    <div class="accordion-item simple-only">
                                        <h2 class="accordion-header">
                                            <button class="accordion-button" type="button" data-bs-toggle="collapse" data-bs-target="#accQuick" aria-expanded="true" aria-controls="accQuick">
                                                Configuração rápida (2 minutos)
                                            </button>
                                        </h2>
                                        <div id="accQuick" class="accordion-collapse collapse show" data-bs-parent="#printAccordion">
                                            <div class="accordion-body">
                                                <div class="row g-3">
                                                    <div class="col-lg-4">
                                                        <label class="z-label">Impressão automática</label>
                                                        <div class="z-check">
                                                            <input type="hidden" name="printer_enabled" value="0">
                                                            <input type="checkbox" name="printer_enabled" value="1" <?php echo !empty($printerConfig['enabled']) ? 'checked' : ''; ?>>
                                                            Impressão automática ativa
                                                        </div>
                                                    </div>
                                                    <div class="col-lg-4">
                                                        <label class="z-label" for="primary_transport">Transporte principal</label>
                                                        <select id="primary_transport" name="primary_transport" class="form-select">
                                                            <?php foreach ($transportOptions as $tValue => $tLabel): ?>
                                                                <option value="<?php echo h($tValue); ?>" <?php echo ((string)($primaryPrinter['transport'] ?? 'NETWORK') === $tValue) ? 'selected' : ''; ?>>
                                                                    <?php echo h($tLabel); ?>
                                                                </option>
                                                            <?php endforeach; ?>
                                                        </select>
                                                    </div>

                                                    <div class="col-lg-4 transport-group" data-transport="NETWORK">
                                                        <label class="z-label" for="primary_host">IP (Rede)</label>
                                                        <input id="primary_host" type="text" name="primary_host" class="form-control" value="<?php echo h((string)($primaryPrinter['host'] ?? '')); ?>" placeholder="192.168.1.50">
                                                    </div>
                                                    <div class="col-lg-3 transport-group" data-transport="NETWORK">
                                                        <label class="z-label" for="primary_port">Porta (Rede)</label>
                                                        <input id="primary_port" type="text" name="primary_port" class="form-control" value="<?php echo h((string)($primaryPrinter['port'] ?? 9100)); ?>">
                                                    </div>

                                                    <div class="col-lg-4 transport-group" data-transport="USB">
                                                        <label class="z-label" for="primary_usb_device">Dispositivo USB</label>
                                                        <input id="primary_usb_device" type="text" name="primary_usb_device" class="form-control" value="<?php echo h((string)($primaryPrinter['usb_device'] ?? '')); ?>" placeholder="Opcional">
                                                    </div>

                                                    <div class="col-lg-4 transport-group" data-transport="BLUETOOTH">
                                                        <label class="z-label" for="primary_bluetooth_address">Endereço Bluetooth</label>
                                                        <input id="primary_bluetooth_address" type="text" name="primary_bluetooth_address" class="form-control" value="<?php echo h((string)($primaryPrinter['bluetooth_address'] ?? '')); ?>" placeholder="XX:XX:XX:XX:XX:XX">
                                                    </div>
                                                    <div class="col-lg-3 transport-group" data-transport="BLUETOOTH">
                                                        <label class="z-label" for="primary_bluetooth_port">Porta Bluetooth</label>
                                                        <input id="primary_bluetooth_port" type="text" name="primary_bluetooth_port" class="form-control" value="<?php echo h((string)($primaryPrinter['bluetooth_port'] ?? 1)); ?>">
                                                    </div>
                                                </div>

                                                <div class="d-flex flex-wrap gap-2 align-items-center mt-3">
                                                    <button type="submit" class="z-save-btn">Guardar impressoras</button>
                                                    <button type="button" class="z-link-btn" data-bs-toggle="modal" data-bs-target="#modalQuickPrintHelp">Ver instruções</button>
                                                </div>
                                            </div>
                                        </div>
                                    </div>

                                    <div class="accordion-item">
                                        <h2 class="accordion-header">
                                            <button class="accordion-button collapsed" type="button" data-bs-toggle="collapse" data-bs-target="#accOfficial" aria-expanded="false" aria-controls="accOfficial">
                                                Zaldo Printer Oficial (Windows)
                                            </button>
                                        </h2>
                                        <div id="accOfficial" class="accordion-collapse collapse" data-bs-parent="#printAccordion">
                                            <div class="accordion-body">
                                                <div id="officialHealthStatus" class="z-status">Zaldo Printer não testado nesta página.</div>
                                                <div class="row g-3 mt-1">
                                                    <div class="col-lg-6">
                                                        <label class="z-label" for="officialServiceUrl">URL</label>
                                                        <input id="officialServiceUrl" class="form-control" type="text" readonly value="<?php echo h($printerServiceUrlCurrent); ?>">
                                                    </div>
                                                    <div class="col-lg-6">
                                                        <label class="z-label" for="printerServiceUrl">URL do ZaldoPrinter</label>
                                                        <input id="printerServiceUrl" name="printer_service_url" class="form-control" type="url" inputmode="url" autocomplete="off" value="<?php echo h($printerServiceUrlCurrent); ?>" placeholder="http://127.0.0.1:16161">
                                                        <small class="z-hint">Normalmente é local no PC do caixa (ex.: <code>http://127.0.0.1:16161</code>). Para rede, use o IP do computador.</small>
                                                    </div>
                                                    <div class="col-lg-6">
                                                        <label class="z-label" for="printerServiceToken">Token (gerado no ZaldoPrinter)</label>
                                                        <div class="z-input-row">
                                                            <input id="printerServiceToken" name="printer_service_token" class="form-control" type="password" autocomplete="off" value="<?php echo h($printerServiceToken); ?>" placeholder="Cole aqui o token de pareamento">
                                                            <button class="z-mini-btn" type="button" id="btnToggleToken">Mostrar</button>
                                                        </div>
                                                        <small class="z-hint">Abra o app <strong>Zaldo Printer Config</strong> no Windows, copie o <em>Token de pareamento</em> e cole aqui. Este token é por empresa/terminal.</small>
                                                    </div>
                                                </div>

                                                <?php if ($printerServiceToken === ''): ?>
                                                    <div class="z-badge-warn">Token não definido para este terminal. Gere no <strong>ZaldoPrinter</strong> e cole acima.</div>
                                                <?php endif; ?>

                                                <div class="d-flex flex-wrap gap-2 mt-3">
                                                    <button type="button" class="small-btn" id="btnOfficialHealthCheck">Testar conexão</button>
                                                    <button type="button" class="small-btn" id="btnLoadZpProfiles">Carregar impressoras</button>
                                                    <button type="button" class="small-btn" data-copy-service-url="<?php echo h($printerServiceUrlCurrent); ?>">Copiar URL</button>
                                                    <button type="button" class="small-btn" data-copy-service-token="<?php echo h($printerServiceToken); ?>" <?php echo $printerServiceToken === '' ? 'disabled' : ''; ?>>Copiar Token</button>
                                                    <a class="z-link-btn" href="/zaldo_printer_download.php"><i class="bi bi-download"></i> Download instalador oficial</a>
                                                    <button type="button" class="z-link-btn" data-bs-toggle="modal" data-bs-target="#modalOfficialGuide"><i class="bi bi-journal-text"></i> Guia de instalação</button>
                                                </div>
                                            </div>
                                        </div>
                                    </div>

                                    <div class="accordion-item">
                                        <h2 class="accordion-header">
                                            <button class="accordion-button collapsed" type="button" data-bs-toggle="collapse" data-bs-target="#accLegacy" aria-expanded="false" aria-controls="accLegacy">
                                                Fallback Legado (Bridge Local)
                                            </button>
                                        </h2>
                                        <div id="accLegacy" class="accordion-collapse collapse" data-bs-parent="#printAccordion">
                                            <div class="accordion-body">
                                                <div class="z-badge-warn">Recomendado apenas se Zaldo Printer não for possível.</div>
                                                <div class="row g-3 mt-1">
                                                    <div class="col-lg-7">
                                                        <label class="z-label" for="bridgeUrlCurrent">URL do bridge</label>
                                                        <input id="bridgeUrlCurrent" class="form-control" type="text" readonly value="<?php echo h($bridgeUrl !== '' ? $bridgeUrl : 'http://127.0.0.1:9123'); ?>">
                                                    </div>
                                                    <div class="col-lg-5 d-flex align-items-end">
                                                        <button type="button" class="small-btn" id="btnBridgeHealthCheck">Testar bridge</button>
                                                    </div>
                                                </div>
                                                <div id="bridgeHealthStatus" class="z-status mt-2">Bridge não testada nesta página.</div>

                                                <div class="d-flex flex-wrap gap-2 mt-3">
                                                    <a class="z-link-btn" href="/pos_bridge_download.php?target=windows"><i class="bi bi-download"></i> Download Windows</a>
                                                    <a class="z-link-btn" href="/pos_bridge_download.php?target=unix"><i class="bi bi-download"></i> Download macOS/Linux</a>
                                                    <button type="button" class="z-link-btn" data-bs-toggle="modal" data-bs-target="#modalLegacyGuide"><i class="bi bi-journal-text"></i> Guia</button>
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                </div>

                                <div class="z-advanced-toggle">
                                    <button type="button" class="z-advanced-btn" id="btnToggleAdvanced">Abrir modo avançado</button>
                                    <span class="z-help-text">Modo avançado exibe configuração completa da impressora principal e auxiliares.</span>
                                </div>

                                <div class="collapse" id="advancedConfigPanel">
                                    <div class="z-card mt-2">
                                        <h4 class="z-card-title" style="font-size:.86rem;">Impressora principal (avançado)</h4>
                                        <div class="row g-3 mt-1">
                                            <div class="col-lg-3">
                                                <label class="z-label" for="primary_name">Nome</label>
                                                <input id="primary_name" type="text" name="primary_name" class="form-control" maxlength="90" value="<?php echo h((string)($primaryPrinter['name'] ?? 'Principal')); ?>">
                                            </div>
                                            <div class="col-lg-3">
                                                <label class="z-label" for="primary_zp_profile_id">Perfil (Zaldo Printer)</label>
                                                <select id="primary_zp_profile_id" name="primary_zp_profile_id" class="form-select zp-profile-select" data-zp-target="primary" data-current="<?php echo h((string)($primaryPrinter['zp_profile_id'] ?? '')); ?>">
                                                    <option value="">(Selecionar...)\</option>
                                                </select>
                                                <small class="z-hint">Escolha o perfil configurado no app <strong>Zaldo Printer Config</strong>.\</small>
                                            </div>
                                            <div class="col-lg-2">
                                                <label class="z-label" for="primary_timeout_ms">Timeout (ms)</label>
                                                <input id="primary_timeout_ms" type="text" name="primary_timeout_ms" class="form-control" value="<?php echo h((string)($primaryPrinter['timeout_ms'] ?? 2500)); ?>">
                                            </div>
                                            <div class="col-lg-2">
                                                <label class="z-label">Principal ativa</label>
                                                <div class="z-check">
                                                    <input type="hidden" name="primary_enabled" value="0">
                                                    <input type="checkbox" name="primary_enabled" value="1" <?php echo !empty($primaryPrinter['enabled']) ? 'checked' : ''; ?>>
                                                    Ativa
                                                </div>
                                            </div>
                                            <div class="col-lg-2">
                                                <label class="z-label">Imprime talão</label>
                                                <div class="z-check">
                                                    <input type="hidden" name="primary_print_receipt" value="0">
                                                    <input type="checkbox" name="primary_print_receipt" value="1" <?php echo !empty($primaryPrinter['print_receipt']) ? 'checked' : ''; ?>>
                                                    Sim
                                                </div>
                                            </div>
                                            <div class="col-lg-2">
                                                <label class="z-label">Abre gaveta</label>
                                                <div class="z-check">
                                                    <input type="hidden" name="primary_open_drawer" value="0">
                                                    <input type="checkbox" name="primary_open_drawer" value="1" <?php echo !empty($primaryPrinter['open_drawer']) ? 'checked' : ''; ?>>
                                                    Sim
                                                </div>
                                            </div>
                                            <div class="col-lg-1">
                                                <label class="z-label">Corta</label>
                                                <div class="z-check">
                                                    <input type="hidden" name="primary_cut_paper" value="0">
                                                    <input type="checkbox" name="primary_cut_paper" value="1" <?php echo !empty($primaryPrinter['cut_paper']) ? 'checked' : ''; ?>>
                                                    Sim
                                                </div>
                                            </div>
                                        </div>
                                    </div>

                                    <div class="z-card mt-2">
                                        <div class="d-flex justify-content-between align-items-center gap-2 flex-wrap">
                                            <h4 class="z-card-title" style="font-size:.86rem;">Impressoras auxiliares (cozinha/salão/bar)</h4>
                                            <div class="d-flex gap-2">
                                                <button type="button" id="btnAddCopyPrinter" class="small-btn">Adicionar auxiliar</button>
                                                <button type="button" class="small-btn" data-bs-toggle="collapse" data-bs-target="#categoriesHintBox" aria-expanded="false" aria-controls="categoriesHintBox">Categorias disponíveis</button>
                                            </div>
                                        </div>

                                        <div class="collapse mt-2" id="categoriesHintBox">
                                            <div class="z-status"><?php echo h($categoryHintText); ?></div>
                                        </div>

                                        <div class="copies-wrap" id="copiesWrap">
                                            <?php foreach ($copyPrinters as $copyIndex => $copy): ?>
                                                <?php
                                                    $idx = (int)$copyIndex;
                                                    $copyCategoryIds = isset($copy['category_ids']) && is_array($copy['category_ids']) ? $copy['category_ids'] : [];
                                                    $copyCategoryValue = implode(',', array_map(static fn($v): string => (string)(int)$v, $copyCategoryIds));
                                                ?>
                                                <div class="copy-row" data-copy-row="<?php echo $idx; ?>">
                                                    <div class="copy-row-grid">
                                                        <div>
                                                            <label class="z-label">ID</label>
                                                            <input type="text" name="copies[<?php echo $idx; ?>][id]" class="form-control" maxlength="40" value="<?php echo h((string)($copy['id'] ?? 'AUX')); ?>">
                                                        </div>
                                                        <div>
                                                            <label class="z-label">Nome</label>
                                                            <input type="text" name="copies[<?php echo $idx; ?>][name]" class="form-control" maxlength="90" value="<?php echo h((string)($copy['name'] ?? 'Auxiliar')); ?>">
                                                        </div>
                                                        <div>
                                                            <label class="z-label">Perfil (ZP)</label>
                                                            <select name="copies[<?php echo $idx; ?>][zp_profile_id]" class="form-select zp-profile-select" data-zp-target="copy" data-current="<?php echo h((string)($copy['zp_profile_id'] ?? '')); ?>">
                                                                <option value="">(Selecionar...)\</option>
                                                            </select>
                                                        </div>
                                                        <div>
                                                            <label class="z-label">Transporte</label>
                                                            <select name="copies[<?php echo $idx; ?>][transport]" class="form-select">
                                                                <?php foreach ($transportOptions as $tValue => $tLabel): ?>
                                                                    <option value="<?php echo h($tValue); ?>" <?php echo ((string)($copy['transport'] ?? 'NETWORK') === $tValue) ? 'selected' : ''; ?>>
                                                                        <?php echo h($tLabel); ?>
                                                                    </option>
                                                                <?php endforeach; ?>
                                                            </select>
                                                        </div>
                                                        <div>
                                                            <label class="z-label">Host/IP</label>
                                                            <input type="text" name="copies[<?php echo $idx; ?>][host]" class="form-control" value="<?php echo h((string)($copy['host'] ?? '')); ?>" placeholder="192.168.1.51">
                                                        </div>
                                                        <div>
                                                            <label class="z-label">Porta</label>
                                                            <input type="text" name="copies[<?php echo $idx; ?>][port]" class="form-control" value="<?php echo h((string)($copy['port'] ?? 9100)); ?>">
                                                        </div>
                                                        <div>
                                                            <label class="z-label">USB</label>
                                                            <input type="text" name="copies[<?php echo $idx; ?>][usb_device]" class="form-control" value="<?php echo h((string)($copy['usb_device'] ?? '')); ?>" placeholder="/dev/usb/lp1">
                                                        </div>
                                                        <div>
                                                            <label class="z-label">Bluetooth</label>
                                                            <input type="text" name="copies[<?php echo $idx; ?>][bluetooth_address]" class="form-control" value="<?php echo h((string)($copy['bluetooth_address'] ?? '')); ?>">
                                                        </div>
                                                        <div>
                                                            <label class="z-label">BT Porta</label>
                                                            <input type="text" name="copies[<?php echo $idx; ?>][bluetooth_port]" class="form-control" value="<?php echo h((string)($copy['bluetooth_port'] ?? 1)); ?>">
                                                        </div>
                                                    </div>

                                                    <div class="copy-row-grid secondary">
                                                        <div>
                                                            <label class="z-label">Route</label>
                                                            <select name="copies[<?php echo $idx; ?>][route_mode]" class="form-select">
                                                                <option value="ALL" <?php echo ((string)($copy['route_mode'] ?? 'ALL') === 'ALL') ? 'selected' : ''; ?>>Todas</option>
                                                                <option value="CATEGORIES" <?php echo ((string)($copy['route_mode'] ?? 'ALL') === 'CATEGORIES') ? 'selected' : ''; ?>>Categorias</option>
                                                            </select>
                                                        </div>
                                                        <div>
                                                            <label class="z-label">Timeout (ms)</label>
                                                            <input type="text" name="copies[<?php echo $idx; ?>][timeout_ms]" class="form-control" value="<?php echo h((string)($copy['timeout_ms'] ?? 2500)); ?>">
                                                        </div>
                                                        <div>
                                                            <label class="z-label">Categorias (IDs)</label>
                                                            <input type="text" name="copies[<?php echo $idx; ?>][category_ids]" class="form-control" value="<?php echo h($copyCategoryValue); ?>" placeholder="3,5,8">
                                                        </div>
                                                        <div>
                                                            <label class="z-label">Ativa</label>
                                                            <div class="z-check">
                                                                <input type="hidden" name="copies[<?php echo $idx; ?>][enabled]" value="0">
                                                                <input type="checkbox" name="copies[<?php echo $idx; ?>][enabled]" value="1" <?php echo !empty($copy['enabled']) ? 'checked' : ''; ?>>
                                                                Sim
                                                            </div>
                                                        </div>
                                                        <div>
                                                            <label class="z-label">Imprimir</label>
                                                            <div class="z-check">
                                                                <input type="hidden" name="copies[<?php echo $idx; ?>][print_receipt]" value="0">
                                                                <input type="checkbox" name="copies[<?php echo $idx; ?>][print_receipt]" value="1" <?php echo !empty($copy['print_receipt']) ? 'checked' : ''; ?>>
                                                                Sim
                                                            </div>
                                                        </div>
                                                        <div></div>
                                                    </div>

                                                    <div class="copy-actions">
                                                        <button type="button" class="small-btn danger" data-remove-copy>Remover</button>
                                                    </div>
                                                </div>
                                            <?php endforeach; ?>
                                        </div>
                                    </div>

                                    <div class="d-flex justify-content-end mt-2">
                                        <button type="submit" class="z-save-btn">Guardar impressoras</button>
                                    </div>
                                </div>

                                <div class="accordion mt-2" id="diagAccordion">
                                    <div class="accordion-item">
                                        <h2 class="accordion-header">
                                            <button class="accordion-button collapsed" type="button" data-bs-toggle="collapse" data-bs-target="#accDiag" aria-expanded="false" aria-controls="accDiag">
                                                Diagnóstico
                                            </button>
                                        </h2>
                                        <div id="accDiag" class="accordion-collapse collapse" data-bs-parent="#diagAccordion">
                                            <div class="accordion-body">
                                                <div class="z-diagnostic-grid">
                                                    <div>
                                                        <label class="z-label">IP deste servidor</label>
                                                        <div class="z-ip-list" id="serverIpList">
                                                            <?php if ($serverIps !== []): ?>
                                                                <?php foreach ($serverIps as $ip): ?>
                                                                    <div class="z-ip-row">
                                                                        <span class="z-ip"><?php echo h($ip); ?></span>
                                                                        <span class="d-flex gap-1">
                                                                            <button type="button" class="small-btn" data-fill-primary-host="<?php echo h($ip); ?>">Usar</button>
                                                                            <button type="button" class="small-btn" data-copy-ip="<?php echo h($ip); ?>">Copiar</button>
                                                                        </span>
                                                                    </div>
                                                                <?php endforeach; ?>
                                                            <?php else: ?>
                                                                <div class="z-status err">Não foi possível detetar IP automático.</div>
                                                            <?php endif; ?>
                                                        </div>
                                                    </div>

                                                    <div>
                                                        <label class="z-label">IP do computador do operador</label>
                                                        <div id="browserIpList" class="z-ip-list">
                                                            <div class="z-status" id="browserIpStatus">A detetar IP local automaticamente neste navegador...</div>
                                                        </div>
                                                        <div class="z-note mt-2">
                                                            Windows: <code>ipconfig</code><br>
                                                            macOS: <code>ipconfig getifaddr en0</code> ou <code>ifconfig</code><br>
                                                            Linux: <code>hostname -I</code> ou <code>ip a</code>
                                                        </div>
                                                    </div>
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </form>
                        </div>
                    </div>

                    <div class="tab-pane fade z-pane" id="tab-pagamentos" role="tabpanel">
                        <div class="z-card">
                            <h3 class="z-card-title">Pagamentos</h3>
                            <p class="z-card-sub">Gestão das formas de pagamento usadas no POS.</p>
                            <div class="z-shortcuts mt-2" style="grid-template-columns:repeat(2,minmax(0,1fr));">
                                <a class="z-tile" href="<?php echo h(z_with_from_pos('/pos_pagamentos.php', $fromPos)); ?>">
                                    <strong>Formas de Pagamento</strong>
                                    <span>Adicionar, ativar e ordenar meios de pagamento.</span>
                                </a>
                                <a class="z-tile" href="<?php echo h(z_with_from_pos('/pos_storefront.php', $fromPos)); ?>">
                                    <strong>Storefront &amp; Mesas</strong>
                                    <span>Fila de aceitação, mesas/dispositivos e turnos.</span>
                                </a>
                            </div>
                        </div>
                    </div>

                    <div class="tab-pane fade z-pane" id="tab-produtos" role="tabpanel">
                        <div class="z-card">
                            <h3 class="z-card-title">Produtos</h3>
                            <p class="z-card-sub">Acesso ao catálogo e cadastro rápido de artigos.</p>
                            <div class="z-shortcuts mt-2" style="grid-template-columns:repeat(2,minmax(0,1fr));">
                                <?php if ($canViewProducts): ?>
                                    <a class="z-tile" href="<?php echo h(z_with_from_pos('/produtos_listar.php', $fromPos)); ?>">
                                        <strong>Produtos do Catálogo</strong>
                                        <span>Editar preços, IVA, imagens e disponibilidade.</span>
                                    </a>
                                <?php endif; ?>
                                <?php if ($canManageProducts): ?>
                                    <a class="z-tile" href="<?php echo h(z_with_from_pos('/produtos_novo.php', $fromPos)); ?>">
                                        <strong>Novo Produto</strong>
                                        <span>Criar rapidamente um novo artigo de venda.</span>
                                    </a>
                                <?php endif; ?>
                            </div>
                        </div>
                    </div>

                    <div class="tab-pane fade z-pane" id="tab-operadores" role="tabpanel">
                        <div class="z-card">
                            <h3 class="z-card-title">Operadores &amp; Permissões</h3>
                            <p class="z-card-sub">Gestão de equipa, perfis de acesso e séries fiscais.</p>
                            <div class="z-shortcuts mt-2" style="grid-template-columns:repeat(3,minmax(0,1fr));">
                                <a class="z-tile" href="<?php echo h(z_with_from_pos('/operadores.php', $fromPos)); ?>">
                                    <strong>Operadores</strong>
                                    <span>Utilizadores e equipas do POS.</span>
                                </a>
                                <a class="z-tile" href="<?php echo h(z_with_from_pos('/cargos.php', $fromPos)); ?>">
                                    <strong>Cargos e Permissões</strong>
                                    <span>Defina níveis de acesso por perfil.</span>
                                </a>
                                <a class="z-tile" href="<?php echo h(z_with_from_pos('/series.php', $fromPos)); ?>">
                                    <strong>Séries Fiscais (ATCUD)</strong>
                                    <span>Gestão centralizada das séries de emissão.</span>
                                </a>
                            </div>
                        </div>
                    </div>

                    <div class="tab-pane fade z-pane" id="tab-atalhos" role="tabpanel">
                        <div class="z-card">
                            <h3 class="z-card-title">Atalhos</h3>
                            <p class="z-card-sub">Acessos rápidos para operações frequentes do POS e fiscal.</p>
                            <div class="z-shortcuts mt-2">
                                <a class="z-tile" href="<?php echo h(z_with_from_pos('/pos_pagamentos.php', $fromPos)); ?>">
                                    <strong>Formas de Pagamento</strong>
                                    <span>Configurar métodos aceites no checkout.</span>
                                </a>
                                <a class="z-tile" href="<?php echo h(z_with_from_pos('/pos_storefront.php', $fromPos)); ?>">
                                    <strong>Storefront &amp; Mesas</strong>
                                    <span>Operar fila de aceitação e sessões de mesa.</span>
                                </a>
                                <?php if ($canViewProducts): ?>
                                    <a class="z-tile" href="<?php echo h(z_with_from_pos('/produtos_listar.php', $fromPos)); ?>">
                                        <strong>Produtos do Catálogo</strong>
                                        <span>Consultar e editar artigos do POS.</span>
                                    </a>
                                <?php endif; ?>
                                <?php if ($canManageProducts): ?>
                                    <a class="z-tile" href="<?php echo h(z_with_from_pos('/produtos_novo.php', $fromPos)); ?>">
                                        <strong>Novo Produto</strong>
                                        <span>Cadastro rápido de produto.</span>
                                    </a>
                                <?php endif; ?>
                                <a class="z-tile" href="<?php echo h(z_with_from_pos('/operadores.php', $fromPos)); ?>">
                                    <strong>Operadores</strong>
                                    <span>Gestão de utilizadores da loja.</span>
                                </a>
                                <a class="z-tile" href="<?php echo h(z_with_from_pos('/cargos.php', $fromPos)); ?>">
                                    <strong>Cargos e Permissões</strong>
                                    <span>Permissões operacionais por perfil.</span>
                                </a>
                                <a class="z-tile" href="<?php echo h(z_with_from_pos('/series.php', $fromPos)); ?>">
                                    <strong>Séries Fiscais (ATCUD)</strong>
                                    <span>Acesso direto à gestão de séries fiscais.</span>
                                </a>
                                <a class="z-tile" href="<?php echo h(z_with_from_pos('/config_empresa.php', $fromPos)); ?>">
                                    <strong>Dados da Empresa e Fiscal</strong>
                                    <span>Parâmetros fiscais e contacto do contabilista.</span>
                                </a>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <div class="modal fade" id="modalQuickPrintHelp" tabindex="-1" aria-hidden="true">
        <div class="modal-dialog modal-dialog-centered">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">Configuração rápida de impressão</h5>
                    <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Fechar"></button>
                </div>
                <div class="modal-body">
                    <ol class="z-modal-list">
                        <li>Ative <strong>Impressão automática ativa</strong>.</li>
                        <li>Escolha o transporte principal: Rede, USB ou Bluetooth.</li>
                        <li>Em Rede, informe IP da impressora e porta 9100.</li>
                        <li>Em USB/Bluetooth, deixe Host/IP vazio.</li>
                        <li>Guarde e teste em ambiente real de caixa.</li>
                    </ol>
                </div>
            </div>
        </div>
    </div>

    <div class="modal fade" id="modalOfficialGuide" tabindex="-1" aria-hidden="true">
        <div class="modal-dialog modal-dialog-centered">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">Guia rápido: Zaldo Printer Oficial</h5>
                    <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Fechar"></button>
                </div>
                <div class="modal-body">
                    <ol class="z-modal-list">
                        <li>Instale o pacote oficial no computador do operador.</li>
                        <li>Confirme serviço ativo em <code>http://127.0.0.1:16161/health</code>.</li>
                        <li>Defina token no ambiente: <code>POS_ZALDO_PRINTER_TOKEN=...</code>.</li>
                        <li>Volte ao POS e clique em <strong>Testar conexão</strong>.</li>
                    </ol>
                    <a class="z-link-btn mt-2" href="/pos_bridge_guia.php?from=pos" target="_blank" rel="noopener">
                        <i class="bi bi-box-arrow-up-right"></i> Abrir guia completo
                    </a>
                </div>
            </div>
        </div>
    </div>

    <div class="modal fade" id="modalLegacyGuide" tabindex="-1" aria-hidden="true">
        <div class="modal-dialog modal-dialog-centered">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">Guia rápido: Bridge legado</h5>
                    <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Fechar"></button>
                </div>
                <div class="modal-body">
                    <ol class="z-modal-list">
                        <li>Use apenas quando o Zaldo Printer oficial não for viável.</li>
                        <li>Instale o pacote correto (Windows ou macOS/Linux).</li>
                        <li>Valide a saúde local em <code>http://127.0.0.1:9123/health</code>.</li>
                        <li>Defina <code>POS_PRINTER_BRIDGE_URL</code> no servidor.</li>
                    </ol>
                    <a class="z-link-btn mt-2" href="/pos_bridge_guia.php?from=pos" target="_blank" rel="noopener">
                        <i class="bi bi-box-arrow-up-right"></i> Abrir guia completo
                    </a>
                </div>
            </div>
        </div>
    </div>

    <script src="https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js"></script>
    <script>
        (function () {
            const wrap = document.getElementById('copiesWrap');
            const addBtn = document.getElementById('btnAddCopyPrinter');
            const primaryTransport = document.getElementById('primary_transport');
            const primaryHost = document.getElementById('primary_host');
            const primaryPort = document.getElementById('primary_port');
            const primaryUsb = document.getElementById('primary_usb_device');
            const primaryBtAddr = document.getElementById('primary_bluetooth_address');
            const primaryBtPort = document.getElementById('primary_bluetooth_port');

            const browserIpList = document.getElementById('browserIpList');
            const browserIpStatus = document.getElementById('browserIpStatus');
            const btnOfficialHealthCheck = document.getElementById('btnOfficialHealthCheck');
            const btnLoadZpProfiles = document.getElementById('btnLoadZpProfiles');
            const officialHealthStatus = document.getElementById('officialHealthStatus');
            const btnBridgeHealthCheck = document.getElementById('btnBridgeHealthCheck');
            const bridgeHealthStatus = document.getElementById('bridgeHealthStatus');

            const pane = document.getElementById('printerConfigPane');
            const btnToggleAdvanced = document.getElementById('btnToggleAdvanced');
            const advancedPanel = document.getElementById('advancedConfigPanel');
            const advancedCollapse = advancedPanel ? new bootstrap.Collapse(advancedPanel, { toggle: false }) : null;

            const printerServiceUrlInput = document.getElementById('printerServiceUrl');
            const printerServiceTokenInput = document.getElementById('printerServiceToken');
const officialServiceUrlFromEnv = <?php echo json_encode($printerServiceUrlCurrent, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES); ?>;
            const officialServiceTokenFromEnv = <?php echo json_encode($printerServiceToken, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES); ?>;
            const bridgeUrlFromEnv = <?php echo json_encode($bridgeUrl, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES); ?>;

            const transportOptionsHtml = <?php
                $opts = '';
                foreach ($transportOptions as $tValue => $tLabel) {
                    $opts .= '<option value="' . h($tValue) . '">' . h($tLabel) . '</option>';
                }
                echo json_encode($opts, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
            ?>;

            function nextIndex() {
                if (!(wrap instanceof HTMLElement)) return 0;
                let max = -1;
                wrap.querySelectorAll('[data-copy-row]').forEach((node) => {
                    const raw = Number(node.getAttribute('data-copy-row') || '0');
                    if (Number.isFinite(raw) && raw > max) max = raw;
                });
                return max + 1;
            }

            function createRow(index) {
                const row = document.createElement('div');
                row.className = 'copy-row';
                row.setAttribute('data-copy-row', String(index));
                row.innerHTML = `
                    <div class="copy-row-grid">
                        <div><label class="z-label">ID</label><input type="text" name="copies[${index}][id]" class="form-control" maxlength="40" value="AUX_${index + 1}"></div>
                        <div><label class="z-label">Nome</label><input type="text" name="copies[${index}][name]" class="form-control" maxlength="90" value="Auxiliar ${index + 1}"></div>
                        <div><label class="z-label">Perfil (ZP)</label><select name="copies[${index}][zp_profile_id]" class="form-select zp-profile-select" data-zp-target="copy"><option value="">(Selecionar...)\</option></select></div>
                        <div><label class="z-label">Transporte</label><select name="copies[${index}][transport]" class="form-select">${transportOptionsHtml}</select></div>
                        <div><label class="z-label">Host/IP</label><input type="text" name="copies[${index}][host]" class="form-control" placeholder="192.168.1.51"></div>
                        <div><label class="z-label">Porta</label><input type="text" name="copies[${index}][port]" class="form-control" value="9100"></div>
                        <div><label class="z-label">USB</label><input type="text" name="copies[${index}][usb_device]" class="form-control" placeholder="/dev/usb/lp1"></div>
                        <div><label class="z-label">Bluetooth</label><input type="text" name="copies[${index}][bluetooth_address]" class="form-control"></div>
                        <div><label class="z-label">BT Porta</label><input type="text" name="copies[${index}][bluetooth_port]" class="form-control" value="1"></div>
                    </div>
                    <div class="copy-row-grid secondary">
                        <div><label class="z-label">Route</label><select name="copies[${index}][route_mode]" class="form-select"><option value="ALL">Todas</option><option value="CATEGORIES">Categorias</option></select></div>
                        <div><label class="z-label">Timeout (ms)</label><input type="text" name="copies[${index}][timeout_ms]" class="form-control" value="2500"></div>
                        <div><label class="z-label">Categorias (IDs)</label><input type="text" name="copies[${index}][category_ids]" class="form-control" placeholder="3,5,8"></div>
                        <div><label class="z-label">Ativa</label><div class="z-check"><input type="hidden" name="copies[${index}][enabled]" value="0"><input type="checkbox" name="copies[${index}][enabled]" value="1" checked>Sim</div></div>
                        <div><label class="z-label">Imprimir</label><div class="z-check"><input type="hidden" name="copies[${index}][print_receipt]" value="0"><input type="checkbox" name="copies[${index}][print_receipt]" value="1" checked>Sim</div></div>
                        <div></div>
                    </div>
                    <div class="copy-actions"><button type="button" class="small-btn danger" data-remove-copy>Remover</button></div>
                `;
                return row;
            }

            function setFieldEnabled(input, enabled) {
                if (!(input instanceof HTMLInputElement) && !(input instanceof HTMLSelectElement)) return;
                input.disabled = !enabled;
                const parent = input.closest('div');
                if (parent) parent.style.opacity = enabled ? '1' : '.5';
            }

            function showPrimaryTransportGroups(mode) {
                const groups = document.querySelectorAll('.transport-group');
                groups.forEach((group) => {
                    const requiredMode = String(group.getAttribute('data-transport') || '').toUpperCase();
                    group.style.display = requiredMode === mode ? '' : 'none';
                });
            }

            function applyPrimaryTransportState() {
                if (!(primaryTransport instanceof HTMLSelectElement)) return;
                const mode = String(primaryTransport.value || 'NETWORK').toUpperCase();
                showPrimaryTransportGroups(mode);
                setFieldEnabled(primaryHost, mode === 'NETWORK');
                setFieldEnabled(primaryPort, mode === 'NETWORK');
                setFieldEnabled(primaryUsb, mode === 'USB');
                setFieldEnabled(primaryBtAddr, mode === 'BLUETOOTH');
                setFieldEnabled(primaryBtPort, mode === 'BLUETOOTH');
            }

            function rowField(row, key) {
                return row.querySelector(`[name$="[${key}]"]`);
            }

            function applyCopyTransportState(row) {
                if (!(row instanceof HTMLElement)) return;
                const transport = rowField(row, 'transport');
                const host = rowField(row, 'host');
                const port = rowField(row, 'port');
                const usb = rowField(row, 'usb_device');
                const btAddr = rowField(row, 'bluetooth_address');
                const btPort = rowField(row, 'bluetooth_port');
                if (!(transport instanceof HTMLSelectElement)) return;
                const mode = String(transport.value || 'NETWORK').toUpperCase();
                setFieldEnabled(host, mode === 'NETWORK');
                setFieldEnabled(port, mode === 'NETWORK');
                setFieldEnabled(usb, mode === 'USB');
                setFieldEnabled(btAddr, mode === 'BLUETOOTH');
                setFieldEnabled(btPort, mode === 'BLUETOOTH');
            }

            function setAdvancedState(open) {
                if (!(pane instanceof HTMLElement)) return;
                pane.classList.toggle('z-advanced-open', open);
                if (btnToggleAdvanced instanceof HTMLButtonElement) {
                    btnToggleAdvanced.textContent = open ? 'Fechar modo avançado' : 'Abrir modo avançado';
                }
                try {
                    window.localStorage.setItem('zaldo_pos_print_mode', open ? 'advanced' : 'simple');
                } catch (_error) {}
            }

            if (advancedPanel instanceof HTMLElement) {
                advancedPanel.addEventListener('shown.bs.collapse', function () {
                    setAdvancedState(true);
                });
                advancedPanel.addEventListener('hidden.bs.collapse', function () {
                    setAdvancedState(false);
                });
            }

            if (btnToggleAdvanced instanceof HTMLButtonElement && advancedCollapse) {
                btnToggleAdvanced.addEventListener('click', function () {
                    const opened = advancedPanel.classList.contains('show');
                    if (opened) advancedCollapse.hide();
                    else advancedCollapse.show();
                });
            }

            if (wrap && addBtn) {
                addBtn.addEventListener('click', function () {
                    const row = createRow(nextIndex());
                    wrap.appendChild(row);
                    applyCopyTransportState(row);
                });

                wrap.addEventListener('click', function (event) {
                    const target = event.target;
                    if (!(target instanceof HTMLElement)) return;
                    const btn = target.closest('[data-remove-copy]');
                    if (!btn) return;
                    const row = btn.closest('[data-copy-row]');
                    if (!row) return;
                    if (wrap.querySelectorAll('[data-copy-row]').length <= 1) {
                        const inputs = row.querySelectorAll('input');
                        inputs.forEach((input) => {
                            if (!(input instanceof HTMLInputElement)) return;
                            if (input.type === 'checkbox') input.checked = false;
                            else input.value = '';
                        });
                        return;
                    }
                    row.remove();
                });

                wrap.addEventListener('change', function (event) {
                    const target = event.target;
                    if (!(target instanceof HTMLSelectElement)) return;
                    if (!target.name.endsWith('[transport]')) return;
                    const row = target.closest('[data-copy-row]');
                    if (row) applyCopyTransportState(row);
                });
            }

            function showButtonDone(btn, text) {
                if (!(btn instanceof HTMLButtonElement)) return;
                const prev = btn.textContent;
                btn.textContent = text;
                btn.disabled = true;
                window.setTimeout(() => {
                    btn.textContent = prev || '';
                    btn.disabled = false;
                }, 900);
            }

            function tryCopyToClipboard(value, btn) {
                const text = String(value || '').trim();
                if (text === '') return;
                if (!navigator.clipboard || !navigator.clipboard.writeText) {
                    if (btn instanceof HTMLButtonElement) showButtonDone(btn, 'Sem suporte');
                    return;
                }
                navigator.clipboard.writeText(text).then(() => {
                    showButtonDone(btn, 'Copiado');
                }).catch(() => {
                    showButtonDone(btn, 'Falhou');
                });
            }

            document.addEventListener('click', function (event) {
                const target = event.target;
                if (!(target instanceof HTMLElement)) return;
                const copyServiceUrlBtn = target.closest('[data-copy-service-url]');
                if (copyServiceUrlBtn) {
                    const value = String(copyServiceUrlBtn.getAttribute('data-copy-service-url') || '').trim();
                    tryCopyToClipboard(value, copyServiceUrlBtn);
                    return;
                }
                const copyServiceTokenBtn = target.closest('[data-copy-service-token]');
                if (copyServiceTokenBtn) {
                    const value = String(copyServiceTokenBtn.getAttribute('data-copy-service-token') || '').trim();
                    tryCopyToClipboard(value, copyServiceTokenBtn);
                    return;
                }
                const fillBtn = target.closest('[data-fill-primary-host]');
                if (fillBtn) {
                    const ip = String(fillBtn.getAttribute('data-fill-primary-host') || '').trim();
                    if (primaryHost instanceof HTMLInputElement && ip !== '') {
                        if (primaryTransport instanceof HTMLSelectElement) {
                            primaryTransport.value = 'NETWORK';
                            applyPrimaryTransportState();
                        }
                        primaryHost.value = ip;
                        primaryHost.focus();
                        primaryHost.select();
                        showButtonDone(fillBtn, 'Aplicado');
                    }
                    return;
                }
                const copyBtn = target.closest('[data-copy-ip]');
                if (copyBtn) {
                    const ip = String(copyBtn.getAttribute('data-copy-ip') || '').trim();
                    tryCopyToClipboard(ip, copyBtn);
                }
            });

            function isValidIpv4(raw) {
                const ip = String(raw || '').trim();
                if (!/^(\d{1,3}\.){3}\d{1,3}$/.test(ip)) return false;
                const parts = ip.split('.').map(Number);
                if (parts.length !== 4) return false;
                return parts.every((part) => Number.isInteger(part) && part >= 0 && part <= 255);
            }

            function isPrivateIpv4(ip) {
                if (!isValidIpv4(ip)) return false;
                const parts = ip.split('.').map(Number);
                if (parts[0] === 10 || parts[0] === 127) return true;
                if (parts[0] === 192 && parts[1] === 168) return true;
                return (parts[0] === 172 && parts[1] >= 16 && parts[1] <= 31);
            }

            function renderBrowserIps(list) {
                if (!(browserIpList instanceof HTMLElement)) return;
                const ips = Array.from(new Set((Array.isArray(list) ? list : []).map((v) => String(v || '').trim()).filter((v) => v !== '')));
                if (ips.length === 0) {
                    if (browserIpStatus instanceof HTMLElement) {
                        browserIpStatus.textContent = 'Não foi possível detectar automaticamente no navegador.';
                    }
                    return;
                }
                browserIpList.innerHTML = '';
                ips.forEach((ip) => {
                    const row = document.createElement('div');
                    row.className = 'z-ip-row';
                    row.innerHTML = '<span class="z-ip"></span><span class="d-flex gap-1"><button type="button" class="small-btn" data-fill-primary-host=""></button><button type="button" class="small-btn" data-copy-ip=""></button></span>';
                    const valueNode = row.querySelector('.z-ip');
                    const fillBtn = row.querySelector('[data-fill-primary-host]');
                    const copyBtn = row.querySelector('[data-copy-ip]');
                    if (valueNode) valueNode.textContent = ip;
                    if (fillBtn instanceof HTMLButtonElement) {
                        fillBtn.textContent = 'Usar';
                        fillBtn.setAttribute('data-fill-primary-host', ip);
                    }
                    if (copyBtn instanceof HTMLButtonElement) {
                        copyBtn.textContent = 'Copiar';
                        copyBtn.setAttribute('data-copy-ip', ip);
                    }
                    browserIpList.appendChild(row);
                });
            }

            function extractIpsFromCandidate(candidate, set) {
                const line = String(candidate || '');
                const matches = line.match(/(\d{1,3}(?:\.\d{1,3}){3})/g) || [];
                matches.forEach((ip) => {
                    if (isPrivateIpv4(ip)) set.add(ip);
                });
            }

            async function detectBrowserLocalIps() {
                if (!(browserIpStatus instanceof HTMLElement)) return;
                const RTCPeer = window.RTCPeerConnection || window.webkitRTCPeerConnection || window.mozRTCPeerConnection;
                if (typeof RTCPeer !== 'function') {
                    browserIpStatus.textContent = 'Detecção automática indisponível neste navegador.';
                    return;
                }
                const found = new Set();
                let pc = null;
                try {
                    pc = new RTCPeer({ iceServers: [] });
                    pc.createDataChannel('ip');
                    pc.onicecandidate = (event) => {
                        if (!event || !event.candidate) return;
                        extractIpsFromCandidate(event.candidate.candidate || '', found);
                    };
                    const offer = await pc.createOffer();
                    await pc.setLocalDescription(offer);
                    await new Promise((resolve) => window.setTimeout(resolve, 1200));
                    renderBrowserIps(Array.from(found));
                } catch (_error) {
                    browserIpStatus.textContent = 'Detecção automática indisponível neste navegador.';
                } finally {
                    if (pc && typeof pc.close === 'function') pc.close();
                }
            }

            function setOfficialStatus(text, ok) {
                if (!(officialHealthStatus instanceof HTMLElement)) return;
                officialHealthStatus.textContent = text;
                officialHealthStatus.classList.remove('ok', 'err');
                officialHealthStatus.classList.add(ok ? 'ok' : 'err');
            }

            function officialServiceCandidates() {
                const candidates = [];
                const push = (value) => {
                    const url = String(value || '').trim().replace(/\/+$/, '');
                    if (url === '') return;
                    if (!candidates.includes(url)) candidates.push(url);
                };
                push(officialServiceUrlFromEnv);
                push('http://127.0.0.1:16161');
                push('http://localhost:16161');
                return candidates;
            }

            async function fetchOfficialHealth(baseUrl) {
                const healthUrl = `${baseUrl}/health`;
                let controller = null;
                let timer = null;
                try {
                    if (typeof AbortController === 'function') {
                        controller = new AbortController();
                        timer = window.setTimeout(() => controller.abort(), 2400);
                    }
                    const headers = {};
                    if (officialServiceTokenFromEnv) {
                        headers['X-ZALDO-TOKEN'] = String(officialServiceTokenFromEnv);
                    }
                    const response = await fetch(healthUrl, {
                        method: 'GET',
                        mode: 'cors',
                        cache: 'no-store',
                        headers,
                        signal: controller ? controller.signal : undefined,
                    });
                    if (!response.ok) return { ok: false, error: `HTTP ${response.status}` };
                    const json = await response.json();
                    if (!json || json.ok !== true) return { ok: false, error: 'Resposta inválida do Zaldo Printer' };
                    const printers = Number(json.printersConfigured || 0);
                    const pendingObj = (json.pendingByPrinter && typeof json.pendingByPrinter === 'object') ? json.pendingByPrinter : {};
                    const pending = Object.values(pendingObj).reduce((sum, val) => sum + Number(val || 0), 0);
                    return { ok: true, printers, pending };
                } catch (_error) {
                    return { ok: false, error: 'Sem resposta do Zaldo Printer local' };
                } finally {
                    if (timer) window.clearTimeout(timer);
                }
            }

            

            function normalizeBaseUrl(raw) {
                const url = String(raw || '').trim().replace(/\/+$/, '');
                return url;
            }

            function currentOfficialBaseUrl() {
                if (printerServiceUrlInput instanceof HTMLInputElement || printerServiceUrlInput instanceof HTMLSelectElement) {
                    const val = normalizeBaseUrl(printerServiceUrlInput.value);
                    if (val) return val;
                }
                return normalizeBaseUrl(officialServiceUrlFromEnv) || 'http://127.0.0.1:16161';
            }

            function currentOfficialToken() {
                if (printerServiceTokenInput instanceof HTMLInputElement || printerServiceTokenInput instanceof HTMLSelectElement) {
                    const val = String(printerServiceTokenInput.value || '').trim();
                    if (val) return val;
                }
                return String(officialServiceTokenFromEnv || '').trim();
            }

            async function fetchZpProfiles(baseUrl, token) {
                const url = `${baseUrl}/printers`;
                let controller = null;
                let timer = null;
                try {
                    if (typeof AbortController === 'function') {
                        controller = new AbortController();
                        timer = window.setTimeout(() => controller.abort(), 2800);
                    }
                    const headers = { 'Content-Type': 'application/json' };
                    if (token) headers['X-ZALDO-TOKEN'] = token;
                    const response = await fetch(url, {
                        method: 'GET',
                        mode: 'cors',
                        cache: 'no-store',
                        headers,
                        signal: controller ? controller.signal : undefined,
                    });
                    if (!response.ok) return { ok: false, error: `HTTP ${response.status}` };
                    const json = await response.json();
                    if (!json || json.ok !== true) return { ok: false, error: 'Resposta inválida do endpoint /printers' };
                    const configured = Array.isArray(json.configured) ? json.configured : [];
                    const profiles = configured
                        .filter((p) => p && typeof p === 'object')
                        .map((p) => ({ id: String(p.id || ''), name: String(p.name || p.id || '') }))
                        .filter((p) => p.id);
                    profiles.sort((a, b) => a.name.localeCompare(b.name, 'pt'));
                    return { ok: true, profiles };
                } catch (_error) {
                    return { ok: false, error: 'Sem resposta do endpoint /printers' };
                } finally {
                    if (timer) window.clearTimeout(timer);
                }
            }

            function populateZpSelects(profiles) {
                const selects = document.querySelectorAll('select.zp-profile-select');
                selects.forEach((sel) => {
                    if (!(sel instanceof HTMLSelectElement)) return;
                    const currentAttr = String(sel.getAttribute('data-current') || '').trim();
                    const keep = String(sel.value || '').trim() || currentAttr;

                    sel.innerHTML = '';
                    const opt0 = document.createElement('option');
                    opt0.value = '';
                    opt0.textContent = '(Selecionar...)';
                    sel.appendChild(opt0);

                    profiles.forEach((p) => {
                        const opt = document.createElement('option');
                        opt.value = p.id;
                        opt.textContent = `${p.name} (${p.id})`;
                        sel.appendChild(opt);
                    });

                    if (keep) sel.value = keep;
                });
            }

            async function loadZpProfilesNow() {
                const baseUrl = currentOfficialBaseUrl();
                const token = currentOfficialToken();
                if (!token) {
                    setOfficialStatus('Token do Zaldo Printer vazio. Cole o token e tente novamente.', false);
                    return;
                }
                setOfficialStatus('A carregar perfis do Zaldo Printer...', false);
                const result = await fetchZpProfiles(baseUrl, token);
                if (!result.ok) {
                    setOfficialStatus(`Falha ao carregar impressoras: ${result.error}.`, false);
                    return;
                }
                populateZpSelects(result.profiles);
                setOfficialStatus(`Perfis carregados · ${result.profiles.length} impressora(s) configurada(s).`, true);
            }
async function testOfficialServiceNow() {
                const candidates = officialServiceCandidates();
                if (candidates.length === 0) {
                    setOfficialStatus('URL do Zaldo Printer não configurada.', false);
                    return;
                }
                setOfficialStatus('A testar Zaldo Printer local...', false);
                for (const url of candidates) {
                    const result = await fetchOfficialHealth(url);
                    if (result.ok) {
                        setOfficialStatus(`Online em ${url} · Impressoras: ${result.printers} · Fila: ${result.pending}`, true);
                        // Tenta carregar perfis configurados para mapeamento no POS
                        await loadZpProfilesNow();
                        return;
                    }
                }
                setOfficialStatus('Offline. Instale e valide http://127.0.0.1:16161/health', false);
            }

            function setBridgeStatus(text, ok) {
                if (!(bridgeHealthStatus instanceof HTMLElement)) return;
                bridgeHealthStatus.textContent = text;
                bridgeHealthStatus.classList.remove('ok', 'err');
                bridgeHealthStatus.classList.add(ok ? 'ok' : 'err');
            }

            function bridgeCandidates() {
                const candidates = [];
                const push = (value) => {
                    const url = String(value || '').trim().replace(/\/+$/, '');
                    if (url === '') return;
                    if (!candidates.includes(url)) candidates.push(url);
                };
                push(bridgeUrlFromEnv);
                push('http://127.0.0.1:9123');
                push('http://localhost:9123');
                return candidates;
            }

            async function fetchBridgeHealth(baseUrl) {
                const healthUrl = `${baseUrl}/health`;
                let controller = null;
                let timer = null;
                try {
                    if (typeof AbortController === 'function') {
                        controller = new AbortController();
                        timer = window.setTimeout(() => controller.abort(), 2200);
                    }
                    const response = await fetch(healthUrl, {
                        method: 'GET',
                        mode: 'cors',
                        cache: 'no-store',
                        signal: controller ? controller.signal : undefined,
                    });
                    if (!response.ok) return { ok: false, error: `HTTP ${response.status}` };
                    const json = await response.json();
                    if (!json || json.ok !== true) return { ok: false, error: 'Resposta inválida da bridge' };
                    return { ok: true, transport: String(json.transport || '').toUpperCase() };
                } catch (_error) {
                    return { ok: false, error: 'Sem resposta da bridge local' };
                } finally {
                    if (timer) window.clearTimeout(timer);
                }
            }

            async function testBridgeNow() {
                const candidates = bridgeCandidates();
                if (candidates.length === 0) {
                    setBridgeStatus('Bridge não configurada.', false);
                    return;
                }
                setBridgeStatus('A testar bridge local...', false);
                for (const url of candidates) {
                    const result = await fetchBridgeHealth(url);
                    if (result.ok) {
                        const transport = result.transport ? ` (${result.transport})` : '';
                        setBridgeStatus(`Online em ${url}${transport}`, true);
                        return;
                    }
                }
                setBridgeStatus('Offline. Execute a bridge e valide http://127.0.0.1:9123/health', false);
            }

            if (primaryTransport instanceof HTMLSelectElement) {
                primaryTransport.addEventListener('change', applyPrimaryTransportState);
                applyPrimaryTransportState();
            }

            if (wrap) {
                wrap.querySelectorAll('[data-copy-row]').forEach((row) => applyCopyTransportState(row));
            }

            let savedMode = 'simple';
            try {
                savedMode = String(window.localStorage.getItem('zaldo_pos_print_mode') || 'simple').toLowerCase();
            } catch (_error) {}
            if (savedMode === 'advanced' && advancedCollapse) {
                advancedCollapse.show();
            } else {
                setAdvancedState(false);
            }

            detectBrowserLocalIps();
            if (btnOfficialHealthCheck instanceof HTMLButtonElement) {
                btnOfficialHealthCheck.addEventListener('click', testOfficialServiceNow);
            }
            if (btnLoadZpProfiles instanceof HTMLButtonElement) {
                btnLoadZpProfiles.addEventListener('click', loadZpProfilesNow);
            }
            testOfficialServiceNow();
            if (btnBridgeHealthCheck instanceof HTMLButtonElement) {
                btnBridgeHealthCheck.addEventListener('click', testBridgeNow);
            }
            
            // Mostrar/ocultar token do ZaldoPrinter
            const btnToggleToken = document.getElementById('btnToggleToken');
            const tokenInput = document.getElementById('printerServiceToken');
            if (btnToggleToken instanceof HTMLButtonElement && tokenInput instanceof HTMLInputElement) {
                btnToggleToken.addEventListener('click', () => {
                    const isPassword = tokenInput.type === 'password';
                    tokenInput.type = isPassword ? 'text' : 'password';
                    btnToggleToken.textContent = isPassword ? 'Ocultar' : 'Mostrar';
                });
            }

testBridgeNow();
        })();
    </script>
</body>
</html>
