"""Editable isometric village furniture, four-legged livestock and near fence layers."""
import math
import random
from PIL import Image, ImageDraw
from isometric_art import painted_sprite, zone_size

DECORS = {
    'plaza': ('Placette pavée',2,0,12,4,False,0),
    'plazalarge': ('Place pavée',3,0,27,9,False,0),
    'statueknight': ('Statue de chevalier',1,5,30,15,True,65),
    'statuelion': ('Lion de pierre',1,4,24,12,True,40),
    'plantedoak': ('Chêne sur pavés',1,12,6,8,True,76),
    'plantedcypress': ('Cyprès sur pavés',1,10,6,8,True,80),
}

def furnishing(name, construction=False):
    im=Image.new('RGBA',(192,176));d=ImageDraw.Draw(im);size=DECORS[name][1]
    def p(x,y,z=0):return (round(96+(x-y)*24),round(120+(x+y)*12-z))
    def face(points,color,outline=None):d.polygon([p(*v) for v in points],fill=color,outline=outline)
    def block(x0,y0,x1,y1,z,h):
        face([(x0,y1,z),(x1,y1,z),(x1,y1,z+h),(x0,y1,z+h)],'#8a887c','#55594f')
        face([(x1,y0,z),(x1,y1,z),(x1,y1,z+h),(x1,y0,z+h)],'#777e75','#55594f')
        face([(x0,y0,z+h),(x1,y0,z+h),(x1,y1,z+h),(x0,y1,z+h)],'#c3c1ac','#6a7265')
    if construction:
        face([(-size/2,-size/2),(size/2,-size/2),(size/2,size/2),(-size/2,size/2)],(106,88,62,160),'#c1aa7b')
        for x,y in ((-size/2,-size/2),(size/2,-size/2),(size/2,size/2),(-size/2,size/2)):
            d.line([p(x,y),p(x,y,9)],fill='#ccac73',width=2)
        for i in range(4):block(-.3+i*.14,-.2,-.18+i*.14,.05,0,3)
        return im
    # Same quarter-cell paving grid, projection and stone palette as stonepath.
    face([(-size/2,-size/2),(size/2,-size/2),(size/2,size/2),(-size/2,size/2)],'#615e4e')
    rng=random.Random(103)
    for y in range(size*4):
        for x in range(size*4):
            x0=-size/2+x/4+.012;y0=-size/2+y/4+.012;x1=x0+.22;y1=y0+.22
            points=[p(x0,y0),p(x1,y0),p(x1,y1),p(x0,y1)]
            d.polygon(points,fill=rng.choice(['#999686','#aaa593','#8d8c7f','#b1ad99','#989b91']))
            d.line(points[:2],fill='#c4bfaa');d.point(p(x0+.08,y0+.09),fill='#7a7b6d')
    if name.startswith('plaza'):
        r=size/2-.06
        d.line([p(-r,-r),p(r,-r),p(r,r),p(-r,r),p(-r,-r)],fill='#d0c4a6',width=2)
        if size==3:
            for i in range(8):
                a=i*math.tau/8;b=a+math.pi/8
                face([(0,0),(.58*math.cos(a),.58*math.sin(a)),(.3*math.cos(b),.3*math.sin(b))], '#d0c4a6' if i%2 else '#747970')
        return im
    if name.startswith('planted'):
        block(-.34,-.34,.34,.34,0,4)
        face([(-.27,-.27,4),(.27,-.27,4),(.27,.27,4),(-.27,.27,4)],'#493f2f','#d7c5a0')
        if name=='plantedoak':
            tree=painted_sprite('tree',(96,116));tree=tree.crop(tree.getbbox());tree.thumbnail((68,82),Image.Resampling.LANCZOS)
            im.alpha_composite(tree,(96-tree.width//2,122-tree.height))
        else:
            d.line((96,119,96,70),fill='#614d33',width=3)
            for row in range(9):
                y=111-row*8;r=round(12*(1-row/11));
                d.polygon([(96-r,y),(96+r,y),(100+r//2,y-10),(96,y-22),(92-r//2,y-11)],fill=('#415d36','#4e6d3d','#567644')[row%3],outline='#354b2d')
                for j in range(7):
                    x=96+rng.randint(-max(1,r-2),max(1,r-2));yy=y-rng.randint(2,11)
                    d.line((x,yy,x-1,yy-3),fill=rng.choice(['#809258','#72904e','#425e35']))
        return im
    # Square pedestal, chamfered capital and weathered joints.
    block(-.35,-.35,.35,.35,0,4);block(-.27,-.27,.27,.27,4,11);block(-.32,-.32,.32,.32,15,4)
    d.line([p(-.27,.27,10),p(.27,.27,10)],fill='#656d63')
    if name=='statueknight':
        def poly(points,c,edge='#626c65'):d.polygon(points,fill=c,outline=edge)
        # Boots, draped tunic, sculpted plate armor and closed visor.
        poly([(88,99),(95,99),(94,88),(89,88)],'#a1aa9d')
        poly([(97,99),(105,97),(101,87),(96,88)],'#808e85')
        poly([(86,89),(105,89),(102,74),(90,73)],'#b4baa7')
        d.line((94,75,93,88),fill='#d9d7ba',width=2);d.line((100,77,102,88),fill='#798879',width=2)
        poly([(87,73),(103,71),(102,61),(98,56),(90,58),(87,64)],'#aeb7a9')
        d.line((91,60,94,70),fill='#d4d8bd',width=2)
        poly([(91,55),(100,55),(102,48),(98,43),(92,44),(89,49)],'#b8c0ab')
        d.line((92,50,100,49),fill='#5e6f67');d.line((96,46,96,54),fill='#dfe0c5')
        d.line((89,62,84,73),fill='#788b7e',width=5)
        poly([(82,67),(92,66),(92,80),(86,87),(81,79)],'#969f8a')
        d.line((86,70,86,81),fill='#d7d4b3',width=2);d.line((82,75,90,74),fill='#d7d4b3')
        d.line((104,62,108,76),fill='#bac0a8',width=4)
        d.line((108,73,109,98),fill='#777f75',width=3);d.line((108,76,108,98),fill='#d9d9ba')
        d.line((104,77,112,77),fill='#afb49d',width=2)
    else:
        d.ellipse((82,78,105,93),fill='#a9af98',outline='#687565')
        d.line((87,88,82,99),fill='#899780',width=5);d.line((100,86,106,96),fill='#d2cdb0',width=5)
        d.ellipse((82,70,97,88),fill='#7d8a70',outline='#616e5e')
        for a in range(0,360,45):
            x=89+round(math.cos(math.radians(a))*6);y=79+round(math.sin(math.radians(a))*7)
            d.line((x,y,x-1,y+3),fill='#c2bd9e',width=2)
        d.ellipse((84,71,94,82),fill='#ccc6a7',outline='#7b866e')
        d.ellipse((82,77,89,82),fill='#afb49a');d.point((85,77),fill='#465d4b')
        d.arc((101,78,114,94),270,490,fill='#929e85',width=3)
    return im

def livestock(kind,facing,frame,moving):
    im=Image.new('RGBA',(64,56));d=ImageDraw.Draw(im)
    a=facing*math.tau/8;co,si=math.cos(a),math.sin(a);phase=frame*math.tau/8;cow=kind=='cow'
    def p(u,v,z=0):return (round(32+u*co-v*si),round(44+(u*si+v*co)*.52-z))
    def line(points,c,w=1):d.line([p(*t) for t in points],fill=c,width=w)
    def cap(a,b,r,c):
        aa,bb=p(*a),p(*b);d.line([aa,bb],fill=c,width=2*r+1)
        for x,y in (aa,bb):d.ellipse((x-r,y-r,x+r,y+r),fill=c)
    ink='#4d4c38';coat='#d5c9a4' if cow else '#dcdac0';shade='#9b9274' if cow else '#adb19b'
    bob=abs(math.sin(phase))* .7 if moving else math.sin(phase)*.3
    d.ellipse((19,40,47,47),fill=(29,36,22,60))
    def leg(u,v):
        t=phase+(0 if u*v>0 else math.pi);stride=math.sin(t)*3 if moving else 0;lift=max(0,math.cos(t))*2 if moving else 0
        line([(u,v,11+bob),(u+stride*.4,v,5+lift*.4),(u+stride,v,lift)],ink,3)
        line([(u,v,10+bob),(u+stride,v,lift+1)],shade,2)
    legs=sorted([(u,v) for u in (-7,7) for v in (-3,3)],key=lambda t:t[0]*si+t[1]*co)
    for u,v in legs[:2]:leg(u,v)
    cap((-7,0,13+bob),(7,0,13+bob),6,ink)
    cap((-7,0,14+bob),(7,0,14+bob),5,coat)
    if cow:
        cap((-4,0,15+bob),(-1,1,17+bob),3,'#896d48');cap((5,-1,13+bob),(7,-1,14+bob),3,'#92724a')
        line([(-12,0,13),(-15,math.sin(phase)*3,9),(-14,math.sin(phase)*3,5)],'#796342',2)
    else:
        for u in (-7,-3,1,5,8):
            for v in (-2,2):cap((u,v,16+bob),(u+1,v,17+bob),2,'#efead1' if v<0 else '#c7ccb5')
    for u,v in legs[2:]:leg(u,v)
    grazing=not moving and frame in (2,3,4,5,6)
    hu=14 if grazing else 11;hz=(6 if grazing else 18)+bob
    cap((8,0,13+bob),(hu,0,hz),3,shade)
    cap((hu,0,hz),(hu+4,0,hz-2),3,'#bea781' if cow else '#7c806c')
    cap((hu+4,0,hz-2),(hu+5,0,hz-2),2,'#bba88a' if cow else '#626a58')
    for s in (-1,1):
        line([(hu,0,hz+1),(hu-1,s*5,hz+1)],shade,2)
        if cow:line([(hu-1,s*2,hz+3),(hu-3,s*3,hz+5)],'#e1cf9d',2)
    x,y=p(hu+2,2 if co>=0 else -2,hz+1);d.point((x,y),fill='#273d2b')
    return im

def near_fence(w,h,sheep=False):
    im=Image.new('RGBA',zone_size(w,h));d=ImageDraw.Draw(im)
    def p(x,y,z=0):return (round(4+h*24+(x-y)*24),round(20+(x+y)*12-z))
    def rail(a,b):
        for z in (4,9):
            d.line([p(*a,z),p(*b,z)],fill='#483a29' if not sheep else '#ae8b57',width=3 if not sheep else 2)
            if not sheep:d.line([p(*a,z+1),p(*b,z+1)],fill='#c19b63')
        n=math.ceil((abs(b[0]-a[0])+abs(b[1]-a[1]))*2)
        for i in range(n+1):
            t=i/max(1,n);x=a[0]+(b[0]-a[0])*t;y=a[1]+(b[1]-a[1])*t
            d.line([p(x,y),p(x,y,13)],fill='#b8945d' if not sheep else '#c9a473',width=2)
    rail((w-.08,.08),(w-.08,h-.08));gate=w//2
    rail((.08,h-.08),(gate,h-.08))
    if gate+1<w-.08:rail((gate+1,h-.08),(w-.08,h-.08))
    rail((gate,h-.08),(gate+.55,h-.7))
    return im
