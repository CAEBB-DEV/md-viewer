# Publica una versión nueva: sube <Version> (1.1 -> 1.2), hace commit, crea la etiqueta vX.Y y la sube a GitHub.
# Al recibir la etiqueta, GitHub Actions (.github/workflows/release.yml) genera los instaladores de Windows, macOS y
# Linux con esa misma versión y los publica como Release descargable.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

if (git status --porcelain) { throw 'Hay cambios sin commit. Haz commit (o descártalos) antes de publicar una versión.' }

& "$PSScriptRoot\subir-version.ps1" | Out-Null
$version = [regex]::Match([IO.File]::ReadAllText('src\MdViewer\MdViewer.csproj'), '<Version>([^<]+)</Version>').Groups[1].Value

git add src/MdViewer/MdViewer.csproj
git commit -q -m "Versión $version"
git tag "v$version"
git push --atomic origin HEAD "refs/tags/v$version"
if ($LASTEXITCODE) { throw "No se pudo subir a GitHub. El commit y la etiqueta v$version quedaron solo en local." }

"Versión $version publicada. Los instaladores se generan en GitHub (unos minutos):"
"  Progreso:   https://github.com/CAEBB-DEV/md-viewer/actions"
"  Descargas:  https://github.com/CAEBB-DEV/md-viewer/releases/tag/v$version"
