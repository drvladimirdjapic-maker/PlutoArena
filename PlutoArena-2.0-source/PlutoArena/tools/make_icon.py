import math, sys
from PIL import Image, ImageDraw, ImageFilter, ImageChops

S = 1024  # master size

def hexagon(cx, cy, r, rot=0):
    return [(cx + r*math.cos(math.radians(60*i+rot)), cy + r*math.sin(math.radians(60*i+rot))) for i in range(6)]

def radial(size, center, radius, inner, outer):
    img = Image.new('RGBA', (size, size))
    px = img.load()
    cx, cy = center
    for y in range(size):
        for x in range(size):
            d = min(1.0, math.hypot(x-cx, y-cy)/radius)
            px[x, y] = tuple(int(inner[i]*(1-d) + outer[i]*d) for i in range(4))
    return img

def build(simple=False):
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    c = S/2

    # --- Hexagon "arena" plate
    plate = Image.new('L', (S, S), 0)
    ImageDraw.Draw(plate).polygon(hexagon(c, c, S*0.48, 30), fill=255)
    bg = radial(S, (c*0.85, c*0.75), S*0.75, (24, 52, 96, 255), (5, 10, 22, 255))
    img.paste(bg, (0, 0), plate)

    d = ImageDraw.Draw(img)
    # hex rim (glow + line)
    glow = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(glow).polygon(hexagon(c, c, S*0.455, 30), outline=(60, 170, 255, 255), width=int(S*0.03))
    glow = glow.filter(ImageFilter.GaussianBlur(S*0.018))
    img = Image.alpha_composite(img, glow)
    d = ImageDraw.Draw(img)
    d.polygon(hexagon(c, c, S*0.455, 30), outline=(120, 210, 255, 255), width=int(S*0.014))

    # --- Planet
    pr = S*0.26
    pcx, pcy = c, c*1.02
    planet_mask = Image.new('L', (S, S), 0)
    ImageDraw.Draw(planet_mask).ellipse([pcx-pr, pcy-pr, pcx+pr, pcy+pr], fill=255)
    planet = radial(S, (pcx-pr*0.45, pcy-pr*0.5), pr*1.9, (175, 235, 255, 255), (10, 40, 110, 255))
    if not simple:
        # icy surface bands + "heart" plain like Pluto
        tex = Image.new('RGBA', (S, S), (0, 0, 0, 0))
        td = ImageDraw.Draw(tex)
        td.ellipse([pcx-pr*0.15, pcy-pr*0.05, pcx+pr*0.6, pcy+pr*0.62], fill=(235, 250, 255, 70))
        td.ellipse([pcx-pr*0.75, pcy+pr*0.15, pcx-pr*0.25, pcy+pr*0.5], fill=(0, 20, 60, 60))
        td.ellipse([pcx-pr*0.6, pcy-pr*0.7, pcx-pr*0.2, pcy-pr*0.45], fill=(0, 20, 60, 45))
        tex = tex.filter(ImageFilter.GaussianBlur(S*0.012))
        planet = Image.alpha_composite(planet, tex)
    # terminator shadow
    shade = radial(S, (pcx+pr*0.9, pcy+pr*0.9), pr*1.6, (0, 0, 20, 170), (0, 0, 20, 0))
    planet = Image.alpha_composite(planet, shade)

    # planet glow
    pg = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(pg).ellipse([pcx-pr*1.08, pcy-pr*1.08, pcx+pr*1.08, pcy+pr*1.08], fill=(70, 180, 255, 150))
    pg = pg.filter(ImageFilter.GaussianBlur(S*0.03))
    img = Image.alpha_composite(img, pg)

    # --- Orbit ring: split into back (upper) and front (lower) half BEFORE rotating
    rw, rh, rot = S*0.43, S*0.12, 18
    width = int(S*(0.034 if simple else 0.024))
    ring = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(ring).ellipse([c-rw, pcy-rh, c+rw, pcy+rh], outline=(255, 196, 90, 255), width=width)
    upper = Image.new('L', (S, S), 0)
    ImageDraw.Draw(upper).rectangle([0, 0, S, pcy], fill=255)
    ring_back = Image.new('RGBA', (S, S), (0, 0, 0, 0)); ring_back.paste(ring, (0, 0), ImageChops.multiply(upper, ring.split()[3]))
    ring_front = Image.new('RGBA', (S, S), (0, 0, 0, 0)); ring_front.paste(ring, (0, 0), ImageChops.multiply(ImageChops.invert(upper), ring.split()[3]))
    ring_back = ring_back.rotate(rot, center=(c, pcy), resample=Image.BICUBIC)
    ring_front = ring_front.rotate(rot, center=(c, pcy), resample=Image.BICUBIC)

    img = Image.alpha_composite(img, ring_back)
    pl = Image.new('RGBA', (S, S), (0, 0, 0, 0)); pl.paste(planet, (0, 0), planet_mask)
    img = Image.alpha_composite(img, pl)
    rg = ring_front.filter(ImageFilter.GaussianBlur(S*0.01))
    img = Image.alpha_composite(img, rg)
    img = Image.alpha_composite(img, ring_front)

    if not simple:
        # small star / moon (Charon) on the orbit
        d = ImageDraw.Draw(img)
        mx, my, mr = c + S*0.205, pcy - S*0.265, S*0.036
        sg = Image.new('RGBA', (S, S), (0, 0, 0, 0))
        ImageDraw.Draw(sg).ellipse([mx-mr*2, my-mr*2, mx+mr*2, my+mr*2], fill=(255, 210, 120, 140))
        sg = sg.filter(ImageFilter.GaussianBlur(S*0.015))
        img = Image.alpha_composite(img, sg)
        d = ImageDraw.Draw(img)
        d.ellipse([mx-mr, my-mr, mx+mr, my+mr], fill=(255, 236, 190, 255))
        # tiny background stars
        for (sx, sy, r) in [(0.27, 0.27, 0.008), (0.70, 0.78, 0.006), (0.30, 0.72, 0.005), (0.62, 0.22, 0.005)]:
            d.ellipse([S*sx-S*r, S*sy-S*r, S*sx+S*r, S*sy+S*r], fill=(200, 230, 255, 220))
    return img

full = build(False)
simple = build(True)
sizes = [256, 128, 64, 48, 32, 24, 16]
frames = []
for s in sizes:
    src = simple if s <= 32 else full
    frames.append(src.resize((s, s), Image.LANCZOS))
full.resize((512, 512), Image.LANCZOS).save('pluto-arena-512.png')
frames[0].save('pluto-arena.ico', format='ICO', sizes=[(s, s) for s in sizes], append_images=frames[1:])
# preview sheet
sheet = Image.new('RGBA', (900, 300), (30, 34, 40, 255))
x = 10
for s, f in zip(sizes, frames):
    sheet.paste(f, (x, 20), f); x += s + 14
light = Image.new('RGBA', (900, 300), (235, 238, 242, 255))
x = 10
for s, f in zip(sizes, frames):
    light.paste(f, (x, 20), f); x += s + 14
both = Image.new('RGBA', (900, 600)); both.paste(sheet, (0, 0)); both.paste(light, (0, 300))
both.save('preview.png')
print('ok')
