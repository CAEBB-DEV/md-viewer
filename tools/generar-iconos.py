#!/usr/bin/env python3
"""Genera el logo de MD Viewer y todos sus iconos a partir de una sola definición geométrica.

Uso (desde la raíz del repo; requiere Pillow: pip install pillow):
    python3 tools/generar-iconos.py

Escribe:
    packaging/linux/mdviewer.svg, mdviewer.png (512)                 logo de la app
    packaging/linux/mdviewer-markdown.svg, mdviewer-markdown.png     icono de los archivos .md (256)
    src/MdViewer/Assets/app.ico (16-256), mdfile.png (64)            ventana, "Acerca de", explorador
    packaging/windows/mdfile.ico (16-256)                            archivos .md en Windows
    packaging/macos/app.icns, mdfile.icns (16-1024)                  app y archivos .md en macOS

Logo (diseño de 64x64): dos paneles inclinados hacia el centro, como un libro abierto, sobre una «V».
El panel izquierdo (oscuro) es una «M» y el derecho (claro) una «D», pegadas en la línea central. Solo líneas
rectas. Cada letra se dibuja en plano (x, v) y se inclina como su panel: y = v + K·(distancia al borde exterior).
"""
import math
import os
from PIL import Image, ImageDraw

DARK, LIGHT = "#0D47A1", "#1976D2"
K = 11.5 / 23.2                 # inclinación de los paneles: 0 en el borde exterior, 11.5 en el centro
H = 35.1                        # alto de las letras (en el borde exterior)
L0, C, R0 = 8.8, 32.0, 55.2     # borde exterior izquierdo, línea central y borde exterior derecho
S = 7.6                         # grosor del trazo de las letras


def left(p):
    x, v = p
    return (x, v + K * (x - L0))


def right(p):
    x, v = p
    return (x, v + K * (R0 - x))


def inset(poly, d):
    """Contorno convexo desplazado d hacia dentro (cada lado paralelo al original): trazo de grosor uniforme."""
    n = len(poly)
    area = sum(poly[i][0] * poly[(i + 1) % n][1] - poly[(i + 1) % n][0] * poly[i][1] for i in range(n))
    sgn = 1 if area > 0 else -1
    lines = []
    for i in range(n):
        (x1, y1), (x2, y2) = poly[i], poly[(i + 1) % n]
        ex, ey = x2 - x1, y2 - y1
        length = math.hypot(ex, ey)
        lines.append(((x1 - ey / length * sgn * d, y1 + ex / length * sgn * d), (ex, ey)))
    out = []
    for i in range(n):
        (p, r), (q, s) = lines[i - 1], lines[i]
        t = ((q[0] - p[0]) * s[1] - (q[1] - p[1]) * s[0]) / (r[0] * s[1] - r[1] * s[0])
        out.append((p[0] + r[0] * t, p[1] + r[1] * t))
    return out


# «M» en plano, de L0 a C. Diagonales: borde exterior desde lo alto de cada trazo vertical hasta el vértice
# (mc, TOP); borde interior desde el trazo vertical (a la altura JOIN) hasta el fondo del vértice (mc, BOTTOM).
mc = (L0 + C) / 2
TOP, JOIN, BOTTOM, TIP = 12.5, 12.0, 26.0, 0.9
M = [(L0, H), (L0, 0), (L0 + S + 0.6, 0), (mc, TOP), (C - S - 0.6, 0), (C, 0), (C, H),
     (C - S, H), (C - S, JOIN), (mc + TIP, BOTTOM), (mc - TIP, BOTTOM), (L0 + S, JOIN), (L0 + S, H)]

# «D» en plano, de C a R0: rectángulo con las dos esquinas derechas cortadas en chaflán, y su hueco
CW, CH = 6.5, 8.0               # ancho y alto de cada chaflán
D = [(C, 0), (R0 - CW, 0), (R0, CH), (R0, H - CH), (R0 - CW, H), (C, H)]

# Formas del logo: (color, contorno, hueco o None). La «V» de abajo es la del logo original.
LOGO = [
    (DARK, [left(p) for p in M], None),
    (LIGHT, [right(p) for p in D], [right(p) for p in inset(D, S)]),
    (DARK, [(32, 64), (32, 52.4), (8.8, 39.7), (8.8, 45.8)], None),
    (LIGHT, [(32, 64), (32, 52.4), (55.2, 39.7), (55.2, 45.8)], None),
]

# Icono de los archivos .md (diseño de 256x256): hoja con esquina doblada, líneas de texto y el logo abajo a la derecha
SHEET = [(36, 8), (144, 8), (196, 60), (196, 248), (36, 248)]
FOLD = [(144, 8), (144, 60), (196, 60)]
SHEET_FILL, FOLD_FILL, EDGE, TEXT = "#FFFFFF", "#DDE3EA", "#8A94A6", "#B8C1CE"
TEXT_LINES = [(64, 84, 96), (64, 112, 116), (64, 140, 104), (64, 168, 64)]  # x, y, ancho (alto 12)
DOC_LOGO_AT, DOC_LOGO_SCALE = (126, 120), 2.125

SS = 8                          # supermuestreo al rasterizar (bordes suaves)


# ---------- SVG ----------

def pts(points, fmt="{:.2f}"):
    return " ".join(f"{fmt.format(x)},{fmt.format(y)}" for x, y in points)


def logo_svg_shapes(indent):
    rows = []
    for color, outer, hole in LOGO:
        if hole is None:
            rows.append(f'{indent}<polygon fill="{color}" points="{pts(outer)}" />')
        else:
            rows.append(f'{indent}<path fill="{color}" fill-rule="evenodd" d="M{pts(outer)} Z M{pts(hole)} Z" />')
    return "\n".join(rows)


def logo_svg():
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64">\n'
            "  <!-- Generado por tools/generar-iconos.py: «M» y «D» sobre la «V» -->\n"
            f"{logo_svg_shapes('  ')}\n</svg>\n")


def doc_svg():
    lines = "\n".join(f'    <rect x="{x}" y="{y}" width="{w}" height="12" />' for x, y, w in TEXT_LINES)
    (lx, ly) = DOC_LOGO_AT
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">\n'
            "  <!-- Icono de los archivos .md: hoja con esquina doblada y el logo de MD Viewer abajo a la derecha\n"
            "       (generado por tools/generar-iconos.py) -->\n"
            f'  <polygon points="{pts(SHEET, "{:g}")}" fill="{SHEET_FILL}" stroke="{EDGE}" stroke-width="6" stroke-linejoin="round" />\n'
            f'  <polygon points="{pts(FOLD, "{:g}")}" fill="{FOLD_FILL}" stroke="{EDGE}" stroke-width="6" stroke-linejoin="round" />\n'
            f'  <g fill="{TEXT}">\n{lines}\n  </g>\n'
            f'  <g transform="translate({lx},{ly}) scale({DOC_LOGO_SCALE})">\n{logo_svg_shapes("    ")}\n  </g>\n'
            "</svg>\n")


# ---------- Imágenes ----------

def draw_logo(img, scale, ox=0.0, oy=0.0):
    """Dibuja el logo (unidades de diseño de 64) en img: x_px = ox + x·scale."""
    T = lambda ps: [(ox + x * scale, oy + y * scale) for x, y in ps]
    for color, outer, hole in LOGO:
        mask = Image.new("L", img.size, 0)
        md = ImageDraw.Draw(mask)
        md.polygon(T(outer), fill=255)
        if hole:
            md.polygon(T(hole), fill=0)
        img.paste(Image.new("RGBA", img.size, color), (0, 0), mask)


def outlined(draw, poly, fill, s):
    """Polígono con borde de 6 unidades y uniones redondeadas (como stroke-linejoin="round" del SVG)."""
    p = [(x * s, y * s) for x, y in poly]
    draw.polygon(p, fill=fill)
    draw.line(p + [p[0], p[1]], fill=EDGE, width=round(6 * s), joint="curve")


def logo_image(size):
    big = Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))
    draw_logo(big, size * SS / 64)
    return big.resize((size, size), Image.LANCZOS)


def doc_image(size):
    s = size * SS / 256
    big = Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    outlined(d, SHEET, SHEET_FILL, s)
    outlined(d, FOLD, FOLD_FILL, s)
    for x, y, w in TEXT_LINES:
        d.rectangle((x * s, y * s, (x + w) * s, (y + 12) * s), fill=TEXT)
    (lx, ly) = DOC_LOGO_AT
    draw_logo(big, DOC_LOGO_SCALE * s, lx * s, ly * s)
    return big.resize((size, size), Image.LANCZOS)


ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]


def save_ico(render, path):
    images = [render(n) for n in ICO_SIZES]
    images[-1].save(path, format="ICO", sizes=[(n, n) for n in ICO_SIZES], append_images=images[:-1])


def save_icns(render, path):
    images = [render(n) for n in (16, 32, 64, 128, 256, 512)]
    render(1024).save(path, format="ICNS", append_images=images)


def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    at = lambda *p: os.path.join(root, *p)
    with open(at("packaging/linux/mdviewer.svg"), "w") as f:
        f.write(logo_svg())
    with open(at("packaging/linux/mdviewer-markdown.svg"), "w") as f:
        f.write(doc_svg())
    logo_image(512).save(at("packaging/linux/mdviewer.png"))
    doc_image(256).save(at("packaging/linux/mdviewer-markdown.png"))
    doc_image(64).save(at("src/MdViewer/Assets/mdfile.png"))
    save_ico(logo_image, at("src/MdViewer/Assets/app.ico"))
    save_ico(doc_image, at("packaging/windows/mdfile.ico"))
    save_icns(logo_image, at("packaging/macos/app.icns"))
    save_icns(doc_image, at("packaging/macos/mdfile.icns"))
    print("Iconos generados.")


if __name__ == "__main__":
    main()
