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
Write-Host "Installer build completed."
