"""v17.4 reward-card icons (RewardPickUI, 400x560 card, icon slot 180 ref px; must read at 96 px).
Chunky toon HUD language (same as ui9_reward_frame / ui_card_tower_frame / ui_coin_gold):
  flat palette fills + one toon shade + one gloss, navy ink strokes inside, cream #FFFAF4 outline around the whole
  silhouette, navy #1E1A3A edge outside that, baked hard shadow (navy 50 %, down-right).
Painted at 1024 px (4x) and LANCZOS-downsampled to 256 px -> Assets/Game/Art/Stylized/UI/ui_reward_<id>.png.
Colour rules: cyan only for frost/slow (path_slow), green only for heal/core repair (base_hp badge, wave_heal).
Usage: python reward_icons_v17_4.py <out_dir>"""
import math, sys, os
import numpy as np, cv2
from PIL import Image, ImageDraw

S = 1024; OUT = 256
INK = (30, 26, 58, 255); CREAM = (255, 250, 244, 255)
W = 22                          # inner ink stroke @1024 (5.5 px @256)
CREAM_W, EDGE_W = 26, 18        # cream outline / navy edge @1024 (6.5 / 4.5 px @256)
SHADOW = (14, 22)               # hard shadow offset @1024
def C(h, a=255): h = h.lstrip('#'); return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)
GOLD, GOLD_D, GOLD_L = C('FABE2C'), C('D98A12'), C('FFE58A')
ORANGE, ORANGE_D = C('FF9628'), C('E0641E')
RED, RED_D, RED_L = C('E2463A'), C('A8282E'), C('FF8A6E')
YEL, YEL_L = C('FFD83A'), C('FFF4B0')
STEEL, STEEL_D, STEEL_L = C('D8DCEA'), C('9AA0BE'), C('FFFFFF')
STONE, STONE_D, STONE_L = C('B4B0C4'), C('7E7896'), C('DCD8E8')
PURP, PURP_D, PURP_L = C('8848D4'), C('4E2A8A'), C('B888F0')
NAVYC = C('3A2E6E')
BLUE, BLUE_D, BLUE_L = C('2E8CE6'), C('1A56B0'), C('9AD2FF')
CYAN, CYAN_D, CYAN_L = C('5FD0FF'), C('2A92D0'), C('D8F6FF')
GREEN, GREEN_D, GREEN_L = C('7CE86A'), C('3AA84A'), C('C8FFB8')
TAN, TAN_D, TAN_L = C('E9C9A0'), C('C09068'), C('FFF0DA')
BROWN, BROWN_D = C('9A5C34'), C('6A3A20')

def rot(pts, ang, cx=0, cy=0, ox=0, oy=0):
    a = math.radians(ang); ca, sa = math.cos(a), math.sin(a)
    return [(ox + (x - cx) * ca - (y - cy) * sa, oy + (x - cx) * sa + (y - cy) * ca) for x, y in pts]
def P(d, pts, fill, w=W): d.polygon([tuple(p) for p in pts], fill=fill, outline=INK, width=w)
def E(d, box, fill, w=W): d.ellipse(box, fill=fill, outline=INK, width=w)
def RR(d, box, r, fill, w=W): d.rounded_rectangle(box, r, fill=fill, outline=INK, width=w)
def L(d, pts, w=W, fill=INK): d.line(pts, fill=fill, width=w, joint='curve')
def star(cx, cy, ro, ri, n, a0=-90):
    return [(cx + (ro if i % 2 == 0 else ri) * math.cos(math.radians(a0 + 180 * i / n)),
             cy + (ro if i % 2 == 0 else ri) * math.sin(math.radians(a0 + 180 * i / n))) for i in range(2 * n)]
def arc_poly(cx, cy, rx, ry, a0, a1, n=48):
    return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]
def gloss(d, box, col=(255, 255, 255, 200)): d.ellipse(box, fill=col)
def sparkle(d, x, y, s, col=CREAM):
    d.polygon([(x, y - s), (x + s * .26, y - s * .26), (x + s, y), (x + s * .26, y + s * .26), (x, y + s), (x - s * .26, y + s * .26), (x - s, y), (x - s * .26, y - s * .26)], fill=col, outline=INK, width=10)

# ---------------- reusable pieces
def coin_face(d, cx, cy, r, emblem=True):
    E(d, (cx - r, cy - r, cx + r, cy + r), GOLD)
    d.ellipse((cx - r * .74, cy - r * .74, cx + r * .74, cy + r * .74), outline=GOLD_D, width=int(r * .09))
    if emblem:   # flame emblem like ui_coin_gold
        f = [(cx, cy - r * .48), (cx + r * .26, cy - r * .02), (cx + r * .22, cy + r * .3), (cx, cy + r * .44), (cx - r * .22, cy + r * .3), (cx - r * .26, cy - r * .02)]
        d.polygon(f, fill=GOLD_D)
    gloss(d, (cx - r * .62, cy - r * .7, cx - r * .22, cy - r * .42), GOLD_L)
def coin_stack(d, cx, base_y, rx, ry, n, th, top_emblem=True):
    """n stacked coins seen from slightly above; base_y = bottom edge of the lowest rim."""
    for i in range(n):
        yb = base_y - i * th               # bottom of this coin's rim
        yt = yb - th
        # rim (side band): left edge down, lower half-ellipse, right edge up
        rim = [(cx - rx, yt), (cx - rx, yt + th)] + [(cx + rx * math.cos(math.radians(180 - 180 * k / 40)), yt + th + ry * math.sin(math.radians(180 - 180 * k / 40))) for k in range(41)] + [(cx + rx, yt)]
        P(d, rim, GOLD_D)
        for k in range(1, 6):              # reeding marks
            x = cx - rx + 2 * rx * k / 6; yy = yt + th * .5 + ry * math.sin(math.acos(max(-1, min(1, (x - cx) / rx))))
            d.line([(x, yy - th * .28), (x, yy + th * .28)], fill=(150, 80, 10, 255), width=8)
        top = (cx - rx, yt - ry, cx + rx, yt + ry)
        E(d, top, GOLD)
        d.ellipse((cx - rx * .72, yt - ry * .72, cx + rx * .72, yt + ry * .72), outline=GOLD_D, width=12)
        if i == n - 1:
            if top_emblem:
                d.polygon([(cx, yt - ry * .5), (cx + rx * .2, yt), (cx, yt + ry * .5), (cx - rx * .2, yt)], fill=GOLD_D)
            gloss(d, (cx - rx * .62, yt - ry * .7, cx - rx * .2, yt - ry * .35), GOLD_L)
    return base_y - n * th - ry     # top y of the stack
def crystal(d, cx, cy, h, w, col=BLUE, dark=BLUE_D, light=BLUE_L):
    top, bot = cy - h * .5, cy + h * .5; mid = cy - h * .12
    outer = [(cx, top), (cx + w * .5, mid), (cx, bot), (cx - w * .5, mid)]
    P(d, outer, col)
    d.polygon([(cx, top + W), (cx + w * .5 - W, mid), (cx, mid + h * .08)], fill=light)
    d.polygon([(cx, mid + h * .08), (cx + w * .5 - W * .8, mid + 4), (cx, bot - W)], fill=dark)
    L(d, [(cx - w * .5, mid), (cx, mid + h * .08), (cx + w * .5, mid)], w=12); L(d, [(cx, mid + h * .08), (cx, bot)], w=12)
    gloss(d, (cx - w * .26, mid - h * .2, cx - w * .14, mid - h * .08), (255, 255, 255, 230))
def arrowhead(d, x, y, ang, s, fill):
    P(d, rot([(s, 0), (-s * .6, -s * .75), (-s * .3, 0), (-s * .6, s * .75)], ang, 0, 0, x, y), fill, w=16)
def plus_badge(d, cx, cy, r, fill, cross=CREAM):
    E(d, (cx - r, cy - r, cx + r, cy + r), fill)
    a, b = r * .55, r * .18
    P(d, [(cx - b, cy - a), (cx + b, cy - a), (cx + b, cy - b), (cx + a, cy - b), (cx + a, cy + b), (cx + b, cy + b), (cx + b, cy + a), (cx - b, cy + a), (cx - b, cy + b), (cx - a, cy + b), (cx - a, cy - b), (cx - b, cy - b)], cross, w=12)
def card(d, box, fill, r=40, emblem=None):
    RR(d, box, r, fill)
    x0, y0, x1, y1 = box
    d.rounded_rectangle((x0 + 30, y0 + 30, x1 - 30, y1 - 30), max(8, r - 22), outline=GOLD, width=12)
    if emblem == 'flame':
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2; rr = (x1 - x0) * .2
        d.ellipse((cx - rr, cy - rr, cx + rr, cy + rr), outline=GOLD, width=14)
        P(d, [(cx, cy - rr * .8), (cx + rr * .5, cy), (cx + rr * .35, cy + rr * .55), (cx, cy + rr * .7), (cx - rr * .35, cy + rr * .55), (cx - rr * .5, cy)], ORANGE, w=10)
def tile(d, x, y, s, fill=STONE, dark=STONE_D, light=STONE_L):
    RR(d, (x, y, x + s, y + s), s * .16, fill)
    d.rounded_rectangle((x + W, y + s * .64, x + s - W, y + s - W), s * .1, fill=dark)
    d.rectangle((x + s * .18, y + W + 6, x + s * .5, y + W + 22), fill=light)
def needle_tower(d, cx, base, h, body=TAN, roof=ORANGE, roof_d=ORANGE_D):
    """Needle (针弩) tower: cream stone drum + orange cap + steel needle spire."""
    w = h * .56
    RR(d, (cx - w * .62, base - h * .2, cx + w * .62, base), 30, STONE)                    # plinth
    d.rounded_rectangle((cx - w * .62 + W, base - max(h * .09, W + 10), cx + w * .62 - W, base - W), 14, fill=STONE_D)
    P(d, [(cx - w * .5, base - h * .2), (cx - w * .4, base - h * .62), (cx + w * .4, base - h * .62), (cx + w * .5, base - h * .2)], body)
    d.polygon([(cx + w * .12, base - h * .2 - W / 2), (cx + w * .2, base - h * .62 + W / 2), (cx + w * .4 - W, base - h * .62 + W / 2), (cx + w * .5 - W, base - h * .2 - W / 2)], fill=TAN_D)
    RR(d, (cx - w * .56, base - h * .74, cx + w * .56, base - h * .6), 18, roof)
    P(d, [(cx - w * .2, base - h * .74), (cx, base - h * 1.0), (cx + w * .2, base - h * .74)], STEEL)
    d.polygon([(cx + 4, base - h * .94), (cx + w * .15, base - h * .76), (cx + 4, base - h * .76)], fill=STEEL_D)
    RR(d, (cx - w * .08, base - h * .52, cx + w * .08, base - h * .32), 12, INK, w=4)          # arrow slit

# ---------------- icons
def all_damage(d):
    P(d, star(512, 500, 440, 300, 10, -90), ORANGE)
    d.polygon(star(512, 500, 300, 200, 10, -72), fill=YEL)
    # sword pointing up-right
    o = (520, 520); a = 45
    blade = [(-58, 150), (-58, -300), (0, -400), (58, -300), (58, 150)]
    P(d, rot(blade, a, 0, 0, *o), STEEL)
    d.polygon(rot([(0, -360), (40, -290), (40, 150 - W), (0, 150 - W)], a, 0, 0, *o), fill=STEEL_D)
    L(d, rot([(0, -350), (0, 120)], a, 0, 0, *o), w=10)
    P(d, rot([(-34, 150), (34, 150), (34, 300), (-34, 300)], a, 0, 0, *o), BROWN)
    P(d, rot([(-170, 120), (170, 120), (170, 190), (-170, 190)], a, 0, 0, *o), GOLD)
    cx, cy = rot([(0, 345)], a, 0, 0, *o)[0]; E(d, (cx - 56, cy - 56, cx + 56, cy + 56), GOLD)
    gloss(d, (cx - 34, cy - 36, cx - 6, cy - 10), GOLD_L)
    sparkle(d, 230, 240, 54); sparkle(d, 810, 760, 40)
def bolt_shape(cx, cy, s):
    return [(cx + x * s, cy + y * s) for x, y in [(.12, -1), (-.52, .12), (-.06, .12), (-.2, 1), (.52, -.18), (.06, -.18), (.3, -1)]]
def arrow(d, x0, y0, x1, y1, col=ORANGE, head=RED, w=44):
    ang = math.degrees(math.atan2(y1 - y0, x1 - x0))
    L(d, [(x0, y0), (x1, y1)], w=w + 2 * W); L(d, [(x0, y0), (x1, y1)], w=w, fill=col)
    arrowhead(d, x1, y1, ang, 92, head)
    for k in (0, 1):   # fletching
        fx, fy = x0 + (x1 - x0) * .02 * k, y0 + (y1 - y0) * .02 * k
        P(d, rot([(0, 0), (-70, -60), (40, -60), (110, 0)], ang, 0, 0, fx + 40 * math.cos(math.radians(ang)) * k, fy + 40 * math.sin(math.radians(ang)) * k), CREAM, w=14)
        P(d, rot([(0, 0), (-70, 60), (40, 60), (110, 0)], ang, 0, 0, fx + 40 * math.cos(math.radians(ang)) * k, fy + 40 * math.sin(math.radians(ang)) * k), CREAM, w=14)
        break
def all_attack_speed(d):
    arrow(d, 150, 380, 740, 220)
    arrow(d, 260, 800, 860, 640)
    P(d, bolt_shape(520, 500, 400), YEL)
    d.polygon(bolt_shape(536, 500, 300)[:3] + [(536 + .06 * 300, 500 + .12 * 300)], fill=YEL_L)
    sparkle(d, 860, 360, 46)
def ring(d, cx, cy, rx, ry, col, dark, w=58):
    E(d, (cx - rx - w / 2 - W / 2, cy - ry - w / 2 - W / 2, cx + rx + w / 2 + W / 2, cy + ry + w / 2 + W / 2), col)
    E(d, (cx - rx + w / 2 + W / 2, cy - ry + w / 2 + W / 2, cx + rx - w / 2 - W / 2, cy + ry - w / 2 - W / 2), (0, 0, 0, 0))
def ring_back_front(d, cx, cy, rx, ry, col, w, draw_front):
    """Thick elliptical band; back half first (behind the tower), then front half."""
    a0, a1 = (0, 180) if draw_front else (180, 360)
    outer = arc_poly(cx, cy, rx + w / 2, ry + w / 2, a0, a1); inner = arc_poly(cx, cy, rx - w / 2, ry - w / 2, a1, a0)
    P(d, outer + inner, col)
def all_range(d):
    cx, cy, rx, ry, w = 512, 700, 330, 140, 62
    ring_back_front(d, cx, cy, rx, ry, GOLD, w, False)
    needle_tower(d, cx, 770, 640)
    ring_back_front(d, cx, cy, rx, ry, GOLD, w, True)
    for ang in (180, 0):   # outward arrows on the ring (all towers: range grows every way)
        x = cx + (rx + w / 2 + 62) * math.cos(math.radians(ang)); arrowhead(d, x, cy, ang, 80, ORANGE)
    arrowhead(d, cx, cy + ry + w / 2 + 60, 90, 72, ORANGE)
def arrow_range(d):
    cx, cy, rx, ry, w = 420, 730, 290, 120, 54
    ring_back_front(d, cx, cy, rx, ry, ORANGE, w, False)
    needle_tower(d, cx, 790, 600)
    ring_back_front(d, cx, cy, rx, ry, ORANGE, w, True)
    # long needle bolt flying out past the ring (to the right/up)
    x0, y0, x1, y1 = 560, 330, 900, 210
    L(d, [(x0 - 120, y0 + 42), (x0 - 10, y0 + 4)], w=16, fill=CREAM)
    L(d, [(x0, y0), (x1, y1)], w=40 + 2 * W); L(d, [(x0, y0), (x1, y1)], w=40, fill=STEEL)
    arrowhead(d, x1 + 20, y1 - 7, math.degrees(math.atan2(y1 - y0, x1 - x0)), 84, STEEL)
    arrowhead(d, cx + rx + w / 2 + 70, cy, 0, 74, ORANGE)
def cannon_radius(d):
    cx, cy, rx, ry, w = 512, 650, 340, 150, 56
    ring_back_front(d, cx, cy, rx, ry, RED, w, False)
    P(d, star(cx, 500, 330, 220, 9, -90), ORANGE)
    d.polygon(star(cx, 500, 220, 140, 9, -70), fill=YEL)
    E(d, (cx - 150, 430, cx + 150, 730), C('4A4266'))              # Seismic shell
    gloss(d, (cx - 90, 475, cx - 24, 540), (190, 184, 220, 255))
    ring_back_front(d, cx, cy, rx, ry, RED, w, True)
    for ang in (180, 0):
        x = cx + (rx + w / 2 + 64) * math.cos(math.radians(ang)); y = cy + (ry + w / 2 + 64) * math.sin(math.radians(ang))
        arrowhead(d, x, y, ang, 72, ORANGE)
    for (x, y, s) in [(250, 260, 50), (780, 250, 44)]:   # rock chips
        P(d, [(x - s, y), (x - s * .2, y - s), (x + s, y - s * .3), (x + s * .5, y + s * .8)], STONE, w=14)
def add_block(d):
    card(d, (230, 130, 760, 880), NAVYC, 56)
    s = 170; x0, y0 = 495 - s, 505 - s
    for i in range(2):
        for j in range(2): tile(d, x0 + i * s, y0 + j * s, s)
    plus_badge(d, 770, 790, 120, GOLD)
def next_draw(d):
    card(d, (140, 260, 560, 860), PURP_D, 46)
    P(d, rot([(-210, -300), (210, -300), (210, 300), (-210, 300)], 10, 0, 0, 520, 520), PURP_D)
    card(d, (400, 140, 820, 740), NAVYC, 46, emblem='flame')
    # up-out arrow (draw one more)
    L(d, [(300, 860), (300, 640)], w=70 + 2 * W); L(d, [(300, 860), (300, 640)], w=70, fill=GOLD)
    arrowhead(d, 300, 560, -90, 120, GOLD)
    plus_badge(d, 800, 800, 110, GOLD)
def path_slow(d):
    # snail: tan body, ice shell (cyan allowed: frost/slow), ice shards
    body = [(140, 800), (180, 700), (300, 690), (720, 700), (860, 650), (900, 560), (950, 580), (950, 700), (880, 800)]
    P(d, body, TAN)
    d.polygon([(200, 790), (880, 790), (900, 760), (220, 760)], fill=TAN_D)
    for x, y in [(880, 470), (960, 500)]:      # eye stalks
        L(d, [(890, 600), (x, y)], w=26 + 2 * W); L(d, [(890, 600), (x, y)], w=26, fill=TAN)
        E(d, (x - 34, y - 34, x + 34, y + 34), CREAM, w=14); E(d, (x - 12, y - 14, x + 14, y + 12), INK, w=2)
    E(d, (220, 230, 760, 760), CYAN)
    d.pieslice((240, 250, 740, 740), 20, 200, fill=CYAN_D)
    E(d, (300, 310, 680, 690), CYAN)
    # spiral
    L(d, [(490 + r * math.cos(a), 495 + r * math.sin(a)) for a, r in [(i * .35, 210 - i * 9.5) for i in range(20)]], w=22)
    gloss(d, (320, 300, 420, 380), CYAN_L)
    for (x, y, s, a) in [(280, 200, 70, -20), (740, 220, 58, 25), (500, 170, 50, 0)]:   # ice shards
        P(d, rot([(0, -s), (s * .45, 0), (0, s * .6), (-s * .45, 0)], a, 0, 0, x, y), CYAN_L, w=14)
def bonus_slot(d):
    s = 190
    cells = [(0, 0), (0, 1), (0, 2), (1, 2)]   # L
    x0, y0 = 190, 170
    # glowing slot next to the L (the bonus tower slot)
    sx, sy = x0 + s, y0 + s
    for i, j in cells: tile(d, x0 + i * s, y0 + j * s, s)
    d.rounded_rectangle((sx - 6, sy - 6, sx + s + 6, sy + s + 6), 36, fill=GOLD, outline=INK, width=W)
    d.rounded_rectangle((sx + 26, sy + 26, sx + s - 26, sy + s - 26), 22, fill=GOLD_L)
    needle_tower(d, sx + s / 2, sy + s - 40, 230)          # the extra tower slot
    plus_badge(d, 770, 300, 130, GOLD)
def skull(d, cx, cy, r):
    E(d, (cx - r, cy - r, cx + r, cy + r * .8), CREAM)
    RR(d, (cx - r * .55, cy + r * .4, cx + r * .55, cy + r * 1.05), 18, CREAM)
    E(d, (cx - r * .58, cy - r * .25, cx - r * .14, cy + r * .25), INK, w=2); E(d, (cx + r * .14, cy - r * .25, cx + r * .58, cy + r * .25), INK, w=2)
    d.polygon([(cx, cy + r * .3), (cx - r * .12, cy + r * .5), (cx + r * .12, cy + r * .5)], fill=INK)
    for x in (-.2, 0, .2): d.line([(cx + x * r, cy + r * .72), (cx + x * r, cy + r * 1.0)], fill=INK, width=10)
def kill_gold(d):
    coin_stack(d, 430, 880, 270, 110, 4, 80)
    skull(d, 740, 330, 150)
    sparkle(d, 220, 300, 50)
def wave_gold(d):
    # red wave pennant planted in the stack (WAVE banner red)
    L(d, [(640, 650), (640, 140)], w=34 + 2 * W); L(d, [(640, 650), (640, 140)], w=34, fill=BROWN)
    P(d, [(658, 150), (930, 220), (850, 290), (930, 360), (658, 380)], RED)
    d.polygon([(674, 330), (860, 330), (920, 360 - W / 2), (674, 364)], fill=RED_D)
    E(d, (612, 100, 668, 156), GOLD, w=14)
    coin_stack(d, 430, 880, 270, 110, 4, 80)
def next_wave_gold(d):
    coin_stack(d, 400, 880, 260, 106, 3, 80)
    coin_face(d, 690, 330, 170)
    for k, x in enumerate((700, 820)):   # >> next
        P(d, [(x - 60, 560), (x + 30, 650), (x - 60, 740), (x - 20, 740 - 0), (x + 70, 650), (x - 20, 560)][:3] + [(x + 10, 740), (x + 100, 650), (x + 10, 560)], ORANGE, w=16)
def tower_discount(d):
    coin_face(d, 400, 610, 270)
    # cream price tag with a red % (string tied to the coin)
    L(d, [(520, 420), (610, 330)], w=14)
    o = (720, 360); a = -24
    tag = [(-150, -150), (-60, -240), (150, -240), (150, 240), (-150, 240)]
    P(d, rot(tag, a, 0, 0, *o), CREAM)
    hx, hy = rot([(-70, -150)], a, 0, 0, *o)[0]; E(d, (hx - 30, hy - 30, hx + 30, hy + 30), C('6A6488'), w=10)
    t = lambda pts: rot(pts, a, 0, 0, *o)
    L(d, t([(-80, 190), (80, -70)]), w=40, fill=RED)
    for px, py in [(-58, -40), (58, 150)]:
        cx_, cy_ = t([(px, py)])[0]; d.ellipse((cx_ - 48, cy_ - 48, cx_ + 48, cy_ + 48), outline=RED, width=30)
def shield(d, cx, cy, w, h, fill, rim):
    sm = [(cx - w / 2, cy - h * .36), (cx - w * .25, cy - h * .45), (cx, cy - h * .5), (cx + w * .25, cy - h * .45), (cx + w / 2, cy - h * .36),
          (cx + w / 2, cy + h * .02), (cx + w * .36, cy + h * .26), (cx, cy + h * .5), (cx - w * .36, cy + h * .26), (cx - w / 2, cy + h * .02)]
    P(d, sm, rim)
    inner = [(cx + (x - cx) * .74, cy + (y - cy) * .74 - 6) for x, y in sm]
    P(d, inner, fill, w=14)
    d.polygon([(cx, cy - h * .37), (cx, cy + h * .36), (cx + w * .26, cy + h * .18), (cx + w * .36, cy + h * .02), (cx + w * .36, cy - h * .27)], fill=BLUE_D)
def base_hp(d):
    crystal(d, 512, 380, 560, 380)                     # core crystal (blue = core, not cyan)
    shield(d, 512, 650, 480, 470, BLUE, GOLD)
    d.polygon([(512, 560), (560, 650), (512, 740), (464, 650)], fill=CREAM, outline=INK, width=12)   # small crystal mark on the shield
    plus_badge(d, 800, 780, 108, GREEN)                # core repair (+heal) -> green allowed
def wave_heal(d):
    crystal(d, 430, 500, 640, 440)
    # green heal cross + circular "every wave" arrow
    arc = arc_poly(470, 520, 380, 380, 200, 330, 40)
    L(d, arc, w=54 + 2 * W); L(d, arc, w=54, fill=GREEN)
    ex, ey = arc[-1]; arrowhead(d, ex + 14, ey + 26, 70, 84, GREEN)
    plus_badge(d, 730, 730, 180, GREEN)
    sparkle(d, 210, 220, 44, GREEN_L); sparkle(d, 820, 360, 36, GREEN_L)

ICONS = [('all_damage', all_damage), ('all_attack_speed', all_attack_speed), ('all_range', all_range), ('arrow_range', arrow_range),
         ('cannon_radius', cannon_radius), ('add_block', add_block), ('next_draw', next_draw), ('path_slow', path_slow),
         ('bonus_slot', bonus_slot), ('kill_gold', kill_gold), ('wave_gold', wave_gold), ('next_wave_gold', next_wave_gold),
         ('tower_discount', tower_discount), ('base_hp', base_hp), ('wave_heal', wave_heal)]

def finish(art):
    """art RGBA @S -> cream outline + navy edge + baked hard shadow, fitted into S (keeps a margin), then downsample."""
    a = np.array(art)[:, :, 3]
    k = lambda r: cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (2 * r + 1, 2 * r + 1))
    cream = cv2.dilate(a, k(CREAM_W)); edge = cv2.dilate(a, k(CREAM_W + EDGE_W))
    def layer(mask, col):
        im = np.zeros((S, S, 4), np.uint8); im[:, :, :3] = col[:3]; im[:, :, 3] = mask; return Image.fromarray(im)
    out = Image.new('RGBA', (S, S))
    sh = np.zeros_like(edge); dx, dy = SHADOW; sh[dy:, dx:] = edge[:S - dy, :S - dx]
    out.alpha_composite(layer((sh * .5).astype(np.uint8), INK))
    out.alpha_composite(layer(edge, INK)); out.alpha_composite(layer(cream, CREAM)); out.alpha_composite(art)
    return out

def render(name, fn):
    big = 1400; art = Image.new('RGBA', (big, big)); d = ImageDraw.Draw(art)
    off = (big - S) // 2
    # draw in 1024 space translated by off
    class T:
        def __init__(s, d): s.d = d
        def __getattr__(s, n):
            f = getattr(s.d, n)
            def g(*a, **kw):
                a = list(a)
                if a: a[0] = tr(a[0])
                return f(*a, **kw)
            return g
    def tr(v):
        if isinstance(v, (list, tuple)) and v and isinstance(v[0], (list, tuple)): return [(x + off, y + off) for x, y in v]
        if isinstance(v, (list, tuple)) and len(v) == 4 and all(isinstance(q, (int, float)) for q in v): return (v[0] + off, v[1] + off, v[2] + off, v[3] + off)
        if isinstance(v, (list, tuple)) and len(v) == 2 and all(isinstance(q, (int, float)) for q in v): return (v[0] + off, v[1] + off)
        return v
    fn(T(d))
    bb = art.getbbox()
    # fit: content + outline + edge + shadow must sit inside S with a 10 px (@1024) margin; centre the silhouette
    pad = CREAM_W + EDGE_W + 10
    cw, ch = bb[2] - bb[0], bb[3] - bb[1]
    sc = min(1.0, (S - 2 * pad - SHADOW[0]) / cw, (S - 2 * pad - SHADOW[1]) / ch)
    crop = art.crop(bb)
    if sc < 1: crop = crop.resize((int(cw * sc), int(ch * sc)), Image.LANCZOS)
    fit = Image.new('RGBA', (S, S)); fit.alpha_composite(crop, ((S - SHADOW[0] - crop.width) // 2, (S - SHADOW[1] - crop.height) // 2))
    return finish(fit).resize((OUT, OUT), Image.LANCZOS), sc

if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else '.'
    os.makedirs(out, exist_ok=True)
    for n, fn in ICONS:
        im, sc = render(n, fn); im.save(os.path.join(out, f'ui_reward_{n}.png'), optimize=True)
        print(n, 'fit scale %.2f' % sc)
