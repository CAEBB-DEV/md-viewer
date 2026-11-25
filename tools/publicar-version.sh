#!/usr/bin/env bash
# Publica una versión nueva: sube <Version> (1.1 -> 1.2), hace commit, crea la etiqueta vX.Y y la sube a GitHub.
# Al recibir la etiqueta, GitHub Actions (.github/workflows/release.yml) genera los instaladores de Windows, macOS y
# Linux con esa misma versión y los publica como Release descargable.
set -euo pipefail
cd "$(dirname "$0")/.."

[ -z "$(git status --porcelain)" ] || { echo "Hay cambios sin commit. Haz commit (o descártalos) antes de publicar una versión." >&2; exit 1; }

bash tools/subir-version.sh > /dev/null
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/MdViewer/MdViewer.csproj)

git add src/MdViewer/MdViewer.csproj
git commit -q -m "Versión $version"
git tag "v$version"
git push --atomic origin HEAD "refs/tags/v$version"

echo "Versión $version publicada. Los instaladores se generan en GitHub (unos minutos):"
echo "  Progreso:   https://github.com/CAEBB-DEV/md-viewer/actions"
echo "  Descargas:  https://github.com/CAEBB-DEV/md-viewer/releases/tag/v$version"
