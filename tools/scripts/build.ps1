param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $scriptDir "..")
$src = Join-Path $root "src"
$dist = Join-Path $root "dist"

New-Item -ItemType Directory -Path $dist -Force | Out-Null

$serviceProj = Join-Path $src "ZaldoPrinter.Service/ZaldoPrinter.Service.csproj"
$configProj = Join-Path $src "ZaldoPrinter.ConfigApp/ZaldoPrinter.ConfigApp.csproj"

$serviceOut = Join-Path $dist "service"
$configOut = Join-Path $dist "config-app"

$sc = if ($SelfContained) { "true" } else { "false" }

Write-Host "Publishing service..."
dotnet publish $serviceProj -c $Configuration -r $Runtime --self-contained $sc /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o $serviceOut
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish service failed with exit code $LASTEXITCODE"
}

Write-Host "Publishing config app..."
dotnet publish $configProj -c $Configuration -r $Runtime --self-contained $sc /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o $configOut
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish config app failed with exit code $LASTEXITCODE"
}

$package = Join-Path $dist "package"
New-Item -ItemType Directory -Path $package -Force | Out-Null

Copy-Item (Join-Path $serviceOut "*") $package -Recurse -Force
Copy-Item (Join-Path $configOut "*") $package -Recurse -Force

$required = @(
    (Join-Path $package "ZaldoPrinter.Service.exe"),
    (Join-Path $package "ZaldoPrinter.ConfigApp.exe")
)

foreach ($req in $required) {
    if (!(Test-Path $req)) {
        throw "Build incompleto: ficheiro obrigatório em falta -> $req"
    }
}

Write-Host "Build complete: $package"
