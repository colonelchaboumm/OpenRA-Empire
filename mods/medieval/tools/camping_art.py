"""Editable camp buildings, animated sheep, workers and stock icons on the 48x24 grid."""
from pathlib import Path
import math
from PIL import Image, ImageDraw
from wilderness_art import save
from animation_art import unit_frame, UNIT_MODES
from isometric_art import zone_ground, zone_size
from camp_building_art import building_frame, CAMP_BUILDING_FRAMES

MOD = Path(__file__).resolve().parents[1]

def building(name, frame=0, site=False, working=True):
    if name in ('sheepfold','tentweaver') and not site:
        return building_frame(name,frame,working)
    im=Image.new('RGBA',(184,184));d=ImageDraw.Draw(im)
    width,height=(2,2) if name=='militarytent' else (3,2)
    def p(x,y,z=0):return (round(92+(x-width/2-y+height/2)*24),round(140+(x-width/2+y-height/2)*12-z))
    def poly(points,color):d.polygon([p(*a) for a in points],fill=color,outline='#443b2c')
    def line(points,color,width=1):d.line([p(*a) for a in points],fill=color,width=width)
    poly([(0,0),(width,0),(width,height),(0,height)],'#897e56')
    if site:
        for x,y in ((0,0),(width,0),(0,height),(width,height)):line([(x,y),(x,y,22)],'#ae8756',3)
        line([(0,height,15),(width,height,15)],'#e0bd81',3)
        return im
    if name=='militarytent':
        # Octagonal pavilion: a high centre pole, coloured canvas gores and valance.
        # Magenta shades use md-livery's hue band and become the country's colour.
        ring=[(.35,.13),(1.65,.13),(1.87,.35),(1.87,1.65),(1.65,1.87),(.35,1.87),(.13,1.65),(.13,.35)]
        wave=math.sin(frame*math.tau/8)
        for x,y in ((.08,.08),(1.92,.08),(.08,1.92),(1.92,1.92)):
            line([(x,y),(x,y,7)],'#8a643c',2)
        # Rear guy ropes are behind the cloth.
        for a,b in ((ring[0],(.08,.08)),(ring[1],(1.92,.08))):line([(*a,30),(*b,3)],'#d8c492')
        for i in (0,1,7,2,6,3,5,4):
            a,b=ring[i],ring[(i+1)%8]
            for j in range(4):
                t=j/4;u=(j+1)/4
                ax,ay=a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t
                bx,by=a[0]+(b[0]-a[0])*u,a[1]+(b[1]-a[1])*u
                colour=('#c44fc4' if i<4 else '#953d95') if j%2==0 else ('#eee1bb' if i<4 else '#c8bb97')
                poly([(ax,ay,2),(bx,by,2),(bx,by,31),(ax,ay,31)],colour)
                line([(ax,ay,4),(ax,ay,30)],'#a69073')
        # Dark entrance and tied-back coloured curtains on the front plane.
        poly([(.62,1.88,1),(1.37,1.88,1),(1.37,1.88,29),(.62,1.88,29)],'#342f2a')
        poly([(.57,1.895,1),(.7,1.895,15),(.57,1.895,29),(1,1.895,29),(.82,1.895,14)],'#da73da')
        poly([(1.43,1.895,1),(1.3,1.895,15),(1.43,1.895,29),(1,1.895,29),(1.18,1.895,14)],'#aa41aa')
        line([(.61,1.91,15),(.8,1.91,14)],'#dfbf6b',2);line([(1.2,1.91,14),(1.4,1.91,15)],'#dfbf6b',2)
        peak=(1,1,68)
        for i in (0,1,7,2,6,3,5,4):
            a,b=ring[i],ring[(i+1)%8]
            for j in range(4):
                t=j/4;u=(j+1)/4
                ax,ay=a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t
                bx,by=a[0]+(b[0]-a[0])*u,a[1]+(b[1]-a[1])*u
                colour=('#e078e0' if i in (0,6,7) else '#b04ab0') if j%2==0 else ('#f2e8c6' if i in (0,6,7) else '#d4c6a1')
                poly([peak,(ax,ay,32),(bx,by,32)],colour)
                # Hanging scallops and golden seam distinguish this from a simple field tent.
                poly([(ax,ay,32),(bx,by,32),((ax+bx)/2,(ay+by)/2,27+wave*.6)],colour)
                line([peak,(ax,ay,32)],'#e2c387')
            line([(*a,32),(*b,32)],'#e2c387',2)
        for a,b in ((ring[5],(.08,1.92)),(ring[3],(1.92,1.92))):line([(*a,31),(*b,3)],'#dfcca0')
        line([(1,1,67),(1,1,85)],'#bea15a',2)
        poly([(1,1,83),(1.55,1,81+wave*2),(1.38,1,76+wave),(1,1,76)],'#d565d5')
        line([(1,1,84),(1,1,86)],'#ffe2a0',3)
    return im

def sheepzone(w,h,state,frame,moving_livestock=False):
    im=zone_ground(w,h,True).copy();d=ImageDraw.Draw(im)
    def p(x,y,z=0):return (round(4+h*24+(x-y)*24),round(20+(x+y)*12-z))
    def fence(a,b):
        for z in (4,9):d.line([p(*a,z),p(*b,z)],fill='#ae8b57',width=2)
        for i in range(round((abs(b[0]-a[0])+abs(b[1]-a[1]))*2)+1):
            n=max(1,round((abs(b[0]-a[0])+abs(b[1]-a[1]))*2));t=i/n
            x=a[0]+(b[0]-a[0])*t;y=a[1]+(b[1]-a[1])*t
            d.line([p(x,y),p(x,y,13)],fill='#c9a473',width=2)
    fence((.08,.08),(w-.08,.08));fence((.08,.08),(.08,h-.08))
    count=max(1,w*h//4);cols=math.ceil(math.sqrt(count*w/h));rows=math.ceil(count/cols)
    if state!=3 and not moving_livestock:
        for i in range(count):
            phase=frame*math.tau/8+i*1.7
            x=.45+(w-.9)*((i%cols)+.5)/cols;y=.45+(h-.9)*((i//cols)+.5)/rows
            px,py=p(x,y);bob=round(math.sin(phase));side=1 if i%2 else -1
            d.ellipse((px-8,py-3,px+9,py+2),fill=(30,40,25,75))
            for dx in (-5,5):d.line((px+dx,py,px+dx+round(math.sin(phase+dx)),py-6),fill='#51493d',width=2)
            d.ellipse((px-9,py-14+bob,px+8,py-4+bob),fill='#dad8bb' if state==0 else '#b9bba7',outline='#6e7260')
            for dx in (-5,0,5):d.arc((px+dx-3,py-14+bob,px+dx+4,py-6+bob),180,355,fill='#f2eed8')
            hx=px+side*8;hy=py-8+round(math.sin(phase)*2)
            d.ellipse((hx-3,hy-4,hx+3,hy+3),fill='#7c7766',outline='#464c3e')
            d.line((hx-5,hy-3,hx+4,hy-3),fill='#9c957c',width=2);d.point((hx+side,hy-1),fill='#232c23')
    fence((w-.08,.08),(w-.08,h-.08));gate=w//2
    fence((.08,h-.08),(gate,h-.08))
    if gate+1<w-.08:fence((gate+1,h-.08),(w-.08,h-.08))
    fence((gate,h-.08),(gate+.55,h-.7))
    return im

def main():
    seq=[];rules=[];contact=Image.new('RGBA',(736,368),'#68764e')
    for i,name in enumerate(('sheepfold','tentweaver','militarytent')):
        frames=([building(name,working=False)]+[building(name,f) for f in range(CAMP_BUILDING_FRAMES)]
                if name!='militarytent' else [building(name,f) for f in range(8)])+[building(name,site=True)]
        save(name,frames);contact.alpha_composite(frames[2],(i*184,0))
        seq += [f'md.{name}:','\tDefaults:',f'\t\tFilename: medieval|art/{name}.png','\t\tOffset: 0, -48','\tidle:']
        if name=='militarytent':seq += ['\t\tLength: 8','\t\tTick: 160']
        else:seq += ['\tworking:','\t\tStart: 1',f'\t\tLength: {CAMP_BUILDING_FRAMES}','\t\tTick: 120']
        seq += ['\tconstruction:',f'\t\tStart: {len(frames)-1}','']
    torch=Image.new('RGBA',(64,72));d=ImageDraw.Draw(torch);d.ellipse((25,57,39,63),fill='#7f8068',outline='#484e3c');d.line((32,60,32,30),fill='#6b4a2d',width=3);d.line((31,59,31,31),fill='#bc9562');d.polygon([(27,29),(37,29),(35,35),(29,35)],fill='#52584c')
    save('torch',[torch,torch]);contact.alpha_composite(torch,(590,100))
    seq += ['md.torch:','\tDefaults:','\t\tFilename: medieval|art/torch.png','\t\tOffset: 0, -24','\tidle:','\tconstruction:','\t\tStart: 1','']
    icons=[]
    for name in ('wool','tent'):
        im=Image.new('RGBA',(16,16));d=ImageDraw.Draw(im)
        if name=='wool':
            for x,y in ((5,6),(10,5),(11,10),(5,10)):d.ellipse((x-4,y-4,x+4,y+4),fill='#e3dcc4',outline='#8e9079')
            d.arc((4,4,12,12),30,300,fill='#adac92')
        else:
            d.polygon([(1,13),(7,2),(15,12)],fill='#d6cba3',outline='#655a3e');d.polygon([(4,13),(7,6),(10,13)],fill='#5a5948');d.line((7,2,15,12),fill='#a59370')
        icons.append(im)
    save('campgoods',icons);seq+=['md.campgoods:','\tDefaults:','\t\tFilename: medieval|art/campgoods.png','\twool:','\ttent:','\t\tStart: 1','\tcloth:','\t\tFilename: medieval|art/cloth-icon.png','']
    for name,base in (('shepherd','shepherd'),('weaver','hauler')):
        frames=[unit_frame(base,f,s,m) for m in UNIT_MODES for f in range(8) for s in range(8)]
        save(name,frames);seq += [f'md.{name}:','\tDefaults:',f'\t\tFilename: medieval|art/{name}.png','\t\tOffset: 0, -12']
        for i,m in enumerate(UNIT_MODES):seq += [f'\t{m}:',f'\t\tStart: {i*64}','\t\tLength: 8','\t\tFacings: 8','\t\tTick: 120']
        seq+=['']
    for w in range(2,11):
        for h in range(2,11):
            name='sheeppasture'+('' if (w,h)==(2,2) else f'.{w}x{h}')
            frames=[sheepzone(w,h,s,f) for s in range(4) for f in range(8 if s<3 else 1)];save(name,frames)
            seq += [f'md.{name}:','\tDefaults:',f'\t\tFilename: medieval|art/{name}.png','\t\tOffset: 0, -6','\t\tZOffset: -8c0']
            for s,n in enumerate(('idle','growing','empty','construction')):seq += [f'\t{n}:',f'\t\tStart: {s*8}',f'\t\tLength: {8 if s<3 else 1}','\t\tTick: 180']
            seq += ['']
            if w==4 and h==4:contact.alpha_composite(frames[0],(65,195))
            if (w,h)==(2,2):continue
            fp=[''.join('x' if (x in (0,w-1) or y in (0,h-1)) and (x,y)!=(w//2,h-1) else '=' for x in range(w)) for y in range(h)]
            rules += [f'md.{name}:','\tInherits: md.sheeppasture','\tBuilding:',f'\t\tDimensions: {w},{h}',f'\t\tFootprint: {" ".join(fp)}','\tMedievalBuilding:',f'\t\tCost: {math.ceil((w+h)*6.25)}, 0, 0, {math.ceil((w+h)*1.25)}',f'\t\tWork: {w*h*3}','\tMedievalSource:',f'\t\tAmount: {max(1,w*h//4)*8}',f'\t\tCultivationWork: {math.ceil(w*h/8)}','']
    (MOD/'sequences/camping.yaml').write_text('\n'.join(seq),encoding='utf-8',newline='\n')
    (MOD/'sheep-enclosures.yaml').write_text('\n'.join(rules),encoding='utf-8',newline='\n')
    out=MOD.parents[1]/'analysis/medieval/camping';out.mkdir(parents=True,exist_ok=True);contact.save(out/'sprites.png')

if __name__=='__main__':main()
