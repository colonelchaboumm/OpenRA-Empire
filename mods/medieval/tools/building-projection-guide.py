"""Geometric reference for the building artist: one 2:1 camera, no vanishing point.

This draws new geometric volumes, not a retouch of the painted artwork.
One world unit projects to (52,26), (-52,26), or (0,-52) pixels.
"""
from pathlib import Path
import math
from PIL import Image, ImageDraw

CELL=448
SCALE=52
BASE=326
image=Image.new('RGBA',(CELL*4,CELL*2))
d=ImageDraw.Draw(image)
origin=(224,BASE)

def p(x,y,z=0):
    return (round(origin[0]+SCALE*(x-y)),round(origin[1]+SCALE*((x+y)/2-z)))

def face(points,color):
    d.polygon([p(*v) for v in points],fill=color,outline='#29322e',width=2)

def line(a,b,color='#715638',width=4):
    d.line((*p(*a),*p(*b)),fill=color,width=width)

def box(cx,cy,w,depth,z,height,colors=('#bdb499','#8f897b','#d4ccb3')):
    x1,x2=cx-w/2,cx+w/2;y1,y2=cy-depth/2,cy+depth/2
    face([(x1,y2,z),(x2,y2,z),(x2,y2,z+height),(x1,y2,z+height)],colors[0])
    face([(x2,y2,z),(x2,y1,z),(x2,y1,z+height),(x2,y2,z+height)],colors[1])
    face([(x1,y1,z+height),(x2,y1,z+height),(x2,y2,z+height),(x1,y2,z+height)],colors[2])

def roof(cx,cy,w,depth,z,rise,along_x=False,colors=('#755447','#a67454')):
    x1,x2=cx-w/2,cx+w/2;y1,y2=cy-depth/2,cy+depth/2
    if along_x:
        face([(x2,y1,z),(x2,y2,z),(x2,cy,z+rise)],'#bcb294')
        face([(x1,y2,z),(x2,y2,z),(x2,cy,z+rise),(x1,cy,z+rise)],colors[0])
        face([(x1,y1,z),(x2,y1,z),(x2,cy,z+rise),(x1,cy,z+rise)],colors[1])
    else:
        face([(x1,y2,z),(x2,y2,z),(cx,y2,z+rise)],'#cfc19e')
        face([(x2,y1,z),(x2,y2,z),(cx,y2,z+rise),(cx,y1,z+rise)],colors[0])
        face([(x1,y1,z),(x1,y2,z),(cx,y2,z+rise),(cx,y1,z+rise)],colors[1])

def door(cx,y,w=.48,height=.95):
    face([(cx-w/2,y,.06),(cx+w/2,y,.06),(cx+w/2,y,height),(cx-w/2,y,height)],'#345569')

def mill():
    cx,cy=-.65,.1;r=.55
    # Back half first, then visible half, all using the same camera.
    sides=[]
    for i in range(16):
        a=i*math.tau/16;b=(i+1)*math.tau/16
        x1,y1=cx+r*math.cos(a),cy+r*math.sin(a)
        x2,y2=cx+r*math.cos(b),cy+r*math.sin(b)
        sides.append(((x1+x2+y1+y2)/2,[(x1,y1,0),(x2,y2,0),(x2,y2,2.5),(x1,y1,2.5)],i))
    for _,points,i in sorted(sides): face(points,('#c6bea2','#aca58f','#928e7a','#ded3b2')[i//4])
    for _,points,i in sorted(sides): face([points[2],points[3],(cx,cy,3.1)],'#965f44' if i<8 else '#684b3b')
    # Sails lie in the vertical front-wall plane, not in the screen plane.
    for i in range(4):
        a=math.pi/4+i*math.pi/2
        vertices=[]
        for radius,offset in ((.12,-.07),(1.15,-.13),(1.15,.13),(.12,.07)):
            vertices.append((cx+radius*math.cos(a)+offset*math.sin(a),cy+r+.08,2.3+radius*math.sin(a)-offset*math.cos(a)))
        face(vertices,'#b69a62')

for i,name in enumerate(('house','warehouse','sawmill','quarry','farm','ranch','market','construction')):
    origin=(i%4*CELL+224,i//4*CELL+BASE)
    w,depth=(2,2) if name=='house' else (3,2) if name in ('sawmill','quarry','market','construction') else (3,3)
    # Contact plane only: no separate floating terrain tile.
    face([(-w/2,-depth/2,0),(w/2,-depth/2,0),(w/2,depth/2,0),(-w/2,depth/2,0)],'#776b53')
    if name in ('house','warehouse','ranch'):
        wall=1.3 if name=='house' else 1.15
        colors=('#c9b997','#958c7d','#d1c19f') if name!='ranch' else ('#845840','#634b3b','#a27651')
        box(0,0,w,depth,0,wall,colors)
        door(-.1,depth/2+.015,.8 if name!='house' else .4)
        roof(0,0,w+.12,depth+.12,wall,.85,name!='house',('#ad914f','#ccad65') if name=='ranch' else ('#636c70','#8c999a') if name=='warehouse' else ('#934f37','#b7754e'))
        if name=='house':box(-.5,-.5,.3,.3,1.6,.85)
    elif name=='sawmill':
        for x in (-1.3,1.3):
            for y in (-.8,.8):line((x,y,0),(x,y,1.45),width=7)
        box(0,.3,1.8,.65,.2,.55,('#9b7c4c','#705b3b','#bea36c'))
        roof(0,0,3,2,1.45,.8,False,('#665843','#928568'))
    elif name=='quarry':
        box(.5,-.25,1.7,1.4,0,.95)
        roof(.5,-.25,1.9,1.6,.95,.7,True,('#626e75','#98a5a6'))
        line((-1.15,-.7,0),(-1.15,-.7,2.6),width=10)
        line((-1.15,-.7,2.6),(-1.15,1.05,2.6),width=9)
        line((-1.15,-.7,1.35),(-1.15,1.05,2.6),width=5)
        line((-1.15,.8,2.6),(-1.15,.8,1.15),color='#373b35',width=2)
        for x,y in ((-.8,.85),(.2,.85),(.8,.85)):box(x,y,.45,.5,0,.5)
    elif name=='farm':
        box(.6,0,1.55,2.3,0,.95)
        door(.7,1.16)
        roof(.6,0,1.7,2.45,.95,.7,False,('#ac904b','#d2b66c'))
        mill()
    elif name=='market':
        for x in (-.9,.9):
            for y in (-.8,.45):line((x,y,0),(x,y,1.6),width=6)
        a=[(-1,-.9,1.6),(1,-.9,1.6),(1,.6,1.6),(-1,.6,1.6)]
        for j in range(4):face([a[j],a[(j+1)%4],(0,-.15,2.5)],('#816646','#635a45','#a68a5b','#74654a')[j])
        for x in (-.75,.75):
            box(x,.77,.95,.5,0,.6,('#937349','#6f583d','#b89c6c'))
            face([(x-.5,.4,1.35),(x+.5,.4,1.35),(x+.5,1.2,1),(x-.5,1.2,1)],'#7890a1')
    else:
        for x,y,ww,dd in ((0,-.96,3,.12),(0,.96,3,.12),(-1.44,0,.12,2),(1.44,0,.12,2)):
            box(x,y,ww,dd,0,.45)
        for x in (-1.4,1.4):
            for y in (-.9,.9):line((x,y,0),(x,y,1.7),width=7)
        for y in (-.9,.9):line((-1.4,y,1.55),(1.4,y,1.55),width=6)
        for x in (-1.4,1.4):line((x,-.9,1.55),(x,.9,1.55),width=6)

path=Path(__file__).resolve().parents[1]/'art-source/building-projection-guide.png'
image.save(path)
print(path)
