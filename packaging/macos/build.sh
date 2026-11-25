#!/usr/bin/env bash
# Genera dist/MdViewer-<versión>-osx-arm64.dmg con "MD Viewer.app" a partir de dist/publish/osx-arm64.
# La tarea de VS Code "Instalador: macOS" publica antes. Se ejecuta en un Mac (usa codesign y hdiutil).
# Para Mac Intel: cambia osx-arm64 por osx-x64 aquí y en la tarea "publicar: osx-arm64".
set -euo pipefail
cd "$(dirname "$0")/../.."

RID=osx-arm64
PUBLISH="dist/publish/$RID"
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/MdViewer/MdViewer.csproj)
[ -f "$PUBLISH/MdViewer" ] || { echo "Falta $PUBLISH/MdViewer: ejecuta la tarea \"Instalador: macOS\" (publica antes)." >&2; exit 1; }

STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
APP="$STAGE/MD Viewer.app"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH"/. "$APP/Contents/MacOS/"
rm -f "$APP"/Contents/MacOS/*.pdb
chmod +x "$APP/Contents/MacOS/MdViewer"
sed "s/__VERSION__/$VERSION/g" packaging/macos/Info.plist > "$APP/Contents/Info.plist"
cp packaging/macos/app.icns packaging/macos/mdfile.icns "$APP/Contents/Resources/"

# Firma ad-hoc (obligatoria en Apple Silicon para que arranque).
# Para distribuir fuera de tu equipo: firma con un certificado "Developer ID" y notariza con notarytool.
codesign --force --deep --sign - "$APP"

ln -s /Applications "$STAGE/Applications"   # para arrastrar la app a Aplicaciones desde el .dmg
mkdir -p dist
hdiutil create -volname "MD Viewer" -srcfolder "$STAGE" -ov -format UDZO "dist/MdViewer-$VERSION-$RID.dmg"
echo "Listo: dist/MdViewer-$VERSION-$RID.dmg"
