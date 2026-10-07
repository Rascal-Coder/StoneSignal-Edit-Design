from PIL import Image, ImageDraw, ImageFilter
S=4
def soft_outline(im,px=5,alpha=150):
    a=im.split()[3]; o=a.filter(ImageFilter.MaxFilter(px*2+1)).filter(ImageFilter.GaussianBlur(px*.6))
    sh=Image.new('RGBA',im.size,(20,16,40,0)); sh.putalpha(o.point(lambda v:int(v*alpha/255))); sh.alpha_composite(im); return sh
def ring(N=96,w=13,name='ui_dir_ring',inner_x=True):
    im=Image.new('RGBA',(N*S,N*S)); d=ImageDraw.Draw(im); m=12
    d.ellipse([m*S,m*S,(N-m)*S,(N-m)*S],outline=(255,255,255,255),width=w*S)
    d.arc([(m+w*.3)*S,(m+w*.3)*S,(N-m-w*.3)*S,(N-m-w*.3)*S],200,320,fill=(255,255,255,255),width=int(w*.25*S))
    if inner_x:   # small cancel X in the middle (transparent background)
        c=N/2; k=10
        d.line([(c-k)*S,(c-k)*S,(c+k)*S,(c+k)*S],fill=(255,255,255,255),width=6*S); d.line([(c-k)*S,(c+k)*S,(c+k)*S,(c-k)*S],fill=(255,255,255,255),width=6*S)
    im=soft_outline(im,5*S//2*2,170).resize((N,N),Image.LANCZOS); im.save(name+'.png'); return im
def arrow(N=88,name='ui_dir_arrow',scale=1.0,shade=255):
    im=Image.new('RGBA',(N*S,N*S)); d=ImageDraw.Draw(im); c=N/2
    pts=[(c,8),(N-10,44),(c+14,44),(c+14,N-10),(c-14,N-10),(c-14,44),(10,44)]
    pts=[(c+(x-c)*scale,c+(y-c)*scale) for x,y in pts]
    d.polygon([(x*S,y*S) for x,y in pts],fill=(shade,shade,shade,255))
    # chunky bevel highlight (stays white-ish so tint reads)
    d.polygon([(c*S,(8*scale+c*(1-scale)+6)*S),((c+8)*S,(c+(30-c)*scale)*S),((c-8)*S,(c+(30-c)*scale)*S)],fill=(255,255,255,255))
    d.rectangle([(c-14*scale)*S,(c+(48-c)*scale)*S,(c-14*scale+5)*S,(c+(N-14-c)*scale)*S],fill=(min(255,shade+20),)*3+(255,))
    im=soft_outline(im,6,170).resize((N,N),Image.LANCZOS); im.save(name+'.png'); return im
ring(); arrow(); arrow(name='ui_dir_arrow_pressed',scale=.9,shade=210); ring(N=128,w=7,name='ui_dir_pulse',inner_x=False)
