"""48 x 24 diamond-grid artwork. Ground geometry uses one exact 2:1 projection.

Bitmap masters are only cropped/resampled/packed. Terrain, crops, fences and
construction geometry remain editable native drawings with upright objects.
"""
from pathlib import Path
from functools import lru_cache
import math
import random
from PIL import Image, ImageDraw
from medieval_art import cow

MOD = Path(__file__).resolve().parents[1]
TILE = (48, 24)
FOOTPRINTS = {'house': (2, 2), 'warehouse': (3, 3), 'sawmill': (3, 2),
              'quarry': (3, 2), 'farm': (3, 3), 'ranch': (3, 3), 'market': (3, 2), 'marketstall': (2, 1), 'forge': (3, 2), 'barracks': (3, 3)}
SIZES = {name: (184, 184) for name in FOOTPRINTS}
OFFSETS = {name: (0, -48) for name in FOOTPRINTS}
OFFSETS.update(tree=(0, -44), pine=(0, -44), birch=(0, -44), stone=(0, -12),
               shrub=(0, -8), grasses=(0, -8), flowers=(0, -8), stump=(0, -44), rubble=(0, -12))
# Canonical 448px atlas-cell coordinates. Anchors refer to the centre of the
# foundation, never to a bounding box that also includes roofs and shadows.
BUILDINGS = {
    'house': (0, (72, 140, 376, 424), (214, 336)),
    'warehouse': (1, (10, 130, 427, 448), (211, 328)),
    'sawmill': (2, (44, 138, 396, 448), (223, 345)),
    'quarry': (3, (40, 95, 432, 448), (240, 356)),
    'farm': (4, (60, 0, 448, 386), (252, 294)),
    'ranch': (5, (24, 76, 448, 400), (235, 301)),
    'market': (6, (40, 54, 447, 388), (244, 312))}
NATURE_RECTS = {'tree': (0, 0, 508, 610), 'pine': (512, 0, 914, 610),
                'birch': (928, 0, 1335, 610), 'stone': (1350, 265, 1792, 610),
                'shrub': (30, 620, 470, 890), 'grasses': (512, 620, 900, 890),
                'flowers': (950, 620, 1320, 890), 'stump': (1380, 620, 1775, 890)}
NATURE_SIZES = {'tree': (96, 116), 'pine': (96, 116), 'birch': (96, 116),
                'stone': (72, 56), 'shrub': (48, 36), 'grasses': (40, 36), 'flowers': (40, 32)}


def painted_sprite(name, size, atlas_path=None):
    if name == 'marketstall':
        from stall_art import stall_frame
        return stall_frame(0)
    if name in ('forge','barracks'):
        from military_art import military_building
        return military_building(name)
    if name in BUILDINGS:
        atlas = Image.open(atlas_path or MOD/'art-source/buildings-isometric.png').convert('RGBA')
        cell, roi, anchor = BUILDINGS[name]
        x, y = cell % 4 * 448, cell // 4 * 448
        ratio = atlas.width / 1792
        crop = atlas.crop(tuple(round(v*ratio) for v in (x+roi[0], y+roi[1], x+roi[2], y+roi[3])))
        # Shared world scale across all buildings; do not fit each roof to a box.
        scale = .36
        crop = crop.resize((round((roi[2]-roi[0])*scale), round((roi[3]-roi[1])*scale)), Image.Resampling.LANCZOS)
        frame = Image.new('RGBA', size)
        frame.alpha_composite(crop, (round(92+(roi[0]-anchor[0])*scale), round(140+(roi[1]-anchor[1])*scale)))
        return frame
    atlas = Image.open(MOD/'art-source/nature-isometric.png').convert('RGBA')
    crop = atlas.crop(tuple(round(v*atlas.width/1792) for v in NATURE_RECTS[name]))
    bounds = crop.getchannel('A').point(lambda a: 255 if a > 12 else 0).getbbox()
    crop = crop.crop(bounds)
    crop.thumbnail((size[0]-6, size[1]-8), Image.Resampling.LANCZOS)
    frame = Image.new('RGBA', size)
    frame.alpha_composite(crop, ((size[0]-crop.width)//2, size[1]-crop.height-5))
    return frame


def stump():
    frame = Image.new('RGBA', NATURE_SIZES['tree'])
    sprite = painted_sprite('stump', (32, 26))
    frame.alpha_composite(sprite, (32, 86))
    return frame


def rubble():
    """Passable, decorative remnants after the last stone has been extracted."""
    im=Image.new('RGBA',(72,56)); d=ImageDraw.Draw(im); rng=random.Random(629)
    d.ellipse((19,31,53,46),fill=(53,53,35,38))
    for i in range(16):
        x=rng.randrange(21,53); y=rng.randrange(33,44); n=rng.randrange(2,5)
        d.polygon([(x-n,y),(x,y-2),(x+n,y),(x+n-1,y+2),(x-1,y+3)],fill='#787d6c',outline='#515845')
        d.line((x-n+1,y,x,y-1,x+n-1,y),fill='#b7b8a0')
    return im


def terrain_frames(road=False):
    frames = []
    for variant in range(16):
        rng = random.Random(1301+variant)
        im = Image.new('RGBA', TILE)
        for py in range(24):
            for px in range(48):
                # Pixel-centre inverse projection: no cracks or doubled edges.
                u = (px+.5-24)/48 + (py+.5)/24
                v = (py+.5)/24 - (px+.5-24)/48
                if not (0 <= u < 1 and 0 <= v < 1):
                    continue
                n = rng.randrange(-5, 6) + round(3*math.sin(u*7+variant)*math.cos(v*7))
                edge = min(v if not variant & 1 else 9, 1-u if not variant & 2 else 9,
                           1-v if not variant & 4 else 9, u if not variant & 8 else 9)
                dirt = road and edge > .065 + rng.random()*.07
                base = (139, 122, 86) if dirt else (86, 101, 58)
                if dirt and ((variant & 5 and abs(u-.3) < .035) or (variant & 10 and abs(v-.7) < .035)):
                    n -= 7
                im.putpixel((px, py), tuple(c+n for c in base)+(255,))
        frames.append(im)
    return frames


def zone_size(w, h):
    return ((w+h)*24+8, (w+h)*12+28)


def trail_frames():
    """Soft, irregular ground stamps, sampled more finely than terrain tiles.

    Six wear depths x eight stable shapes. Low-opacity overlaps form continuous
    paths in every direction, without square tile outlines or repeating discs.
    """
    frames=[]
    for level in range(6):
        for variant in range(8):
            rng=random.Random(7409+variant*137)
            im=Image.new('RGBA',(32,20))
            phases=[rng.random()*math.tau for _ in range(4)]
            radius=8+level*.7
            for py in range(2,18):
                for px in range(2,30):
                    x=(px+.5-16)/radius
                    y=(py+.5-10)/(radius*.5)
                    angle=math.atan2(y,x)
                    edge=1+.12*math.sin(angle*3+phases[0])+.08*math.sin(angle*5+phases[1])
                    distance=math.sqrt(x*x+y*y)/edge
                    if distance>=1.16: continue
                    grain=rng.random()
                    soft=max(0,min(1,(1.13-distance)/.5))
                    alpha=round(soft*(26+level*15)*(.65+grain*.35))
                    if grain<.1: alpha//=3
                    # Trampled grass -> moss/earth -> warm compacted dirt.
                    t=level/5
                    noise=rng.randrange(-8,9)
                    color=tuple(round(a+(b-a)*t)+noise for a,b in zip((88,99,56),(137,116,76)))
                    im.putpixel((px,py),color+(alpha,))
            frames.append(im)
    return frames


@lru_cache(maxsize=4)
def zone_ground(width, height, cattle):
    im = Image.new('RGBA', zone_size(width, height))
    ox, oy = 4+height*24, 20
    rng = random.Random(width*101+height*17+int(cattle))
    base = (85, 100, 55) if cattle else (101, 78, 47)
    for py in range(20, im.height-8):
        for px in range(4, im.width-4):
            x = (px+.5-ox)/48 + (py+.5-oy)/24
            y = (py+.5-oy)/24 - (px+.5-ox)/48
            if 0 <= x < width and 0 <= y < height:
                n = rng.randrange(-7, 8)
                im.putpixel((px, py), tuple(c+n for c in base)+(255,))
    return im


def agricultural_zone(width, height, state, cattle=False, frame=0):
    im = zone_ground(width, height, cattle).copy()
    d = ImageDraw.Draw(im)
    ox, oy = 4+height*24, 20
    def p(x, y, z=0): return (round(ox+(x-y)*24), round(oy+(x+y)*12-z))
    rng = random.Random(width*101+height*17+int(cattle))
    phase = frame * math.tau / 8
    d.line([p(0, height), p(width, height), p(width, 0)], fill='#42432c')
    if not cattle or state == 3:
        for row in range(round(height*4)):
            y = .15+row*.25
            d.line([p(.10, y), p(width-.1, y)], fill='#493d2a')
            d.line([p(.10, y+.055), p(width-.1, y+.055)], fill='#947543')
    if state == 3:
        for x,y in ((0,0),(width,0),(0,height),(width,height)):
            d.line([p(x,y),p(x,y,9)], fill='#b9945e', width=2)
        return im
    if not cattle:
        if state < 2:
            stalks = [(x*.19+.15, y*.26+.22) for x in range(max(1, int((width-.3)/.19)))
                      for y in range(max(1, int((height-.3)/.26)))]
            for x,y in sorted(stalks, key=lambda c: sum(c)):
                px,py = p(x+rng.uniform(-.025,.025),y)
                length = rng.randrange(6,10) if state == 0 else rng.randrange(3,5)
                sway=round(math.sin(phase+x*.7+y*.45)*(1.6 if state==0 else .8))
                d.line((px,py,px+sway,py-length), fill='#b7a258' if state == 0 else '#72913d')
                d.line((px,py-2,px+2+sway,py-5), fill='#9b893f' if state == 0 else '#97b855')
                if state == 0:
                    d.line((px+sway,py-length,px+sway,py-length+3), fill='#deca82', width=2)
                    d.point((px-1+sway,py-length), fill='#efe0aa')
        return im
    for _ in range(width*height*4):
        px,py = p(rng.uniform(.15,width-.15),rng.uniform(.15,height-.15))
        d.line((px,py,px-1,py-3), fill='#7e914b')
    def fence(a,b):
        for z in (4,9):
            d.line([p(*a,z),p(*b,z)], fill='#483a29', width=3)
            d.line([p(*a,z+1),p(*b,z+1)], fill='#c19b63')
        length = abs(b[0]-a[0])+abs(b[1]-a[1])
        for i in range(math.ceil(length*2)+1):
            t = i/max(1,math.ceil(length*2))
            x,y = a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t
            d.line([p(x,y),p(x,y,13)], fill='#57412b', width=3)
            d.line([p(x,y,1),p(x,y,13)], fill='#b8945d')
    # Rear rails first; upright cattle; foreground rails last.
    fence((.08,.08),(width-.08,.08))
    fence((.08,.08),(.08,height-.08))
    count=max(1,width*height//4)
    cols=math.ceil(math.sqrt(count*width/height)); rows=math.ceil(count/cols)
    animals=[]
    for i in range(count):
        x=.4+(width-.8)*((i%cols)+.5)/cols
        y=.4+(height-.8)*((i//cols)+.5)/rows
        animals.append((x,y,i))
    for x,y,i in sorted(animals,key=lambda c:c[0]+c[1]):
        px,py=p(x,y)
        cow(d,px,py-9,state==0,1 if i%3 else -1,phase+i*1.9)
    trough=[p(.3,height-.35),p(.8,height-.35),p(.8,height-.65),p(.3,height-.65)]
    d.polygon(trough, fill='#5d7770', outline='#514533')
    fence((width-.08,.08),(width-.08,height-.08))
    gate=width//2
    fence((.08,height-.08),(gate,height-.08))
    if gate+1<width-.08: fence((gate+1,height-.08),(width-.08,height-.08))
    # Open gate swings inward; its opening matches the passable footprint cell.
    fence((gate,height-.08),(gate+.55,height-.7))
    return im


def construction(name):
    w,h=FOOTPRINTS[name]
    im=Image.new('RGBA',SIZES[name]); d=ImageDraw.Draw(im)
    def p(x,y,z=0): return (round(92+(x-w/2-y+h/2)*24),round(140+(x-w/2+y-h/2)*12-z))
    corners=[(0,0),(w,0),(w,h),(0,h)]
    d.polygon([p(*c) for c in corners],fill='#887753',outline='#4b4433')
    for i in range(4):
        a,b=corners[i],corners[(i+1)%4]
        d.line([p(*a,6),p(*b,6)],fill='#c4b693',width=4)
        d.line([p(*a,7),p(*b,7)],fill='#dfcfaa')
    for a,b in ((corners[0],corners[1]),(corners[0],corners[3]),(corners[1],corners[2])):
        d.line([p(*a,31),p(*b,31)],fill='#84613b',width=3)
        d.line([p(*a,2),p(*b,28)],fill='#b99660',width=2)
    for c in corners:
        d.line([p(*c),p(*c,38)],fill='#493b2a',width=4)
        d.line([p(*c,1),p(*c,38)],fill='#be985e',width=2)
    for z in range(0,7,2): d.line([p(w*.4,h*.6,z),p(w*.8,h*.6,z)],fill='#c6a16b',width=2)
    return im
