"""v17 reward cards: same construction as ui_card_tower_frame (sampled): navy #1E1A3A 4 px edge, cream #FFFAF4 8 px outline,
flat fill, lighter gloss strip near the top, dark bottom band with a light top line. Hard edges, no gradients/inlays."""
from PIL import Image, ImageDraw
INK=(30,26,58,255); CREAM=(255,250,244,255)
# fill, gloss, band, bandLine per GDD tier
RAR={'common':((150,146,164),(176,173,188),(92,88,108),(190,186,202)),
     'rare':((52,116,216),(96,152,236),(26,62,140),(110,166,240)),
     'epic':((136,72,212),(170,116,232),(76,34,132),(184,132,240)),
     'legendary':((240,150,40),(255,190,96),(168,84,16),(255,200,110))}
def lerp(a,b,t): return tuple(int(a[i]+(b[i]-a[i])*t) for i in range(3))
def frame(name,fill,gloss,N=160,R=22):
    im=Image.new('RGBA',(N,N)); d=ImageDraw.Draw(im)
    d.rounded_rectangle([0,0,N-1,N-1],R,fill=INK)
    d.rounded_rectangle([4,4,N-5,N-5],R-4,fill=CREAM)
    d.rounded_rectangle([12,12,N-13,N-13],R-12,fill=fill+(255,))
    d.rounded_rectangle([22,18,N-23,30],6,fill=gloss+(255,))         # gloss strip (stretches horizontally)
    im.save(f'ui9_reward_frame_{name}.png')
def band(name,col,line,W=96,H=96,R=10):
    im=Image.new('RGBA',(W,H)); d=ImageDraw.Draw(im)
    d.rounded_rectangle([0,0,W-1,H-1],R,fill=col+(255,)); d.rectangle([0,0,W-1,R],fill=col+(255,)); d.rectangle([0,0,W-1,2],fill=line+(255,))
    im.save(f'ui9_reward_band_{name}.png')
for k,(f,g,b,l) in RAR.items(): frame(k,f,g); band(k,b,l)
W,H=120,60; im=Image.new('RGBA',(W,H)); d=ImageDraw.Draw(im)       # tier pill (like the hotkey / 2x2 badges)
d.rounded_rectangle([0,0,W-1,H-1],29,fill=INK); d.rounded_rectangle([4,4,W-5,H-5],25,fill=CREAM); d.rounded_rectangle([9,9,W-10,H-10],20,fill=INK)
im.save('ui9_reward_tier_pill.png')
