## Descargas

| Sistema | Archivo |
|---|---|
| Windows 10/11 (x64) | `MdViewer-Setup-<versión>.exe` |
| macOS (Apple Silicon) | `MdViewer-<versión>-osx-arm64.dmg` |
| Debian / Ubuntu (x64) | `mdviewer_<versión>_amd64.deb` |

## Instalación

Los instaladores **no están firmados con un certificado comercial**, así que Windows y macOS muestran un aviso la
primera vez. Es normal: el código es abierto y puedes revisarlo o compilarlo tú mismo desde este repositorio.

### Windows
1. Ejecuta `MdViewer-Setup-<versión>.exe`.
2. Si aparece **"Windows protegió tu equipo"** (SmartScreen), haz clic en **Más información → Ejecutar de todas formas**.
3. Sigue el asistente. Para actualizar, ejecuta el instalador nuevo encima del anterior: conserva tu configuración.

### macOS
1. Abre el `.dmg` y arrastra **MD Viewer** a **Aplicaciones**.
2. La primera vez, macOS bloquea la app porque no está notarizada por Apple:
   - **macOS 15 o posterior:** intenta abrirla, luego ve a **Ajustes del Sistema → Privacidad y seguridad** y pulsa
     **Abrir igualmente**.
   - **macOS 14 o anterior:** clic derecho sobre la app → **Abrir** → **Abrir**.
3. Si macOS dice que la app **"está dañada"**, ejecuta en la Terminal y vuelve a abrirla:
   ```bash
   xattr -dr com.apple.quarantine "/Applications/MD Viewer.app"
   ```

### Linux (Debian / Ubuntu)
```bash
sudo apt install ./mdviewer_<versión>_amd64.deb
```
Ábrela desde el menú de aplicaciones o con el comando `mdviewer`. No muestra avisos.
