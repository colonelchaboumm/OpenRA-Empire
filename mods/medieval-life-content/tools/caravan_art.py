"""Native isometric drawing: covered founding wagon, three settlers, eight facings."""
from pathlib import Path
import math
from PIL import Image, ImageDraw, PngImagePlugin
from animation_art import unit_frame

MOD=Path(__file__).resolve().parents[1]

def frame(facing,step,moving):
    im=Image.new('RGBA',(104,104));d=ImageDraw.Draw(im)
    a=-facing*math.tau/8;phase=step*math.tau/8 if moving else 0
    bounce=round(math.sin(phase*2)*.7)
    def p(f,s,z=0):return round(52+math.sin(a)*f+math.cos(a)*s),round(67-math.cos(a)*f*.5+math.sin(a)*s*.5-z)
    d.ellipse((22,57,84,80),fill=(20,28,18,75))
    crew=[(-24,-6),(-22,9),(-30,0)]
    def person(f,s,index):
        x,y=p(f,s);im.alpha_composite(unit_frame('hauler' if index==2 else 'builder',facing,step,'push' if moving else 'push-stand'),(x-28,y-38))
    if math.cos(a)>0:
        for i,(f,s) in enumerate(crew):person(f,s,i)
    for side in (-11,11):
        for f in (-9,10):
            x,y=p(f,side,1)
            d.ellipse((x-4,y-5,x+4,y+7),fill='#443528',outline='#251f18',width=2)
            d.ellipse((x-3,y-4,x+3,y+6),outline='#b7a073')
            for k in range(4):
                t=phase+k*math.pi/4;d.line((x,y+1,x+round(math.cos(t)*3),y+1+round(math.sin(t)*5)),fill='#c5ae79')
        d.line((*p(-13,side/2,6),*p(-27,side/2,6)),fill='#b9955d',width=2)
    faces=[(-14,-10),(15,-10),(15,10),(-14,10)]
    d.polygon([p(f,s,6+bounce) for f,s in faces],fill='#a17c47',outline='#4b3928')
    for side in (-10,10):
        d.polygon([p(-14,side,6+bounce),p(15,side,6+bounce),p(15,side,15+bounce),p(-14,side,15+bounce)],fill='#9e7542',outline='#4b3928')
        d.line((*p(-14,side,13+bounce),*p(15,side,13+bounce)),fill='#dab17a',width=2)
    # Arched canvas canopy. Segment depth order is stable in every direction.
    panels=[]
    for i in range(8):
        s0=-10+i*2.5;s1=s0+2.5
        z0=17+11*math.sqrt(max(0,1-(s0/10)**2))+bounce;z1=17+11*math.sqrt(max(0,1-(s1/10)**2))+bounce
        points=[p(-13,s0,z0),p(14,s0,z0),p(14,s1,z1),p(-13,s1,z1)]
        panels.append((sum(q[1] for q in points),i,points))
    for _,i,points in sorted(panels):
        color=('#b9ad8d','#d1c4a3','#e1d6b6','#e8ddbc','#e1d5b4','#d8ccaa','#c6b994','#ac9f7f')[i]
        d.polygon(points,fill=color)
    for f in (-12,0,13):
        points=[p(f,s,17+11*math.sqrt(max(0,1-(s/10)**2))+bounce) for s in range(-10,11,2)]
        d.line(points,fill='#9e9274',width=1)
    # Founder's blue pennant and bundled tools identify this special wagon.
    x,y=p(8,9,15);d.line((x,y,x,y-33),fill='#796040',width=2)
    wave=round(math.sin(phase+.5)*2);d.polygon([(x+1,y-33),(x+16,y-31+wave),(x+9,y-25),(x+1,y-26)],fill='#5e86a5',outline='#304c61')
    if math.cos(a)<=0:
        for i,(f,s) in enumerate(crew):person(f,s,i)
    return im

def main():
    frames=[frame(f,s,moving) for moving in (False,True) for f in range(8) for s in range(8)]
    sheet=Image.new('RGBA',(104*16,104*8))
    for i,im in enumerate(frames):sheet.alpha_composite(im,((i%16)*104,(i//16)*104))
    meta=PngImagePlugin.PngInfo();meta.add_text('FrameSize','104,104');meta.add_text('FrameAmount','128')
    sheet.save(MOD/'art/caravan.png',pnginfo=meta)
    text='md.caravan:\n\tDefaults:\n\t\tFilename: medieval|art/caravan.png\n\t\tOffset: 0, -15\n'
    for name in ('stand','run','work','harvest','carry-stand','carry-run','load','attack','push','push-stand'):
        text+=f'\t{name}:\n\t\tStart: {64 if name in ("run","carry-run","push") else 0}\n\t\tFacings: 8\n\t\tLength: 8\n\t\tTick: 120\n'
    (MOD/'sequences/founding.yaml').write_text(text,encoding='utf-8',newline='\n')

if __name__=='__main__':main()
