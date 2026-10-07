from PIL import Image, ImageDraw, ImageFilter, ImageChops
import math, random
S=4; INK=(26,20,48,255); NAVY=(42,36,80,255); NAVY2=(30,26,58,255); GOLD=(240,184,56,255)
R={'common':((150,148,160),(205,203,212),(98,96,110)),'rare':((58,128,230),(130,190,255),(30,70,150)),
   'epic':((150,72,220),(205,150,255),(84,36,140)),'legendary':((245,150,40),(255,220,110),(180,80,20))}
def frame(name,base,hi,lo,N=160,B=48):
    im=Image.new('RGBA',(N*S,N*S)); d=ImageDraw.Draw(im); s=lambda v:int(v*S)
    d.rounded_rectangle([0,0,s(N)-1,s(N)-1],s(30),fill=INK)
    d.rounded_rectangle([s(4),s(4),s(N-4),s(N-4)],s(26),fill=base+(255,))
    d.rounded_rectangle([s(4),s(4),s(N-4),s(N/2)],s(26),fill=hi+(255,))          # top bevel light
    d.rounded_rectangle([s(4),s(10),s(N-4),s(N-4)],s(26),fill=base+(255,))
    d.rounded_rectangle([s(4),s(N-14),s(N-4),s(N-4)],s(26),fill=lo+(255,))        # bottom shade
    d.rounded_rectangle([s(6),s(8),s(N-6),s(N-10)],s(24),fill=base+(255,))
    d.rounded_rectangle([s(14),s(14),s(N-14),s(N-14)],s(18),fill=INK)
    d.rounded_rectangle([s(17),s(17),s(N-17),s(N-17)],s(15),fill=NAVY)
    d.rounded_rectangle([s(22),s(22),s(N-22),s(N-22)],s(11),outline=GOLD,width=s(2))  # gold inlay
    for cx,cy in [(22,22),(N-22,22),(22,N-22),(N-22,N-22)]:                        # rune studs at inlay corners
        d.regular_polygon((s(cx),s(cy),s(5.5)),4,rotation=45,fill=GOLD); d.regular_polygon((s(cx),s(cy),s(2.4)),4,rotation=45,fill=INK)
    im=im.resize((N,N),Image.LANCZOS); im.save(f'ui9_reward_frame_{name}.png')
for k,(b,h,l) in R.items(): frame(k,b,h,l)
# rarity gem 48px
def gem(name,b,h,l,N=48):
    im=Image.new('RGBA',(N*S,N*S)); d=ImageDraw.Draw(im); c=N*S/2
    d.regular_polygon((c,c,N*S*.48),6,fill=INK); d.regular_polygon((c,c,N*S*.40),6,fill=b+(255,))
    d.polygon([(c,c-N*S*.40),(c+N*S*.346,c-N*S*.2),(c,c),(c-N*S*.346,c-N*S*.2)],fill=h+(255,))
    d.polygon([(c,c),(c+N*S*.346,c+N*S*.2),(c,c+N*S*.40),(c-N*S*.346,c+N*S*.2)],fill=l+(255,))
    d.ellipse([c-N*S*.2,c-N*S*.3,c-N*S*.06,c-N*S*.18],fill=(255,255,255,230))
    im.resize((N,N),Image.LANCZOS).save(f'ui_reward_gem_{name}.png')
for k,(b,h,l) in R.items(): gem(k,b,h,l)
# white ribbon 9-slice (tinted in code) 160x48, border 40/0
im=Image.new('RGBA',(160*S,48*S)); d=ImageDraw.Draw(im); s=lambda v:int(v*S)
d.polygon([(0,s(6)),(s(160),s(6)),(s(146),s(24)),(s(160),s(42)),(0,s(42)),(s(14),s(24))],fill=INK)
d.polygon([(s(5),s(10)),(s(155),s(10)),(s(142),s(24)),(s(155),s(38)),(s(5),s(38)),(s(18),s(24))],fill=(255,255,255,255))
d.rectangle([s(20),s(30),s(140),s(38)],fill=(215,215,225,255))
im.resize((160,48),Image.LANCZOS).save('ui9_reward_ribbon.png')
# icon slot 112: ember-rune socket
N=112; im=Image.new('RGBA',(N*S,N*S)); d=ImageDraw.Draw(im); c=N*S/2
d.ellipse([0,0,N*S-1,N*S-1],fill=INK); d.ellipse([s(5),s(5),s(N-5),s(N-5)],fill=GOLD); d.ellipse([s(10),s(10),s(N-10),s(N-10)],fill=INK); d.ellipse([s(13),s(13),s(N-13),s(N-13)],fill=(58,50,108,255))
for k in range(8):
    a=k/8*6.283; d.regular_polygon((c+math.cos(a)*s(N/2-7.5),c+math.sin(a)*s(N/2-7.5),s(3)),4,rotation=45,fill=INK)
im.resize((N,N),Image.LANCZOS).save('ui_reward_icon_slot.png')
# epic sheen: diagonal soft white band 128x256 (UV-scrolled / moved under RectMask2D)
im=Image.new('L',(128,256)); d=ImageDraw.Draw(im); d.polygon([(40,0),(88,0),(60,256),(12,256)],fill=255); im=im.filter(ImageFilter.GaussianBlur(10))
sh=Image.new('RGBA',im.size,(255,255,255,0)); sh.putalpha(im.point(lambda v:int(v*.55))); sh.save('ui_reward_sheen.png')
# legendary flame edge: 9-slice frame of painted flame tongues around a 160 rect (centre transparent), border 56
random.seed(7); N=200; im=Image.new('RGBA',(N*S,N*S)); L=Image.new('RGBA',im.size); d=ImageDraw.Draw(L)
def flame(x,y,nx,ny,h,col):
    # tongue polygon pointing outward along normal
    tx,ty=-ny,nx; w=h*.38
    pts=[(x+tx*w,y+ty*w),(x+nx*h*.55+tx*w*.6,y+ny*h*.55+ty*w*.6),(x+nx*h+tx*w*.15,y+ny*h+ty*w*.15),(x+nx*h*.45-tx*w*.1,y+ny*h*.45-ty*w*.1),(x-tx*w,y-ty*w)]
    d.polygon([(px*S,py*S) for px,py in pts],fill=col)
m=20; edges=[]
for t in range(0,161,10):
    edges+= [(m+t,m,0,-1),(m+t,N-m,0,1),(m,m+t,-1,0),(N-m,m+t,1,0)]
for col,sc in [((180,60,10,255),1.0),((255,140,30,255),.75),((255,220,110,255),.45)]:
    for x,y,nx,ny in edges:
        h=random.uniform(10,18)*sc; flame(x,y,nx,ny,h,col)
glow=L.filter(ImageFilter.GaussianBlur(10*S)); g=glow.split()[3].point(lambda v:int(v*.8)); glow.putalpha(g)
im.alpha_composite(glow); im.alpha_composite(L)
cut=Image.new('L',im.size,0); ImageDraw.Draw(cut).rounded_rectangle([m*S+12,m*S+12,(N-m)*S-12,(N-m)*S-12],100,fill=255)
a=ImageChops.subtract(im.split()[3],cut); im.putalpha(a)
im.resize((N,N),Image.LANCZOS).save('ui9_reward_flame_edge.png')
