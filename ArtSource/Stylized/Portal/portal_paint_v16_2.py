import numpy as np, random, math
from PIL import Image, ImageDraw, ImageFilter, ImageChops
random.seed(11); np.random.seed(11)
N=1024; C=N/2
def brush(d, pts, w, col, jitter=2.0, steps=1):
    # painterly stroke: tapered width, slight wobble, 2 offset passes (bristle feel); drawn onto a layer and composited
    im=d._image; L=Image.new('RGBA',im.size); ld=ImageDraw.Draw(L)
    n=len(pts)-1
    for p in range(2):
        off=(random.gauss(0,w*.25),random.gauss(0,w*.25)); shade=1-.12*p
        for i,((x0,y0),(x1,y1)) in enumerate(zip(pts,pts[1:])):
            t=i/max(1,n); taper=.55+.45*math.sin(math.pi*min(1,max(0,t*1.05)))
            ww=max(1,int(w*2*taper*random.uniform(.85,1.1)*(1-.3*p)))
            c=tuple(int(v*shade) for v in col[:3])+(255,)
            ld.line([x0+off[0],y0+off[1],x1+off[0]+random.gauss(0,jitter*.3),y1+off[1]+random.gauss(0,jitter*.3)],fill=c,width=ww,joint='curve')
            ld.ellipse([x1+off[0]-ww/2,y1+off[1]-ww/2,x1+off[0]+ww/2,y1+off[1]+ww/2],fill=c)
    a=L.split()[3].point(lambda v:int(v*col[3]/255)); L.putalpha(a); im.alpha_composite(L)
def arc(cx,cy,R,a0,a1,wob=6,n=60):
    out=[]
    for i in range(n+1):
        a=math.radians(a0+(a1-a0)*i/n); rr=R+wob*math.sin(a*3.1+1)+random.gauss(0,1.2)
        out.append((cx+math.cos(a)*rr,cy+math.sin(a)*rr))
    return out
# ---------------- rune circle (RGBA, emissive-ish colours, alpha = coverage)
glow=Image.new('RGBA',(N,N)); g=ImageDraw.Draw(glow)
ink=Image.new('RGBA',(N,N)); d=ImageDraw.Draw(ink)
# dark purple core wash
core=Image.new('L',(N,N)); ImageDraw.Draw(core).ellipse([C-300,C-300,C+300,C+300],fill=210); core=core.filter(ImageFilter.GaussianBlur(70))
wash=Image.new('RGBA',(N,N),(44,18,62,0)); wash.putalpha(core)
# outer ring: 3 overlapping uneven hand strokes with gaps
for k in range(3):
    a0=random.uniform(0,40); brush(d,arc(C,C,400+k*5,a0,a0+random.uniform(300,350),wob=7),random.uniform(9,13),(255,150,50,255),jitter=1.5)
brush(d,arc(C,C,330,0,360,wob=4,n=90),6,(255,190,90,235),jitter=1.2)
# glyphs between the rings (hand-drawn runes)
for i in range(12):
    a=math.radians(i*30+random.uniform(-4,4)); cx=C+math.cos(a)*366; cy=C+math.sin(a)*366
    ca,sa=math.cos(a+math.pi/2),math.sin(a+math.pi/2)
    shapes=[[(-10,-16),(0,16),(10,-16)],[(-10,-16),(-10,16),(8,4)],[(0,-18),(0,18)],[(-12,-14),(12,14)],[(-10,16),(0,-16),(10,16),(-8,2)],[(-10,-14),(10,-2),(-10,10),(10,18)]]
    sh=random.choice(shapes); pts=[(cx+(x*ca-y*sa)*1.2,cy+(x*sa+y*ca)*1.2) for x,y in sh]
    brush(d,pts,5,(255,200,110,255),jitter=1.0)
    if random.random()<.5: brush(d,[(cx+ca*-14-sa*0,cy+sa*-14),(cx+ca*14,cy+sa*14)],4,(255,170,80,230))
# inner sigil: triangle + 3 spokes + small ring, uneven
tri=[(C+math.cos(math.radians(-90+120*i))*230+random.gauss(0,4),C+math.sin(math.radians(-90+120*i))*230+random.gauss(0,4)) for i in range(4)]; tri[3]=tri[0]
brush(d,tri,7,(255,140,45,245),jitter=1.8)
for i in range(3):
    a=math.radians(30+120*i); brush(d,[(C+math.cos(a)*60,C+math.sin(a)*60),(C+math.cos(a)*300,C+math.sin(a)*300)],5,(255,120,40,220),jitter=2)
brush(d,arc(C,C,90,10,340,wob=5,n=40),6,(255,210,120,255))
# painterly glow: blurred copy of ink, warmer
gl=ink.filter(ImageFilter.GaussianBlur(18)); r,gg,b,a=gl.split(); gl=Image.merge('RGBA',(r,gg.point(lambda v:v*.6),b.point(lambda v:v*.3),a.point(lambda v:min(255,v*1.6))))
rune=Image.new('RGBA',(N,N)); rune.alpha_composite(wash); rune.alpha_composite(gl); rune.alpha_composite(ink)
# brush texture breakup
noise=Image.effect_noise((N,N),60).filter(ImageFilter.GaussianBlur(1.2)).point(lambda v:200+v//5 if v<255 else 255)
rune.putalpha(ImageChops.multiply(rune.split()[3],noise))
rune.save('T_Portal_RuneCircle.png')
# ---------------- scorched cracked ground decal (RGBA) + crack mask (L)
def fbm(n,oct=5):
    out=np.zeros((n,n));amp=1
    for o in range(oct):
        s_=2**(o+2); g=np.random.rand(s_+1,s_+1); im=Image.fromarray((g*255).astype(np.uint8)).resize((n,n),Image.BICUBIC)
        out+=np.asarray(im)/255*amp; amp*=.5
    return out/out.max()
yy,xx=np.mgrid[0:N,0:N]; rr=np.hypot(xx-C,yy-C)/C
nz=fbm(N); nz2=fbm(N)
edge=.78+.14*(nz2-.5)*2
alpha=np.clip((edge-rr)/.12,0,1)*np.clip(.55+.6*nz,0,1)
burnt=np.clip(1-rr/.75,0,1)
col=np.dstack([42+40*nz-20*burnt,30+26*nz-14*burnt,30+18*nz-10*burnt])
# brushy streaks: radial smears
ang=np.arctan2(yy-C,xx-C); streak=(np.sin(ang*37+nz*6)*.5+.5)**3
col*= (1-.25*streak)[...,None]
dec=Image.fromarray(np.dstack([np.clip(col,0,255),np.clip(alpha*235,0,255)]).astype(np.uint8),'RGBA')
cr=Image.new('L',(N,N)); cd=ImageDraw.Draw(cr)
def crack(x,y,a,L,w,depth=0):
    for s in range(int(L/14)):
        a+=random.gauss(0,.35); nx=x+math.cos(a)*14; ny=y+math.sin(a)*14; cd.line([x,y,nx,ny],fill=255,width=max(1,int(w))); x,y=nx,ny; w*=.93
        if depth<2 and random.random()<.08: crack(x,y,a+random.choice([-1,1])*random.uniform(.5,1),L*.45,w*.8,depth+1)
for i in range(11):
    a=math.radians(i*33+random.uniform(-8,8)); crack(C+math.cos(a)*180,C+math.sin(a)*180,a,random.uniform(200,300),9)
crg=cr.filter(ImageFilter.GaussianBlur(1))
# dark crack lines on decal
dk=Image.new('RGBA',(N,N),(18,10,12,255)); dk.putalpha(crg.point(lambda v:int(v*.9))); dec.alpha_composite(dk)
dec.save('T_Portal_Scorch.png'); crg.save('T_Portal_CrackMask.png')
# ---------------- flipbook 4x4 painted flare (512, 128 cells)
fb=Image.new('RGBA',(512,512))
for f in range(16):
    t=f/15; cell=Image.new('RGBA',(128,128)); cdr=ImageDraw.Draw(cell)
    R=10+100*t**.6; alpha=int(255*(1-t)**1.3)
    for k in range(26):
        a=random.uniform(0,6.283); L=R*random.uniform(.5,1.0); w=random.uniform(3,7)*(1-t*.6)
        p=[(64,64),(64+math.cos(a)*L*.5+random.gauss(0,3),64+math.sin(a)*L*.5+random.gauss(0,3)),(64+math.cos(a)*L,64+math.sin(a)*L)]
        col=(255,int(200-120*t),int(90-60*t),alpha)
        brush(cdr,p,w*.5+1,col,jitter=.8)
    lay=Image.new('RGBA',(128,128)); ImageDraw.Draw(lay).ellipse([64-R*.35,64-R*.35,64+R*.35,64+R*.35],fill=(255,240,190,int(alpha*.8*(1-t)))); cell.alpha_composite(lay)
    cell=cell.filter(ImageFilter.GaussianBlur(1.2)); fb.alpha_composite(cell,((f%4)*128,(f//4)*128))
fb.save('T_Portal_FlareSheet_4x4.png')
