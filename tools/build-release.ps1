<#
  Сборка выпуска InvAuto и установщика (exe).

  Нужно: Windows, .NET SDK 8, установленный Inventor нужной версии (для Autodesk.Inventor.Interop.dll),
  Inno Setup 6 (https://jrsoftware.org/isdl.php).

  Примеры (из корня репозитория):
    powershell -ExecutionPolicy Bypass -File tools\build-release.ps1
    powershell -ExecutionPolicy Bypass -File tools\build-release.ps1 -InventorVersion 2024
#>
param(
    [string]$InventorVersion = "2025",
    [string]$Iscc = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Inventor 2025+ — .NET 8, более ранние — .NET Framework 4.8 (см. InvAuto.Inventor.csproj).
$framework = if ([int]$InventorVersion -ge 2025) { "net8.0-windows" } else { "net48" }
$output = Join-Path $root "src\InvAuto.Inventor\bin\Release\$framework"

Write-Host "== Тесты" -ForegroundColor Cyan
dotnet test tests\InvAuto.Core.Tests -c Release
if ($LASTEXITCODE -ne 0) { throw "Тесты не прошли — выпуск не собирается." }

Write-Host "== Сборка InvAuto для Inventor $InventorVersion ($framework)" -ForegroundColor Cyan
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
dotnet build src\InvAuto.Inventor\InvAuto.Inventor.csproj -c Release -p:InventorVersion=$InventorVersion -p:DeployToInventor=false
if ($LASTEXITCODE -ne 0) { throw "Сборка не удалась." }

foreach ($file in "InvAuto.dll", "InvAuto.Core.dll", "Clipper2Lib.dll", "InvAuto.addin", "Config\dxf_export_config.ini") {
    if (-not (Test-Path (Join-Path $output $file))) { throw "В выходной папке нет $file — установщик был бы неполным." }
}

if (-not $Iscc) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $Iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc) { throw "Не найден ISCC.exe (Inno Setup 6). Установите его или укажите путь: -Iscc 'C:\...\ISCC.exe'." }

Write-Host "== Установщик" -ForegroundColor Cyan
& $Iscc "installer\InvAuto.iss" "/DInventorVersion=$InventorVersion" "/DSourceDir=$output"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup завершился с ошибкой." }

$setup = Get-ChildItem (Join-Path $root "installer\Output") -Filter "*Inventor$InventorVersion-setup.exe" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "Готово: $($setup.FullName)" -ForegroundColor Green
