"""Stationary painted workshop plates with small articulated game mechanisms."""
from functools import lru_cache
from pathlib import Path
import math
from PIL import Image, ImageDraw

MOD=Path(__file__).resolve().parents[1]
CAMP_BUILDING_FRAMES=32
SCALE=4

@lru_cache(maxsize=2)
def plate(name):
    filename='sheepfold-animation-base' if name=='sheepfold' else 'tentweaver-isometric'
    source=Image.open(MOD/'art-source'/f'{filename}.png').convert('RGBA')
    box=source.getchannel('A').point(lambda a:255 if a>12 else 0).getbbox()
    sprite=source.crop(box);sprite.thumbnail((146,142),Image.Resampling.LANCZOS)
    origin=((184-sprite.width)//2,170-sprite.height)
    im=Image.new('RGBA',(184,184));im.alpha_composite(sprite,origin)
    return im,box,sprite.size,origin

def registered(name,x,y):
    _,box,size,origin=plate(name)
    return origin[0]+(x-box[0])*size[0]/(box[2]-box[0]),origin[1]+(y-box[1])*size[1]/(box[3]-box[1])

def mechanisms(name,frame,working):
    im=Image.new('RGBA',(184*SCALE,184*SCALE));d=ImageDraw.Draw(im)
    def p(x,y):return round(x*SCALE),round(y*SCALE)
    def line(points,color,width=1):d.line([p(*a) for a in points],fill=color,width=max(1,round(width*SCALE)))
    def poly(points,color,outline=None):d.polygon([p(*a) for a in points],fill=color,outline=outline,width=max(1,round(.45*SCALE)))
    def ellipse(box,color,outline=None):d.ellipse((*p(*box[:2]),*p(*box[2:])),fill=color,outline=outline,width=max(1,round(.4*SCALE)))
    phase=frame*math.tau/CAMP_BUILDING_FRAMES
    if name=='sheepfold':
        for side,source in enumerate(((512,711),(1110,870))):
            x,y=registered(name,*source)
            motion=phase+side*2.7
            bob=math.sin(motion)*.35 if working else 0
            turn=math.sin(motion)*1.15 if working else 0
            nod=(1-math.cos(motion*2))*.8 if working else 1.2
            w=7.3 if side else 4.7
            ellipse((x-w,y-2,x+w,y+1),(25,24,17,85))
            for dx in (-w*.65,w*.65):
                line([(x+dx,y-6),(x+dx+.45,y-.6)],'#706455',1.7)
                line([(x+dx-.4,y-1),(x+dx+1,y-1)],'#37352e',1)
            ellipse((x-w,y-13+bob,x+w,y-3+bob),'#b9b4a1','#756e59')
            # Stable fleece tufts, with only a slight breathing rise.
            for k in range(15):
                a=k*2.399;radius=math.sqrt((k+.5)/15)
                cx=x+math.cos(a)*w*.78*radius;cy=y-8+bob+math.sin(a)*3.7*radius
                ellipse((cx-2.1,cy-2.2,cx+2.2,cy+1.7),('#dcd8c2','#eee6ce','#c9c6b0')[k%3])
            hx=x+(-w+turn if side else turn);hy=y-12+bob+nod
            if side:
                line([(x+w-1,y-8),(x+w+1.7,y-7+turn)],'#bdbba5',1.5)
            ellipse((hx-2.4,hy-2.3,hx+2.3,hy+3.8),'#c5b8a0','#796d59')
            ellipse((hx-1.9,hy-1.5,hx+1.8,hy+2.5),'#e2d4b6')
            ear=math.sin(motion*3)*.9 if working else -.7
            poly([(hx-1.4,hy-1),(hx-4.4,hy-1.8+ear),(hx-3.4,hy+.3),(hx-1.2,hy+.5)],'#cbb598','#7f6c55')
            poly([(hx+1.4,hy-1),(hx+4,hy-1.3-ear),(hx+3,hy+.8),(hx+1.2,hy+.5)],'#d8c3a1','#7f6c55')
            for dx in (-1.2,1.15):
                if not working or frame%16!=5:ellipse((hx+dx-.35,hy+.2,hx+dx+.25,hy+.8),'#352d25')
                else:line([(hx+dx-.4,hy+.6),(hx+dx+.4,hy+.6)],'#746451',.45)
            ellipse((hx-.85,hy+2.4,hx+.85,hy+3.4),'#837263')
    else:
        # Upright loom aligned with the open bay's 2:1 floor projection.
        cx,cy=registered(name,634,766)
        def q(u,v,z=0):return (cx+u-v,cy+(u+v)*.5-z)
        swing=(.5-.5*math.cos(phase))*3 if working else 0
        shuttle=math.sin(phase)*7.4 if working else -7.4
        # Rear weaver, visible only when the assigned citizen is actually at work.
        if working:
            x,y=q(0,-5,16);lean=math.sin(phase)*.5
            poly([(x-3,y+3),(x+2,y+3),(x+4,y+10),(x-4,y+9)],'#738078','#344b42')
            poly([(x-2,y+5),(x+2,y+5),(x+3,y+10),(x-3,y+9)],'#bba886')
            ellipse((x-2,y-3+lean,x+2,y+2+lean),'#c8a478','#644b33')
            poly([(x-2.4,y-2+lean),(x-1.5,y-4.5+lean),(x+1.9,y-4+lean),(x+2.5,y-1+lean)],'#7d6045','#423c2d')
            for side in (-1,1):
                hand=q(side*8,2+swing,10)
                line([(x+side*2.4,y+4),(x+side*5,y+7),hand],'#39493b',2.2)
                line([(x+side*2.4,y+4),(x+side*5,y+7),hand],'#a29b7b',1.35)
                ellipse((hand[0]-.8,hand[1]-.8,hand[0]+.8,hand[1]+.8),'#dbb789')
        # A wide timber frame, stretched warp threads and cloth wound on a roller.
        for u in (-10,10):
            for v in (-2,6):
                line([q(u,v),q(u,v,25)],'#493b29',2.9)
                line([q(u-.5,v-.4,1),q(u-.5,v-.4,25)],'#bd9860',1.35)
            line([q(u,-2,23),q(u,6,23)],'#8f683b',2)
            line([q(u,-2,3),q(u,6,3)],'#83643e',2)
        for v,z in ((-2,23),(-2,17),(6,8),(6,2)):
            line([q(-11,v,z),q(11,v,z)],'#443626',2.8)
            line([q(-11,v,z+.7),q(11,v,z+.7)],'#c19c65',1.3)
        poly([q(-8,-1,17),q(8,-1,17),q(8,6,7),q(-8,6,7)],'#cbbd96','#8c7955')
        for u in range(-8,9):
            line([q(u,-1,22),q(u,-1,17),q(u,6,8)],'#efe4c3' if u%2 else '#ad9b7a',.5)
        # Shuttle crosses the warp, while the reed rocks forward to beat the weft.
        line([q(shuttle-1,2,16),q(shuttle+2.8,2,16)],'#654624',1.6)
        line([q(shuttle-1,2,16.5),q(shuttle+2.8,2,16.5)],'#cfb880',.65)
        for u in range(-8,9,2):line([q(u,2+swing,18),q(u,2+swing,11)],'#6b5437',.55)
        for z in (11,18):
            line([q(-9,2+swing,z),q(9,2+swing,z)],'#654628',1.65)
            line([q(-9,2+swing,z+.45),q(9,2+swing,z+.45)],'#d0ad72',.65)
        line([q(-9,6,7),q(9,6,7)],'#c0ac80',3.3)
        line([q(-9,6,8),q(9,6,8)],'#ece0b9',1.2)
        # Turning winding handle and foot treadle are linked to the same cycle.
        hx,hy=q(11,6,7);angle=phase if working else 0
        line([(hx,hy),(hx+math.cos(angle)*2,hy+math.sin(angle)*2.7)],'#755435',1.1)
        ellipse((hx-.75,hy-.75,hx+.75,hy+.75),'#c1a672')
        line([q(-5,1,1),q(4,5,1+(math.sin(phase)*.7 if working else 0))],'#a5834f',1.4)
    return im.resize((184,184),Image.Resampling.LANCZOS)

def building_frame(name,frame=0,working=False):
    base=plate(name)[0];im=base.copy();im.alpha_composite(mechanisms(name,frame,working))
    if name=='sheepfold':
        # Restore the existing near posts and crossbar in front of the sheep.
        mask=Image.new('L',base.size);d=ImageDraw.Draw(mask)
        for polygon in [((1076,718),(1120,731),(1120,923),(1076,912)),((1002,786),(1076,806),(1076,824),(1002,806))]:
            d.polygon([registered(name,*point) for point in polygon],fill=255)
        im.paste(base,(0,0),mask)
    return im

def preview():
    out=MOD.parents[1]/'analysis/medieval/camp-buildings';out.mkdir(parents=True,exist_ok=True)
    contact=Image.new('RGBA',(736,368),'#65764f');d=ImageDraw.Draw(contact)
    for i,name in enumerate(('sheepfold','tentweaver')):
        base,box,size,origin=plate(name);print(name,box,size,origin)
        contact.alpha_composite(base.resize((368,368)),(i*368,0))
        for x in range(0,184,20):
            d.line((i*368+x*2,0,i*368+x*2,368),fill=(255,255,255,75));d.text((i*368+x*2,0),str(x),fill='white')
        for y in range(20,184,20):
            d.line((i*368,y*2,i*368+368,y*2),fill=(255,255,255,75));d.text((i*368,y*2),str(y),fill='white')
    contact.save(out/'plates-grid.png')
    poses=[]
    for f in range(CAMP_BUILDING_FRAMES):
        sheet=Image.new('RGBA',(736,368),'#65764f')
        for i,name in enumerate(('sheepfold','tentweaver')):sheet.alpha_composite(building_frame(name,f,True).resize((368,368),Image.Resampling.NEAREST),(i*368,0))
        poses.append(sheet.convert('RGB'))
    poses[0].save(out/'animation.gif',save_all=True,append_images=poses[1:],duration=120,loop=0)
    poses[7].save(out/'workshops.png')

if __name__=='__main__':preview()
