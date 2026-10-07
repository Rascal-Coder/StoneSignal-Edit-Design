from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageEnhance
import glob, os
CJK='/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc'
F=lambda s:ImageFont.truetype(CJK,s,index=2)  # SC
Fn=lambda s:ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',s)
def nine(path,w,h,b,by=None):
    by=b if by is None else by
    im=Image.open(path).convert('RGBA'); W,H=im.size; out=Image.new('RGBA',(w,h))
    xs=[0,b,W-b,W]; ys=[0,by,H-by,H]; X=[0,b,w-b,w]; Y=[0,by,h-by,h]
    for i in range(3):
        for j in range(3):
            if xs[i+1]<=xs[i] or ys[j+1]<=ys[j]: continue
            c=im.crop((xs[i],ys[j],xs[i+1],ys[j+1])).resize((max(1,X[i+1]-X[i]),max(1,Y[j+1]-Y[j])),Image.LANCZOS); out.paste(c,(X[i],Y[j]))
    return out
def tint(im,c):
    r,g,b=c; a=im.split()[3]; l=im.convert('L'); return Image.merge('RGBA',[l.point(lambda v,k=k:int(v/255*k)) for k in (r,g,b)]+[a])
ST=os.path.dirname(glob.glob('/workspace/v16/**/ui_status_burn.png',recursive=True)[0])
RIB={'common':(150,148,160),'rare':(58,128,230),'epic':(150,72,220),'legendary':(245,150,40)}
CN={'common':'普通','rare':'精良','epic':'稀有','legendary':'传说'}
def card(r,title,desc,icon,w=360,h=520,sheen=None):
    c=Image.new('RGBA',(w+80,h+80)); o=40
    if r=='legendary':
        fe=nine('ui9_reward_flame_edge.png',w+64,h+64,56); c.alpha_composite(fe,(o-32,o-32))
    c.alpha_composite(nine(f'ui9_reward_frame_{r}.png',w,h,48),(o,o)); d=ImageDraw.Draw(c)
    if sheen is not None:
        s=Image.open('ui_reward_sheen.png').resize((140,h)); m=Image.new('L',(w,h)); ImageDraw.Draw(m).rounded_rectangle([18,18,w-18,h-18],14,fill=255)
        L=Image.new('RGBA',(w,h)); L.alpha_composite(s,(int(sheen*w)-70,0)); L.putalpha(Image.composite(L.split()[3],Image.new('L',(w,h)),m)); c.alpha_composite(L,(o,o))
    slot=Image.open('ui_reward_icon_slot.png').resize((150,150)); c.alpha_composite(slot,(o+w//2-75,o+52))
    ic=Image.open(icon).convert('RGBA').resize((96,96)); c.alpha_composite(ic,(o+w//2-48,o+79))
    rb=tint(nine('ui9_reward_ribbon.png',w-40,48,40,0),RIB[r]); c.alpha_composite(rb,(o+20,o+226)); d.text((o+w//2,o+250),CN[r],font=F(24),fill=(255,255,255),anchor='mm',stroke_width=2,stroke_fill=(26,20,48))
    g=Image.open(f'ui_reward_gem_{r}.png'); c.alpha_composite(g,(o+w//2-24,o-6))
    d.text((o+w//2,o+305),title,font=F(34),fill=(255,236,190),anchor='mm')
    yy=o+350
    for line in desc: d.text((o+w//2,yy),line,font=F(22),fill=(205,200,235),anchor='mm'); yy+=32
    return c
if __name__=='__main__':
    opts=[('common','条石 Duo',['获得 1 张 2 格墙牌','灵活补位'],f'{ST}/ui_status_slow.png'),
          ('rare','基座',['其上塔射程 +0.8','石纹词缀'],f'{ST}/ui_status_frost.png'),
          ('epic','导体',['信号经过该石块','不衰减'],f'{ST}/ui_status_shock.png'),
          ('legendary','信标王冠',['核心信号值 +6','L3 阈值 9 → 8'],f'{ST}/ui_status_burn.png')]
    # A) 4 rarities row
    out=Image.new('RGB',(1940,1700),(24,22,44)); D=ImageDraw.Draw(out)
    D.text((20,14),'RewardPickUI v16.2 — rarity frames (9-slice 160px, border 48) · GDD tiers 普通/精良/稀有/传说 = common/rare/epic/legendary',font=F(26),fill=(255,236,190))
    for i,o in enumerate(opts):
        out.paste(card(*o,sheen=.55 if o[0]=='epic' else None),(20+i*478,60),card(*o,sheen=.55 if o[0]=='epic' else None))
    D.text((20,690),'epic: ui_reward_sheen sweeps every 2.4 s (RectMask2D) · legendary: ui9_reward_flame_edge (9-slice, border 56) + additive glow pulse · gem ui_reward_gem_* 48 · ribbon ui9_reward_ribbon (white, tinted) · slot ui_reward_icon_slot 112',font=F(18),fill=(170,160,210))
    # B) in-HUD layout 16:9 (scaled 0.5)
    shot=Image.open('/workspace/previews/game_v16_1_shot.png').convert('RGBA').resize((1920,1080))
    shot=ImageEnhance.Brightness(shot.filter(ImageFilter.GaussianBlur(4))).enhance(.45)
    d2=ImageDraw.Draw(shot); d2.text((960,150),'选择 1 项强化',font=F(56),fill=(255,236,190),anchor='mm',stroke_width=4,stroke_fill=(26,20,48))
    cw,gap=400,56; x0=960-(3*cw+2*gap)//2
    for i,o in enumerate([opts[0],opts[2],opts[3]]):
        cc=card(*o,w=cw,h=560,sheen=.55 if o[0]=='epic' else None); shot.alpha_composite(cc,(x0+i*(cw+gap)-40,250-40))
    d2.text((960,1000),'tap a card to pick  ·  cards 400×560 ref px (touch ≫ 88) · gap 56 · centred · safe-area aware',font=F(24),fill=(200,195,230),anchor='mm')
    out.paste(shot.resize((1280,720)).convert('RGB'),(20,740))
    D.text((1320,760),'Layout per aspect\n(1920×1080 ref, CanvasScaler\nmatch height):\n\n16:9   cards 400×560, gap 56\n19.5:9 same, more side margin\n20:9   same, more side margin\n4:3    cards 380×540, gap 32\n       (3×380+2×32 = 1204\n        < 1440 safe width)\n\ntitle y=150 · cards top y=250\nhint y=1000\nall icons/text inside 9-slice\ncentre, ≥ 22 px font',font=F(24),fill=(220,215,240))
    out.save('/workspace/previews/reward_pick_v16_2.png')
