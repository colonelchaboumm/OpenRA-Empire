"""Wooden fortifications on the same 48 x 24 cell grid as the village."""
import math
from pathlib import Path
from PIL import Image, ImageDraw

NAMES={'palisade':'Palissade en bois','palisadey':'Palissade en bois',
       'woodgate':'Porte en bois','woodgatey':'Porte en bois','woodtower':'Tour en bois'}
FRAME=(96,144)
GATE_FRAME=(192,160)
TOWER_FRAME=(184,224)
GATE_FRAMES=9

def point(x,y,z=0): return (round(48+24*(x-y)),round(116+12*(x+y)-z))

def log(d,x,y,height=35):
    px,py=point(x,y)
    d.polygon([(px-3,py),(px-3,py-height+5),(px,py-height),(px+3,py-height+5),(px+3,py)],fill='#6b442a',outline='#3c2c21')
    d.line((px-1,py-2,px-1,py-height+6),fill='#bd8a50',width=2)
    d.line((px+1,py-3,px+1,py-height+7),fill='#8e6037')
    d.polygon([(px-2,py-height+5),(px,py-height+1),(px+2,py-height+5)],fill='#d1a16a')

def wall(name,mask):
    im=Image.new('RGBA',FRAME);d=ImageDraw.Draw(im)
    # Native WithWallSpriteBody masks: -Y, +X, +Y, -X.
    mask=mask or (5 if name.endswith('y') else 10)
    arms=[(0,-1),(1,0),(0,1),(-1,0)]
    positions={(0.,0.)}
    for bit,(x,y) in enumerate(arms):
        if not mask&(1<<bit):continue
        d.line((point(0,0),point(x*.5,y*.5)),fill=(38,30,20,85),width=9)
        for i in range(1,5):positions.add((x*i/8,y*i/8))
    for x,y in sorted(positions,key=lambda p:(sum(p),p[0])):log(d,x,y,34+(round((x-y)*16)%3))
    for bit,(x,y) in enumerate(arms):
        if not mask&(1<<bit):continue
        for h in (9,23):
            d.line((point(0,0,h),point(x*.5,y*.5,h)),fill='#483321',width=3)
            d.line((point(0,0,h+1),point(x*.5,y*.5,h+1)),fill='#a47744')
    return im

def gate_point(name,t,depth=0,z=0):
    x,y=(depth,t) if name.endswith('y') else (t,depth)
    return (round(96+24*(x-y)),round(112+12*(x+y)-z))

def gate_leaf(side,phase,u):
    # Opposing hinges stay fixed; both leaves swing through 90 degrees at ground level.
    angle=phase/(GATE_FRAMES-1)*math.pi/2
    return (side*1.40-side*1.37*u*math.cos(angle),1.37*u*math.sin(angle))

def gate(name,phase):
    im=Image.new('RGBA',GATE_FRAME);d=ImageDraw.Draw(im)
    def p(t,depth=0,z=0):return gate_point(name,t,depth,z)
    d.line((p(-1.5),p(1.5)),fill=(52,36,22,75),width=7)
    # Each leaf and its pillar is drawn in ground-depth order for either orientation.
    for side in (-1,1):
        def leaf(u,z=0,edge=0):
            t,depth=gate_leaf(side,phase,u)
            return p(t,depth+edge,z)
        d.polygon([leaf(0),leaf(1),leaf(1,0,.18),leaf(0,0,.18)],fill=(35,28,19,60))
        for i in range(11):
            a,b=i/11,(i+1)/11
            shade=('#9b7041','#a87945','#8e6036')[i%3]
            d.polygon([leaf(a,3),leaf(b,3),leaf(b,39),leaf(a,39)],fill=shade,outline='#503820')
            d.line((leaf(a+.018,6),leaf(a+.018,37)),fill='#c59a61')
            d.line((leaf((a+b)/2,10),leaf((a+b)/2,30)),fill='#82572e')
        # Reinforced top/bottom rails, diagonal timber bracing and ironwork.
        for z in (8,32,39):
            d.line((leaf(0,z),leaf(1,z)),fill='#4d3421',width=4)
            d.line((leaf(0,z+1),leaf(1,z+1)),fill='#c09258')
        d.line((leaf(.04,10),leaf(.96,31)),fill='#54371f',width=4)
        d.line((leaf(.04,11),leaf(.96,32)),fill='#b98a50',width=2)
        for z in (8,32):
            d.line((leaf(0,z),leaf(.28,z)),fill='#3a3932',width=3)
            for u in (.06,.22):d.point(leaf(u,z+1),fill='#a4a18b')
        px,py=leaf(.89,20)
        d.ellipse((px-2,py-2,px+2,py+2),outline='#292b27')
        px,py=p(side*1.45)
        d.polygon([(px-4,py),(px-4,py-44),(px,py-49),(px+4,py-44),(px+4,py)],fill='#67462c',outline='#3d2a1e')
        d.line((px-2,py-2,px-2,py-43),fill='#c29458',width=2)
        d.polygon([(px-3,py-44),(px,py-48),(px+3,py-44)],fill='#deb782')
        for z in (9,33):d.line((px-4,py-z,px+3,py-z),fill='#333630',width=2)
    return im

def gate_site(name):
    im=Image.new('RGBA',GATE_FRAME);d=ImageDraw.Draw(im)
    def p(t,depth=0,z=0):return gate_point(name,t,depth,z)
    d.polygon([p(-1.45,-.44),p(1.45,-.44),p(1.45,.44),p(-1.45,.44)],fill=(92,69,38,155),outline='#b79257')
    for t in (-1.45,1.45):
        for depth in (-.4,.4):d.line((p(t,depth),p(t,depth,12)),fill='#d1a371',width=3)
    for i in range(5):d.line((p(-.8,-.3+i*.12,3),p(.8,-.3+i*.12,3)),fill='#b1824b',width=3)
    return im

def site(tower=False):
    size=TOWER_FRAME if tower else FRAME
    im=Image.new('RGBA',size);d=ImageDraw.Draw(im)
    def p(x,y,z=0):
        if tower:return (round(92+24*(x-y)),round(178+12*(x+y)-z))
        return point(x,y,z)
    radius=.88 if tower else .44
    d.polygon([p(-radius,-radius),p(radius,-radius),p(radius,radius),p(-radius,radius)],fill=(92,69,38,155),outline='#b79257')
    for x,y in ((-radius,-radius),(radius,-radius),(radius,radius),(-radius,radius)):
        d.line((p(x,y),p(x,y,17 if tower else 10)),fill='#d1a371',width=3)
    for i in range(5):d.line((p(-radius*.6,-.3+i*.12,3),p(radius*.6,-.3+i*.12,3)),fill='#b1824b',width=3)
    return im

def frames(name):
    if name=='woodtower':
        src=Image.open(Path(__file__).resolve().parents[1]/'art-source/woodtower-isometric.png').convert('RGBA')
        bounds=src.getchannel('A').point(lambda a:255 if a>12 else 0).getbbox()
        src=src.crop(bounds);src.thumbnail((106,174),Image.Resampling.LANCZOS)
        im=Image.new('RGBA',TOWER_FRAME);im.alpha_composite(src,((184-src.width)//2,198-src.height))
        return [im,site(True)]
    poses=[gate(name,i) for i in range(GATE_FRAMES)] if name.startswith('woodgate') else [wall(name,i) for i in range(16)]
    return poses+[gate_site(name) if name.startswith('woodgate') else site()]

def arrows():
    poses=[]
    for i in range(16):
        im=Image.new('RGBA',(24,24));d=ImageDraw.Draw(im)
        a=i*math.tau/16
        def p(t,s=0):return (round(12+math.sin(a)*t+math.cos(a)*s),round(12-math.cos(a)*t+math.sin(a)*s))
        d.line((p(-6),p(5)),fill='#6f4628',width=2)
        d.line((p(-6,-2),p(-3)),fill='#e1d7b6')
        d.line((p(-6,2),p(-3)),fill='#bdbba9')
        d.polygon([p(7),p(3,-2),p(3,2)],fill='#bec0b2')
        poses.append(im)
    return poses

def sequences():
    lines=[]
    for name in NAMES:
        count=1 if name=='woodtower' else GATE_FRAMES if name.startswith('woodgate') else 16
        lines += [f'md.{name}:','\tDefaults:',f'\t\tFilename: medieval|art/{name}.png',
                  '\t\tOffset: 0, '+('-66' if name=='woodtower' else '-32' if name.startswith('woodgate') else '-44'),
                  '\tidle:',f'\t\tLength: {count}','\tconstruction:',f'\t\tStart: {count}','']
    lines+=['md.arrow:','\tidle:','\t\tFilename: medieval|art/arrow.png','\t\tFacings: 16','']
    return lines
