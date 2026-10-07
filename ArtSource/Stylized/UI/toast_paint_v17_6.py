"""v17.6 toast notice art (GameUI notice pill -> chunky toon HUD, same family as ui9_pill_counter / reward icons).
  ui9_toast.png     128x78 9-slice: navy #2A2F5A fill + top gloss + bottom toon shade, navy ink stroke, cream #FFFAF4 outline,
                    navy #1E1A3A edge, baked hard shadow (navy 50 %, +3,+6). Pill = the top-left 125x72 (72 px tall @1920x1080
                    ref); the bottom/right 6/3 px are the shadow. 9-slice border L40 B41 R43 T35 (StylizedFxV14.BuildAtlas pins it).
  ui_icon_warn.png  88x88 (shown at 44): red #E5484D rounded triangle, white '!'  -> invalid actions
  ui_icon_info.png  88x88 (shown at 44): gold #FABE2C circle, navy 'i'           -> neutral info
Painted at 4x, LANCZOS-downsampled. Usage: python toast_paint_v17_6.py <out_dir>"""
import sys, os, math
import numpy as np, cv2
from PIL import Image, ImageDraw

K = 4
INK = (30, 26, 58); CREAM = (255, 250, 244)
FILL, GLOSS, SHADE = (42, 47, 90), (64, 71, 128), (31, 35, 70)
RED, RED_D, RED_L = (229, 72, 77), (184, 48, 58), (255, 140, 140)
GOLD, GOLD_D, GOLD_L = (250, 190, 44), (217, 138, 18), (255, 229, 138)

def rr_mask(size, box, r):
    m = Image.new('L', size, 0); ImageDraw.Draw(m).rounded_rectangle(box, r, fill=255); return np.array(m)
def layer(mask, col):
    im = np.zeros(mask.shape + (4,), np.uint8); im[..., :3] = col; im[..., 3] = mask; return Image.fromarray(im)
def comp(size, parts):
    out = Image.new('RGBA', size)
    for m, c in parts: out.alpha_composite(layer(m, c))
    return out

def toast():
    W, H = 128 * K, 78 * K; bw, bh = 125 * K, 72 * K; sx, sy = 3 * K, 6 * K
    edge, cream, ink = 3 * K, 4 * K, int(2.5 * K)
    size = (W, H); r = bh // 2
    m_edge = rr_mask(size, (0, 0, bw - 1, bh - 1), r)
    m_sh = np.zeros_like(m_edge); m_sh[sy:, sx:] = m_edge[:H - sy, :W - sx]
    i1 = edge; m_cream = rr_mask(size, (i1, i1, bw - 1 - i1, bh - 1 - i1), r - i1)
    i2 = edge + cream; m_ink = rr_mask(size, (i2, i2, bw - 1 - i2, bh - 1 - i2), r - i2)
    i3 = i2 + ink; m_fill = rr_mask(size, (i3, i3, bw - 1 - i3, bh - 1 - i3), r - i3)
    yy = np.arange(H)[:, None] * np.ones((1, W))
    m_shade = np.where(yy > bh - i3 - 9 * K, m_fill, 0).astype(np.uint8)                          # bottom toon shade (9 px)
    gy0, gy1 = i3 + 3 * K, i3 + 10 * K                                                              # top gloss band (7 px)
    m_gloss = rr_mask(size, (i3 + 16 * K, gy0, bw - 1 - i3 - 16 * K, gy1), (gy1 - gy0) // 2)
    im = comp(size, [((m_sh * .5).astype(np.uint8), INK), (m_edge, INK), (m_cream, CREAM), (m_ink, INK), (m_fill, FILL),
                     (m_shade, SHADE), (m_gloss, GLOSS)])
    return im.resize((W // K, H // K), Image.LANCZOS)

def finish_icon(art, S, cream_w, edge_w, shadow):
    a = np.array(art)[..., 3]
    k = lambda r: cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * r + 1, 2 * r + 1))
    m_cream = cv2.dilate(a, k(cream_w)); m_edge = cv2.dilate(a, k(cream_w + edge_w))
    m_sh = np.zeros_like(m_edge); dx, dy = shadow; m_sh[dy:, dx:] = m_edge[:S - dy, :S - dx]
    out = comp((S, S), [((m_sh * .5).astype(np.uint8), INK), (m_edge, INK), (m_cream, CREAM)]); out.alpha_composite(art)
    return out

def icon(kind):
    OUT = 88; S = OUT * K; inkw = 4 * K; cream_w, edge_w = 5 * K, 4 * K; shadow = (2 * K, 3 * K)
    art = Image.new('RGBA', (S, S)); d = ImageDraw.Draw(art)
    pad = cream_w + edge_w + 2 * K; c = (S - shadow[0]) / 2; cy = (S - shadow[1]) / 2
    if kind == 'warn':
        rr = 10 * K   # rounded triangle = small triangle dilated by rr
        top, bot, half = pad + rr, S - shadow[1] - pad - rr, (S - shadow[0] - 2 * pad - 2 * rr) / 2
        tri = Image.new('L', (S, S), 0); ImageDraw.Draw(tri).polygon([(c, top), (c + half, bot), (c - half, bot)], fill=255)
        kk = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * rr + 1, 2 * rr + 1))
        m_out = cv2.dilate(np.array(tri), kk); m_in = cv2.erode(m_out, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * inkw + 1, 2 * inkw + 1)))
        yy = np.arange(S)[:, None] * np.ones((1, S))
        m_shade = np.where(yy > bot + rr - inkw - 9 * K, m_in, 0).astype(np.uint8)
        art = comp((S, S), [(m_out, INK), (m_in, RED), (m_shade, RED_D)]); d = ImageDraw.Draw(art)
        d.ellipse((c - 13 * K, top + 2 * K, c - 4 * K, top + 9 * K), fill=RED_L)                      # gloss
        ot, ob = top - rr, bot + rr; hh = ob - ot                                                     # '!' on the outer triangle height
        d.rounded_rectangle((c - 5.5 * K, ot + hh * .30, c + 5.5 * K, ot + hh * .66), 5 * K, fill=CREAM)
        d.ellipse((c - 6 * K, ot + hh * .79 - 6 * K, c + 6 * K, ot + hh * .79 + 6 * K), fill=CREAM)
    else:
        r = (S - shadow[0] - 2 * pad) / 2
        d.ellipse((c - r, cy - r, c + r, cy + r), fill=INK)
        ri = r - inkw; d.ellipse((c - ri, cy - ri, c + ri, cy + ri), fill=GOLD)
        d.chord((c - ri, cy - ri, c + ri, cy + ri), 25, 155, fill=GOLD_D)
        rj = ri - 5 * K; d.ellipse((c - rj, cy - rj, c + rj, cy + rj * .62), fill=GOLD)
        d.ellipse((c - ri * .62, cy - ri * .74, c - ri * .2, cy - ri * .46), fill=GOLD_L)                # gloss
        d.ellipse((c - 5.5 * K, cy - 19 * K, c + 5.5 * K, cy - 8 * K), fill=INK)                       # 'i' (navy for contrast on gold)
        d.rounded_rectangle((c - 5 * K, cy - 4 * K, c + 5 * K, cy + 19 * K), 4 * K, fill=INK)
    return finish_icon(art, S, cream_w, edge_w, shadow).resize((OUT, OUT), Image.LANCZOS)

if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else '.'; os.makedirs(out, exist_ok=True)
    toast().save(os.path.join(out, 'ui9_toast.png'), optimize=True)
    icon('warn').save(os.path.join(out, 'ui_icon_warn.png'), optimize=True)
    icon('info').save(os.path.join(out, 'ui_icon_info.png'), optimize=True)
    print('ok')
