"""Registered stationary plates plus articulated mechanisms and a shopkeeper.

Only the moving parts are redrawn per pose. Foundations and roofs never move.
"""
import math
from PIL import Image, ImageDraw
from isometric_art import MOD, SIZES, painted_sprite

BUILDING_FRAMES = 24
ANIMATED_BUILDINGS = ('sawmill', 'quarry', 'market', 'marketstall')


def mechanism(name, frame, working):
    # Supersampling gives small iron teeth and ropes the painted atlas's soft edge.
    scale = 3
    im = Image.new('RGBA', (184*scale, 184*scale))
    d = ImageDraw.Draw(im)
    def p(x, y): return round(x*scale), round(y*scale)
    def line(points, fill, width=1): d.line([p(*a) for a in points], fill=fill, width=max(1, round(width*scale)))
    def poly(points, fill, outline=None): d.polygon([p(*a) for a in points], fill=fill, outline=outline, width=scale)
    def ellipse(box, fill, outline=None, width=1):
        d.ellipse((*p(*box[:2]), *p(*box[2:])), fill=fill, outline=outline, width=max(1, round(width*scale)))
    phase = frame / BUILDING_FRAMES * math.tau
    if name == 'sawmill':
        # The blade sits on the original spindle, in an upright oblique plane.
        cx, cy = 56, 124
        angle = phase/3
        def wheel(a, radius=1): return cx+math.cos(a)*7.3*radius, cy+math.sin(a)*14.5*radius+math.cos(a)*1.5*radius
        teeth = [wheel(angle+i*math.tau/96, 1.07 if i % 4 == 0 else .95) for i in range(96)]
        poly(teeth, '#758078', '#303c36')
        ellipse((cx-6.2, cy-12.7, cx+6.2, cy+12.7), '#9ca69a', '#b5bead', .45)
        for i in range(3):
            a=angle+i*math.tau/3
            line([wheel(a,.25),wheel(a,.81)], '#424e46', 1.45)
            line([wheel(a+.075,.29),wheel(a+.075,.79)], '#d3d7be', .55)
        ellipse((cx-2.1,cy-3.4,cx+2.1,cy+3.4),'#393f36','#d5c6a0',.6)
        line([(cx,cy),(cx+5.7,cy+2.5)],'#524833',2.2)
        ellipse((cx+4,cy+.5,cx+7,cy+4.5),'#b6a077','#383d32',.6)
        if working:
            # A few chips arc away from the cutting point; no stock is represented.
            for i in range(8):
                t=((frame+i*3)%BUILDING_FRAMES)/BUILDING_FRAMES
                x=63+t*(8+i%3*3); y=133+t*11-math.sin(t*math.pi)*(6+i%4)
                alpha=round(220*(1-t))
                line([(x,y),(x+1.2,y+.4)],(203,164,96,alpha),.65)
    elif name == 'quarry':
        # Ease up, hold, lower and settle. The head of the crane remains fixed.
        t=frame/BUILDING_FRAMES
        lift=0 if t<.12 or t>.92 else (.5-.5*math.cos((t-.12)/.3*math.pi)) if t<.42 else 1 if t<.55 else (.5+.5*math.cos((t-.55)/.37*math.pi))
        lift=lift if working else 0
        sway=math.sin(phase*2)*1.2*lift
        top=(40,77); hook=(40+sway,115-25*lift)
        line([(top[0]-1,top[1]+1),(hook[0]-1,hook[1])], '#493c2b', 1.45)
        line([(top[0]+1,top[1]+1),(hook[0]+1,hook[1])], '#c1a873', .75)
        # A winding drum turns as the load rises and descends.
        ellipse((65,89,76,102),'#61482c','#302e22',.8)
        ellipse((66,90,75,101),'#9c7946','#cca96b',.6)
        for i in range(4):
            a=lift*math.tau+i*math.pi/2
            line([(70.5,95.5),(70.5+math.cos(a)*3.7,95.5+math.sin(a)*5)],'#d3b57b',.8)
        ellipse((69.3,94.3,71.7,96.7),'#393b30')
        ellipse((37.5,74,42.5,80),'#a58b52','#443c2c',.75)
        line([(38.5,76.8),(41,77.8)],'#e0c58a',.65)
        line([hook,(hook[0]+1,hook[1]+3),(hook[0]-1,hook[1]+4)],'#55584b',1)
        if working:
            x,y=hook[0],hook[1]+6
            poly([(x-6,y-2),(x,y-5),(x+7,y-1),(x+1,y+2)],'#d0c8ad','#706e5b')
            poly([(x-6,y-2),(x+1,y+2),(x+1,y+10),(x-6,y+6)],'#a6a28c','#626554')
            poly([(x+1,y+2),(x+7,y-1),(x+7,y+7),(x+1,y+10)],'#bcbba5','#626554')
            line([(x-6,y-2),(x,y-5),(x+7,y-1)],'#e5ddc3',.7)
            # The lifting sling stays attached to the block throughout the cycle.
            line([hook,(x-4,y+1),(x-4,y+7),(x+1,y+10),(x+5,y+8),(x+5,y+1),hook],'#7b6139',.75)
    else:
        # Upper body behind the left stall. The counter hides the apron below.
        lean=math.sin(phase)*1.1 if working else 0
        x,y=64+lean,123
        poly([(x-4,y+4),(x-2,y+1),(x+3,y+1),(x+5,y+6),(x+3,y+14),(x-3,y+14)],'#526c75','#283c38')
        poly([(x-2,y+5),(x+2,y+5),(x+3,y+14),(x-3,y+14)],'#c0ad83','#796c4e')
        # One hand sorts produce; the other presents an item over the counter.
        reach=(.5-.5*math.cos(phase)) if working else 0
        left=(x-5-reach*5,y+10-reach*2)
        right=(x+5-reach*3,y+8+reach*3)
        for shoulder, elbow, hand in [((x-3,y+4),(x-6,y+6),left),((x+3,y+4),(x+6,y+5),right)]:
            line([shoulder,elbow,hand],'#2e3f3c',2.7)
            line([shoulder,elbow,hand],'#809093',1.5)
            ellipse((hand[0]-1,hand[1]-1,hand[0]+1,hand[1]+1),'#c8a171')
        ellipse((x-2.7,y-5,x+2.6,y+1.5),'#ccaa7d','#564833',.6)
        line([(x-2.5,y-3.5),(x+2,y-3)],'#6c5539',1)
        poly([(x-3,y-5),(x-1,y-7),(x+2,y-6),(x+3,y-3),(x-3,y-3)],'#ad7c43','#56432b')
        line([(x-3,y-3),(x+3,y-2.7)],'#dbc28d',.8)
        ellipse((x-1.9,y-1.5,x-1.3,y-.9),'#303b32')
        line([(x-1,y+.2),(x+.7,y+.5)],'#77523e',.45)
        if working and reach>.35:
            ellipse((left[0]-1.4,left[1]-.5,left[0]+1.6,left[1]+1.8),'#b09a42','#655c2d',.4)
    return im.resize((184,184),Image.Resampling.LANCZOS)


def building_frames(name):
    if name == 'marketstall':
        from stall_art import stall_frame
        return [stall_frame(0)] + [stall_frame(i) for i in range(BUILDING_FRAMES)]
    atlas=MOD/'art-source/buildings-animation-base.png' if name in ('sawmill','quarry') else None
    base=painted_sprite(name,SIZES[name],atlas)
    result=[]
    for frame in range(BUILDING_FRAMES+1):
        im=base.copy()
        im.alpha_composite(mechanism(name,max(0,frame-1),frame>0))
        if name == 'market':
            # The produce and front boards occlude the seller's lower body.
            # These foreground pixels are identical in every pose.
            foreground=Image.new('L',base.size)
            ImageDraw.Draw(foreground).polygon([(21,124),(42,132),(48,130),(58,132),(74,125),(77,133),(57,148),(23,139)],fill=255)
            im.paste(base,(0,0),foreground)
        result.append(im)
    return result
