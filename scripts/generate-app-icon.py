"""Regenerate the original application mark. Requires Pillow; no network access."""
from pathlib import Path
from PIL import Image, ImageDraw

assets = Path(__file__).resolve().parents[1] / "csharp/ParserDesigner.CrossPlatform/Assets"
assets.mkdir(parents=True, exist_ok=True)
svg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
<rect x="8" y="8" width="240" height="240" rx="52" fill="#17324d"/>
<g fill="none" stroke="#55d6be" stroke-width="16" stroke-linecap="round" stroke-linejoin="round">
<path d="M76 66H52V190H76"/><path d="M180 66H204V190H180"/>
</g>
<g stroke-width="14" stroke-linecap="round">
<path d="M99 94H157" stroke="#ffffff"/>
<path d="M99 128H145" stroke="#ffcf70"/>
<path d="M99 162H157" stroke="#ffffff"/>
</g></svg>
"""
(assets / "app-icon.svg").write_text(svg, encoding="utf-8")

scale = 4
image = Image.new("RGBA", (256 * scale, 256 * scale))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle(tuple(v * scale for v in (8, 8, 248, 248)),
                       radius=52 * scale, fill="#17324d")

def line(points, color, width):
    coordinates = [(x * scale, y * scale) for x, y in points]
    draw.line(coordinates, fill=color, width=width * scale, joint="curve")
    radius = width * scale / 2
    for x, y in coordinates:
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=color)

line([(76, 66), (52, 66), (52, 190), (76, 190)], "#55d6be", 16)
line([(180, 66), (204, 66), (204, 190), (180, 190)], "#55d6be", 16)
for y, end, color in [(94, 157, "#ffffff"), (128, 145, "#ffcf70"), (162, 157, "#ffffff")]:
    line([(99, y), (end, y)], color, 14)
image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(assets / "app-icon.png")
image.save(assets / "app-icon.ico", sizes=[(n, n) for n in (16, 24, 32, 48, 64, 128, 256)])
