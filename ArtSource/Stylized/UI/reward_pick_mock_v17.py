from PIL import Image, ImageDraw, ImageFont
CJK='/usr/share/fonts/opentype/noto/NotoSansCJK-Black.ttc'
import os
if not os.path.exists(CJK): CJK='/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc'
F=lambda s:ImageFont.truetype(CJK,s,index=2)
INK=(30,26,58)
ST='/workspace/v16/status'
def nine(path,w,h,b):
    im=Image.open(path).convert('RGBA'); W,H=im.size; out=Image.new('RGBA',(w,h)); xs=[0,b,W-b,W]; ys=[0,b,H-b,H]; X=[0,b,w-b,w]; Y=[0,b,h-b,h]
    for i in range(3):
        for j in range(3):
            if X[i+1]<=X[i] or Y[j+1]<=Y[j] or xs[i+1]<=xs[i] or ys[j+1]<=ys[j]: continue
            out.paste(im.crop((xs[i],ys[j],xs[i+1],ys[j+1])).resize((X[i+1]-X[i],Y[j+1]-Y[j]),Image.LANCZOS),(X[i],Y[j]))
    return out
CN={'common':'普通','rare':'精良','epic':'稀有','legendary':'传说'}
def card(r,title,desc,icon,w=400,h=560):
    c=Image.new('RGBA',(w+40,h+60)); o=(20,36)
    c.alpha_composite(nine(f'ui9_reward_frame_{r}.png',w,h,36),o)
    c.alpha_composite(nine(f'ui9_reward_band_{r}.png',w-24,180,12),(o[0]+12,o[1]+h-12-180))
    ic=Image.open(icon).convert('RGBA').resize((180,180),Image.LANCZOS)
    sh=Image.new('RGBA',ic.size,(0,0,0,0)); sh.putalpha(ic.split()[3].point(lambda v:int(v*.25))); c.alpha_composite(sh,(o[0]+w//2-90+6,o[1]+70+8)); c.alpha_composite(ic,(o[0]+w//2-90,o[1]+70))
    d=ImageDraw.Draw(c)
    d.text((o[0]+w//2,o[1]+300),title,font=F(44),fill=(255,255,255),anchor='mm',stroke_width=5,stroke_fill=INK)
    yy=o[1]+h-12-180+56
    for line in desc: d.text((o[0]+w//2,yy),line,font=F(28),fill=(255,255,255),anchor='mm',stroke_width=3,stroke_fill=INK); yy+=44
    pw=150; c.alpha_composite(nine('ui9_reward_tier_pill.png',pw,60,29),(o[0]+w//2-pw//2,o[1]-28))
    d.text((o[0]+w//2,o[1]+1),CN[r],font=F(28),fill=(255,236,170) if r=='legendary' else (255,255,255),anchor='mm')
    if r=='legendary':   # simple flat sparkles, no glow/flame ornament
        for (x,y,s) in [(o[0]+40,o[1]+44,16),(o[0]+w-46,o[1]+60,12),(o[0]+w-70,o[1]+230,9)]:
            d.polygon([(x,y-s),(x+s*.28,y-s*.28),(x+s,y),(x+s*.28,y+s*.28),(x,y+s),(x-s*.28,y+s*.28),(x-s,y),(x-s*.28,y-s*.28)],fill=(255,255,255))
    return c
OPTS=[('common','条石 Duo',['+1 张 2 格墙牌'],f'{ST}/ui_status_slow.png'),('rare','基座',['其上塔射程 +0.8'],f'{ST}/ui_status_frost.png'),
      ('epic','导体',['信号经过该石块','不衰减'],f'{ST}/ui_status_shock.png'),('legendary','信标王冠',['核心信号值 +6','L3 阈值 9 → 8'],f'{ST}/ui_status_burn.png')]
if __name__=='__main__':
    shot=Image.open('/workspace/previews/game_v16_1_shot.png').convert('RGBA'); k=1920/1024; q=shot.width/1024
    up=lambda box: shot.crop(tuple(int(v*q) for v in box)).resize((int((box[2]-box[0])*k),int((box[3]-box[1])*k)),Image.LANCZOS)
    tower=up((118,428,238,560)); hud=up((860,500,1024,576)); pill=up((96,18,222,64)); wave=up((394,8,630,62))
    out=Image.new('RGB',(2040,1620),(24,22,44)); D=ImageDraw.Draw(out)
    D.text((30,18),'RewardPickUI v17 — chunky toon cards (same language as tower card / BATTLE / gold pill), 1:1 at 1920×1080 ref',font=F(34),fill=(255,236,190))
    x=30
    for o in OPTS:
        out.paste(card(*o),(x,90),card(*o)); x+=470
    D.text((30,720),'普通 grey #9692A4  ·  精良 blue #3474D8  ·  稀有 purple #8848D4  ·  传说 orange-gold #F09628 (+3 flat sparkles; glow only during pick FX)',font=F(24),fill=(200,195,230))
    D.text((30,780),'Reference crops from the current game (same 1920 ref scale):',font=F(26),fill=(255,236,190))
    out.paste(tower.convert('RGB'),(30,830)); D.text((30,830+tower.height+8),'ui_card_tower_frame',font=F(20),fill=(170,160,210))
    out.paste(pill.convert('RGB'),(330,830)); out.paste(wave.convert('RGB'),(330,930)); out.paste(hud.convert('RGB'),(330,1060))
    D.text((330,1060+hud.height+8),'gold pill · WAVE banner · BATTLE',font=F(20),fill=(170,160,210))
    D.text((1000,830),'Shared language now:\n• navy ink edge 4 px + cream #FFFAF4 outline 8 px (sampled)\n• flat solid fill, no gradients / bevels / inlays\n• bold white text with navy stroke (like “70”, BATTLE)\n• gloss strip at top + dark text band with light top line,\n   exactly like the tower card cost strip\n• small navy pill badge on the top edge (like ×3 / 2x2)\n• rarity = fill colour + pill text, one cue each\n• icon centred directly on the fill, soft drop shadow',font=F(26),fill=(220,215,240),spacing=10)
    old=Image.open('/workspace/previews/reward_pick_v16_2.png').convert('RGB'); q=old.width/1024
    oc=old.crop((int(780*q),int(40*q),int(995*q),int(335*q))); oc=oc.resize((int(oc.width*.62),int(oc.height*.62)))
    out.paste(oc,(1000,1250)); D.text((1000+oc.width+20,1270),'v16.2 (rejected): thin rim, navy body,\ngold inlay + studs, bevel gradients,\nnotched ribbon + gem + flame edge,\nsmall cream text on navy',font=F(22),fill=(230,120,120),spacing=8)
    out.save('/workspace/previews/reward_pick_v17.png')
