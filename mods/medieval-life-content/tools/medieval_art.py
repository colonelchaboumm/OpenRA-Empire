"""Deterministic game art and atlas import for Les Terres du Bourg.

The painted master is kept in art-source. Cropping, resizing and sheet packing
are asset compilation; editable terrain, animation and UI are drawn here at
their native pixel resolution. No network service is needed to rebuild.
"""
from pathlib import Path
import math
import random
from PIL import Image, ImageDraw

MOD = Path(__file__).resolve().parents[1]
INK = '#252922'
WOOD = '#65472d'
TIMBER = '#a37b47'
GOLD = '#cead69'

SIZES = {'house': (64, 76), 'warehouse': (92, 88), 'sawmill': (88, 80),
         'quarry': (88, 80), 'farm': (92, 104), 'ranch': (92, 88), 'market': (88, 88)}
OFFSETS = {'house': -10, 'warehouse': -6, 'sawmill': -12, 'quarry': -12,
           'farm': -16, 'ranch': -6, 'market': -16, 'tree': -19, 'stone': -4}
ATLAS_RECTS = {
    'house': (0, 0, 410, 420), 'warehouse': (410, 0, 852, 420),
    'sawmill': (852, 0, 1280, 420), 'quarry': (0, 420, 415, 796),
    'farm': (415, 420, 840, 796), 'ranch': (840, 420, 1280, 796),
    'market': (0, 796, 430, 1280), 'tree': (430, 796, 848, 1280),
    'stone': (848, 796, 1280, 1280)}
ROLE_COLORS = {'builder': '#ad8651', 'hauler': '#997151', 'lumberjack': '#547146',
               'stonecutter': '#8c9697', 'farmer': '#b79959', 'breeder': '#94744d',
               'militia': '#7a5b3e', 'manatarms': '#889da8', 'supplycart': '#8d704b',
               'blacksmith':'#79503c', 'recruiter':'#3c7288', 'archer':'#5c7854', 'swordsman':'#737d94',
               'elitearcher':'#49746b', 'royalguard':'#48708d', 'drummer':'#a4773c', 'messenger':'#7d5b97'}


def painted_sprite(name, size):
    """Import one isolated atlas cell, preserving its transparent silhouette."""
    atlas = Image.open(MOD / 'art-source/medieval-atlas.png').convert('RGBA')
    rect = tuple(round(v * atlas.width / 1280) for v in ATLAS_RECTS[name])
    sprite = atlas.crop(rect)
    bounds = sprite.getchannel('A').point(lambda a: 255 if a > 12 else 0).getbbox()
    if bounds is None:
        raise ValueError(f'Empty atlas cell: {name}')
    sprite = sprite.crop(bounds)
    # A single downsample gives clean detail at the game's actual 24px cell scale.
    sprite.thumbnail((size[0] - 4, size[1] - 4), Image.Resampling.LANCZOS)
    frame = Image.new('RGBA', size)
    frame.alpha_composite(sprite, ((size[0] - sprite.width) // 2, size[1] - sprite.height - 2))
    return frame


def terrain_frames(road=False):
    frames = []
    for variant in range(16):
        rng = random.Random(1301 + variant)
        im = Image.new('RGBA', (24, 24))
        for y in range(24):
            for x in range(24):
                noise = rng.randrange(-5, 6)
                patch = round(3 * math.sin(x * .29 + variant) * math.cos(y * .32))
                value = noise + patch
                grass = (86 + value, 101 + value, 58 + value, 255)
                # NESW bitmask: missing neighbours expose soft, irregular verges.
                edge = min(y if not variant & 1 else 99,
                           23 - x if not variant & 2 else 99,
                           23 - y if not variant & 4 else 99,
                           x if not variant & 8 else 99)
                if road and edge > rng.randrange(1, 5):
                    rut = -7 if (not variant & 5 and y in (7, 17)) or (not variant & 10 and x in (7, 17)) else 0
                    im.putpixel((x, y), (139 + value + rut, 122 + value + rut, 86 + value + rut, 255))
                else:
                    im.putpixel((x, y), grass)
        d = ImageDraw.Draw(im)
        if not road:
            for _ in range(4):
                x, y = rng.randrange(2, 22), rng.randrange(3, 22)
                d.line((x, y, x-1, y-2), fill='#64733f')
                d.point((x+1, y-2), fill='#758047')
        frames.append(im)
    return frames


def ui_texture():
    im = Image.new('RGBA', (128, 32))
    d = ImageDraw.Draw(im)
    for state, (base, edge) in enumerate((('#353b39', '#8e8469'), ('#48554f', '#c4b17b'),
                                         ('#202a29', '#76735f'), ('#303330', '#575a50'))):
        x = state * 24
        d.rectangle((x, 0, x+23, 23), fill='#131b1c', outline=edge)
        d.rectangle((x+2, 2, x+21, 21), fill=base)
        d.line((x+2, 2, x+21, 2), fill='#64695c' if state < 2 else '#343e39')
        d.line((x+2, 21, x+21, 21), fill='#151d1d')
        for y in range(5, 20, 4):
            d.line((x+3, y, x+20, y), fill='#383f3a' if state == 0 else base)
        for px, py in ((2, 2), (21, 2), (2, 21), (21, 21)):
            d.point((x+px, py), fill='#b2aa88' if state < 3 else '#696c5b')
    return im


def site(size):
    w, h = size
    im = Image.new('RGBA', size)
    d = ImageDraw.Draw(im)
    y = h - 27
    d.ellipse((5, y-4, w-3, h-2), fill=(24, 28, 19, 95))
    d.polygon([(6, y), (w-18, y-8), (w-4, h-12), (18, h-3)], fill='#77674d', outline='#433c2e')
    rng = random.Random(w*100+h)
    for _ in range(w):
        x, yy = rng.randrange(9, w-10), rng.randrange(y, h-9)
        d.point((x, yy), fill='#9d8b61')
    for x, yy in ((8, y), (w-18, y-7), (18, h-6), (w-7, h-13)):
        d.line((x+2, yy+3, x+10, yy+7), fill=(25, 28, 21, 80), width=3)
        d.line((x, yy, x, yy-22), fill=INK, width=4)
        d.line((x-1, yy-2, x-1, yy-23), fill=TIMBER, width=2)
    d.line((8, y-18, w-18, y-25), fill=WOOD, width=4)
    d.line((8, y-19, w-18, y-26), fill='#c29b5d')
    d.line((18, h-21, w-7, h-28), fill=TIMBER, width=3)
    d.line((8, y-1, w-18, y-25), fill='#8c683b', width=2)
    d.line((w-18, y-5, 8, y-20), fill='#ba9256')
    for i in range(3):
        d.line((w//2-8, h-9-i*3, w//2+10, h-12-i*3), fill='#b18a50', width=2)
    return im


def cow(d, x, y, adult=True, facing=1, phase=0):
    s = 1 if adult else .75
    def p(a, b): return (round(x + a*s*facing), round(y + b*s))
    def line(a, b, color, width=1): d.line((*p(*a), *p(*b)), fill=color, width=width)
    d.ellipse((x-10*s, y+4*s, x+12*s, y+10*s), fill=(24, 29, 20, 80))
    for a in (-6, 5):
        line((a, 1), (a+1, 8), '#473f32', 2)
        line((a+1, 8), (a+3, 8), '#242a24', 2)
    d.ellipse((x-9*s, y-5*s, x+8*s, y+5*s), fill='#bcae8b', outline='#4b4436')
    d.ellipse((x-8*s, y-5*s, x+6*s, y+1*s), fill='#e1d5b4')
    d.polygon([p(-7,-3),p(-3,-4),p(0,-1),p(-2,3),p(-6,2)], fill='#604b34')
    tail=math.sin(phase)*2
    line((-8,-1), (-12+tail,3), '#6c5a3e')
    line((-12+tail,3), (-11+tail,6), '#342f25')
    for a in (-5, 5): line((a, 3), (a, 9), '#b7a886', 2)
    graze=round((1-math.cos(phase))*1.5)
    d.polygon([p(6,-4),p(10,-5+graze),p(13,-2+graze),p(12,4+graze),p(8,4+graze),p(6,0)], fill='#d4c6a3', outline='#4e4533')
    line((8,-4+graze), (7,-7+graze), '#e9dec0')
    line((11,-4+graze), (12,-6+graze), '#e9dec0')
    d.point(p(11,-1+graze), fill='#1d2420')
    line((10,3+graze),(13,2+graze),'#937a58',2)


def agricultural_zone(width, height, state, cattle=False):
    w, h = width*24, height*24
    im = Image.new('RGBA', (w+8, h+8))
    d = ImageDraw.Draw(im)
    rng = random.Random(width*101+height*17+int(cattle))
    base = (86, 100, 55) if cattle else (101, 78, 47)
    for y in range(4, h+4):
        for x in range(4, w+4):
            n = rng.randrange(-8, 8)
            im.putpixel((x, y), tuple(c+n for c in base)+(255,))
    d.line((4,h+3,w+3,h+3,w+3,4), fill='#3d4128')
    if state == 3:
        for x in range(7, w+3, 12): d.line((x, 6, x, h+1), fill='#85744f')
        for x,y in ((5,6),(w+2,6),(5,h+2),(w+2,h+2)):
            d.line((x,y,x,y-5), fill=TIMBER, width=2)
        return im
    if not cattle:
        for y in range(12, h+1, 7):
            d.line((7,y,w,y), fill='#463b28', width=2)
            d.line((7,y+2,w,y+2), fill='#8c7042')
            if state < 2:
                for x in range(10, w, 5):
                    xx = x+rng.randrange(-1,2)
                    length = rng.randrange(6,10) if state == 0 else rng.randrange(3,5)
                    stem = '#b3a053' if state == 0 else '#667e3c'
                    d.line((xx,y,xx-1,y-length), fill=stem)
                    d.line((xx,y-2,xx+2,y-5), fill='#94833e' if state==0 else '#91a751')
                    if state == 0:
                        d.line((xx-1,y-length,xx-1,y-length+3), fill='#d9c479', width=2)
                        d.point((xx-2,y-length), fill='#ede0a3')
        return im
    # Worn grass, trough and fence cast shadows; the southern gate is open.
    for _ in range(width*height*4):
        x,y = rng.randrange(8,w),rng.randrange(8,h)
        d.line((x,y,x-1,y-2), fill='#75824a')
    gate = 4+(width//2)*24
    for y in (8, h):
        segments = ((5,w+2),) if y==8 else ((5,gate),(gate+24,w+2))
        for left,right in segments:
            d.line((left+3,y+5,right+3,y+5), fill=(23,28,20,90), width=3)
            for yy in (y-3,y+1):
                d.line((left,yy,right,yy), fill='#4c3b28', width=3)
                d.line((left,yy-1,right,yy-1), fill='#ba9159')
    for x in (5,w+2):
        d.line((x+3,7,x+3,h+2), fill=(23,28,20,70), width=3)
        d.line((x,5,x,h+1), fill=WOOD, width=3)
        d.line((x-1,5,x-1,h+1), fill=TIMBER)
    for x in range(5,w+3,12):
        for y in (8,h):
            if y==h and gate<x<gate+24: continue
            d.rectangle((x-1,y-8,x+1,y+3), fill='#765637', outline='#443a28')
            d.line((x-1,y-8,x-1,y+1), fill='#c1a26a')
    for y in range(20,h,12):
        for x in (5,w+2):
            d.rectangle((x-1,y-6,x+1,y+3), fill=TIMBER, outline=WOOD)
    d.line((gate,h-3,gate+9,h-12), fill=TIMBER, width=3)
    d.line((gate,h,gate+9,h-9), fill=WOOD, width=2)
    count=max(1,width*height//4)
    cols=math.ceil(math.sqrt(count*width/height));rows=math.ceil(count/cols)
    for i in range(count):
        x=int(13+(w-20)*((i%cols)+.5)/cols)
        y=int(13+(h-26)*((i//cols)+.5)/rows)
        cow(d,x,y,state==0,1 if i%3 else -1)
    # A small water trough is decorative and never claims production stock.
    d.rectangle((9, h-12, 21, h-7), fill='#565f58', outline='#3e392b')
    d.line((10,h-11,20,h-11), fill='#8bada0')
    return im


def stump():
    im=Image.new('RGBA',(60,76));d=ImageDraw.Draw(im)
    d.ellipse((18,59,44,69),fill=(23,30,18,70))
    d.polygon([(25,54),(34,53),(38,65),(20,65)],fill=WOOD,outline=INK)
    d.ellipse((24,51,35,57),fill='#b99b63',outline='#58472d')
    d.ellipse((27,53,32,55),outline='#80643a')
    return im


def resource_icon(resource, size=16):
    im=Image.new('RGBA',(16,16));d=ImageDraw.Draw(im)
    if resource==0:
        for x,y in ((2,10),(4,6),(6,2)):
            d.polygon([(x,y),(x+7,y-1),(x+10,y+2),(x+3,y+4)],fill='#85552e',outline='#362e22')
            d.line((x+1,y,x+7,y-1),fill='#bf9557')
            d.ellipse((x+5,y-1,x+10,y+3),fill='#c7a76b',outline='#76532f')
    elif resource==1:
        d.polygon([(4,5),(11,5),(14,11),(12,14),(3,14),(2,11)],fill='#b9a472',outline='#49422c')
        d.line((6,6,4,11),fill='#e0c78b',width=2)
        for x in (5,8,11):
            d.line((8,8,x,1),fill='#a58c48')
            d.line((x,1,x,4),fill='#e4ca70',width=2)
    elif resource==2:
        d.polygon([(2,6),(5,2),(10,3),(13,7),(10,11),(5,12),(2,10)],fill='#a95f46',outline='#4e3329')
        d.line((5,4,8,4,11,7),fill='#d68c69',width=2)
        d.line((10,10,14,14),fill='#e2d2a8',width=2)
        d.ellipse((12,12,15,15),fill='#e4dab8')
    else:
        for x,y in ((1,8),(7,9),(4,3)):
            d.polygon([(x,y),(x+2,y-3),(x+6,y-2),(x+7,y+3),(x+2,y+5)],fill='#86928c',outline='#343f3d')
            d.line((x,y,x+2,y-3,x+6,y-2),fill='#c6cdc0')
    return im if size==16 else im.resize((size,size),Image.Resampling.NEAREST)


def pile(resource, level):
    im=Image.new('RGBA',(32,28))
    d=ImageDraw.Draw(im)
    d.ellipse((1,18,30,27),fill=(20,26,18,90))
    icon=resource_icon(resource)
    for i in range(level):
        im.alpha_composite(icon, (2+(i%2)*11, 11-(i//2)*7))
    return im


def unit_frame(name, facing, step, attacking=False):
    """Hand-pixelled eight-direction gait with distinct tools and shaded bodies."""
    im=Image.new('RGBA',(44,44));d=ImageDraw.Draw(im)
    angle=facing*math.pi/4
    dx,dy=math.sin(angle),-math.cos(angle)
    side=1 if dx>=0 else -1
    rear=dy<-.25
    profile=abs(dx)>.9
    stride=(-2,0,2,0)[step]
    bob=step%2
    x,y=22,27+bob
    shirt=ROLE_COLORS[name]
    d.ellipse((14,32,32,37),fill=(17,24,18,90))
    for s in (-1,1):
        hip=(x+s*2,26+bob)
        foot=(round(x+s*2+dx*stride*s),round(34+dy*stride*s*.55))
        d.line((*hip,*foot),fill='#343c35',width=3)
        d.line((hip[0]-1,hip[1],foot[0]-1,foot[1]-1),fill='#6b6250')
        d.line((foot[0]-1,foot[1],foot[0]+2,foot[1]),fill='#272b25',width=2)
    half=3 if profile else 4
    d.polygon([(x-half,17+bob),(x+half,17+bob),(x+half+1,27+bob),(x-half-1,27+bob)],fill=shirt,outline=INK)
    d.line((x-half+1,19+bob,x-half+1,24+bob),fill='#c8b184' if name not in ('manatarms','lumberjack') else '#a6b6a8',width=2)
    d.line((x+half,19+bob,x+half,26+bob),fill='#555341')
    d.line((x-half,25+bob,x+half,25+bob),fill='#473c2d',width=2)
    d.point((x,25+bob),fill='#c8ad67')
    # Cobalt cloth detail is visible in every direction, tying the army together.
    d.line((x-2,18+bob,x+1,19+bob),fill='#5d89a0',width=2)
    arms=[]
    for s in (-1,1):
        shoulder=(x+s*(half+1),19+bob)
        hand=(round(x+s*(half+3)+dx*stride*s*.5),round(25+bob+dy*stride*s))
        d.line((*shoulder,*hand),fill=INK,width=3)
        d.line((shoulder[0],shoulder[1],hand[0],hand[1]-1),fill=shirt,width=2)
        d.point(hand,fill='#d0ae7e')
        arms.append(hand)
    # Neck, shaded head and nose reflect the actual movement direction.
    d.rectangle((x-1,15+bob,x+1,18+bob),fill='#ac815a')
    d.ellipse((x-3,9+bob,x+3,16+bob),fill='#98714b' if rear else '#cda775',outline=INK)
    d.line((x-2,10+bob,x+1,10+bob),fill='#674f35',width=2)
    if rear: d.rectangle((x-2,11+bob,x+2,14+bob),fill='#6b5136')
    else:
        d.point((x+side*2,13+bob),fill='#343a2b')
        d.point((x+side*3,14+bob),fill='#e0bc85')
    hand=arms[1 if side>0 else 0]
    hx,hy=hand
    if name in ('farmer','breeder'):
        d.ellipse((x-6,10+bob,x+6,13+bob),fill='#bb9d5b',outline='#5a4e31')
        d.rectangle((x-3,8+bob,x+2,11+bob),fill='#c5ab69',outline='#746038')
        tx=hx+side*2
        d.line((tx,13,tx,34),fill='#60482c',width=2)
        d.line((tx-1,14,tx-1,33),fill='#bd9b60')
        if name=='farmer':
            for j in (-2,0,2): d.line((tx+j,9,tx+j,14),fill='#a8aca0')
            d.line((tx-2,14,tx+2,14),fill='#92978c')
        else: d.arc((tx-4,8,tx+1,14),180,340,fill='#be9a5d',width=2)
    elif name=='hauler':
        # Empty shoulder basket; actual carried resources are drawn from inventory.
        bx=x-4 if rear else x+4
        d.polygon([(bx,17),(bx+6,18),(bx+6,27),(bx,26)],fill='#8b693e',outline='#433c2b')
        for yy in (20,23): d.line((bx+1,yy,bx+5,yy+1),fill='#c0a270')
        d.line((x-3,18,x+2,24),fill='#c1ac7e')
    elif name in ('builder','lumberjack','stonecutter'):
        tx=hx+side*2
        d.line((tx,28,tx+side*2,13),fill='#c2a477',width=2)
        if name=='builder':
            d.rectangle((tx-3,11,tx+4,14),fill='#869089',outline='#323c36')
            d.line((tx-2,11,tx+3,11),fill='#d3d6bc')
            d.rectangle((x-2,26,x+2,29),fill='#6f5637',outline='#3c382b')
        elif name=='lumberjack':
            d.polygon([(tx+1,11),(tx+6*side,9),(tx+7*side,15),(tx+1,15)],fill='#939e96',outline='#3d4840')
            d.line((tx+6*side,10,tx+7*side,14),fill='#d4ddca')
        else:
            d.line((tx-5,15,tx,11,tx+5,13),fill='#bac3b4',width=2)
        if name=='stonecutter':
            d.line((x-3,10+bob,x+3,10+bob),fill='#a3b0ac',width=3)
    else:
        military=name=='manatarms'
        if military:
            d.polygon([(x-4,10+bob),(x-2,7+bob),(x+2,7+bob),(x+4,11+bob),(x+3,14+bob),(x-3,14+bob)],fill='#7d9098',outline='#313c3d')
            d.line((x-2,8+bob,x+1,8+bob),fill='#e0e1ce')
            d.line((x-3,12+bob,x+3,12+bob),fill='#293334')
            d.line((x,9+bob,x,14+bob),fill='#bdc9c3')
        swing=(-1.6,-.8,.35,1.0)[step] if attacking else -.9
        tip=(round(hx+side*math.cos(swing)*11),round(hy+math.sin(swing)*11))
        d.line((hx,hy,*tip),fill=INK,width=3)
        d.line((hx,hy,*tip),fill='#cfddd4' if military else '#aa8b52',width=1 if military else 2)
        if military: d.line((hx-2,hy-1,hx+2,hy+1),fill='#c4a970')
        else: d.ellipse((tip[0]-2,tip[1]-2,tip[0]+2,tip[1]+2),fill='#8e7650',outline='#33372d')
        sx=x-side*6
        d.polygon([(sx-3,19),(sx+3,19),(sx+3,25),(sx,29),(sx-3,25)],fill='#49748d' if military else '#8f703f',outline='#25352e')
        d.line((sx-2,20,sx-2,25,sx,27),fill='#b2beb0' if military else '#c8aa6a')
        d.line((sx,21,sx,25),fill='#cfb870')
    return im


def cart_frame(facing,step):
    # Enough headroom for the pusher when travelling south, and for all tools.
    im=Image.new('RGBA',(64,72));d=ImageDraw.Draw(im)
    angle=facing*math.pi/4
    def p(f,s,z=0): return (round(32+math.sin(angle)*f+math.cos(angle)*s),round(38-math.cos(angle)*f*.7+math.sin(angle)*s*.7-z))
    d.ellipse((13,36,53,54),fill=(18,26,19,85))
    for side in (-9,9):
        x,y=p(0,side)
        d.ellipse((x-3,y-3,x+3,y+6),fill='#463d2e',outline='#202a24',width=2)
        d.ellipse((x-2,y-2,x+2,y+5),outline='#a28b5c')
        d.line((x-2,y+(step%2),x+2,y+3-(step%2)),fill='#c5ac72')
        d.line((*p(-6,side/2,3),*p(-17,side/2,3)),fill='#ae8e54',width=2)
    d.polygon([p(f,s,4) for f,s in ((-7,-7),(8,-7),(8,7),(-7,7))],fill='#8e7042',outline='#302f23')
    for f in (-5,-1,3,7): d.line((*p(f,-7,4),*p(f,7,4)),fill='#b99a60')
    for side in (-7,7):
        d.polygon([p(-7,side,4),p(8,side,4),p(8,side,10),p(-7,side,10)],fill='#8e6c3b',outline='#3d3425')
        d.line((*p(-7,side,10),*p(8,side,10)),fill='#d1b375')
        for f in (-6,7): d.line((*p(f,side,3),*p(f,side,11)),fill='#514b37',width=2)
    # Pusher animated in the same eight directions; the bed is empty until loaded.
    human=unit_frame('hauler',facing,step)
    x,y=p(-18,0)
    im.alpha_composite(human,(x-22,y-33))
    return im
