"""Generates src/MultiSnip/Assets/app.ico (run: python tools/make_icon.py)."""
from pathlib import Path
from PIL import Image, ImageDraw

S = 256
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
# Rounded tile background
d.rounded_rectangle((8, 8, S - 8, S - 8), radius=48, fill=(24, 92, 210, 255))
# Three stacked "snips" being combined
cards = [((48, 44, 176, 104), (255, 255, 255, 255)),
         ((48, 112, 176, 172), (210, 228, 255, 255)),
         ((48, 180, 176, 212), (255, 255, 255, 255))]
for box, col in cards:
    d.rounded_rectangle(box, radius=10, fill=col)
# Plus badge
d.ellipse((150, 140, 236, 226), fill=(255, 166, 0, 255), outline=(255, 255, 255, 255), width=8)
d.rectangle((187, 158, 199, 208), fill=(255, 255, 255, 255))
d.rectangle((168, 177, 218, 189), fill=(255, 255, 255, 255))

out = Path(__file__).resolve().parent.parent / "src" / "MultiSnip" / "Assets" / "app.ico"
img.save(out, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("wrote", out)
