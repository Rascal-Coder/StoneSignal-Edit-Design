"""v17.6 preview: new toast (ui9_toast + ui_icon_warn / ui_icon_info, ToastStyle / ToastFx spec) on the real v18 notice captures,
both icon variants, the motion curve, and ExtraDraw's new icon on the 精良 reward card next to NextDraw.
Usage: python toast_extradraw_preview_v17_6.py <ui_dir with the new pngs> <font ttf> <captures root> <out png>"""
import sys, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont

UI, TTF, CAP, OUT = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
CJK = '/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc'
LF = lambda s: ImageFont.truetype(CJK, s, index=2)
GF = lambda s: ImageFont.truetype(TTF, s)
BORDER = (40, 41, 43, 35)            # L, B, R, T (= StylizedFxV14 pin)
PAD, ICON, GAP, SHX, SHY, PILL_H = 28, 44, 10, 3, 6, 72
INFO = {"建造完成", "已放置，路线已更新", "没有墙牌了，开始下一波吧"}

def nine(sp, w, h):
    L, B, R, T = BORDER; sw, sh = sp.size
    out = Image.new('RGBA', (w, h))
    xs = [(0, L, 0, L), (L, sw - R, L, w - R), (sw - R, sw, w - R, w)]
    ys = [(0, T, 0, T), (T, sh - B, T, h - B), (sh - B, sh, h - B, h)]
    for sx0, sx1, dx0, dx1 in xs:
        for sy0, sy1, dy0, dy1 in ys:
            if dx1 <= dx0 or dy1 <= dy0 or sx1 <= sx0 or sy1 <= sy0: continue
            out.alpha_composite(sp.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.LANCZOS), (dx0, dy0))
    return out

def toast(text, kind='auto', k=1.0):
    """Toast at 1920x1080 ref px x canvas scale k (rect = text + padding, 78 high = 72 pill + 6 shadow)."""
    if kind == 'auto': kind = 'info' if text in INFO else 'warn'
    f = GF(round(30 * k)); tw = f.getbbox(text)[2] - f.getbbox(text)[0]
    icon = kind in ('warn', 'info')
    w = int(round(min(900 * k, max(200 * k, tw + (PAD * 2 + SHX + (ICON + GAP if icon else 0)) * k)))); h = int(round((PILL_H + SHY) * k))
    sp = Image.open(UI + '/ui9_toast.png').convert('RGBA')
    if k != 1: sp = sp.resize((round(sp.width * k), round(sp.height * k)), Image.LANCZOS)
    global BORDER
    b0 = BORDER; BORDER = tuple(int(round(v * k)) for v in b0); im = nine(sp, w, h); BORDER = b0
    d = ImageDraw.Draw(im); x0 = PAD * k
    if icon:
        ic = Image.open(UI + f'/ui_icon_{kind}.png').convert('RGBA').resize((round(ICON * k), round(ICON * k)), Image.LANCZOS)
        im.alpha_composite(ic, (round(PAD * k), round((PILL_H - ICON) / 2 * k))); x0 += (ICON + GAP) * k
    x1 = w - (PAD + SHX) * k; cy = PILL_H * k / 2
    d.text(((x0 + x1) / 2, cy + 1 * k), text, font=f, fill=(255, 255, 255), anchor='mm', stroke_width=max(1, round(2.2 * k)), stroke_fill=(30, 26, 58))
    return im

def motion(t, pop=.18, hold=1.2, out=.2, rise=12):
    s, a, y = 1., 1., 0.
    if t < pop:
        u = t / pop
        s = .85 + (1.05 - .85) * (1 - (1 - u / .6) ** 2) if u < .6 else 1.05 + (1 - 1.05) * (lambda q: q * q * (3 - 2 * q))((u - .6) / .4)
        a = 1 - (1 - u) ** 2
    elif t > pop + hold:
        u = min(1, (t - pop - hold) / out); a = 1 - u; y = rise * (1 - (1 - u) ** 2)
    return s, a, y

def place(bg, im, cx, cy, s=1., a=1., y=0.):
    """centre the PILL (not the shadow) on cx, cy; scale about the rect centre; alpha; rise y px."""
    if s != 1: im = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.LANCZOS)
    if a < 1: im = im.copy(); im.putalpha(im.getchannel('A').point(lambda v: int(v * a)))
    bg.alpha_composite(im, (round(cx - im.width / 2 + SHX / 2 * s), round(cy - im.height / 2 + SHY / 2 * s - y)))

def clean(res):
    n = Image.open(f'{CAP}/cn_ui_v2/notice_{res}.png').convert('RGBA'); c = Image.open(f'{CAP}/cn_ui_v2/drawpile_used_{res}.png').convert('RGBA')
    box = {'1920x1080': (832, 472, 1088, 562), '2400x1080': (1072, 472, 1328, 562), '2048x1536': (888, 897, 1160, 1010)}[res]
    n.paste(c.crop(box), box[:2]); return n, box

def card_crop(shot, idx, icon=None):
    CX = [304, 760, 1216]; CY = 299; CW, CH = 400, 560
    im = Image.open(shot).convert('RGBA'); x0 = CX[idx]
    crop = im.crop((x0 - 20, CY - 40, x0 + CW + 20, CY + CH + 20)); ox, oy = 20, 40
    if icon:
        fill = crop.getpixel((ox + 40, oy + 200)); d = ImageDraw.Draw(crop)
        d.rectangle((ox + 100, oy + 62, ox + 300, oy + 262), fill=fill)
        ic = Image.open(icon).convert('RGBA').resize((180, 180), Image.LANCZOS); crop.alpha_composite(ic, (ox + 110, oy + 70))
    return crop

if __name__ == '__main__':
    W = 2400; out = Image.new('RGBA', (W, 3000), (24, 22, 44, 255)); D = ImageDraw.Draw(out)
    D.text((40, 24), 'StoneSignal v17.6 — toast notice (ui9_toast + ui_icon_warn / ui_icon_info) and ExtraDraw reward icon', font=LF(40), fill=(255, 236, 190))
    D.text((40, 82), 'Toast: navy #2A2F5A, ink stroke, cream outline, navy edge, baked hard shadow · 72 px pill @1920×1080 (+6 px shadow) · padding 28 · icon 44 · CN Heavy 30 px white, outline #1E1A3A .25', font=LF(21), fill=(200, 195, 230))
    D.text((40, 110), 'Motion: pop 0.85→1.05→1 + fade 0.18 s, hold 1.2 s, fade-out 0.2 s + rise 12 px · one toast at a time, a repeat restarts the timer · ExtraDraw: draw-pile back + fanned 2nd card + gold +1, no ad badge', font=LF(21), fill=(200, 195, 230))
    y = 156
    # 1) before / after on the real notice capture (1:1 crop), plus the full frame
    n = Image.open(f'{CAP}/cn_ui_v2/notice_1920x1080.png').convert('RGBA'); cl, box = clean('1920x1080')
    after = cl.copy(); place(after, toast('这里不能放'), 960, 522)
    cb = (560, 330, 1360, 700)
    D.text((40, y), 'notice_1920x1080 — before (dev pill: ui9_panel_navy, 32 px) | after (v17.6 toast, warn) — 1:1 crop', font=LF(24), fill=(255, 236, 190)); y += 40
    out.alpha_composite(n.crop(cb), (40, y)); out.alpha_composite(after.crop(cb), (40 + 800 + 30, y))
    sm = after.resize((690, 388), Image.LANCZOS); out.alpha_composite(sm, (40 + 1660, y))
    D.text((1700, y + 392), 'full frame (after), 690 px wide', font=LF(18), fill=(170, 160, 210)); y += 370 + 50
    # 2) variants on the board
    D.text((40, y), 'Variants (warn = invalid action, info = neutral; ToastStyle.KindFor picks from the text) — 1:1 on the board:', font=LF(24), fill=(255, 236, 190)); y += 40
    strip = cl.crop((420, 360, 1500, 700)).resize((1080 * 2 + 160, 340 * 2), Image.LANCZOS).crop((0, 0, 2320, 420))
    out.alpha_composite(cl.crop((300, 560, 2400 - 100, 560 + 300)) if False else strip, (40, y))
    items = [('这里不能放', 'auto'), ('金币不足', 'auto'), ('不能堵死通往核心的路线', 'auto'), ('建造完成', 'auto'), ('已放置，路线已更新', 'auto'), ('先选一座塔', 'none')]
    xs = [40 + 220, 40 + 760, 40 + 1440, 40 + 260, 40 + 860, 40 + 1500]; ys = [y + 110, y + 110, y + 110, y + 290, y + 290, y + 290]
    for (t, k), cx, cy in zip(items, xs, ys):
        im = toast(t, k); place(out, im, cx + 100, cy)
        D.text((cx + 100 - 120, cy + 46), ('warn' if (k == 'auto' and t not in INFO) else 'info' if k == 'auto' else 'no icon (optional)') + f' · {im.width}×{im.height}', font=LF(17), fill=(255, 255, 255), stroke_width=2, stroke_fill=(24, 22, 44))
    y += 420 + 40
    # 3) motion
    D.text((40, y), 'Motion (ToastFx, unscaled time): frames at t = 0, .04, .08, .12, .18, 0.80, 1.40, 1.45, 1.50, 1.55 s (pop-in, hold, fade-out + rise)', font=LF(24), fill=(255, 236, 190)); y += 40
    times = [0, .04, .08, .12, .18, .80, 1.40, 1.45, 1.50, 1.55]
    tile = cl.crop((760, 440, 1160, 600))
    for i, t in enumerate(times):
        x = 40 + i * 232; bgt = tile.resize((224, 90 * 2), Image.LANCZOS).crop((0, 0, 224, 160)).copy()
        s, a, rise = motion(t); place(bgt, toast('这里不能放').resize((round(toast('这里不能放').width * .7), round(78 * .7)), Image.LANCZOS), 112, 92, s, a, rise * .7)
        out.alpha_composite(bgt, (x, y)); D.text((x + 6, y + 164), f't={t:.2f}  s={s:.2f}  a={a:.2f}  +{rise:.0f}px', font=LF(16), fill=(220, 215, 240))
    y += 200 + 30
    # 4) other aspects
    D.text((40, y), 'Other aspects (canvas scale: 2048×1536 = 1.07, 2400×1080 = 1.00):', font=LF(24), fill=(255, 236, 190)); y += 40
    c2, _ = clean('2048x1536'); place(c2, toast('这里不能放', k=1.07), 1024, 954)
    c3, _ = clean('2400x1080'); place(c3, toast('这里不能放'), 1200, 522)
    out.alpha_composite(c2.resize((600, 450), Image.LANCZOS), (40, y)); out.alpha_composite(c2.crop((724, 800, 1324, 1110)), (660, y))
    out.alpha_composite(c3.resize((1000, 450), Image.LANCZOS), (1300, y)); y += 450 + 50
    # 5) reward cards
    D.text((40, y), 'ExtraDraw 免广告再抽 (精良) — new ui_reward_extra_draw vs the current placeholder, next to NextDraw / AddBlock (real v18 cards, 1:1):', font=LF(24), fill=(255, 236, 190)); y += 40
    shot3 = f'{CAP}/cn_ui_v3/reward_extradraw_1920x1080.png'; shot2 = f'{CAP}/cn_ui_v2/reward_addblock_nextdraw_1920x1080.png'
    cards = [(card_crop(shot3, 1), ['ExtraDraw 精良 — current', '(ui_draw_pile placeholder)']), (card_crop(shot3, 1, UI + '/ui_reward_extra_draw.png'), ['ExtraDraw 精良 — v17.6', 'ui_reward_extra_draw']),
             (card_crop(shot2, 1), ['NextDraw (符文保底)', 'ui_reward_next_draw']), (card_crop(shot2, 0), ['AddBlock (墙牌补给)', 'ui_reward_add_block'])]
    x = 40
    for c, ls in cards:
        out.alpha_composite(c, (x, y))
        for j, l in enumerate(ls): D.text((x + 6, y + c.height + 6 + j * 26), l, font=LF(20), fill=(220, 215, 240))
        x += c.width + 30
    px = x + 20; D.text((px, y), 'at ~phone size (96 px):', font=LF(20), fill=(200, 195, 230))
    for i, (n_, fill) in enumerate([('extra_draw', (52, 116, 216)), ('next_draw', (52, 116, 216)), ('extra_draw', (150, 146, 164)), ('next_draw', (150, 146, 164))]):
        p = UI + f'/ui_reward_{n_}.png'
        bg = Image.new('RGBA', (104, 104), fill + (255,)); bg.alpha_composite(Image.open(p).convert('RGBA').resize((96, 96), Image.LANCZOS), (4, 4)); out.alpha_composite(bg, (px + (i % 2) * 120, y + 40 + (i // 2) * 140))
        D.text((px + (i % 2) * 120, y + 148 + (i // 2) * 140), n_, font=LF(14), fill=(170, 160, 210))
    y += 620 + 70
    out = out.crop((0, 0, W, y)); out.convert('RGB').save(OUT); print(out.size)
