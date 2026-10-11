"""Village furnishings, using the world's exact 48 x 24 isometric cell axes."""
import math
import random
from pathlib import Path
from PIL import Image, ImageDraw
from isometric_art import painted_sprite

FRAME=(96,112)
NAMES={'well':'Puits','gardenfence':'Clôture de jardin','gardenfencey':'Clôture de jardin',
       'gardengate':'Portillon ouvert','gardengatey':'Portillon ouvert','flowerbed':'Massif de fleurs',
       'banner':'Drapeau','stonepath':'Chemin de pierre'}
# Wood, stone, work, blocks movement, selection height.
SPECS={'well':(15,25,20,True,58),'gardenfence':(3,0,3,True,16),'gardenfencey':(3,0,3,True,16),
       'gardengate':(5,0,4,False,16),'gardengatey':(5,0,4,False,16),
       'flowerbed':(4,0,3,False,12),'banner':(6,0,5,False,50),'stonepath':(0,3,2,False,0)}

def point(x,y,z=0): return (round(48+24*(x-y)),round(84+12*(x+y)-z))

def decoration(name,phase=0):
    im=Image.new('RGBA',FRAME); d=ImageDraw.Draw(im)
    if name=='well':
        src=Image.open(Path(__file__).resolve().parents[1]/'art-source/well-isometric.png').convert('RGBA')
        src=src.crop(src.getbbox());src.thumbnail((48,62),Image.Resampling.LANCZOS)
        im.alpha_composite(src,(48-src.width//2,89-src.height))
    elif name.startswith(('gardenfence','gardengate')):
        along_y=name.endswith('y');gate=name.startswith('gardengate')
        def p(t,z=0): return point(0 if along_y else t,t if along_y else 0,z)
        d.line([p(-.5),p(.5)],fill=(30,25,17,80),width=5)
        # Gate leaf folded beside the rear post leaves the middle open.
        mask=phase or (12 if along_y else 3)
        ends=[point(x,y) for i,(x,y) in enumerate(((.5,0),(-.5,0),(0,.5),(0,-.5))) if mask & (1<<i)]
        segments=[(p(-.5),p(-.27))] if gate else [(point(0,0),end) for end in ends]
        for a,b in segments:
            for z in (5,11):
                d.line([(a[0],a[1]-z),(b[0],b[1]-z)],fill='#4c3721',width=4)
                d.line([(a[0],a[1]-z-1),(b[0],b[1]-z-1)],fill='#b09561',width=2)
        posts=[p(-.48),p(.48)] if gate else ends+[point(0,0)]
        for x,y in sorted(posts,key=lambda p:p[1]):
            d.polygon([(x-2,y),(x+2,y+1),(x+2,y-15),(x,y-17),(x-2,y-15)],fill='#795632',outline='#433320')
            d.line((x-1,y-14,x-1,y-1),fill='#ba9661');d.point((x+1,y-10),fill='#32291e')
    elif name=='stonepath':
        d.polygon([point(-.5,-.5),point(.5,-.5),point(.5,.5),point(-.5,.5)],fill='#615e4e')
        rng=random.Random(103)
        for y in range(4):
            for x in range(4):
                x0=-.5+x/4+.012;y0=-.5+y/4+.012;x1=x0+.22;y1=y0+.22
                corners=[point(x0,y0),point(x1,y0),point(x1,y1),point(x0,y1)]
                d.polygon(corners,fill=rng.choice(['#999686','#aaa593','#8d8c7f','#b1ad99','#989b91']))
                d.line(corners[:2],fill='#bdb8a3');d.point(point(x0+.08,y0+.09),fill='#7a7b6d')
    elif name=='flowerbed':
        corners=[point(-.4,-.4),point(.4,-.4),point(.4,.4),point(-.4,.4)]
        d.polygon(corners,fill='#503b27',outline='#a08b57')
        d.line([corners[1],corners[2],corners[3]],fill='#6b5235',width=3)
        im.alpha_composite(painted_sprite('flowers',(38,30)),(29,60))
        for x,y,c in [(40,73,'#dda1c0'),(50,77,'#eccc68'),(56,73,'#d47eab'),(43,80,'#f5ebca')]:
            d.ellipse((x-1,y-1,x+1,y+1),fill=c)
    elif name=='banner':
        d.ellipse((44,82,57,87),fill=(32,27,20,65))
        d.line((48,84,48,35),fill='#4e3925',width=3);d.line((47,82,47,35),fill='#c7a570')
        d.ellipse((46,32,49,35),fill='#dfbd69');wave=phase*math.tau/12
        for x in range(23):
            sway=round(math.sin(wave-x*.27)*x/13);shade=round(16*math.cos(wave-x*.27))
            d.line((49+x,37+sway,49+x,51+sway),fill=(48+shade,90+shade,134+shade,255))
            d.point((49+x,37+sway),fill='#94b3c2');d.point((49+x,51+sway),fill='#cead66')
            if 8<=x<=12:d.line((49+x,41+sway,49+x,47+sway),fill='#dfbf77')
        d.polygon([(59,38),(61,42),(59,46),(57,42)],fill='#e1c177')
    return im

def frames(name):
    poses=[decoration(name,i) for i in range(12 if name=='banner' else 16 if name.startswith('gardenfence') else 1)]
    im=Image.new('RGBA',FRAME);d=ImageDraw.Draw(im)
    d.polygon([point(-.42,-.42),point(.42,-.42),point(.42,.42),point(-.42,.42)],fill=(84,64,39,155),outline='#ba9a61')
    for x,y in ((-.35,-.35),(.35,-.35),(.35,.35),(-.35,.35)):
        d.line((point(x,y),point(x,y,7)),fill='#bc9960',width=2)
    d.line((point(-.22,0,1),point(.2,0,1)),fill='#88603b',width=3)
    return poses+[im]

def rules():
    text='^MDDECORATION:\n\tInherits: ^MDBUILDING\n\tMedievalDecoration:\n\t-AppearsOnRadar:\n\t-RevealsShroud:\n\tBuilding:\n\t\tDimensions: 1,1\n\tIsometricSelectable:\n\t\tPriority: 1\n\tMedievalStockDisplay:\n\t\tLabelHeight: 32\n\n'
    for name,(wood,stone,work,solid,height) in SPECS.items():
        text+=f'md.{name}:\n\tInherits: ^MDDECORATION\n\tTooltip:\n\t\tName: medieval-{name}\n\tBuilding:\n\t\tFootprint: {"x" if solid else "="}\n\tIsometricSelectable:\n\t\tHeight: {height}\n\t\tDecorationHeight: {max(height,4)}\n\tMedievalBuilding:\n\t\tCost: {wood}, 0, 0, {stone}\n\t\tWork: {work}\n'
        if name.startswith('gardenfence'):text+='\tMedievalGardenFence:\n'
        text+='\n'
    return text.rstrip()+'\n'

def sequences():
    lines=[]
    for name in NAMES:
        length=12 if name=='banner' else 16 if name.startswith('gardenfence') else 1
        lines += [f'md.{name}:','\tDefaults:',f'\t\tFilename: medieval|art/{name}.png','\t\tOffset: 0, -28']
        if name=='stonepath':lines+=['\t\tZOffset: -8c0']
        lines+=['\tidle:',f'\t\tLength: {length}','\t\tTick: 100','\tconstruction:',f'\t\tStart: {length}','']
    return lines
