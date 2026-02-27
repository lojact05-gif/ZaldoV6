param(
    [string]$InnoPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $scriptDir "..")
$iss = Join-Path $root "installer/ZaldoPrinter.iss"

if (!(Test-Path $InnoPath)) {
    throw "ISCC not found at '$InnoPath'."
}

& $InnoPath $iss
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE"
}

$installer = Join-Path $root "out\installer\ZaldoPrinterSetup.exe"
if (!(Test-Path $installer)) {
    throw "Installer build completed, but setup not found at '$installer'."
}

Write-Host "Installer build completed: $installer"
