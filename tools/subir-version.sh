#!/usr/bin/env bash
# Sube el segundo número de <Version> del .csproj (1.3 -> 1.4). Lo ejecutan las tareas "Instalador: ..." antes de publicar.
set -euo pipefail
cd "$(dirname "$0")/.."
csproj=src/MdViewer/MdViewer.csproj
grep -qE '<Version>[0-9]+\.[0-9]+</Version>' "$csproj" || { echo "No se encontró <Version>x.y</Version> en $csproj" >&2; exit 1; }
# Se reescribe el contenido (sin renombrar el archivo) para que funcione también sobre discos de Windows desde WSL
tmp=$(mktemp)
perl -pe 's{<Version>(\d+)\.(\d+)</Version>}{"<Version>$1." . ($2 + 1) . "</Version>"}e' "$csproj" > "$tmp"
cat "$tmp" > "$csproj" && rm -f "$tmp"
echo "Versión: $(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$csproj")"