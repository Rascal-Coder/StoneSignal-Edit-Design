from PIL import Image, ImageDraw, ImageFilter
import random, math
random.seed(4)
O=(30,26,58)
def puff_frame(t,S=256):
    im=Image.new('RGBA',(S,S)); 
    n=7; R=S*.16+S*.22*(t**.5); fade=1-max(0,(t-.55)/.45)
    for k in range(n):
        a=k/n*6.283+.3; d=R*.55*(.4+t)
        cx=S/2+math.cos(a)*d; cy=S/2+math.sin(a)*d*.8 - S*.06*t
        r=R*(.55+.2*math.sin(k*1.7))*(1-.35*t)
        lay=Image.new('RGBA',(S,S)); g=ImageDraw.Draw(lay)
        # toon puff: outline, base, shade, highlight (painted)
        g.ellipse([cx-r-5,cy-r-5,cx+r+5,cy+r+5],fill=O+(255,))
        g.ellipse([cx-r,cy-r,cx+r,cy+r],fill=(236,230,240,255))
        g.ellipse([cx-r*.9,cy-r*.1,cx+r*.95,cy+r],fill=(196,186,214,255))
        g.ellipse([cx-r*.55,cy-r*.75,cx+r*.1,cy-r*.2],fill=(255,255,255,255))
        # brush texture strokes
        for _ in range(10):
            x=cx+random.uniform(-r*.7,r*.7); y=cy+random.uniform(-r*.6,r*.7)
            g.line([x,y,x+random.uniform(-r*.3,r*.3),y+random.uniform(-4,4)],fill=(215,205,230,140),width=3)
        im.alpha_composite(lay)
    # little sparkle stars early
    if t<.6:
        g=ImageDraw.Draw(im)
        for k in range(4):
            a=k*1.6+.5; d=R*1.25; x=S/2+math.cos(a)*d; y=S/2+math.sin(a)*d; s=10*(1-t)
            g.polygon([(x,y-s),(x+s*.3,y-s*.3),(x+s,y),(x+s*.3,y+s*.3),(x,y+s),(x-s*.3,y+s*.3),(x-s,y),(x-s*.3,y-s*.3)],fill=(255,236,190,255))
    im=im.filter(ImageFilter.GaussianBlur(.6))
    a=im.split()[3].point(lambda v:int(v*fade)); im.putalpha(a); return im
sheet=Image.new('RGBA',(1024,1024))
for f in range(16): sheet.alpha_composite(puff_frame(f/15),((f%4)*256,(f//4)*256))
sheet.save('T_FX_DeathPuff_4x4.png')
# cute skull icon 128
S=4;N=128; im=Image.new('RGBA',(N*S,N*S)); d=ImageDraw.Draw(im)
def E(b,c): d.ellipse([v*S for v in b],fill=c)
def RR(b,r,c): d.rounded_rectangle([v*S for v in b],r*S,fill=c)
E((14,10,114,96),O); RR((38,70,90,118),14,O)
E((20,16,108,90),(250,246,236)); RR((44,70,84,112),10,(250,246,236))
E((22,52,106,92),(222,214,206)); RR((44,86,84,112),10,(222,214,206)); E((30,20,70,46),(255,255,255))
E((32,46,58,74),O); E((70,46,96,74),O); E((38,52,46,60),(255,255,255)); E((76,52,84,60),(255,255,255))
d.polygon([(64*S,74*S),(58*S,84*S),(70*S,84*S)],fill=O)
for x in (52,64,76): d.line([x*S,96*S,x*S,110*S],fill=O,width=3*S)
E((18,58,30,68),(255,170,170)); E((98,58,110,68),(255,170,170))
im.resize((N,N),Image.LANCZOS).save('ui_fx_skull.png')
