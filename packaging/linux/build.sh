#!/usr/bin/env bash
# Genera dist/mdviewer_<versión>_amd64.deb (Debian/Ubuntu) a partir de dist/publish/linux-x64.
# La tarea de VS Code "Instalador: Linux" publica antes. Requiere dpkg-deb (en Windows, se ejecuta en WSL).
# Instala la app en /opt/mdviewer, el comando "mdviewer", el lanzador del menú y el icono.
set -euo pipefail
cd "$(dirname "$0")/../.."

PUBLISH=dist/publish/linux-x64
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/MdViewer/MdViewer.csproj)
[ -f "$PUBLISH/MdViewer" ] || { echo "Falta $PUBLISH/MdViewer: ejecuta la tarea \"Instalador: Linux\" (publica antes)." >&2; exit 1; }

STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT

mkdir -p "$STAGE/DEBIAN" "$STAGE/opt/mdviewer" "$STAGE/usr/bin" "$STAGE/usr/share/applications" \
         "$STAGE/usr/share/icons/hicolor/512x512/apps" "$STAGE/usr/share/icons/hicolor/scalable/apps" \
         "$STAGE/usr/share/icons/hicolor/256x256/mimetypes" "$STAGE/usr/share/icons/hicolor/scalable/mimetypes" \
         "$STAGE/usr/share/mime/packages"
cp -r "$PUBLISH"/. "$STAGE/opt/mdviewer/"
rm -f "$STAGE"/opt/mdviewer/*.pdb
# Permisos normales (los archivos copiados desde Windows llegan con 777)
find "$STAGE/opt" -type d -exec chmod 755 {} +
find "$STAGE/opt" -type f -exec chmod 644 {} +
chmod 755 "$STAGE/opt/mdviewer/MdViewer"
ln -s /opt/mdviewer/MdViewer "$STAGE/usr/bin/mdviewer"
install -m 644 packaging/linux/mdviewer.desktop "$STAGE/usr/share/applications/"
install -m 644 packaging/linux/mdviewer.png "$STAGE/usr/share/icons/hicolor/512x512/apps/"
install -m 644 packaging/linux/mdviewer.svg "$STAGE/usr/share/icons/hicolor/scalable/apps/"
# Icono de los archivos .md (hoja + logo) y su tipo; dpkg actualiza las cachés de iconos y tipos al instalar
install -m 644 packaging/linux/mdviewer-markdown.png "$STAGE/usr/share/icons/hicolor/256x256/mimetypes/"
install -m 644 packaging/linux/mdviewer-markdown.svg "$STAGE/usr/share/icons/hicolor/scalable/mimetypes/"
install -m 644 packaging/linux/mdviewer-markdown.xml "$STAGE/usr/share/mime/packages/mdviewer.xml"

# libwebkit2gtk-4.1-0: navegador del sistema para el visor web (documentos con HTML).
# ponytail: no declara ICU (libicuNN cambia de nombre en cada versión de Ubuntu); viene en cualquier escritorio.
cat > "$STAGE/DEBIAN/control" <<EOF
Package: mdviewer
Version: $VERSION
Section: editors
Priority: optional
Architecture: amd64
Maintainer: MD Viewer <noreply@localhost>
Depends: libc6, libstdc++6, libfontconfig1, libx11-6, libice6, libsm6, libwebkit2gtk-4.1-0
Description: Visor y editor de archivos Markdown
 Visor y editor de archivos Markdown con vista estilo GitHub, tema claro y oscuro.
EOF

mkdir -p dist
dpkg-deb --build --root-owner-group "$STAGE" "dist/mdviewer_${VERSION}_amd64.deb"
echo "Listo: dist/mdviewer_${VERSION}_amd64.deb   (instalar: sudo apt install ./dist/mdviewer_${VERSION}_amd64.deb)"
