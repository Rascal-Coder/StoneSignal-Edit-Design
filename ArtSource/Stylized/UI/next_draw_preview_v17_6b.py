"""v17.6b preview: ui_reward_next_draw (符文保底) old vs new on the real reward card, plus 96 px comparison with ExtraDraw / AddBlock.
Usage: python next_draw_preview_v17_6b.py <new_ui_dir> <old_ui_dir> <capture_root> <font.ttf> <out.png>"""
import sys
from PIL import Image, ImageDraw, ImageFont
NEW, OLD, CAP, FONT, OUT = [a.rstrip('/') for a in sys.argv[1:6]]
F = lambda s: ImageFont.truetype(FONT, s)

def card_crop(shot, idx, icon=None):
    CX = [304, 760, 1216]; CY = 299; CW, CH = 400, 560
    im = Image.open(shot).convert('RGBA'); x0 = CX[idx]
    crop = im.crop((x0 - 20, CY - 40, x0 + CW + 20, CY + CH + 20)); ox, oy = 20, 40
    if icon:
        fill = crop.getpixel((ox + 40, oy + 200)); d = ImageDraw.Draw(crop)
        d.rectangle((ox + 100, oy + 62, ox + 300, oy + 262), fill=fill)
        ic = Image.open(icon).convert('RGBA').resize((180, 180), Image.LANCZOS); crop.alpha_composite(ic, (ox + 110, oy + 70))
    return crop

shot = f'{CAP}/cn_ui_v2/reward_addblock_nextdraw_1920x1080.png'
out = Image.new('RGBA', (1500, 1080), (24, 22, 44, 255)); D = ImageDraw.Draw(out)
D.text((30, 20), 'StoneSignal v17.6b — NextDraw 「符文保底」 icon (下次抽牌至少 1 张带符文)', font=F(34), fill=(255, 236, 190))
D.text((30, 66), 'real v18 reward card @1920×1080, 1:1 · old: deck + up arrow (old “draw +N”) → new: blueprint wall card + neutral rune socket + gold shield check', font=F(19), fill=(200, 195, 230))
y = 110
for i, (lab, ic) in enumerate([('before (v17.4)', f'{OLD}/ui_reward_next_draw.png'), ('after (v17.6b)', f'{NEW}/ui_reward_next_draw.png')]):
    D.text((30 + i * 470, y), lab, font=F(24), fill=(255, 236, 190))
    out.alpha_composite(card_crop(shot, 1, ic), (30 + i * 470, y + 36))
x = 30 + 2 * 470 + 20
D.text((x, y), 'same frame: 墙牌补给 (card 0)', font=F(20), fill=(170, 160, 210))
out.alpha_composite(card_crop(shot, 0).resize((330, 462), Image.LANCZOS), (x, y + 36))
y = 110 + 36 + 620 + 20
D.text((30, y), '96 px side by side (rarity fills 普通 / 精良 / 稀有 / 传说 + dark):', font=F(24), fill=(255, 236, 190)); y += 40
row = [('符文保底 new', f'{NEW}/ui_reward_next_draw.png'), ('免广告再抽 ExtraDraw', f'{NEW}/ui_reward_extra_draw.png'), ('墙牌补给 AddBlock', f'{NEW}/ui_reward_add_block.png'), ('符文保底 old', f'{OLD}/ui_reward_next_draw.png')]
fills = [(150, 146, 164), (52, 116, 216), (150, 80, 210), (230, 150, 40), (30, 28, 52)]
for c, (lab, ic) in enumerate(row):
    cx = 30 + c * 365
    D.text((cx, y), lab, font=F(20), fill=(220, 215, 240))
    im = Image.open(ic).convert('RGBA').resize((96, 96), Image.LANCZOS)
    for k, f in enumerate(fills[:3]):
        t = Image.new('RGBA', (108, 108), f + (255,)); t.alpha_composite(im, (6, 6)); out.alpha_composite(t, (cx + k * 112, y + 30))
    for k, f in enumerate(fills[3:]):
        t = Image.new('RGBA', (108, 108), f + (255,)); t.alpha_composite(im, (6, 6)); out.alpha_composite(t, (cx + k * 112, y + 144))
out.convert('RGB').save(OUT)
print('wrote', OUT)
