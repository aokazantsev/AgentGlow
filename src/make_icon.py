import os

from PIL import Image, ImageDraw

S = 1024
ORANGE = (217, 119, 87)
TEAL = (45, 212, 191)


def rings(color):
    layer = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    for r, a in ((380, 60), (330, 110), (280, 255)):
        d.ellipse((S // 2 - r, S // 2 - r, S // 2 + r, S // 2 + r), fill=color + (a,))
    return layer


base = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(base)
d.rounded_rectangle((32, 32, S - 32, S - 32), radius=220, fill=(15, 23, 42, 255))

left = rings(ORANGE)
right = rings(TEAL)
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).polygon([(S // 2 + 90, 0), (S, 0), (S, S), (S // 2 - 90, S)], fill=255)
base.alpha_composite(left)
base.paste(right, (0, 0), Image.composite(right.split()[3], Image.new("L", (S, S), 0), mask))

d = ImageDraw.Draw(base)
d.ellipse((S // 2 - 150, S // 2 - 150, S // 2 + 150, S // 2 + 150), fill=(241, 245, 249, 255))
base.save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "app.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
