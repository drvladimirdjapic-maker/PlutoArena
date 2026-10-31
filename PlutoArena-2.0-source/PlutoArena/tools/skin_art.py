"""Pluto Arena - design mockup v2 (original art, no game assets)."""
import math, random, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

S = 2                      # supersampling
W, H = 1000, 772
SW, SH = W * S, H * S

INTER_B = "/usr/share/fonts/opentype/inter/Inter-Bold.otf"
INTER_XB = "/usr/share/fonts/opentype/inter/Inter-ExtraBold.otf"
INTER_SB = "/usr/share/fonts/opentype/inter/Inter-SemiBold.otf"
MONO = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"
MONOB = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf"

def font(p, size): return ImageFont.truetype(p, int(size * S))

RACES = {
    "TERRAN":  dict(acc=(80, 165, 255),  acc2=(170, 220, 255), neb=((20, 60, 140), (90, 40, 30))),
    "PROTOSS": dict(acc=(255, 196, 70),  acc2=(70, 235, 225),  neb=((10, 90, 110), (120, 80, 10))),
    "ZERG":    dict(acc=(185, 85, 255),  acc2=(255, 80, 140),  neb=((70, 15, 100), (110, 20, 40))),
    "RANDOM":  dict(acc=(200, 205, 215), acc2=(150, 160, 175), neb=((40, 45, 60), (60, 60, 70))),
}
PHOS = (125, 255, 150)      # phosphor green
PHOS_DIM = (55, 140, 80)

# ---------------------------------------------------------------- helpers
def R(*v): return tuple(int(round(x * S)) for x in v)

def noise(w, h, scale, seed, octaves=5):
    rnd = np.random.default_rng(seed)
    out = np.zeros((h, w), np.float32); amp = 1.0; tot = 0
    for o in range(octaves):
        sw, sh = max(2, int(w / scale) + 2), max(2, int(h / scale) + 2)
        a = rnd.random((sh, sw)).astype(np.float32)
        img = Image.fromarray((a * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        out += np.asarray(img, np.float32) / 255 * amp
        tot += amp; amp *= 0.5; scale /= 2
    return out / tot

def mask_new(): return Image.new("L", (SW, SH), 0)

def chamfer_pts(r, c, corners="1111"):
    x0, y0, x1, y1 = r
    tl, tr, br, bl = [c if k == "1" else 0 for k in corners]
    return [(x0 + tl, y0), (x1 - tr, y0), (x1, y0 + tr), (x1, y1 - br),
            (x1 - br, y1), (x0 + bl, y1), (x0, y1 - bl), (x0, y0 + tl)]

def chamfer_mask(r, c, corners="1111"):
    m = mask_new(); ImageDraw.Draw(m).polygon(chamfer_pts(R(*r), c * S, corners), fill=255); return m

def solid(color, a=255): return Image.new("RGBA", (SW, SH), tuple(color) + (a,))

def paste(canvas, src, mask):
    """Composite src (RGBA) onto canvas through mask (L)."""
    m = ImageChops.multiply(src.getchannel("A"), mask)
    s = src.copy(); s.putalpha(m); canvas.alpha_composite(s)

def vgrad(r, stops):
    """Vertical gradient image (full canvas) inside rect r (logical); stops=[(t,(r,g,b))]."""
    x0, y0, x1, y1 = R(*r)
    h = max(1, y1 - y0)
    t = np.clip((np.arange(SH) - y0) / h, 0, 1)
    cols = np.zeros((SH, 3), np.float32)
    ts = [s[0] for s in stops]; cs = np.array([s[1] for s in stops], np.float32)
    for k in range(3): cols[:, k] = np.interp(t, ts, cs[:, k])
    arr = np.repeat(cols[:, None, :], SW, axis=1)
    a = np.full((SH, SW, 1), 255, np.float32)
    return Image.fromarray(np.concatenate([arr, a], 2).astype(np.uint8), "RGBA")

def glow(canvas, mask, color, radius, strength=1.0):
    g = mask.filter(ImageFilter.GaussianBlur(radius * S))
    if strength != 1.0: g = g.point(lambda v: min(255, int(v * strength)))
    paste(canvas, solid(color), g)

def inner_shadow(canvas, mask, offset=(0, 3), radius=6, alpha=200, color=(0, 0, 0)):
    inv = ImageChops.invert(mask)
    sh = inv.filter(ImageFilter.GaussianBlur(radius * S))
    sh = ImageChops.offset(sh, offset[0] * S, offset[1] * S)
    sh = ImageChops.multiply(sh, mask).point(lambda v: v * alpha // 255)
    paste(canvas, solid(color), sh)

def drop_shadow(canvas, mask, offset=(0, 4), radius=6, alpha=180):
    sh = ImageChops.offset(mask, offset[0] * S, offset[1] * S).filter(ImageFilter.GaussianBlur(radius * S))
    paste(canvas, solid((0, 0, 0)), sh.point(lambda v: v * alpha // 255))

def edge_light(canvas, mask, width=1.2, top=(255, 255, 255), a_top=70, bottom=(0, 0, 0), a_bot=160):
    """Bevel: light on the top/left edge, dark on the bottom/right."""
    w = max(1, int(width * S))
    sh_dn = ImageChops.offset(mask, 0, w); sh_up = ImageChops.offset(mask, 0, -w)
    top_e = ImageChops.subtract(mask, sh_dn)       # pixels whose upper neighbour is outside
    bot_e = ImageChops.subtract(mask, sh_up)
    paste(canvas, solid(top), top_e.point(lambda v: v * a_top // 255))
    paste(canvas, solid(bottom), bot_e.point(lambda v: v * a_bot // 255))

def outline(canvas, mask, color, width=1.0, a=255):
    w = max(1, int(width * S))
    big = mask.filter(ImageFilter.MaxFilter(2 * w + 1))
    ring = ImageChops.subtract(big, mask)
    paste(canvas, solid(color, a), ring)

def text_mask(xy, txt, f, anchor="la", spacing=0):
    m = mask_new(); d = ImageDraw.Draw(m)
    if spacing:
        x, y = R(*xy)
        if anchor[0] == "m":
            total = sum(d.textlength(ch, font=f) for ch in txt) + spacing * S * (len(txt) - 1)
            x -= total / 2
        for ch in txt:
            d.text((x, y), ch, font=f, fill=255, anchor="l" + anchor[1]); x += d.textlength(ch, font=f) + spacing * S
    else:
        d.text(R(*xy), txt, font=f, fill=255, anchor=anchor)
    return m

def glow_text(canvas, xy, txt, f, color, blur=3, anchor="la", strength=1.4, spacing=0, core=None):
    m = text_mask(xy, txt, f, anchor, spacing)
    glow(canvas, m, color, blur, strength)
    paste(canvas, solid(core or color), m)

def plain_text(canvas, xy, txt, f, color, anchor="la", spacing=0, a=255):
    paste(canvas, solid(color, a), text_mask(xy, txt, f, anchor, spacing))

# ------------------------------------------------------------ textures
def brushed_metal(base=(46, 50, 55), seed=1):
    n = np.random.default_rng(seed).random((SH, SW)).astype(np.float32)
    img = Image.fromarray((n * 255).astype(np.uint8)).filter(ImageFilter.BoxBlur(0))
    # horizontal streaks
    img = img.resize((SW // 40, SH), Image.BILINEAR).resize((SW, SH), Image.BILINEAR)
    streak = np.asarray(img, np.float32) / 255 - 0.5
    grain = np.random.default_rng(seed + 9).random((SH, SW)).astype(np.float32) - 0.5
    blot = noise(SW, SH, 260 * S, seed + 3, 4) - 0.5
    yy, xx = np.mgrid[0:SH, 0:SW].astype(np.float32)
    light = 1.18 - 0.30 * (yy / SH) - 0.12 * (xx / SW)          # light from upper left
    v = light * (1 + streak * 0.10 + grain * 0.06 + blot * 0.22)
    arr = np.clip(np.stack([v * c for c in base], 2), 0, 255)
    return Image.fromarray(np.concatenate([arr, np.full((SH, SW, 1), 255)], 2).astype(np.uint8), "RGBA")

def screw(canvas, x, y, r=5.5):
    cx, cy, rr = x * S, y * S, r * S
    yy, xx = np.mgrid[0:int(rr * 2 + 4), 0:int(rr * 2 + 4)].astype(np.float32)
    dx, dy = xx - rr - 2, yy - rr - 2
    d = np.sqrt(dx * dx + dy * dy)
    a = np.clip(rr - d + 0.5, 0, 1)
    shade = np.clip(0.55 - (dx + dy) / (rr * 3.2), 0.05, 1)
    rim = np.clip(1 - np.abs(d - rr + 1.2 * S) / (0.9 * S), 0, 1) * 0.5
    v = shade * 175 + 25 - rim * 60
    # hex socket
    ang = np.arctan2(dy, dx); hexr = rr * 0.42 / np.cos(((ang + math.pi / 6) % (math.pi / 3)) - math.pi / 6)
    sock = d < hexr
    v = np.where(sock, 18 + (dy > 0) * 30, v)
    arr = np.stack([v, v * 1.02, v * 1.06, a * 255], 2).clip(0, 255).astype(np.uint8)
    sp = Image.fromarray(arr, "RGBA")
    # recess shadow
    m = mask_new(); ImageDraw.Draw(m).ellipse([cx - rr - 1.5 * S, cy - rr - 0.5 * S, cx + rr + 1.5 * S, cy + rr + 2.5 * S], fill=170)
    paste(canvas, solid((0, 0, 0)), m.filter(ImageFilter.GaussianBlur(1.5 * S)))
    canvas.alpha_composite(sp, (int(cx - rr - 2), int(cy - rr - 2)))

def hazard(canvas, r, seed=4, alpha=255):
    x0, y0, x1, y1 = R(*r); w, h = x1 - x0, y1 - y0
    t = Image.new("RGBA", (w, h), (18, 18, 16, 255)); d = ImageDraw.Draw(t)
    step = 26 * S
    for x in range(-h - step, w + step, step):
        d.polygon([(x, h), (x + step // 2, h), (x + step // 2 + h, 0), (x + h, 0)], fill=(222, 170, 28, 255))
    arr = np.asarray(t, np.float32).copy()
    wear = noise(w, h, 18 * S, seed, 4)
    scratch = np.random.default_rng(seed).random((h, w)) > 0.985
    k = np.clip((wear - 0.30) * 3, 0.55, 1.0)[..., None]
    arr[..., :3] = arr[..., :3] * k
    arr[scratch, :3] = 70
    yy = np.linspace(1.15, 0.7, h)[:, None, None]
    arr[..., :3] *= yy
    t = Image.fromarray(arr.clip(0, 255).astype(np.uint8), "RGBA")
    m = mask_new(); m.paste(255, (x0, y0, x1, y1))
    layer = Image.new("RGBA", (SW, SH), (0, 0, 0, 0)); layer.paste(t, (x0, y0))
    paste(canvas, layer, m.point(lambda v: v * alpha // 255))
    inner_shadow(canvas, m, (0, 2), 3, 220)
    edge_light(canvas, m, 1, a_top=40)

def scanlines(canvas, mask, alpha=40, period=3):
    m = mask_new(); d = ImageDraw.Draw(m)
    for y in range(0, SH, period * S): d.rectangle([0, y, SW, y + S - 1], fill=alpha)
    paste(canvas, solid((0, 0, 0)), ImageChops.multiply(m, mask))

def crt(canvas, r, c=8, tint=(8, 22, 14)):
    m = chamfer_mask(r, c)
    paste(canvas, vgrad(r, [(0, tuple(int(v * 1.6) for v in tint)), (1, tint)]), m)
    # soft phosphor bloom in the middle
    x0, y0, x1, y1 = R(*r)
    b = mask_new(); ImageDraw.Draw(b).ellipse([x0 + (x1 - x0) * 0.15, y0 + (y1 - y0) * 0.1, x1 - (x1 - x0) * 0.15, y1 - (y1 - y0) * 0.1], fill=60)
    paste(canvas, solid((40, 120, 70)), ImageChops.multiply(b.filter(ImageFilter.GaussianBlur(30 * S)), m))
    scanlines(canvas, m, 55)
    inner_shadow(canvas, m, (0, 3), 7, 235)
    outline(canvas, m, (8, 9, 10), 1.6)
    return m

def glass_sheen(canvas, mask, r):
    x0, y0, x1, y1 = R(*r)
    s = mask_new(); ImageDraw.Draw(s).polygon([(x0, y0), (x0 + (x1 - x0) * 0.55, y0), (x0 + (x1 - x0) * 0.35, y1), (x0, y1)], fill=14)
    paste(canvas, solid((255, 255, 255)), ImageChops.multiply(s.filter(ImageFilter.GaussianBlur(6 * S)), mask))

def hud_brackets(canvas, r, color, L=14, a=200):
    x0, y0, x1, y1 = R(*r); L *= S; w = int(1.6 * S)
    m = mask_new(); d = ImageDraw.Draw(m)
    for (x, y, sx, sy) in [(x0, y0, 1, 1), (x1, y0, -1, 1), (x0, y1, 1, -1), (x1, y1, -1, -1)]:
        d.line([(x, y + sy * L), (x, y), (x + sx * L, y)], fill=a, width=w)
    glow(canvas, m, color, 2.5, 0.8); paste(canvas, solid(color), m)

# ------------------------------------------------------------ logo
def logo_mask(x, y, hgt, gap=0.9):
    """Original angular logotype for PLUTO ARENA. Returns (mask, width)."""
    u = hgt / 10.0
    s = 2.35        # stroke
    c = 1.6         # chamfer
    m = Image.new("L", (int((hgt * 9) * S), int((hgt + 4) * S)), 0)
    d = ImageDraw.Draw(m)
    def P(pts, fill=255):
        d.polygon([((px * u) * S + ox * S, (py * u) * S + 2 * S) for px, py in pts], fill=fill)
    def cr(x0, y0, x1, y1, cc, fill=255, corners="1111"):
        P(chamfer_pts((x0, y0, x1, y1), cc, corners), fill)
    ox = 0.0
    letters = "PLUTO ARENA"
    widths = {"P": 6.6, "L": 5.8, "U": 6.8, "T": 6.6, "O": 6.9, "A": 7.0, "R": 6.9, "E": 6.0, "N": 7.0, " ": 2.6}
    for ch in letters:
        w = widths[ch]
        if ch == "P":
            cr(0, 0, w, 6.2, c, corners="0110"); cr(s, s, w - s, 6.2 - s, c * 0.5, 0); cr(0, 0, s, 10, 0)
        elif ch == "L":
            cr(0, 0, s, 10, 0); cr(0, 10 - s, w, 10, c, corners="0010")
        elif ch == "U":
            cr(0, 0, w, 10, c, corners="0011"); cr(s, -1, w - s, 10 - s, c * 0.5, 0, corners="0011")
        elif ch == "T":
            cr(0, 0, w, s, 0); cr(w / 2 - s / 2, 0, w / 2 + s / 2, 10, 0)
            P([(w / 2 + s / 2, s), (w / 2 + s / 2 + 0.8, s), (w / 2 + s / 2, s + 0.8)])
        elif ch == "O":
            cr(0, 0, w, 10, c); cr(s, s, w - s, 10 - s, c * 0.5, 0)
        elif ch == "A":
            cr(0, 0, w, 10, c * 1.4, corners="1100"); cr(s, s, w - s, 5.4, c * 0.6, 0, corners="1100")
            cr(s, 5.4 + s * 0.85, w - s, 11, 0, 0)
        elif ch == "R":
            cr(0, 0, w, 6.2, c, corners="0110"); cr(s, s, w - s, 6.2 - s, c * 0.5, 0); cr(0, 0, s, 10, 0)
            P([(w - s - 1.2, 6.2 - 0.01), (w - 1.2 + 0.3, 6.2 - 0.01), (w, 10), (w - s, 10)])
            P([(w - s - 1.2, 6.2), (w - 1.2 + 0.3, 6.2), (w - 0.5, 7.4), (w - s - 0.6, 7.4)])
        elif ch == "E":
            cr(0, 0, w, 10, c, corners="1000"); cr(s, s, w + 1, 10 - s, 0, 0)
            cr(s, 5 - s * 0.45, w - 1.0, 5 + s * 0.45, 0)
        elif ch == "N":
            cr(0, 0, s, 10, 0); cr(w - s, 0, w, 10, 0)
            P([(0, 0), (s * 1.05, 0), (w, 10), (w - s * 1.05, 10)])
        ox += (w + gap) * u
    # tech groove: thin horizontal cut through all letters
    d.rectangle([0, int((2 + 6.95 * u) * S), m.width, int((2 + 6.95 * u) * S + 0.9 * u * S)], fill=0)
    full = mask_new(); full.paste(m, (int(x * S), int((y - 2) * S)))
    return full, ox

def logo_mask2(x, y, hgt):
    f = ImageFont.truetype("/usr/share/fonts/opentype/inter/InterDisplay-Black.otf", int(hgt * 1.36 * S))
    txt = "PLUTO ARENA"
    tmp = Image.new("L", (int(hgt * 16 * S), int(hgt * 2 * S)), 0); d = ImageDraw.Draw(tmp)
    cx = 0
    for ch in txt:
        d.text((cx, hgt * 0.2 * S), ch, font=f, fill=255); cx += d.textlength(ch, font=f) + (0.08 * hgt * S if ch != " " else 0.12 * hgt * S)
    tmp = tmp.crop(tmp.getbbox())
    tmp = tmp.resize((int(tmp.width * 1.12), int(hgt * S)), Image.LANCZOS)
    sk = 0.16
    tmp = tmp.transform((int(tmp.width + tmp.height * sk), tmp.height), Image.AFFINE, (1, sk, -tmp.height * sk, 0, 1, 0), Image.BICUBIC)
    full = mask_new(); full.paste(tmp, (int(x * S), int(y * S)))
    # tech groove
    gy = int((y + hgt * 0.60) * S)
    ImageDraw.Draw(full).rectangle([0, gy, SW, gy + int(hgt * 0.045 * S)], fill=0)
    return full, tmp.width / S

def draw_logo(canvas, x, y, hgt, acc, acc2):
    m, w = logo_mask2(x, y, hgt)
    # outer glow + shadow
    drop_shadow(canvas, m, (0, 4), 5, 230)
    glow(canvas, m.filter(ImageFilter.MaxFilter(5)), acc, 14, 0.9)
    outline(canvas, m, (6, 8, 12), 2.4)
    big = m.filter(ImageFilter.MaxFilter(2 * int(2.4 * S) + 1))
    ring = ImageChops.subtract(big.filter(ImageFilter.MaxFilter(3)), big)
    paste(canvas, solid(acc), ring)
    # chrome fill with a horizon line, tinted by the race color
    y0, y1 = y, y + hgt
    def mix(a, b, t): return tuple(int(a[i] * (1 - t) + b[i] * t) for i in range(3))
    chrome = vgrad((0, y0, 1, y1), [
        (0.00, (250, 252, 255)), (0.30, mix((200, 210, 222), acc2, 0.25)), (0.52, (110, 120, 134)),
        (0.53, (38, 42, 52)), (0.70, mix((70, 80, 96), acc, 0.35)), (1.0, mix((170, 180, 195), acc, 0.45))])
    paste(canvas, chrome, m)
    # brushed streaks in the chrome
    n = noise(SW, SH, 3 * S, 11, 2)
    st = Image.fromarray((n * 255).astype(np.uint8)).resize((SW // 30, SH)).resize((SW, SH))
    paste(canvas, solid((255, 255, 255)), ImageChops.multiply(st.point(lambda v: max(0, v - 128) // 3), m))
    edge_light(canvas, m, 1.4, a_top=200, a_bot=200)
    inner_shadow(canvas, m, (0, -2), 2, 120, acc)
    return w

# ------------------------------------------------------------ space scene
def space_scene(canvas, r, race, mask):
    x0, y0, x1, y1 = R(*r); w, h = x1 - x0, y1 - y0
    c1, c2 = RACES[race]["neb"]
    n1 = noise(w, h, 140 * S, 21, 6); n2 = noise(w, h, 90 * S, 37, 6)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    band = np.exp(-((yy / h - 0.25 - 0.35 * xx / w) ** 2) / 0.08)
    a1 = np.clip((n1 - 0.42) * 2.6, 0, 1) * band
    a2 = np.clip((n2 - 0.55) * 3.0, 0, 1) * band
    img = np.zeros((h, w, 3), np.float32) + np.array([4, 6, 12], np.float32)
    img += a1[..., None] * np.array(c1, np.float32) * 1.3
    img += a2[..., None] * np.array(c2, np.float32) * 1.1
    # dark dust lanes
    dust = np.clip((noise(w, h, 50 * S, 55, 5) - 0.5) * 3, 0, 1)
    img *= (1 - dust[..., None] * 0.6)
    # stars
    rnd = np.random.default_rng(7)
    for _ in range(int(w * h / (S * S) / 90)):
        sx, sy = rnd.integers(0, w), rnd.integers(0, h); b = rnd.random() ** 3
        img[sy, sx] += 255 * b
        if b > 0.5 and 1 < sx < w - 2 and 1 < sy < h - 2:
            img[sy - 1:sy + 2, sx] += 90 * b; img[sy, sx - 1:sx + 2] += 90 * b
    sc = Image.fromarray(img.clip(0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))
    # planet (dwarf planet with a pale heart-shaped plain - original drawing)
    pr = int(112 * S); pcx, pcy = int(w * 0.86), int(h * 0.98)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    dx, dy = (xx - pcx) / pr, (yy - pcy) / pr
    d2 = dx * dx + dy * dy
    inside = d2 < 1
    nz = np.sqrt(np.clip(1 - d2, 0, 1))
    lx, ly, lz = -0.55, -0.55, 0.62
    lam = np.clip(dx * lx + dy * ly + nz * lz, 0, 1)
    tex = noise(w, h, 14 * S, 77, 6); tex = np.clip((tex - 0.5) * 2.2 + 0.5, 0, 1)
    # heart-ish bright region
    hx, hy = (dx + 0.25) * 2.4, (dy + 0.72) * 2.4
    heart = ((hx * hx + hy * hy - 1) ** 3 - hx * hx * hy ** 3) < 0
    base = np.stack([95 + tex * 95, 72 + tex * 80, 60 + tex * 66], 2)
    base = np.where(heart[..., None], base * 0.45 + np.array([215, 205, 192], np.float32) * 0.55, base)
    planet = base * (0.02 + 0.98 * lam[..., None] ** 1.4)
    pl = np.asarray(sc, np.float32).copy()
    pl[inside] = planet[inside]
    # atmosphere rim lit by the race color
    acc = np.array(RACES[race]["acc"], np.float32)
    rim = np.clip(1 - np.abs(np.sqrt(d2) - 1.0) / 0.035, 0, 1) * np.clip(-(dx * 0.7 + dy * 0.7) + 0.2, 0, 1)
    halo = np.clip(1 - (np.sqrt(d2) - 1) / 0.12, 0, 1) * (np.sqrt(d2) > 1) * np.clip(-(dx + dy) * 0.6 + 0.3, 0, 1)
    pl += rim[..., None] * acc * 1.2 + halo[..., None] * acc * 0.5
    sc = Image.fromarray(pl.clip(0, 255).astype(np.uint8)).convert("RGBA")
    layer = Image.new("RGBA", (SW, SH), (0, 0, 0, 0)); layer.paste(sc, (x0, y0))
    paste(canvas, layer, mask)
    # perspective holo grid at the bottom
    g = mask_new(); d = ImageDraw.Draw(g)
    hor = y0 + h * 0.62; vx = x0 + w * 0.35
    for i in range(-30, 31):
        d.line([(vx, hor), (vx + i * 70 * S, y1 + 40 * S)], fill=40, width=S)
    for k in range(1, 9):
        yy_ = hor + (y1 - hor) * (k / 8) ** 1.8
        d.line([(x0, yy_), (x1, yy_)], fill=int(25 + 30 * k / 8), width=S)
    fade = vgrad((0, (hor / S), 1, y1 / S), [(0, (0, 0, 0)), (1, (255, 255, 255))]).getchannel("R")
    paste(canvas, solid(RACES[race]["acc"]), ImageChops.multiply(ImageChops.multiply(g, fade), mask))

# ------------------------------------------------------------ widgets
def panel(canvas, r, c=12, raised=True, base=None):
    m = chamfer_mask(r, c)
    if raised: drop_shadow(canvas, m, (0, 4), 6, 200)
    if base: paste(canvas, base, m)
    edge_light(canvas, m, 1.2, a_top=55, a_bot=200)
    outline(canvas, m, (6, 7, 8), 1.4)
    return m

def recess(canvas, r, c=12, fill=(14, 16, 18)):
    m = chamfer_mask(r, c)
    paste(canvas, vgrad(r, [(0, tuple(int(v * 0.8) for v in fill)), (1, tuple(int(v * 1.25) for v in fill))]), m)
    inner_shadow(canvas, m, (0, 3), 6, 230)
    # bottom lip highlight
    sh = ImageChops.subtract(chamfer_mask((r[0], r[1], r[2], r[3] + 1.2), c), m)
    paste(canvas, solid((255, 255, 255), 40), sh)
    return m

def hexmesh(canvas, mask, r, alpha=16):
    x0, y0, x1, y1 = R(*r); m = mask_new(); d = ImageDraw.Draw(m); rr = 9 * S
    for j, y in enumerate(np.arange(y0, y1 + rr, rr * 1.5)):
        for x in np.arange(x0 + (j % 2) * rr * 0.866, x1 + rr, rr * 1.732):
            pts = [(x + rr * math.cos(math.pi / 3 * k + math.pi / 6), y + rr * math.sin(math.pi / 3 * k + math.pi / 6)) for k in range(7)]
            d.line(pts, fill=alpha, width=S)
    paste(canvas, solid((150, 170, 190)), ImageChops.multiply(m, mask))

def key_button(canvas, r, text, acc, selected=False, primary=False, fsize=15):
    x0, y0, x1, y1 = r
    m = chamfer_mask(r, 10)
    drop_shadow(canvas, m, (0, 3), 4, 210)
    if selected or primary:
        dark = tuple(int(v * 0.16) for v in acc); mid = tuple(int(v * 0.36) for v in acc)
        paste(canvas, vgrad(r, [(0, mid), (0.5, dark), (1, tuple(int(v * 0.10) for v in acc))]), m)
        # lit core
        cm = chamfer_mask((x0 + 6, y0 + 6, x1 - 6, y1 - 6), 7)
        glow(canvas, ImageChops.multiply(cm, m), acc, 12, 0.35)
        hexmesh(canvas, cm, r, 26)
        edge_light(canvas, m, 1.2, top=tuple(min(255, v + 80) for v in acc), a_top=170, a_bot=220)
        outline(canvas, m, acc, 1.3)
        glow(canvas, ImageChops.subtract(m.filter(ImageFilter.MaxFilter(5)), m), acc, 5, 1.6)
        f = font(INTER_XB, fsize)
        glow_text(canvas, ((x0 + x1) / 2, (y0 + y1) / 2), text, f, acc, 4, "mm", 1.6, spacing=1.5 if not primary else 4,
                  core=tuple(min(255, v + 120) for v in acc))
    else:
        paste(canvas, vgrad(r, [(0, (78, 84, 92)), (0.08, (56, 61, 68)), (0.55, (40, 44, 50)), (1, (28, 31, 35))]), m)
        hexmesh(canvas, m, r, 10)
        edge_light(canvas, m, 1.2, a_top=110, a_bot=220)
        outline(canvas, m, (5, 6, 7), 1.3)
        f = font(INTER_B, fsize)
        plain_text(canvas, ((x0 + x1) / 2, (y0 + y1) / 2 + 1), text, f, (0, 0, 0), "mm", spacing=1.5, a=160)
        plain_text(canvas, ((x0 + x1) / 2, (y0 + y1) / 2), text, f, (176, 184, 194), "mm", spacing=1.5)
    # LED
    lx, ly = x1 - 14, y0 + 12
    led = mask_new(); ImageDraw.Draw(led).ellipse(R(lx - 3, ly - 3, lx + 3, ly + 3), fill=255)
    if selected or primary:
        glow(canvas, led, acc, 4, 2.0); paste(canvas, solid(tuple(min(255, v + 100) for v in acc)), led)
    else:
        paste(canvas, solid((20, 22, 24)), led); edge_light(canvas, led, 1, a_top=0, a_bot=90)

def dropdown(canvas, r, text, acc):
    m = crt(canvas, r, 8)
    x0, y0, x1, y1 = r
    glow_text(canvas, (x0 + 18, (y0 + y1) / 2), text, font(MONO, 17), PHOS, 3, "lm", 1.1)
    # chevron button
    cr = (x1 - 46, y0 + 6, x1 - 6, y1 - 6)
    cm = chamfer_mask(cr, 6)
    paste(canvas, vgrad(cr, [(0, (60, 66, 72)), (1, (30, 33, 37))]), cm)
    edge_light(canvas, cm, 1, a_top=90)
    cx, cy = (cr[0] + cr[2]) / 2, (cr[1] + cr[3]) / 2
    ch = mask_new(); ImageDraw.Draw(ch).polygon([R(cx - 7, cy - 3), R(cx + 7, cy - 3), R(cx, cy + 5)], fill=255)
    glow(canvas, ch, acc, 3, 1.5); paste(canvas, solid(acc), ch)
    glass_sheen(canvas, m, r)

def toggle(canvas, x, y, label, on, acc):
    r = (x, y, x + 34, y + 18)
    m = chamfer_mask(r, 4); paste(canvas, solid((10, 12, 13)), m); inner_shadow(canvas, m, (0, 2), 3, 230)
    kx = x + (18 if on else 2)
    km = chamfer_mask((kx, y + 2, kx + 14, y + 16), 3)
    paste(canvas, vgrad((0, y + 2, 1, y + 16), [(0, (120, 128, 136)), (1, (60, 64, 70))]), km)
    edge_light(canvas, km, 1, a_top=120)
    if on:
        sl = chamfer_mask((x + 3, y + 3, x + 17, y + 15), 3)
        glow(canvas, sl, PHOS, 4, 1.2); paste(canvas, solid(PHOS, 200), sl)
    glow_text(canvas, (x + 46, y + 9), label, font(MONO, 13.5), PHOS if on else PHOS_DIM, 2.5 if on else 0.1, "lm", 1.0 if on else 0.01)

# ------------------------------------------------------------ scene
def render(race, pluto_race="PROTOSS"):
    A = RACES[race]["acc"]; A2 = RACES[race]["acc2"]
    cv = brushed_metal((44, 48, 53), 1)

    # outer frame: hazard edges, seams, corner plates
    hazard(cv, (0, 0, W, 9), 3); hazard(cv, (0, H - 9, W, H), 5)
    frame = chamfer_mask((4, 14, W - 4, H - 14), 18)
    edge_light(cv, frame, 1.4, a_top=60, a_bot=200)

    # ---- header viewscreen
    hr = (18, 24, W - 18, 172)
    plate = panel(cv, hr, 16, True, brushed_metal((36, 39, 44), 2))
    vs = (28, 34, 660, 162)
    vm = chamfer_mask(vs, 12)
    space_scene(cv, vs, race, vm)
    scanlines(cv, vm, 38)
    inner_shadow(cv, vm, (0, 4), 9, 240)
    outline(cv, vm, (5, 6, 8), 2)
    hud_brackets(cv, (36, 42, 652, 154), A, 16, 230)
    glass_sheen(cv, vm, vs)
    draw_logo(cv, 50, 58, 42, A, A2)
    glow_text(cv, (54, 135), "> LAN MELEE  //  VS PLUTO AI  //  v1.4", font(MONO, 12.5), PHOS, 2.5, "lm", 1.0)

    # status CRT
    st = (672, 34, W - 28, 162)
    sm = crt(cv, st, 10)
    fm = font(MONO, 13.5)
    rows = [("SYSTEM", "ONLINE"), ("PLUTO", "2.0  CPU"), ("STARCRAFT", "1.23.10"), ("STATUS", "READY")]
    for i, (k, v) in enumerate(rows):
        yy = 52 + i * 25
        plain_text(cv, (690, yy), k, fm, PHOS_DIM, "lm")
        glow_text(cv, (800, yy), v, fm, PHOS, 2.5, "lm", 1.1)
    # signal bars + LED
    for i in range(7):
        h = 6 + i * 3.2; x = 900 + i * 8
        bm = chamfer_mask((x, 150 - h, x + 5, 150), 0)
        if i < 6: glow(cv, bm, PHOS, 2, 0.8)
        paste(cv, solid(PHOS if i < 6 else PHOS_DIM), bm)
    glass_sheen(cv, sm, st)
    for (x, y) in [(28, 34), (W - 28, 34)]:
        pass

    # ---- content block
    cr = (18, 184, W - 18, 644)
    panel(cv, cr, 16, True, brushed_metal((38, 41, 46), 3))
    rows = [("YOUR RACE", "COMMANDER", 198, 108), ("PLUTO RACE", "OPPONENT", 316, 108),
            ("MAP", "SECTOR", 434, 92), ("RANK", "THREAT LEVEL / VERSION", 536, 92)]
    for idx, (lab, sub, y, h) in enumerate(rows):
        r = (32, y, W - 32, y + h)
        rm = recess(cv, r, 12)
        hexmesh(cv, rm, r, 12)
        # race-colored light strip
        strip = chamfer_mask((40, y + 14, 45, y + h - 14), 0)
        glow(cv, strip, A, 7, 1.4); paste(cv, solid(tuple(min(255, v + 60) for v in A)), strip)
        # label
        plain_text(cv, (64, y + h / 2 - 30), f"0{idx + 1}", font(MONOB, 11.5), A, "lm")
        plain_text(cv, (88, y + h / 2 - 30), "─" * 6, font(MONO, 11.5), (90, 98, 108), "lm")
        plain_text(cv, (64, y + h / 2 + 1), lab, font(INTER_XB, 25), (0, 0, 0), "lm", 1.2, 180)
        plain_text(cv, (64, y + h / 2 - 1), lab, font(INTER_XB, 25), (232, 236, 242), "lm", 1.2)
        plain_text(cv, (64, y + h / 2 + 26), sub, font(MONO, 11.5), (120, 130, 142), "lm", 1)
        screw(cv, W - 46, y + 12, 4.5); screw(cv, W - 46, y + h - 12, 4.5)
        if h == 108:
            sel = race if idx == 0 else pluto_race
            for i, n in enumerate(["TERRAN", "PROTOSS", "ZERG", "RANDOM"]):
                x = 300 + i * 160
                key_button(cv, (x, y + 14, x + 150, y + h - 14), n, RACES[n]["acc"], selected=(n == sel))
        elif idx == 2:
            dropdown(cv, (300, y + 18, W - 62, y + h - 18), "(2) Fighting Spirit 1.3", A)
        else:
            dropdown(cv, (300, y + 18, W - 62 - 214, y + h - 18), "C  -  medium", A)
            dropdown(cv, (W - 62 - 202, y + 18, W - 62, y + h - 18), "PLUTO 2.0", A)

    # ---- bottom console
    hazard(cv, (18, 652, 296, 660), 8)
    toggle(cv, 30, 672, "HIDE PLUTO WINDOW", True, A)
    toggle(cv, 30, 700, "MUTE PLUTO", True, A)
    toggle(cv, 30, 728, "LADDER MODE", False, A)
    key_button(cv, (316, 660, 470, 752), "SETUP", A, fsize=16)
    # PLAY: heavy button inside a hazard collar
    collar = (478, 652, 802, 760)
    hazard(cv, collar, 12)
    cm = chamfer_mask(collar, 14)
    big = (488, 662, 792, 750)
    key_button(cv, big, "PLAY", A, primary=True, fsize=34)
    key_button(cv, (810, 660, W - 18, 752), "EXIT", A, fsize=16)

    for (x, y) in [(12, 22), (W - 12, 22), (12, H - 22), (W - 12, H - 22)]:
        screw(cv, x, y, 5)

    # global vignette
    yy, xx = np.mgrid[0:SH, 0:SW].astype(np.float32)
    v = 1 - 0.35 * (((xx / SW - 0.5) * 2) ** 2 * 0.5 + ((yy / SH - 0.5) * 2) ** 2 * 0.5) ** 1.5
    arr = np.asarray(cv, np.float32).copy(); arr[..., :3] *= v[..., None]
    return Image.fromarray(arr.clip(0, 255).astype(np.uint8), "RGBA").convert("RGB").resize((W, H), Image.LANCZOS)

if __name__ == "__main__":
    which = sys.argv[1:] or ["TERRAN"]
    for r in which:
        render(r).save(f"v2_{r.lower()}.png")
        print("ok", r)
