# Draws the WinGCal icon: a cyan calendar page with today's square lit, on a dark glass tile. Usage: python make_icon.py <output path without extension>
import sys
from PIL import Image, ImageDraw, ImageFilter
size = 1024
transparent = lambda: Image.new("RGBA", (size, size), (0, 0, 0, 0))
mask = Image.new("L", (size, size), 0); ImageDraw.Draw(mask).rounded_rectangle([40, 40, size - 40, size - 40], radius=220, fill=255)
gradient = Image.new("RGBA", (size, size)); gradient_draw = ImageDraw.Draw(gradient)
for row in range(size): blend = row / size; gradient_draw.line([(0, row), (size, row)], fill=(int(26 - 18 * blend), int(30 - 20 * blend), int(40 - 26 * blend), 255))
tile = transparent(); tile.paste(gradient, (0, 0), mask)
highlight = transparent(); ImageDraw.Draw(highlight).ellipse([-200, -560, size + 200, 380], fill=(255, 255, 255, 26))
tile = Image.alpha_composite(tile, Image.composite(highlight, transparent(), mask))
cyan, cyan_dim = (94, 231, 255, 255), (94, 231, 255, 90)
art, glow = transparent(), transparent()
for layer in (art, glow):
    draw = ImageDraw.Draw(layer)
    draw.rounded_rectangle([190, 230, 834, 820], radius=70, outline=cyan, width=34)  # page
    draw.line([(190, 380), (834, 380)], fill=cyan, width=30)  # header rule
    for ring_x in (360, 664): draw.line([(ring_x, 170), (ring_x, 290)], fill=cyan, width=40)  # binder rings
grid = ImageDraw.Draw(art)
for column in range(4):
    for row in range(3):
        left, top = 270 + column * 130, 440 + row * 120
        grid.rounded_rectangle([left, top, left + 80, top + 70], radius=14, fill=cyan_dim)
today = transparent(); ImageDraw.Draw(today).rounded_rectangle([530, 560, 610, 630], radius=14, fill=(255, 92, 72, 255))
tile = Image.alpha_composite(tile, Image.composite(glow.filter(ImageFilter.GaussianBlur(26)), transparent(), mask))
tile = Image.alpha_composite(tile, art)
tile = Image.alpha_composite(tile, Image.composite(today.filter(ImageFilter.GaussianBlur(24)), transparent(), mask))
tile = Image.alpha_composite(tile, today)
tile.save(sys.argv[1] + ".png")
tile.resize((256, 256), Image.LANCZOS).save(sys.argv[1] + "-256.png")
tile.save(sys.argv[1] + ".ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
