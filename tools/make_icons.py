#!/usr/bin/env python3
"""
Generates VlcNightlyUpdater/Assets/app.ico and app-christmas.ico (and a preview PNG).

    pip install pillow numpy
    python tools/make_icons.py

All design knobs are the constants in the DESIGN block. Colors must match Theme/Tokens.xaml (brand orange).
Small sizes (<= 32 px) are drawn simpler (bolder arrow, no moon) so they stay readable.
"""
import io, struct, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageChops

# ------------------------------ DESIGN ------------------------------
BG_TOP, BG_BOTTOM = (255, 178, 70), (230, 92, 0)     # body gradient (brand orange family)
GLYPH_TOP, GLYPH_BOTTOM = (255, 255, 255), (255, 238, 214)
SHADOW = (140, 45, 0)                                 # glyph drop shadow tint
SQUIRCLE_POWER = 5.0                                  # 2 = circle, higher = squarer
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
HI = 2048                                             # supersampled working canvas
OUT = Path(__file__).resolve().parent.parent / "VlcNightlyUpdater" / "Assets"
# --------------------------------------------------------------------

def vgrad(top, bottom, size):
    t = np.linspace(0, 1, size)[:, None, None]
    a = np.array(top, float)[None, None, :]; b = np.array(bottom, float)[None, None, :]
    row = a * (1 - t) + b * t
    return np.repeat(row, size, axis=1)

def squircle_mask(size, scale):
    ys, xs = np.mgrid[0:size, 0:size].astype(float)
    c = (size - 1) / 2; r = size / 2 * scale
    v = (np.abs(xs - c) / r) ** SQUIRCLE_POWER + (np.abs(ys - c) / r) ** SQUIRCLE_POWER
    # soft edge of ~1.2 px at HI res, so downsampling gives clean anti-aliasing
    return Image.fromarray((np.clip((1 - v) * r * 0.9, 0, 1) * 255).astype(np.uint8), "L")

def rounded(mask, radius):
    """Round the corners of a hard mask: blur then threshold."""
    m = mask.filter(ImageFilter.GaussianBlur(radius))
    return m.point(lambda p: 255 if p > 127 else 0)

def glyph_mask(size, simple):
    m = Image.new("L", (size, size), 0); d = ImageDraw.Draw(m)
    s = size / 1024
    if simple:   # bolder, fills more of the tile
        shaft = (512 - 120, 190, 512 + 120, 520); head = [(512 - 290, 480), (512 + 290, 480), (512, 760)]; tray = (222, 815, 802, 905)
    else:
        shaft = (512 - 92, 215, 512 + 92, 520); head = [(512 - 245, 490), (512 + 245, 490), (512, 715)]; tray = (262, 775, 762, 840)
    d.rectangle([v * s for v in shaft], fill=255)
    d.polygon([(x * s, y * s) for x, y in head], fill=255)
    arrow = rounded(m, 20 * s)
    t = Image.new("L", (size, size), 0)
    ImageDraw.Draw(t).rounded_rectangle([v * s for v in tray], radius=(tray[3] - tray[1]) / 2 * s, fill=255)
    return ImageChops.lighter(arrow, t)

def moon_mask(size):
    s = size / 1024
    m = Image.new("L", (size, size), 0); d = ImageDraw.Draw(m)
    cx, cy, r = 790, 232, 58
    d.ellipse([(cx - r) * s, (cy - r) * s, (cx + r) * s, (cy + r) * s], fill=255)
    cut = Image.new("L", (size, size), 0)
    ImageDraw.Draw(cut).ellipse([(cx + 22 - r * .86) * s, (cy - 14 - r * .86) * s, (cx + 22 + r * .86) * s, (cy - 14 + r * .86) * s], fill=255)
    return ImageChops.subtract(m, cut)

def paint(layer_mask, top, bottom, size):
    rgb = Image.fromarray(vgrad(top, bottom, size).astype(np.uint8), "RGB")
    out = rgb.convert("RGBA"); out.putalpha(layer_mask); return out

def _bez(p0, p1, p2, n=40):
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
             (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]) for t in [i / n for i in range(n + 1)]]

def santa_hat(size):
    """Small hat on the top-left corner: red drooping cone, white fur band, pompom. Stays clear of the arrow."""
    s = size / 1024
    k, ox, oy = 0.70, 78, 40                       # scale + placement in 1024 space
    P = lambda x, y: ((ox + x * k) * s, (oy + y * k) * s)
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    cone = _bez((20, 300), (60, 70), (400, 25)) + _bez((400, 25), (470, 150), (445, 300))
    body = Image.new("L", (size, size), 0)
    ImageDraw.Draw(body).polygon([P(x, y) for x, y in cone], fill=255)
    pom = Image.new("L", (size, size), 0)
    ImageDraw.Draw(pom).ellipse([*P(345, -30), *P(455, 80)], fill=255)
    fur = Image.new("L", (size, size), 0)
    ImageDraw.Draw(fur).rounded_rectangle([*P(-25, 262), *P(490, 372)], radius=55 * k * s, fill=255)
    parts = [(body, (242, 76, 66), (176, 24, 36)), (fur, (255, 255, 255), (226, 228, 238)), (pom, (255, 255, 255), (226, 228, 238))]

    union = ImageChops.lighter(ImageChops.lighter(body, fur), pom)
    shadow = Image.new("RGBA", (size, size), (60, 0, 0, 0))
    shadow.putalpha(ImageChops.offset(union, 0, int(size * .012)).filter(ImageFilter.GaussianBlur(size * .010)).point(lambda p: int(p * .5)))
    layer.alpha_composite(shadow)
    for m, top, bottom in parts:
        layer.alpha_composite(paint(m, top, bottom, size))
    return layer.rotate(-8, resample=Image.BICUBIC, center=(size * .12, size * .22))

def master(size_px, christmas=False):
    """One finished icon at HI resolution, then reduced to size_px."""
    simple = size_px <= 32
    full_bleed = size_px <= 48
    scale = 0.985 if full_bleed else 0.90
    N = HI
    canvas = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    body = squircle_mask(N, scale)

    if not full_bleed:      # soft ground shadow
        sh = Image.new("RGBA", (N, N), (0, 0, 0, 0))
        sh.putalpha(body.point(lambda p: int(p * 0.38)))
        sh = ImageChops.offset(sh, 0, int(N * 0.018)).filter(ImageFilter.GaussianBlur(N * 0.014))
        canvas.alpha_composite(sh)

    tile = paint(body, BG_TOP, BG_BOTTOM, N)
    ys, xs = np.mgrid[0:N, 0:N].astype(float)
    # soft light from the top-left
    glow = np.clip(1 - np.hypot((xs - N * .32) / (N * .75), (ys - N * .05) / (N * .55)), 0, 1) ** 1.6 * 0.30
    arr = np.array(tile).astype(float)
    arr[..., :3] = arr[..., :3] * (1 - glow[..., None]) + 255 * glow[..., None]
    tile = Image.fromarray(arr.astype(np.uint8), "RGBA")
    # rim: bright on top, dark on bottom, hugging the edge
    inner = body.filter(ImageFilter.MinFilter(int(N * 0.006) * 2 + 1))
    rim = ImageChops.subtract(body, inner)
    top_fade = (np.clip(1 - ys / (N * .6), 0, 1) * 150).astype(np.uint8)
    bot_fade = (np.clip((ys / N - .55) / .45, 0, 1) * 70).astype(np.uint8)
    for fade, col in ((top_fade, (255, 255, 255)), (bot_fade, (120, 40, 0))):
        a = ImageChops.multiply(rim, Image.fromarray(fade, "L"))
        layer = Image.new("RGBA", (N, N), col + (0,)); layer.putalpha(a)
        tile.alpha_composite(layer)
    canvas.alpha_composite(tile)

    # glyph with soft shadow (scaled to the tile)
    g = glyph_mask(N, simple)
    k = scale / 0.985
    if k != 1:
        g = g.resize((int(N * k), int(N * k)), Image.LANCZOS)
        pad = Image.new("L", (N, N), 0); o = (N - g.width) // 2; pad.paste(g, (o, o)); g = pad
    shadow = Image.new("RGBA", (N, N), SHADOW + (0,))
    shadow.putalpha(ImageChops.offset(g, 0, int(N * .016)).filter(ImageFilter.GaussianBlur(N * .012)).point(lambda p: int(p * .55)))
    canvas.alpha_composite(shadow)
    canvas.alpha_composite(paint(g, GLYPH_TOP, GLYPH_BOTTOM, N))

    if size_px >= 64:       # crescent moon = "nightly" (too small to read below 64 px)
        mm = moon_mask(N)
        if k != 1:
            mm = mm.resize((int(N * k), int(N * k)), Image.LANCZOS)
            pad = Image.new("L", (N, N), 0); o = (N - mm.width) // 2; pad.paste(mm, (o, o)); mm = pad
        canvas.alpha_composite(paint(mm.point(lambda p: int(p * .92)), (255, 255, 255), (255, 236, 205), N))

    if christmas:
        canvas.alpha_composite(santa_hat(N))
    return canvas.resize((size_px, size_px), Image.LANCZOS)

# ---- ICO writer: BMP frames for small sizes (max compatibility), PNG for >= 64 ----
def bmp_frame(img):
    w, h = img.size
    px = img.tobytes("raw", "BGRA", 0, -1)           # bottom-up rows
    mask_row = ((w + 31) // 32) * 4
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(px), 0, 0, 0, 0)
    return header + px + b"\x00" * (mask_row * h)    # AND mask unused with 32-bit alpha

def png_frame(img):
    b = io.BytesIO(); img.save(b, "PNG", optimize=True); return b.getvalue()

def write_ico(path, frames):
    data = [png_frame(im) if im.width >= 64 else bmp_frame(im) for im in frames]
    out = bytearray(struct.pack("<HHH", 0, 1, len(frames)))
    off = 6 + 16 * len(frames)
    for im, d in zip(frames, data):
        w = 0 if im.width >= 256 else im.width
        out += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(d), off); off += len(d)
    for d in data: out += d
    Path(path).write_bytes(bytes(out))

if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for name, xmas in (("app.ico", False), ("app-christmas.ico", True)):
        frames = [master(s, xmas) for s in SIZES]
        write_ico(OUT / name, frames)
        if "--preview" in sys.argv:
            master(512, xmas).save(OUT.parent.parent / "tools" / (name.replace(".ico", "-preview.png")))
        print("wrote", OUT / name)
