# Zaldo Printer (Windows)

Componente oficial do Zaldo para impressão térmica no POS Web (navegador), sem plugin de browser.

## O que faz

- Imprime talão ESC/POS
- Abre gaveta
- Faz corte de papel (full/partial)
- Funciona com:
  - USB (impressora instalada no Windows spooler, envio RAW)
  - Rede TCP/IP (IP + porta, normalmente `9100`)
- API local em `127.0.0.1:16161` protegida por token
- Fila por impressora, retries e logs

## Arquitetura

- `ZaldoPrinter.Service`
  - Serviço Windows em background
  - API local (`localhost`)
  - Execução de fila por `printerId`
- `ZaldoPrinter.ConfigApp`
  - UI de configuração
  - Lista impressoras instaladas no Windows
  - Configura múltiplas impressoras e padrão
  - Testa impressão, gaveta e corte
- `ZaldoPrinter.Common`
  - Builder ESC/POS
  - Transporte USB RAW (Winspool)
  - Transporte Rede TCP

## API local

Base URL:
- `http://127.0.0.1:16161`

Endpoints:
- `GET /health`
- `GET /printers`
- `GET /config`
- `POST /config`
- `POST /token/regenerate`
- `POST /print/receipt`
- `POST /print/cashdrawer`
- `POST /print/cut`
- `POST /print/test`

Headers:
- `X-ZALDO-TOKEN: <token>` (obrigatório para rotas protegidas)
- `X-PRINTER-ID: <printer-id>` (opcional; se não vier usa `defaultPrinterId`)

`POST /print/receipt` aceita `receipt.logo_base64` (imagem base64/data URI) e `receipt.qrcode`.

## Segurança

- Aceita apenas chamadas localhost (`127.0.0.1` / loopback)
- Token por pareamento (gerado no primeiro arranque)
- CORS restrito por `allowedOrigins`

## Configuração (JSON)

Arquivo:
- `%PROGRAMDATA%\ZaldoPrinter\config\config.json`

Exemplo:

```json
{
  "pairingToken": "<token>",
  "defaultPrinterId": "balcao",
  "allowedOrigins": [
    "https://zaldo.pt",
    "https://www.zaldo.pt"
  ],
  "requestTimeoutMs": 3000,
  "retryCount": 1,
  "printers": [
    {
      "id": "balcao",
      "name": "Balcão",
      "enabled": true,
      "mode": "usb",
      "usb": { "printerName": "EPSON TM-T20III Receipt" },
      "network": { "ip": "", "port": 9100 },
      "cashDrawer": {
        "enabled": true,
        "kickPulse": { "m": 0, "t1": 25, "t2": 250 }
      },
      "cut": { "enabled": true, "mode": "partial" }
    },
    {
      "id": "cozinha",
      "name": "Cozinha",
      "enabled": true,
      "mode": "network",
      "usb": { "printerName": "" },
      "network": { "ip": "192.168.1.60", "port": 9100 },
      "cashDrawer": { "enabled": false, "kickPulse": { "m": 0, "t1": 25, "t2": 250 } },
      "cut": { "enabled": true, "mode": "partial" }
    }
  ]
}
```

## USB vs Rede

- **USB:** não precisa IP. A impressora deve estar instalada no Windows.
- **Rede:** usar IP da própria impressora (não do computador), porta `9100` na maioria dos modelos.

## Logs

Pasta:
- `%PROGRAMDATA%\ZaldoPrinter\logs\`

Formato:
- `zaldo-printer-YYYYMMDD.log`

## Instalação para cliente final (Windows)

1. Instalar o `ZaldoPrinterSetup.exe`.
2. Abrir `Zaldo Printer Config` pelo atalho.
3. Verificar status do serviço: **Online**.
4. Copiar token de pareamento.
5. Adicionar 1 ou mais perfis de impressora.
6. Definir impressora padrão.
7. Clicar em:
   - `Teste: imprimir`
   - `Teste: abrir gaveta`
   - `Teste: cortar papel`
8. No POS, configurar token/base URL para usar o serviço local.

### Troubleshooting rápido

- Erro no setup: `Unable to execute file ... ZaldoPrinter.ConfigApp.exe (CreateProcess failed; code 2)`
  - Significa instalador/pacote incompleto (faltou o `ConfigApp.exe`) ou antivírus moveu o executável.
  - Recomendado:
    1. Reinstalar com o instalador mais recente.
    2. Confirmar se existe `C:\Program Files\ZaldoPrinter\ZaldoPrinter.ConfigApp.exe`.
    3. Se não existir, usar temporariamente o bridge legado (`/pos_bridge_download.php?target=windows`) até novo setup ser gerado.

## Build (equipa técnica)

Pré-requisitos:
- Windows x64
- .NET SDK 8+
- Inno Setup 6 (para instalador)

Publicar binários:

```powershell
cd scripts
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Gerar instalador:

```powershell
cd scripts
powershell -ExecutionPolicy Bypass -File .\build_installer.ps1
```

Saída:
- `dist\ZaldoPrinterSetup.exe`

## Integração no POS Web

O POS usa o `modules/moloni_pos/assets/printer-client.js`.

Fluxo:
1. `GET /health`
2. `POST /print/receipt`
3. fallback local/visualização quando indisponível

Variáveis (POS):
- `POS_ZALDO_PRINTER_URL` (ex.: `http://127.0.0.1:16161`)
- `POS_ZALDO_PRINTER_TOKEN` (token do serviço)

## Testes rápidos (PowerShell)

```powershell
Invoke-RestMethod http://127.0.0.1:16161/health
```

```powershell
$token = "<token>"
Invoke-RestMethod http://127.0.0.1:16161/printers -Headers @{"X-ZALDO-TOKEN"=$token}
```

```powershell
$token = "<token>"
Invoke-RestMethod http://127.0.0.1:16161/print/test -Method Post -Headers @{"X-ZALDO-TOKEN"=$token;"X-PRINTER-ID"="balcao"} -Body '{"title":"Teste"}' -ContentType 'application/json'
```


## Build automático no GitHub

- Crie uma tag no formato `zp-v1.0.0`.
- O workflow `Build ZaldoPrinter Windows Installer` vai gerar e anexar `ZaldoPrinterSetup.exe` no Release.

Alternativa: execute manualmente via *Actions* → *Run workflow*.
