# Genera el instalador de MobileSpeaker.
# Requisitos: .NET 8 SDK e Inno Setup 6.3 o superior.
# Uso: .\build-installer.ps1            (version 1.0.0)
#      .\build-installer.ps1 -Version 1.1.0

param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$publishDir = Join-Path $root "publish\installer"

Write-Host "1/2 Publicando MobileSpeaker $Version..." -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

dotnet publish (Join-Path $root "MobileSpeaker.csproj") -c Release `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish fallo."
}

Write-Host "2/2 Compilando el instalador..." -ForegroundColor Cyan
$iscc = $null
$command = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
if ($command) {
    $iscc = $command.Source
}
if (-not $iscc) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $iscc = $candidate
            break
        }
    }
}
if (-not $iscc) {
    Write-Host "No se encontro Inno Setup 6." -ForegroundColor Yellow
    Write-Host "Instalalo desde https://jrsoftware.org/isdl.php o con:" -ForegroundColor Yellow
    Write-Host "    winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
    Write-Host "La aplicacion ya quedo publicada en: $publishDir"
    exit 1
}

& $iscc "/DAppVersion=$Version" "/DPublishDir=$publishDir" (Join-Path $root "installer\MobileSpeaker.iss")
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup fallo."
}

Write-Host ""
Write-Host "Listo: publish\MobileSpeaker-Setup-$Version.exe" -ForegroundColor Green
