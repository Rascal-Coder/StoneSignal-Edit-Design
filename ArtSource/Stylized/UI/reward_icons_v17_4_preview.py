"""Preview: 3 real v17 reward cards (1920x1080 cn_ui captures, 1:1 ref scale) with the new icons composited exactly where
RewardPickUI puts them (slot 180x180 @ card top+70, IconShadow = same sprite 25 % black @ +6,+8 -> hidden for baked-shadow icons
by the v17.4 RewardPickUI change), plus the full icon sheet (256 source shown at 180 and 96)."""
from PIL import Image, ImageDraw, ImageFont
import numpy as np
CJK = '/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc'
F = lambda s: ImageFont.truetype(CJK, s, index=2)
UI = '/workspace/v174/out/ui/'; OLD = '/workspace/v174/src/Assets/Game/Art/Stylized/UI/'
CN = '/workspace/stonesignal/cn_ui/1920x1080/'
CARD_X = [304, 760, 1216]; CARD_Y = 299; CW, CH = 400, 560
def card_with(shot, idx, icon_png, baked=True):
    im = Image.open(CN + shot).convert('RGBA')
    x0 = CARD_X[idx]; crop = im.crop((x0 - 20, CARD_Y - 40, x0 + CW + 20, CARD_Y + CH + 20))
    ox, oy = 20, 40
    fill = crop.getpixel((ox + 40, oy + 200))
    d = ImageDraw.Draw(crop); d.rectangle((ox + 100, oy + 62, ox + 300, oy + 262), fill=fill)      # erase old icon + shadow
    ic = Image.open(icon_png).convert('RGBA').resize((180, 180), Image.LANCZOS)
    if not baked:
        sh = Image.new('RGBA', ic.size, (0, 0, 0, 0)); sh.putalpha(ic.split()[3].point(lambda v: int(v * .25))); crop.alpha_composite(sh, (ox + 110 + 6, oy + 70 + 8))
    crop.alpha_composite(ic, (ox + 110, oy + 70))
    return crop
ORDER = [('all_damage', '全塔伤害', 'AllDamage'), ('all_attack_speed', '全塔攻速', 'AllAttackSpeed'), ('all_range', '全塔射程', 'AllRange'),
         ('arrow_range', '针弩射程', 'ArrowRange'), ('cannon_radius', '震岩半径', 'CannonRadius'), ('add_block', '石牌补给', 'AddBlock'),
         ('next_draw', '下次抽牌 +N', 'NextDraw'), ('path_slow', '路径减速', 'PathSlow'), ('bonus_slot', '扶壁', 'BonusSlot'),
         ('kill_gold', '击杀金币', 'KillGold'), ('wave_gold', '每波金币', 'WaveGold'), ('next_wave_gold', '下波金币', 'NextWaveGold'),
         ('tower_discount', '塔价折扣', 'TowerDiscount'), ('base_hp', '核心加固', 'BaseHP'), ('wave_heal', '每波回血', 'WaveHeal')]
RUNES = [('blade', '锋·赤符文'), ('swift', '疾·苍符文'), ('sight', '望·金符文'), ('frost', '霜·紫符文'), ('bounty', '丰·绿符文'), ('resonance', '共鸣·白符文')]
if __name__ == '__main__':
    out = Image.new('RGB', (2200, 2700), (24, 22, 44)); D = ImageDraw.Draw(out)
    D.text((40, 24), 'StoneSignal v17.4 — reward card icons (RewardPickUI, real v18 cards @1920×1080, 1:1)', font=F(38), fill=(255, 236, 190))
    D.text((40, 80), '256 px source · chunky toon HUD: palette fill + toon shade, navy ink, cream outline + navy edge, baked hard shadow · cyan only = slow/frost · green only = heal/core repair', font=F(22), fill=(200, 195, 230))
    cards = [card_with('reward_cards_CRE_1920x1080.png', 0, UI + 'ui_reward_add_block.png'),
             card_with('reward_cards_REL_1920x1080.png', 1, UI + 'ui_reward_all_attack_speed.png'),
             card_with('reward_cards_REL_1920x1080.png', 2, UI + 'ui_reward_all_damage.png'),
             card_with('reward_cards_REL_1920x1080.png', 0, OLD + 'ui_rune_blade.png', baked=False)]
    labels = ['普通 · 石牌补给 → ui_reward_add_block', '稀有 · 全塔攻速 → ui_reward_all_attack_speed', '传说 · 全塔伤害 → ui_reward_all_damage', '精良 · 锋·赤符文 → ui_rune_blade (unchanged, 96 px source)']
    x = 40
    for c, l in zip(cards, labels):
        out.paste(c.convert('RGB'), (x, 130)); D.text((x + 10, 130 + c.height + 6), l, font=F(19), fill=(220, 215, 240)); x += c.width + 72
    # in-context: whole REL screen with the two new icons, downscaled
    y0 = 130 + 620 + 50
    D.text((40, y0), 'In context (REL capture, icons replaced on cards 2/3; at phone size the 180 ref slot is ~96 px):', font=F(24), fill=(255, 236, 190))
    full = Image.open(CN + 'reward_cards_REL_1920x1080.png').convert('RGBA')
    for idx, n in [(1, 'all_attack_speed'), (2, 'all_damage')]:
        c = card_with('reward_cards_REL_1920x1080.png', idx, UI + f'ui_reward_{n}.png'); full.alpha_composite(c, (CARD_X[idx] - 20, CARD_Y - 40))
    small = full.resize((1000, 562), Image.LANCZOS).convert('RGB'); out.paste(small, (40, y0 + 44))
    ph = full.resize((960, 540), Image.LANCZOS).convert('RGB'); out.paste(ph, (1100, y0 + 44 + 11))
    D.text((1100, y0 + 44 + 560), '960×540 (≈ phone, slot ≈ 90 px)', font=F(19), fill=(170, 160, 210))
    # icon sheet
    y1 = y0 + 44 + 600
    D.text((40, y1), 'Icon sheet — ui_reward_<id>.png (Assets/Game/Art/Stylized/UI → HUD.spriteatlas), shown at 180 px on its card fill + 96 px:', font=F(24), fill=(255, 236, 190))
    fills = [(150, 146, 164), (52, 116, 216), (136, 72, 212), (240, 150, 40)]
    cw, chh = 420, 250
    for i, (n, cn, eff) in enumerate(ORDER):
        gx, gy = 40 + (i % 5) * cw, y1 + 50 + (i // 5) * chh
        ic = Image.open(UI + f'ui_reward_{n}.png').convert('RGBA')
        bg = Image.new('RGBA', (196, 196), fills[i % 4] + (255,)); bg.alpha_composite(ic.resize((180, 180), Image.LANCZOS), (8, 8)); out.paste(bg.convert('RGB'), (gx, gy))
        bg2 = Image.new('RGBA', (104, 104), fills[(i + 2) % 4] + (255,)); bg2.alpha_composite(ic.resize((96, 96), Image.LANCZOS), (4, 4)); out.paste(bg2.convert('RGB'), (gx + 206, gy))
        D.text((gx + 206, gy + 112), cn, font=F(24), fill=(255, 255, 255)); D.text((gx + 206, gy + 144), eff, font=F(17), fill=(190, 185, 220))
        D.text((gx, gy + 202), 'ui_reward_' + n, font=F(17), fill=(170, 160, 210))
    y2 = y1 + 50 + 3 * chh + 10
    D.text((40, y2), 'Runes (rune options only) — existing ui_rune_* (96 px source, upscaled ×1.9 in the 180 slot):', font=F(24), fill=(255, 236, 190))
    for i, (n, cn) in enumerate(RUNES):
        gx = 40 + i * 350; ic = Image.open(OLD + f'ui_rune_{n}.png').convert('RGBA')
        bg = Image.new('RGBA', (196, 196), fills[(i + 1) % 4] + (255,)); bg.alpha_composite(ic.resize((180, 180), Image.LANCZOS), (8, 8)); out.paste(bg.convert('RGB'), (gx, y2 + 44))
        D.text((gx, y2 + 250), cn + '  ui_rune_' + n, font=F(18), fill=(220, 215, 240))
    out = out.crop((0, 0, 2200, y2 + 290)); out.save('/workspace/previews/reward_icons_v17_4.png'); print(out.size)
