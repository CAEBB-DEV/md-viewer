# Genera dist\MdViewer-Setup-<versión>.exe con Inno Setup 6 a partir de dist\publish\win-x64.
# La tarea de VS Code "Instalador: Windows" publica antes. Inno Setup: winget install JRSoftware.InnoSetup
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

if (-not (Test-Path "$root\dist\publish\win-x64\MdViewer.exe")) {
    throw 'Falta dist\publish\win-x64\MdViewer.exe: ejecuta la tarea "Instalador: Windows" (publica antes).'
}

$version = ([xml](Get-Content "$root\src\MdViewer\MdViewer.csproj")).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1

$iscc = @(
    (Get-Command iscc -ErrorAction SilentlyContinue).Source
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'No se encontró Inno Setup 6 (ISCC.exe). Instálalo con: winget install JRSoftware.InnoSetup' }

& $iscc "/DAppVersion=$version" "$root\packaging\windows\MdViewer.iss"
if ($LASTEXITCODE) { exit $LASTEXITCODE }
