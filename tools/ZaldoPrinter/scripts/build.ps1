param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $scriptDir "..")
$src = Join-Path $root "src"
$out = Join-Path $root "out"

New-Item -ItemType Directory -Path $out -Force | Out-Null

$serviceProj = Join-Path $src "ZaldoPrinter.Service/ZaldoPrinter.Service.csproj"
$configProj = Join-Path $src "ZaldoPrinter.ConfigApp/ZaldoPrinter.ConfigApp.csproj"

$serviceOut = Join-Path $out "_service"
$configOut = Join-Path $out "_configapp"
$publishOut = Join-Path $out "publish"

$sc = if ($SelfContained) { "true" } else { "false" }

Write-Host "Publishing service..."
if (Test-Path $serviceOut) { Remove-Item -Recurse -Force $serviceOut }
dotnet publish $serviceProj -c $Configuration -r $Runtime --self-contained $sc /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o $serviceOut
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish service failed with exit code $LASTEXITCODE"
}

Write-Host "Publishing config app..."
if (Test-Path $configOut) { Remove-Item -Recurse -Force $configOut }
dotnet publish $configProj -c $Configuration -r $Runtime --self-contained $sc /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o $configOut
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish config app failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Path $publishOut -Force | Out-Null
Get-ChildItem $publishOut -Force -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Copy-Item (Join-Path $serviceOut "*") $publishOut -Recurse -Force
Copy-Item (Join-Path $configOut "*") $publishOut -Recurse -Force

$required = @(
    (Join-Path $publishOut "ZaldoPrinter.Service.exe"),
    (Join-Path $publishOut "ZaldoPrinter.ConfigApp.exe")
)

foreach ($req in $required) {
    if (!(Test-Path $req)) {
        throw "Build incompleto: ficheiro obrigatório em falta -> $req"
    }
}

Write-Host "Build complete: $publishOut"
