"""Editable siege mechanisms projected from jointed timber models.

The painted workshop master comes from image_gen. Its alpha is preserved;
mechanisms, eight directions and all animation frames are code-native.
"""
import math
from functools import lru_cache
from pathlib import Path
from PIL import Image, ImageDraw, PngImagePlugin
from animation_art import unit_frame
from trebuchet_art import frame as trebuchet_frame, FRAME as TREBUCHET_FRAME, LENGTHS as TREBUCHET_LENGTHS, OFFSET as TREBUCHET_OFFSET

MOD = Path(__file__).resolve().parents[1]
NAMES = {'siegeworkshop': 'Atelier de siège', 'ram': 'Bélier',
         'trebuchet': 'Trébuchet', 'stonecart': 'Charrette de pierres'}
FRAME = (224, 224)
WORKSHOP_FRAMES = 16
RAM_FIRE_FRAMES = 24
MODES = {'ram': ('idle', 'run', 'fire'), 'trebuchet': ('idle', 'run', 'assemble', 'deployed', 'fire'),
         'stonecart': ('idle', 'run')}

@lru_cache(maxsize=1)
def workshop_base():
    source = Image.open(MOD/'art-source/siege-workshop-isometric.png').convert('RGBA')
    # Register the centre of the measured foundation, not the bounding box or roof.
    # At source width 1280 the 4 x 3 foundation is 904px wide, centred on (632,793).
    scale = 168 / (904 * source.width / 1280)
    crop = source.resize((round(source.width*scale), round(source.height*scale)), Image.Resampling.LANCZOS)
    frame = Image.new('RGBA', (224, 224))
    frame.alpha_composite(crop, (round(112-632*source.width/1280*scale), round(164-793*source.height/1280*scale)))
    return frame

def workshop_motion(product=-1, stage=0, step=0, ordered=False):
    # Only articulated yard parts change. The painted foundation, posts and roof stay registered.
    scale=3
    im=Image.new('RGBA',(224*scale,224*scale)); d=ImageDraw.Draw(im)
    def p(x,y,z=0): return (round((112+24*(x-y))*scale),round((164+12*(x+y)-z)*scale))
    def line(points,color,width=1): d.line([p(*q) for q in points],fill=color,width=max(1,round(width*scale)))
    def beam(a,b,width=3):
        line([a,b],'#403326',width+1.3);line([a,b],'#9f7445',width)
        line([(a[0],a[1],a[2]+.6),(b[0],b[1],b[2]+.6)],'#d3ac71',max(.5,width/4))
    def poly(points,color): d.polygon([p(*q) for q in points],fill=color,outline='#493929',width=scale)
    def wheel(x,y,z,angle=0):
        cx,cy=p(x,y,z);rx=3.3*scale;ry=4.6*scale
        d.ellipse((cx-rx,cy-ry,cx+rx,cy+ry),fill='#987344',outline='#3b3025',width=scale)
        for i in range(4):
            a=angle+i*math.pi/2
            d.line((cx,cy,cx+math.cos(a)*rx*.8,cy+math.sin(a)*ry*.8),fill='#d3b37a',width=scale)
        d.ellipse((cx-scale,cy-scale,cx+scale,cy+scale),fill='#484b3e')
    phase=step/WORKSHOP_FRAMES*math.tau
    # Pulley and rope attach to the empty beam on the painted master.
    top=(.55,.8,40)
    hook=(.55+(.028*math.sin(phase) if ordered or product>=0 else 0),.8,22+(7*math.sin(phase) if product>=0 else 0))
    wheel(*top,phase if product>=0 else 0)
    line([top,hook],'#483a27',1.6)
    line([(.57,.8,38),(hook[0]+.02,.8,hook[2])],'#d3bb88',.55)
    line([hook,(hook[0]+.06,.8,hook[2]-2),(hook[0]-.04,.8,hook[2]-3)],'#6c746a',1.2)
    if ordered:
        # A suspended work pennant makes a requested order visible even before deliveries arrive.
        sway=.045*math.sin(phase)
        flutter=.85*math.cos(phase)
        line([(-.2,.8,36),(-.2,.8,23)],'#c4ae7e',.6)
        poly([(-.2,.8,32),(.12,.8+sway,32+flutter),(.12,.8+sway,24+flutter),(-.04,.8,26),(-.2,.8,24)],'#446c85')
        line([(-.12,.8,30),(.05,.8+sway,27+flutter)],'#e5c06c',.85)
    if product>=0:
        # One workshop animation represents the builders currently working on the real order.
        artisan=unit_frame('builder',6,step//2,'work').resize((56*scale,56*scale),Image.Resampling.NEAREST)
        ax,ay=p(-.55,.9)
        im.alpha_composite(artisan,(ax-28*scale,ay-38*scale))
        # Chassis on two trestles; later stages fit the wheels and the product-specific parts.
        for x in (-.1,1.15):
            for y in (.6,1.15): beam((x,y,1),(x,y,7),1.8)
            beam((x,.55,7),(x,1.2,7),2)
        for y in (.66,1.1): beam((-.3,y,10),(1.35,y,10),3)
        for x in (-.12,.3,.75,1.2): beam((x,.63,10),(x,1.13,10),2)
        # The hoist lifts a cross member into place without wobbling the building itself.
        z=hook[2]-4
        beam((hook[0]-.28,.8,z),(hook[0]+.3,.8,z),2.5)
        line([hook,(hook[0]-.24,.8,z),(hook[0]+.26,.8,z),hook],'#c1a473',.65)
        if stage>=1:
            for x in (-.08,1.12):
                for y in (.55,1.2): wheel(x,y,6)
            if product==0:
                beam((-.35,.88,18),(1.5,.88,18),5)
                for x in (-.1,1.15):
                    for y in (.62,1.12): beam((x,y,12),(x,y,29),2)
                if stage==2:
                    for y in (.55,1.2): poly([(-.28,.875,35),(1.35,.875,35),(1.35,y,28),(-.28,y,28)],'#948257')
            elif product==1:
                for y,z in ((.66,16),(.92,19),(1.07,16)): beam((-.4,y,z),(1.5,y,z),3)
                if stage==2:
                    for x in (.1,1): line([(x,.58,23),(x,1.16,23)],'#d0b58b',1.4)
                    poly([(.32,.63,28),(.76,.63,28),(.76,1.03,28),(.32,1.03,28)],'#647c84')
                    poly([(.32,1.03,28),(.76,1.03,28),(.76,1.03,20),(.32,1.03,20)],'#806345')
            else:
                for x in (0,1.2):
                    for y in (.63,1.13): beam((x,y,10),(x,y,24),1.7)
                for y in (.63,1.13):
                    beam((0,y,16),(1.2,y,16),2)
                    if stage==2: beam((0,y,22),(1.2,y,22),2)
        # A handful of wood chips moves beside the chassis with the carpenter's blows.
        for i in range(4):
            t=((step+i*3)%WORKSHOP_FRAMES)/WORKSHOP_FRAMES
            x=-.25-t*.45;y=1.0+t*.35;z=9+math.sin(t*math.pi)*5-t*8
            line([(x,y,z),(x+.03,y,z+.4)],(218,180,108,round(180*(1-t))),.6)
    return im.resize((224,224),Image.Resampling.LANCZOS)

def workshop_frames():
    base=workshop_base()
    frame=base.copy(); frame.alpha_composite(workshop_motion())
    site = Image.new('RGBA', (224, 224)); d = ImageDraw.Draw(site)
    def p(x,y,z=0): return (round(112+24*(x-y)),round(164+12*(x+y)-z))
    d.polygon([p(-1.9,-1.4),p(1.9,-1.4),p(1.9,1.4),p(-1.9,1.4)],fill=(83,63,39,140),outline='#c9ac79')
    for x in (-1.7,1.7):
        for y in (-1.2,1.2): d.line((p(x,y),p(x,y,24)),fill='#c89d69',width=3)
    for i in range(9): d.line((p(-1.3,-.8+i*.2,4),p(1.1,-.8+i*.2,4)),fill='#b2834d',width=4)
    frames=[frame,site]
    for step in range(WORKSHOP_FRAMES):
        im=base.copy();im.alpha_composite(workshop_motion(step=step,ordered=True));frames.append(im)
    for product in range(3):
        for stage in range(3):
            for step in range(WORKSHOP_FRAMES):
                im=base.copy();im.alpha_composite(workshop_motion(product,stage,step,True));frames.append(im)
    return frames

def ram_thrust(step):
    # Contact is pose 10 (tick 14 of a 32-tick stroke), matching RamMotion.
    keys=((0,0),(6,-16),(10,18),(13,5),(16,8),(23,0))
    for (a,x),(b,y) in zip(keys,keys[1:]):
        if step<=b:
            u=max(0,min(1,(step-a)/(b-a)))
            return x+(y-x)*u*u*(3-2*u)
    return 0

def engine_frame(name, facing, step, mode):
    if name=='trebuchet': return trebuchet_frame(facing,step,mode)
    im=Image.new('RGBA',FRAME); d=ImageDraw.Draw(im)
    angle=-facing*math.tau/8
    dx,dy=math.sin(angle),-math.cos(angle)
    moving=mode=='run'
    def p(f,s,z=0):
        return (round(112+dx*f-dy*s),round(174+(dy*f+dx*s)*.5-z))
    def beam(a,b,width=5,color='#976a3b'):
        d.line([p(*a),p(*b)],fill='#392c20',width=width+2)
        d.line([p(*a),p(*b)],fill=color,width=width)
        aa,bb=p(*a),p(*b)
        d.line([(aa[0]-1,aa[1]-1),(bb[0]-1,bb[1]-1)],fill='#c49960',width=max(1,width//4))
        for pt in (aa,bb): d.ellipse((pt[0]-1,pt[1]-1,pt[0]+1,pt[1]+1),fill='#4d4940')
    def poly(points,fill,outline='#423222'):
        d.polygon([p(*q) for q in points],fill=fill,outline=outline)
    def box(f,s,z,l,w,h):
        poly([(f,s,z),(f+l,s,z),(f+l,s,z+h),(f,s,z+h)],'#79522f')
        poly([(f+l,s,z),(f+l,s+w,z),(f+l,s+w,z+h),(f+l,s,z+h)],'#69482c')
        poly([(f,s,z+h),(f+l,s,z+h),(f+l,s+w,z+h),(f,s+w,z+h)],'#bc9057')
    def wheel(f,s):
        x,y=p(f,s,8); rx=4+abs(dx)*2
        d.ellipse((x-rx,y-9,x+rx,y+9),fill='#473326',outline='#292923',width=2)
        d.ellipse((x-rx+2,y-7,x+rx-2,y+7),fill='#976c3e',outline='#c09963')
        phase=step*math.tau/8 if moving else 0
        for a in range(3):
            a=phase+a*math.pi/3
            d.line((x+math.cos(a)*(rx-1),y+math.sin(a)*7,x-math.cos(a)*(rx-1),y-math.sin(a)*7),fill='#3d3426')
        d.ellipse((x-2,y-2,x+2,y+2),fill='#c9b080',outline='#403a2e')
    def operator(f=-60,s=0,work=False):
        human=unit_frame('builder' if work else 'hauler',facing,step,'work' if work else 'push' if moving else 'push-stand')
        x,y=p(f,s); im.alpha_composite(human,(x-28,y-38))
    # A transparent contact shadow, not a terrain tile.
    poly([(-36,-19,0),(36,-19,0),(36,19,0),(-36,19,0)],(20,23,16,55),None)
    if name=='ram':
        for f in (-20,20):
            for s in sorted((-17,17),key=lambda s:dx*s): wheel(f,s)
        for s in (-13,13): beam((-28,s,12),(28,s,12),5)
        for f in (-22,22): beam((f,-17,13),(f,17,13),5)
        thrust=ram_thrust(step) if mode=='fire' else 0
        lift=4*(thrust/18)**2
        for s in (-12,12):
            for f in (-22,22): beam((f,s,13),(f,s,44),5)
        for f in (-16,16):
            d.line((p(f,0,49),p(f+thrust,0,24+lift)),fill='#483b28',width=3)
            d.line((p(f,0,49),p(f+thrust,0,24+lift)),fill='#c8b689',width=1)
        beam((-28+thrust,0,25+lift),(33+thrust,0,25+lift),12,'#89613a')
        box(28+thrust,-7,19+lift,10,14,12)
        # Steel striking face and its raised rim catch the light during the swing.
        poly([(38+thrust,-7,19+lift),(38+thrust,7,19+lift),(38+thrust,7,31+lift),(38+thrust,-7,31+lift)],'#798075','#303931')
        d.line((p(38+thrust,-7,31+lift),p(38+thrust,7,31+lift)),fill='#c4c7ad',width=2)
        for f in (-17+thrust,15+thrust,33+thrust):
            d.line((p(f,-6,22+lift),p(f,0,31+lift),p(f,6,22+lift)),fill='#52564e',width=3)
        # Pitched hide roof, blue edging and steel bands.
        for s in sorted((-19,19),key=lambda s:dx*s):
            poly([(-31,0,56),(31,0,56),(31,s,43),(-31,s,43)],'#8c7854' if s<0 else '#766345')
            for f in range(-28,32,10): beam((f,0,56),(f,s,43),2,'#a38e65')
            beam((-32,s,42),(32,s,42),3,'#496b7e')
        beam((-33,0,57),(33,0,57),3)
    else:
        # Dedicated stone supply wagon. The articulated trebuchet has its own model.
        operator()
        for s in sorted((-16,16),key=lambda s:dx*s):
            for f in (-13,15): wheel(f,s)
        for f in range(-23,25,6): box(f,-13,13,5,26,3)
        for s in (-14,14):
            beam((-30,s,13),(-56,s,13),3)
            beam((-25,s,15),(26,s,15),4)
        for s in (-14,14):
            for z in (21,28): beam((-25,s,z),(26,s,z),4)
        for f in (-25,26):
            for s in (-14,14): beam((f,s,13),(f,s,34),3)
    return im

def frames(name):
    return [engine_frame(name,facing,step,mode) for mode in MODES[name] for facing in range(8) for step in range(sequence_length(name,mode))]

def sequence_length(name,mode): return TREBUCHET_LENGTHS[mode] if name=='trebuchet' else RAM_FIRE_FRAMES if name=='ram' and mode=='fire' else 8

def sequences():
    seq=['md.siegeworkshop:','\tDefaults:','\t\tFilename: medieval|art/siegeworkshop.png','\t\tOffset: 0, -52','\tidle:','\tconstruction:','\t\tStart: 1',
         '\tordered:','\t\tStart: 2',f'\t\tLength: {WORKSHOP_FRAMES}','\t\tTick: 120']
    for product in range(3):
        for stage in range(3):
            seq += [f'\twork-{product}-{stage}:',f'\t\tStart: {2+WORKSHOP_FRAMES*(1+product*3+stage)}',f'\t\tLength: {WORKSHOP_FRAMES}','\t\tTick: 120']
    seq.append('')
    for name,modes in MODES.items():
        offset=TREBUCHET_OFFSET if name=='trebuchet' else (0,-62)
        seq += [f'md.{name}:','\tDefaults:',f'\t\tFilename: medieval|art/{name}.png',f'\t\tOffset: {offset[0]}, {offset[1]}']
        start=0
        for mode in modes:
            length=sequence_length(name,mode)
            seq += [f'\t{mode}:',f'\t\tStart: {start}','\t\tFacings: 8',f'\t\tLength: {length}','\t\tTick: 80']
            start+=length*8
        seq.append('')
    seq+=['md.siegestone:','\tidle:','\t\tFilename: medieval|art/siegestone.png','']
    return seq

def stone():
    im=Image.new('RGBA',(16,16));d=ImageDraw.Draw(im)
    d.polygon([(4,3),(10,2),(14,7),(11,13),(4,13),(2,8)],fill='#888b7d',outline='#343b32')
    d.polygon([(4,3),(10,2),(11,6),(4,8)],fill='#c0c3aa')
    d.line((4,8,9,11,11,6),fill='#616a5c',width=2)
    return im

if __name__ == '__main__':
    poses=frames('ram')
    sheet=Image.new('RGBA',(FRAME[0]*len(poses),FRAME[1]))
    for i,pose in enumerate(poses): sheet.paste(pose,(i*FRAME[0],0))
    meta=PngImagePlugin.PngInfo()
    meta.add_text('FrameSize',','.join(map(str,FRAME)))
    meta.add_text('FrameAmount',str(len(poses)))
    sheet.save(MOD/'art/ram.png',pnginfo=meta)
    print(f'Compiled ram: {len(poses)} poses, {RAM_FIRE_FRAMES} striking frames in each of 8 directions.')
