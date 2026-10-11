"""Original articulated iron cauldron and stone firepit sprites, native 2:1 projection."""
import math
from PIL import Image, ImageDraw
from wilderness_art import save, MOD

def frame(cauldron=False, facing=0, step=0, construction=False):
    size=(128,128) if cauldron else (96,112)
    ground=96 if cauldron else 84
    scale=3
    im=Image.new('RGBA',(size[0]*scale,size[1]*scale));d=ImageDraw.Draw(im)
    angle=facing*math.tau/8
    def p(x,y,z=0):
        u=x*math.cos(angle)-y*math.sin(angle);v=x*math.sin(angle)+y*math.cos(angle)
        return round((size[0]/2+24*(u-v))*scale),round((ground+12*(u+v)-z)*scale)
    def line(points,color,width=1): d.line([p(*v) for v in points],fill=color,width=max(1,round(width*scale)))
    def poly(points,color,edge='#282921'):d.polygon([p(*v) for v in points],fill=color,outline=edge,width=scale)
    def beam(a,b,width=3):
        line([a,b],'#382e24',width+1);line([a,b],'#916b42',width)
        line([(a[0],a[1],a[2]+.7),(b[0],b[1],b[2]+.7)],'#d2ac6e',.7)
    def ring(radius,z,color,width=1):
        line([(radius*math.cos(a*math.tau/32),radius*math.sin(a*math.tau/32),z) for a in range(33)],color,width)
    if cauldron:
        # Rear wheels then chassis and bowl, finally foreground wheels. Spokes
        # rotate with the eight movement frames; crew are actual game actors.
        def wheel(x,y):
            pts=[(x+.19*math.cos(a*math.tau/32),y,6+6*math.sin(a*math.tau/32)) for a in range(33)]
            poly(pts,'#685138');line(pts,'#282e2b',2)
            for a in range(6):
                t=a*math.tau/6+step*math.tau/8
                line([(x,y,6),(x+.16*math.cos(t),y,6+5*math.sin(t))],'#c19e63',1)
            line([(x-.03,y,6),(x+.03,y,6)],'#b3b4a1',2)
        wheels=sorted([(-.52,-.52),(.52,-.52),(-.52,.52),(.52,.52)],key=lambda v:p(*v)[1])
        for x,y in wheels[:2]:wheel(x,y)
        for y in (-.47,.47):beam((-.8,y,9),(.77,y,9),4)
        for x in (-.65,-.3,0,.3,.65):beam((x,-.52,10),(x,.52,10),3)
        for y in (-.38,.38):beam((-.45,y,12),(-1.25,y,12),2)
        # Blackened iron belly, riveted bands, hot coals and rolled lip.
        for i in range(24):
            a=i*math.tau/24;b=(i+1)*math.tau/24
            poly([(.31*math.cos(a),.31*math.sin(a),12),(.50*math.cos(a),.50*math.sin(a),27),
                  (.50*math.cos(b),.50*math.sin(b),27),(.31*math.cos(b),.31*math.sin(b),12)],
                 '#565956' if i<12 else '#363d3c', '#41453f')
        ring(.38,17,'#8b8170',1.5);ring(.5,27,'#a2a292',2)
        poly([(.46*math.cos(i*math.tau/32),.46*math.sin(i*math.tau/32),27) for i in range(32)],'#4c291c')
        for i in range(24):
            a=i*2.399;radius=.10+.29*(i%7)/6
            x=radius*math.cos(a);y=radius*math.sin(a)
            poly([(x-.04,y-.04,28),(x+.06,y-.04,28),(x+.04,y+.04,28),(x-.04,y+.04,28)],
                 ['#ef8c27','#5e3120','#b3451b','#ffc259'][i%4])
        ring(.5,28,'#b6a28a',1)
        for x,y in wheels[2:]:wheel(x,y)
        for x in (-.55,.55):line([(x,-.13,22),(x*1.2,-.13,22),(x*1.2,.13,22),(x,.13,22)],'#a5a998',1.5)
    else:
        poly([(.5*math.cos(i*math.tau/24),.5*math.sin(i*math.tau/24),0) for i in range(24)],'#403a2d')
        for i in range(10):
            a=i*math.tau/10; x=.47*math.cos(a);y=.47*math.sin(a)
            poly([(x-.11,y-.11,1),(x+.11,y-.10,1),(x+.11,y+.11,1),(x-.11,y+.11,1)],'#72756b')
            poly([(x-.11,y-.11,5),(x+.11,y-.10,5),(x+.11,y+.11,5),(x-.11,y+.11,5)],'#a2a493','#595e55')
        for a,b in [((-.3,-.2,5),(.28,.2,5)),((-.3,.22,6),(.31,-.19,6)),((-.24,0,9),(.23,0,9))]:beam(a,b,4)
        if not construction:
            for i in range(13):
                a=i*2.4;x=.23*math.cos(a);y=.2*math.sin(a)
                line([(x-.035,y,7),(x+.035,y,7)],['#e36120','#f6ad35','#9e3219'][i%3],2)
    return im.resize(size,Image.Resampling.LANCZOS)

def icon(cauldron):
    im=frame(cauldron,1)
    d=ImageDraw.Draw(im);x=im.width//2;y=70 if cauldron else 79
    for dx in (-8,0,7):d.polygon([(x+dx-5,y),(x+dx-2,y-12),(x+dx+2,y-23),(x+dx+6,y-4)],fill='#f48125')
    d.polygon([(x-3,y),(x,y-15),(x+4,y)],fill='#ffe195')
    box=im.getbbox();im=im.crop(box);im.thumbnail((30,32),Image.Resampling.LANCZOS)
    out=Image.new('RGBA',(32,34));out.alpha_composite(im,((32-im.width)//2,(34-im.height)//2));return out

def main():
    save('campfire',[frame(),frame(construction=True)])
    save('cauldron',[frame(True,f,step if mode else 0) for mode in range(2) for f in range(8) for step in range(8)])
    save('fire-icons',[icon(False),icon(True)])
    (MOD/'sequences/fire-support.yaml').write_text('''md.campfire:
	Defaults:
		Filename: medieval|art/campfire.png
		Offset: 0, -28
	idle:
	construction:
		Start: 1

md.cauldron:
	Defaults:
		Filename: medieval|art/cauldron.png
		Offset: 0, -32
	idle:
		Length: 8
		Facings: 8
		Tick: 80
	run:
		Start: 64
		Length: 8
		Facings: 8
		Tick: 80

md.fire-icons:
	idle:
		Filename: medieval|art/fire-icons.png
		Length: 2
''',encoding='utf-8',newline='\n')
    print('Generated campfire, cauldron (8 directions) and icons.')

if __name__=='__main__':main()
