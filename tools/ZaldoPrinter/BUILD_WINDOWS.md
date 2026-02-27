# Build do instalador Windows (.exe)

## Requisitos (máquina local)
- Windows 10/11
- .NET SDK 8.x
- Inno Setup 6 (ISCC)

## Build manual (PowerShell)
Na raiz do repositório:

```powershell
# 1) gerar binários single-file no out/publish
./scripts/build.ps1 -Configuration Release -Runtime win-x64 -SelfContained:$true

# 2) gerar instalador Inno Setup em out/installer/ZaldoPrinterSetup.exe
./scripts/build_installer.ps1 -InnoPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

Saída:
- `out/publish/` (ZaldoPrinter.Service.exe + ZaldoPrinter.ConfigApp.exe)
- `out/installer/ZaldoPrinterSetup.exe` (instalador wizard)

## Build via GitHub Actions
Workflow incluído em `.github/workflows/build-zaldo-printer.yml`.

Como usar:
1. Faça push do código.
2. Crie uma tag (ex.: `zp-v1.0.1`) e faça push da tag.
3. O workflow irá gerar e anexar o artefacto **instalador** `ZaldoPrinterSetup.exe` (não anexa `ZaldoPrinter.Service.exe`).

> Alternativa: execute manualmente pelo botão **Run workflow**.
