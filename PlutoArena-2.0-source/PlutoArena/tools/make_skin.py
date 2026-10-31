"""
Builds Pluto Arena's skin: window backgrounds (one per race accent), button
and switch sprites and the UI sounds, into ..\\Skin. All art is generated
here from code (original drawings - no game assets).

    python tools/make_skin.py            (needs Pillow + numpy)

The layout numbers must match Form1.cs (Layout section).
"""
import math, os, struct, sys, wave
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops

sys.path.insert(0, os.path.dirname(__file__))
import skin_art as A
from skin_art import (S, W, H, R, RACES, PHOS, PHOS_DIM, font, INTER_B, INTER_XB, MONO, MONOB,
                      mask_new, chamfer_mask, solid, paste, vgrad, glow, inner_shadow, drop_shadow,
                      edge_light, outline, glow_text, plain_text, brushed_metal, screw, hazard,
                      scanlines, crt, glass_sheen, hud_brackets, draw_logo, space_scene, panel,
                      recess, hexmesh)

OUT = os.path.join(os.path.dirname(__file__), "..", "Skin")

# ---------------------------------------------------------------- layout
HEADER = (18, 24, W - 18, 172)
VIEW = (28, 34, 660, 162)
STATUS = (672, 34, W - 28, 162)
CONTENT = (18, 184, W - 18, 644)
ROWS = [("YOUR RACE", "COMMANDER", 198, 108), ("PLUTO RACE", "OPPONENT", 316, 108),
        ("MAP", "SECTOR", 434, 92), ("RANK", "THREAT LEVEL / VERSION", 536, 92)]
CX = 300                                   # controls start
RACE_W, RACE_H, RACE_STEP = 150, 80, 160   # race buttons: y = row + 14
MAP_FIELD = (CX, 452, W - 62, 508)
RANK_FIELD = (CX, 554, W - 62 - 214, 610)
VER_FIELD = (W - 62 - 202, 554, W - 62, 610)
SETUP = (316, 660, 470, 752)
PLAY = (488, 662, 792, 750)
COLLAR = (478, 652, 802, 760)
EXIT = (810, 660, W - 18, 752)
M = 5                                      # sprite margin (glow / shadow)


def mix(a, b, t): return tuple(int(a[i] * (1 - t) + b[i] * t) for i in range(3))
def lift(c, k): return tuple(min(255, int(v + k)) for v in c)


# ---------------------------------------------------------------- buttons
def key(cv, r, text, acc, state="normal", lit=False, fsize=15, spacing=1.5):
    """Metal key. lit = selected / primary (glows in acc)."""
    x0, y0, x1, y1 = r
    press = state == "pressed"
    if press:
        r = (x0, y0 + 1.5, x1, y1 + 1.5); x0, y0, x1, y1 = r
    m = chamfer_mask(r, 10)
    drop_shadow(cv, m, (0, 1 if press else 3), 2 if press else 4, 210)
    hov = state == "hover"
    dis = state == "disabled"
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    if lit and not dis:
        k = 1.25 if hov else (0.8 if press else 1.0)
        mid = tuple(int(v * 0.36 * k) for v in acc); dark = tuple(int(v * 0.16 * k) for v in acc)
        paste(cv, vgrad(r, [(0, mid), (0.5, dark), (1, tuple(int(v * 0.10) for v in acc))]), m)
        core = chamfer_mask((x0 + 6, y0 + 6, x1 - 6, y1 - 6), 7)
        glow(cv, ImageChops.multiply(core, m), acc, 12, 0.5 if hov else 0.35)
        hexmesh(cv, core, r, 34 if hov else 26)
        edge_light(cv, m, 1.2, top=lift(acc, 80), a_top=170, a_bot=220)
        outline(cv, m, lift(acc, 40) if hov else acc, 1.3)
        f = font(INTER_XB, fsize)
        glow_text(cv, (cx, cy), text, f, acc, 4, "mm", 2.0 if hov else 1.6, spacing=spacing, core=lift(acc, 130))
    else:
        if dis:
            stops = [(0, (52, 56, 62)), (0.08, (40, 44, 49)), (0.55, (32, 35, 39)), (1, (24, 26, 29))]
        elif hov:
            stops = [(0, (98, 105, 115)), (0.08, (70, 76, 85)), (0.55, (52, 57, 64)), (1, (36, 40, 45))]
        elif press:
            stops = [(0, (40, 44, 49)), (0.5, (34, 37, 42)), (1, (30, 33, 37))]
        else:
            stops = [(0, (78, 84, 92)), (0.08, (56, 61, 68)), (0.55, (40, 44, 50)), (1, (28, 31, 35))]
        paste(cv, vgrad(r, stops), m)
        hexmesh(cv, m, r, 10)
        edge_light(cv, m, 1.2, a_top=60 if press else 110, a_bot=220)
        if hov:
            outline(cv, m, mix((5, 6, 7), acc, 0.55), 1.3)
            glow(cv, ImageChops.subtract(m.filter(ImageFilter.MaxFilter(5)), m), acc, 4, 0.7)
        else:
            outline(cv, m, (5, 6, 7), 1.3)
        f = font(INTER_B, fsize)
        col = (110, 115, 122) if dis else ((235, 240, 246) if hov else (176, 184, 194))
        plain_text(cv, (cx, cy + 1), text, f, (0, 0, 0), "mm", spacing=spacing, a=160)
        plain_text(cv, (cx, cy), text, f, col, "mm", spacing=spacing)
    # LED
    lx, ly = x1 - 14, y0 + 12
    led = mask_new(); ImageDraw.Draw(led).ellipse(R(lx - 3, ly - 3, lx + 3, ly + 3), fill=255)
    if lit and not dis:
        glow(cv, led, acc, 4, 2.0); paste(cv, solid(lift(acc, 100)), led)
    elif hov:
        glow(cv, led, acc, 3, 0.8); paste(cv, solid(mix((20, 22, 24), acc, 0.5)), led)
    else:
        paste(cv, solid((20, 22, 24)), led); edge_light(cv, led, 1, a_top=0, a_bot=90)


def sprite(name, w, h, draw):
    """Draws at (M, M) on a transparent canvas and saves a (w+2M) x (h+2M) PNG."""
    cv = Image.new("RGBA", (A.SW, A.SH), (0, 0, 0, 0))
    draw(cv, (M, M, M + w, M + h))
    box = (0, 0, (w + 2 * M) * S, (h + 2 * M) * S)
    img = cv.crop(box).resize((w + 2 * M, h + 2 * M), Image.LANCZOS)
    img.save(os.path.join(OUT, name + ".png"), optimize=True)
    print("  ", name)


def toggle_sprite(name, on, hov):
    w, h, m = 34, 18, 3
    cv = Image.new("RGBA", (A.SW, A.SH), (0, 0, 0, 0))
    x, y = m, m
    r = (x, y, x + w, y + h)
    tm = chamfer_mask(r, 4)
    paste(cv, solid((10, 12, 13)), tm); inner_shadow(cv, tm, (0, 2), 3, 230)
    edge_light(cv, tm, 1, a_top=0, a_bot=0)
    outline(cv, tm, (90, 96, 104) if hov else (4, 5, 6), 1)
    if on:
        sl = chamfer_mask((x + 3, y + 3, x + 17, y + 15), 3)
        glow(cv, sl, PHOS, 4, 1.4 if hov else 1.2); paste(cv, solid(PHOS, 220), sl)
    kx = x + (18 if on else 2)
    km = chamfer_mask((kx, y + 2, kx + 14, y + 16), 3)
    top = (150, 158, 166) if hov else (120, 128, 136)
    paste(cv, vgrad((0, y + 2, 1, y + 16), [(0, top), (1, (60, 64, 70))]), km)
    edge_light(cv, km, 1, a_top=120)
    img = cv.crop((0, 0, (w + 2 * m) * S, (h + 2 * m) * S)).resize((w + 2 * m, h + 2 * m), Image.LANCZOS)
    img.save(os.path.join(OUT, name + ".png"), optimize=True)
    print("  ", name)


# ---------------------------------------------------------------- background
def background(race):
    acc = RACES[race]["acc"]; acc2 = RACES[race]["acc2"]
    cv = brushed_metal((44, 48, 53), 1)
    hazard(cv, (0, 0, W, 9), 3); hazard(cv, (0, H - 9, W, H), 5)
    edge_light(cv, chamfer_mask((4, 14, W - 4, H - 14), 18), 1.4, a_top=60, a_bot=200)

    # header
    panel(cv, HEADER, 16, True, brushed_metal((36, 39, 44), 2))
    vm = chamfer_mask(VIEW, 12)
    space_scene(cv, VIEW, race, vm)
    scanlines(cv, vm, 38)
    inner_shadow(cv, vm, (0, 4), 9, 240)
    outline(cv, vm, (5, 6, 8), 2)
    hud_brackets(cv, (36, 42, 652, 154), acc, 16, 230)
    glass_sheen(cv, vm, VIEW)
    draw_logo(cv, 50, 58, 42, acc, acc2)

    # status screen (text is drawn by the program)
    sm = crt(cv, STATUS, 10)
    for i in range(7):
        h = 6 + i * 3.2; x = 900 + i * 8
        bm = chamfer_mask((x, 150 - h, x + 5, 150), 0)
        if i < 6: glow(cv, bm, PHOS, 2, 0.8)
        paste(cv, solid(PHOS if i < 6 else PHOS_DIM), bm)
    glass_sheen(cv, sm, STATUS)

    # content
    panel(cv, CONTENT, 16, True, brushed_metal((38, 41, 46), 3))
    for idx, (lab, sub, y, h) in enumerate(ROWS):
        r = (32, y, W - 32, y + h)
        rm = recess(cv, r, 12)
        hexmesh(cv, rm, r, 12)
        strip = chamfer_mask((40, y + 14, 45, y + h - 14), 0)
        glow(cv, strip, acc, 7, 1.4); paste(cv, solid(lift(acc, 60)), strip)
        plain_text(cv, (64, y + h / 2 - 30), f"0{idx + 1}", font(MONOB, 11.5), acc, "lm")
        plain_text(cv, (88, y + h / 2 - 30), "─" * 6, font(MONO, 11.5), (90, 98, 108), "lm")
        plain_text(cv, (64, y + h / 2 + 1), lab, font(INTER_XB, 25), (0, 0, 0), "lm", 1.2, 180)
        plain_text(cv, (64, y + h / 2 - 1), lab, font(INTER_XB, 25), (232, 236, 242), "lm", 1.2)
        plain_text(cv, (64, y + h / 2 + 26), sub, font(MONO, 11.5), (120, 130, 142), "lm", 1)
        screw(cv, W - 46, y + 12, 4.5); screw(cv, W - 46, y + h - 12, 4.5)

    # drop-down fields (text and the hover glow are drawn by the program)
    for f in (MAP_FIELD, RANK_FIELD, VER_FIELD):
        fm = crt(cv, f, 8)
        x0, y0, x1, y1 = f
        cr = (x1 - 46, y0 + 6, x1 - 6, y1 - 6)
        cm = chamfer_mask(cr, 6)
        paste(cv, vgrad(cr, [(0, (60, 66, 72)), (1, (30, 33, 37))]), cm)
        edge_light(cv, cm, 1, a_top=90)
        ccx, ccy = (cr[0] + cr[2]) / 2, (cr[1] + cr[3]) / 2
        ch = mask_new(); ImageDraw.Draw(ch).polygon([R(ccx - 7, ccy - 3), R(ccx + 7, ccy - 3), R(ccx, ccy + 5)], fill=255)
        glow(cv, ch, acc, 3, 1.5); paste(cv, solid(acc), ch)
        glass_sheen(cv, fm, f)

    # bottom console
    hazard(cv, (18, 652, 296, 660), 8)
    hazard(cv, COLLAR, 12)
    for (x, y) in [(12, 22), (W - 12, 22), (12, H - 22), (W - 12, H - 22)]:
        screw(cv, x, y, 5)

    yy, xx = np.mgrid[0:A.SH, 0:A.SW].astype(np.float32)
    v = 1 - 0.35 * (((xx / A.SW - 0.5) * 2) ** 2 * 0.5 + ((yy / A.SH - 0.5) * 2) ** 2 * 0.5) ** 1.5
    arr = np.asarray(cv, np.float32).copy(); arr[..., :3] *= v[..., None]
    img = Image.fromarray(arr.clip(0, 255).astype(np.uint8), "RGBA").convert("RGB").resize((W, H), Image.LANCZOS)
    name = f"bg_{race.lower()}.jpg"
    img.save(os.path.join(OUT, name), quality=93, subsampling=0, optimize=True)
    print("  ", name)
    return img


# ---------------------------------------------------------------- sounds
RATE = 44100

def save_wav(name, x):
    x = np.clip(x, -1, 1)
    data = (x * 32767 * 0.9).astype("<i2").tobytes()
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE); w.writeframes(data)
    print("  ", name, f"{len(x) / RATE * 1000:.0f} ms")

def env(n, attack, decay):
    t = np.arange(n) / RATE
    return np.minimum(1, t / max(attack, 1e-4)) * np.exp(-t / decay)

def lowpass(x, cutoff):
    a = math.exp(-2 * math.pi * cutoff / RATE); y = np.zeros_like(x); s = 0.0
    for i in range(len(x)): s = (1 - a) * x[i] + a * s; y[i] = s
    return y

def sounds():
    rnd = np.random.default_rng(5)
    # hover: soft, short electronic tick
    n = int(0.045 * RATE); t = np.arange(n) / RATE
    hover = (np.sin(2 * np.pi * 2600 * t) * 0.5 + np.sin(2 * np.pi * 3900 * t) * 0.2) * env(n, 0.001, 0.010)
    hover += lowpass(rnd.standard_normal(n), 6000) * env(n, 0.0005, 0.004) * 0.3
    save_wav("sfx_hover", hover * 0.32)

    # click: mechanical relay - noise snap + low thump + short metallic ring
    n = int(0.16 * RATE); t = np.arange(n) / RATE
    snap = lowpass(rnd.standard_normal(n), 4500) * env(n, 0.0003, 0.006)
    thump = np.sin(2 * np.pi * (140 - 60 * t / t[-1]) * t) * env(n, 0.001, 0.035)
    ring = np.sin(2 * np.pi * 1850 * t) * env(n, 0.001, 0.030) * 0.25 + np.sin(2 * np.pi * 2770 * t) * env(n, 0.001, 0.02) * 0.15
    snap2 = np.roll(lowpass(rnd.standard_normal(n), 3000) * env(n, 0.0003, 0.004), int(0.028 * RATE)) * 0.6
    save_wav("sfx_click", (snap * 0.9 + thump * 0.7 + ring + snap2) * 0.55)

    # toggle: two quick ticks
    n = int(0.09 * RATE); t = np.arange(n) / RATE
    tick = np.sin(2 * np.pi * 1700 * t) * env(n, 0.0005, 0.006) + lowpass(rnd.standard_normal(n), 5000) * env(n, 0.0003, 0.003) * 0.5
    save_wav("sfx_toggle", (tick + np.roll(tick, int(0.035 * RATE)) * 0.7) * 0.4)

    # play: power-up sweep + whoosh + lock-in clunk
    dur = 0.95; n = int(dur * RATE); t = np.arange(n) / RATE
    f = 110 + 780 * (t / dur) ** 1.6
    ph = 2 * np.pi * np.cumsum(f) / RATE
    rise = (np.sin(ph) * 0.45 + np.sin(ph * 2.01) * 0.2 + np.sign(np.sin(ph * 0.5)) * 0.08)
    rise *= np.clip(t / 0.6, 0, 1) ** 1.5 * np.exp(-np.clip(t - 0.62, 0, None) / 0.08)
    whoosh = lowpass(rnd.standard_normal(n), 2500) * np.sin(np.pi * np.clip(t / 0.7, 0, 1)) ** 2 * 0.35
    hit_at = int(0.62 * RATE); m = n - hit_at; th = np.arange(m) / RATE
    hit = np.zeros(n)
    hit[hit_at:] = (np.sin(2 * np.pi * 70 * th) * np.exp(-th / 0.12) * 0.9
                    + lowpass(rnd.standard_normal(m), 3500) * np.exp(-th / 0.015) * 0.8
                    + np.sin(2 * np.pi * 880 * th) * np.exp(-th / 0.18) * 0.18
                    + np.sin(2 * np.pi * 1320 * th) * np.exp(-th / 0.14) * 0.10)
    save_wav("sfx_play", (rise + whoosh + hit) * 0.5)


# ---------------------------------------------------------------- main
def main():
    os.makedirs(OUT, exist_ok=True)
    only = set(sys.argv[1:])
    if not only or "bg" in only:
        print("backgrounds")
        for race in RACES: background(race)
    if not only or "sprites" in only:
        print("race buttons")
        for race in ("TERRAN", "PROTOSS", "ZERG", "RANDOM"):
            acc = RACES[race]["acc"]; low = race.lower()
            for st in ("normal", "hover", "pressed"):
                sprite(f"race_{low}_{st}", RACE_W, RACE_H, lambda cv, r, st=st: key(cv, r, race, acc, st))
            for st in ("normal", "hover", "pressed"):
                sprite(f"race_{low}_sel_{st}", RACE_W, RACE_H, lambda cv, r, st=st: key(cv, r, race, acc, st, lit=True))
            sprite(f"race_{low}_disabled", RACE_W, RACE_H, lambda cv, r: key(cv, r, race, acc, "disabled"))
            sprite(f"race_{low}_sel_disabled", RACE_W, RACE_H, lambda cv, r: key(cv, r, race, tuple(int(v * 0.55) for v in acc), "normal", lit=True))
        print("play / setup / exit")
        pw, ph = PLAY[2] - PLAY[0], PLAY[3] - PLAY[1]
        for race in RACES:
            acc = RACES[race]["acc"]; low = race.lower()
            for st in ("normal", "hover", "pressed"):
                sprite(f"play_{low}_{st}", pw, ph, lambda cv, r, st=st: key(cv, r, "PLAY", acc, st, lit=True, fsize=34, spacing=4))
            sprite(f"play_{low}_disabled", pw, ph, lambda cv, r: key(cv, r, "RUNNING", tuple(int(v * 0.5) for v in acc), "normal", lit=True, fsize=24, spacing=4))
        for name, box in (("setup", SETUP), ("exit", EXIT)):
            for st in ("normal", "hover", "pressed"):
                sprite(f"btn_{name}_{st}", box[2] - box[0], box[3] - box[1],
                       lambda cv, r, st=st, name=name: key(cv, r, name.upper(), (200, 210, 225), st, fsize=16))
        print("switches")
        for on in (False, True):
            for hov in (False, True):
                toggle_sprite(f"toggle_{'on' if on else 'off'}{'_hover' if hov else ''}", on, hov)
    if not only or "sfx" in only:
        print("sounds")
        sounds()

if __name__ == "__main__":
    main()
