from PIL import Image, ImageDraw
S = 1024
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((32, 32, S - 32, S - 32), radius=220, fill=(15, 23, 42, 255))
for r, a in ((380, 60), (330, 110), (280, 255)):
    d.ellipse((S // 2 - r, S // 2 - r, S // 2 + r, S // 2 + r), fill=(217, 119, 87, a))
d.ellipse((S // 2 - 150, S // 2 - 150, S // 2 + 150, S // 2 + 150), fill=(255, 214, 190, 255))
img.save(r"D:\ClaudeGlow\src\app.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
