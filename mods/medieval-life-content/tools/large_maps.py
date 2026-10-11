"""Deterministic, finite-resource continents on OpenRA's staggered isometric grid.

Run directly to regenerate these maps and their terrain, without rebuilding the
other artwork. Only Pillow is required. Coordinates in the file are CPos; the
binary terrain is stored column-major in MPos, exactly as the engine expects.
"""
from collections import Counter, deque
from pathlib import Path
import json
import math
import random
import struct
from PIL import Image, ImageDraw, ImageFilter, PngImagePlugin

MOD = Path(__file__).resolve().parents[1]
ROOT = MOD.parents[1]
WATER, BRIDGE, FOREST = 61001, 61002, 61003
SPECS = [
    ('grands-meandres', 'Les Grands Meandres', 256, 12891, 3200, 0),
    ('marches-boisees', 'Les Marches Boisees', 384, 28407, 7000, 1),
    ('terres-sans-fin', 'Les Terres sans Fin', 512, 39173, 12000, 2),
]


def cpos(u, v):
    y = v // 2 - u
    return v - y, y


def mpos(x, y):
    return (x - y) // 2, x + y


def sheet(name, frames, offset=None):
    w, h = frames[0].size
    image = Image.new('RGBA', (w * len(frames), h))
    for i, frame in enumerate(frames):
        image.alpha_composite(frame, (w * i, 0))
    meta = PngImagePlugin.PngInfo()
    meta.add_text('FrameSize', f'{w},{h}')
    meta.add_text('FrameAmount', str(len(frames)))
    if offset:
        meta.add_text('Offset', offset)
    image.save(MOD / 'art' / (name + '.png'), pnginfo=meta)


def terrain_art():
    from isometric_art import terrain_frames
    water, forest = [], []
    for mask in range(16):
        rng = random.Random(6237 + mask)
        im = Image.new('RGBA', (48, 24))
        floor = terrain_frames()[mask]
        for py in range(24):
            for px in range(48):
                a = (px + .5 - 24) / 48 + (py + .5) / 24
                b = (py + .5) / 24 - (px + .5 - 24) / 48
                if not (0 <= a < 1 and 0 <= b < 1):
                    continue
                n = rng.randrange(-3, 4)
                edge = min(a if mask & 1 else 2, b if mask & 2 else 2,
                           1-a if mask & 4 else 2, 1-b if mask & 8 else 2)
                # Shore lines are on the water side of each shared diamond edge.
                if edge < .05 + .018 * math.sin(px * .8 + py):
                    rgb = (86, 101, 58)
                elif edge < .14 + .025 * math.sin(px * .5 + py):
                    rgb = (131, 130, 91)
                elif edge < .25:
                    rgb = (74, 124, 116)
                else:
                    ripple = round(4 * math.sin(py * 1.7 + px * .14))
                    rgb = (46 + ripple, 94 + ripple, 106 + ripple)
                im.putpixel((px, py), tuple(c+n for c in rgb)+(255,))
                r, g, b0, alpha = floor.getpixel((px, py))
                floor.putpixel((px, py), (r-12, g-12, b0-7, alpha))
        water.append(im)
        forest.append(floor)
    sheet('river-terrain', water)
    sheet('woodland-terrain', forest)
    decks = []
    for axis in range(2):
        for side in range(3):
            im = Image.new('RGBA', (48, 40))
            rng = random.Random(593 + axis * 3 + side)
            for py in range(24):
                for px in range(48):
                    a = (px+.5-24)/48 + (py+.5)/24
                    b = (py+.5)/24 - (px+.5-24)/48
                    if not (0 <= a < 1 and 0 <= b < 1):
                        continue
                    along, across = (a, b) if axis == 0 else (b, a)
                    plank = int(along * 5)
                    seam = along * 5 % 1 < .12
                    border = (side == 0 and across < .13) or (side == 2 and across > .87)
                    grain = rng.randrange(-6, 7) + plank % 2 * 7
                    rgb = (74, 58, 37) if seam or border else (155+grain, 124+grain, 78+grain)
                    im.putpixel((px, py+16), (*rgb, 255))
            # Side beams and posts have the same 2:1 projection as the deck.
            if side != 1:
                def point(a, b, z=0):
                    return round(24+24*(a-b)), round(16+12*(a+b)-z)
                edge = 0.05 if side == 0 else .95
                start = point(0, edge, 5) if axis == 0 else point(edge, 0, 5)
                end = point(1, edge, 5) if axis == 0 else point(edge, 1, 5)
                d = ImageDraw.Draw(im)
                d.line((start, end), fill='#584832', width=3)
                d.line((start[0], start[1]-1, end[0], end[1]-1), fill='#b99a63')
                for t in (.12, .85):
                    p = point(t, edge) if axis == 0 else point(edge, t)
                    d.line((p[0], p[1], p[0], p[1]-7), fill='#594532', width=2)
                    d.point((p[0], p[1]-7), fill='#d2b887')
            decks.append(im)
    sheet('bridge-terrain', decks, '0,-8')


def terrain_templates():
    result = ''
    for ident, image, kind, category, count in (
            (WATER, 'river-terrain', 'Water', 'River', 16),
            (BRIDGE, 'bridge-terrain', 'Bridge', 'Bridge', 6),
            (FOREST, 'woodland-terrain', 'Rough', 'Terrain', 16)):
        result += f'\tTemplate@{ident}:\n\t\tId: {ident}\n\t\tImages: medieval|art/{image}.png\n\t\tSize: 1,1\n\t\tPickAny: True\n\t\tCategories: {category}\n\t\tTiles:\n'
        result += ''.join(f'\t\t\t{i}: {kind}\n' for i in range(count))
    return result


def generate(spec):
    folder, title, w, seed, tree_target, layout = spec
    h = w * 2
    rng = random.Random(seed)
    # Work in (u, v/2) for geographically round lakes and groves.
    hydro = Image.new('L', (w, w))
    hd = ImageDraw.Draw(hydro)
    rivers = []
    for axis, base, amplitude, phase in ([(0, .51, .12, .4)] if layout == 0 else
            [(0, .36, .08, .1), (0, .72, .07, 1.8)] if layout == 1 else
            [(0, .50, .11, .6), (1, .54, .10, 2.1)]):
        points = []
        for t in range(-5, w+6):
            cross = w*(base+amplitude*math.sin(t/w*math.tau*1.35+phase)+.025*math.sin(t/w*math.tau*4+phase))
            points.append((round(cross), t) if axis == 0 else (t, round(cross)))
        width = max(5, w//60)
        hd.line(points, fill=255, width=width, joint='curve')
        rivers.append((axis, points, width))
    for i in range(3 + layout * 2):
        x, y = rng.randrange(w//7, w*6//7), rng.randrange(w//7, w*6//7)
        rx, ry = rng.randrange(6, w//20), rng.randrange(5, w//25)
        hd.ellipse((x-rx, y-ry, x+rx, y+ry), fill=255)
        hd.ellipse((x-rx//2, y-ry, x+rx+3, y+ry//2), fill=255)
    start_uv = (round(w*.19), round(h*.23))
    sx, sy = cpos(*start_uv)
    hd.ellipse((start_uv[0]-24, start_uv[1]/2-24, start_uv[0]+24, start_uv[1]/2+24), fill=0)
    hydro = hydro.resize((w, h), Image.Resampling.NEAREST)
    wet = bytearray(hydro.tobytes())
    bridge_cells, bridge_specs, reserved = {}, [], set()

    def inside(x, y, margin=3):
        u, v = mpos(x, y)
        return margin <= u < w-margin and margin <= v < h-margin

    def water_at(x, y):
        u, v = mpos(x, y)
        return 0 <= u < w and 0 <= v < h and wet[v*w+u] != 0

    # Bridges follow an actual cell axis, three cells wide. No decorative
    # pictures over impassable water: the entire deck has Bridge terrain.
    for river_axis, points, width in rivers:
        for fraction in (.13, .31, .52, .73, .90):
            u, half_v = points[round(w*fraction)+5]
            cx, cy = cpos(u, half_v*2)
            def cell(t, side=0):
                return (cx+t, cy+side) if axis == 0 else (cx+side, cy+t)
            candidates = []
            for axis in (0, 1):
                hits = [t for t in range(-width*4, width*4+1) if any(water_at(*cell(t, s)) for s in (-1, 0, 1))]
                if not hits: continue
                lo, hi = min(hits)-3, max(hits)+3
                if not all(inside(*cell(t, s), 6) for t in (lo-3, hi+3) for s in (-3, 3)): continue
                if any(water_at(*cell(t, s)) for t in (lo, hi) for s in (-1, 0, 1)): continue
                candidates.append((hi-lo, axis, lo, hi))
            if not candidates: continue
            _, axis, lo, hi = min(candidates)
            for t in range(lo, hi+1):
                for side in (-1, 0, 1):
                    xy = cell(t, side)
                    bridge_cells[xy] = axis*3+side+1
            for t in range(lo-5, hi+6):
                for side in range(-4, 5):
                    reserved.add(cell(t, side))
            bridge_specs.append(dict(start=cell(lo-2), end=cell(hi+2), center=cell((lo+hi)//2), length=hi-lo+1))

    # The starter is the established working village, translated as one unit.
    actors = []
    village = (MOD/'maps/village/map.yaml').read_text(encoding='utf-8').split('Actors:\n', 1)[1].split('Rules:\n', 1)[0]
    for block in village.split('\n\tActor'):
        if not block.strip():
            continue
        lines = block.splitlines()
        if lines[0].startswith('\tSpawn:'):
            continue
        name = lines[0].split(': ', 1)[1]
        owner = next(line.split(': ', 1)[1] for line in lines if '\t\tOwner:' in line)
        location = next(line.split(': ', 1)[1] for line in lines if '\t\tLocation:' in line)
        x, y = map(int, location.split(','))
        stock = next((line.split(': ', 1)[1] for line in lines if '\t\tMedievalStock:' in line), None)
        actors.append((name, x-60+sx, y+4+sy, owner, stock))
    for x in range(sx-31, sx+32):
        for y in range(sy-31, sy+32):
            reserved.add((x, y))
            u, v = mpos(x, y)
            if inside(x, y): wet[v*w+u] = 0

    groves = Image.new('L', (w, w)); gd = ImageDraw.Draw(groves)
    for _ in range(w * 2):
        x, y = rng.randrange(w), rng.randrange(w)
        radius = rng.randrange(4, max(9, w//32))
        gd.ellipse((x-radius, y-radius*.75, x+radius, y+radius*.75), fill=rng.randrange(140, 256))
    groves = groves.filter(ImageFilter.GaussianBlur(2)).resize((w, h), Image.Resampling.BILINEAR)
    density = groves.tobytes()
    occupied = {(a[1], a[2]) for a in actors}
    blocked = set(occupied)
    def add_nature(name, x, y):
        actors.append(('md.'+name, x, y, 'Neutral', None))
        occupied.add((x, y))
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1): blocked.add((x+dx, y+dy))

    trees = 0
    for _ in range(tree_target * 100):
        if trees >= tree_target: break
        u, v = rng.randrange(5, w-5), rng.randrange(8, h-8)
        x, y = cpos(u, v)
        if density[v*w+u] < 100 or wet[v*w+u] or (x,y) in reserved or (x,y) in blocked:
            continue
        species = ('pine', 'pine', 'tree') if v < h*.30 else ('tree', 'tree', 'birch', 'pine')
        add_nature(rng.choice(species), x, y); trees += 1
    # Scattered individual stones plus small geological deposits everywhere.
    stone_target = tree_target//10
    stones = 0
    for _ in range(stone_target*50):
        if stones >= stone_target: break
        u, v = rng.randrange(6, w-6), rng.randrange(8, h-8)
        cx, cy = cpos(u, v)
        for j in range(rng.randrange(1, 6)):
            x, y = cx+rng.randrange(-4, 5), cy+rng.randrange(-4, 5)
            if stones >= stone_target: break
            if not inside(x,y) or water_at(x,y) or (x,y) in blocked or (x,y) in reserved: continue
            add_nature('stone', x, y); stones += 1
    for _ in range(tree_target//4):
        u, v = rng.randrange(5, w-5), rng.randrange(8, h-8)
        x, y = cpos(u, v)
        if wet[v*w+u] or (x,y) in reserved or (x,y) in occupied: continue
        actors.append(('md.'+rng.choice(('flowers', 'grasses', 'grasses', 'shrub')), x, y, 'Neutral', None))

    # Playable worlds begin with a mobile settlement; neutral geography is kept.
    actors = [a for a in actors if a[3] == 'Neutral'] + [('md.caravan', sx+1, sy+1, 'Village', None)]
    data = bytearray(17 + 5*w*h)
    struct.pack_into('<BHHIII', data, 0, 2, w, h, 17, 0, 17+3*w*h)
    overview = Image.new('RGB', (w, h)); pixels = overview.load()
    terrain_counts = Counter()
    walkable = bytearray(w*h)
    for u in range(w):
        for v in range(h):
            x, y = cpos(u,v)
            ident, index, color = 255, rng.randrange(16), (103, 121, 72)
            if (x,y) in bridge_cells:
                ident, index, color = BRIDGE, bridge_cells[x,y], (206, 174, 113)
            elif wet[v*w+u]:
                ident, index, color = WATER, 0, (52, 106, 122)
                for bit, (dx,dy) in enumerate(((-1,0),(0,-1),(1,0),(0,1))):
                    if not water_at(x+dx,y+dy): index |= 1<<bit
            elif density[v*w+u] > 115 and (x,y) not in reserved:
                ident, color = FOREST, (81, 99, 59)
            struct.pack_into('<HB', data, 17+3*(u*h+v), ident, index)
            pixels[u,v] = color
            terrain_counts[ident] += 1
            walkable[v*w+u] = int(ident != WATER and inside(x,y))
    for name,x,y,owner,stock in actors:
        u,v = mpos(x,y)
        assert 3 <= u < w-3 and 3 <= v < h-3, (name,x,y)
        assert not water_at(x,y), (name, 'on water', x,y)
        if name in ('md.tree','md.pine','md.birch','md.stone'):
            walkable[v*w+u] = 0
            pixels[u,v] = (160,159,143) if name == 'md.stone' else (32,66,37)
    # Four-neighbour flood fill on CPos, not image pixels: catches broken banks,
    # diagonal-only connections and unreachable regions, including tree blocks.
    begin = (sx, sy+5); bu,bv = mpos(*begin)
    reached = bytearray(w*h); reached[bv*w+bu] = 1; queue = deque([begin])
    while queue:
        x,y = queue.popleft()
        for dx,dy in ((-1,0),(0,-1),(1,0),(0,1)):
            nx,ny = x+dx,y+dy; u,v = mpos(nx,ny)
            if not (0 <= u < w and 0 <= v < h): continue
            i = v*w+u
            if walkable[i] and not reached[i]: reached[i]=1; queue.append((nx,ny))
    total_land = sum(walkable); reachable = sum(reached)
    assert reachable / total_land > .995, f'{folder}: isolated regions: {total_land-reachable} cells'
    for bridge in bridge_specs:
        for xy in (bridge['start'],bridge['end']):
            u,v=mpos(*xy); assert reached[v*w+u], (folder,'unreachable bridge',xy)
    header = (MOD/'maps/village/map.yaml').read_text(encoding='utf-8').split('Actors:\n',1)[0]
    header = header.replace('Le Bourg - economie medievale', title + ' - immense').replace('Visibility: MissionSelector','Visibility: Lobby')
    header = header.replace('MapSize: 64,128', f'MapSize: {w},{h}').replace('Bounds: 2,2,60,124', f'Bounds: 3,3,{w-6},{h-6}')
    text = header + f'Actors:\n\tSpawn: mpspawn\n\t\tOwner: Neutral\n\t\tLocation: {sx},{sy}\n'
    for i,(name,x,y,owner,stock) in enumerate(actors):
        text += f'\tActor{i}: {name}\n\t\tOwner: {owner}\n\t\tLocation: {x},{y}\n'
        if stock: text += f'\t\tMedievalStock: {stock}\n'
    # Explorable worlds; options remain changeable in the lobby.
    text += 'Rules:\n\tPlayer:\n\t\tShroud:\n\t\t\tFogCheckboxEnabled: True\n\t\t\tExploredMapCheckboxEnabled: False\n'
    path = MOD/'maps'/folder; path.mkdir(exist_ok=True)
    (path/'map.yaml').write_text(text, encoding='utf-8', newline='\n')
    (path/'map.bin').write_bytes(data)
    overview = overview.resize((w*2,h), Image.Resampling.NEAREST)
    d=ImageDraw.Draw(overview); u,v=start_uv
    d.ellipse((u*2-6,v-6,u*2+6,v+6),fill='#f0d690',outline='#fff5d5',width=2)
    for bridge in bridge_specs:
        u,v=mpos(*bridge['center']); d.ellipse((u*2-3,v-3,u*2+3,v+3),fill='#e4bb7c')
    overview.save(path/'map.png')
    counts = Counter(a[0] for a in actors)
    report = dict(folder=folder,title=title,width=w,height=h,seed=seed,areaMultiplier=w*h//8192,
                  actors=len(actors),trees=trees,stoneDeposits=stones,bridges=bridge_specs,
                  terrain=dict(terrain_counts),reachableCells=reachable,landCells=total_land,
                  start=[sx,sy],finiteWood=200*sum(counts[k] for k in ('md.tree','md.pine','md.birch')),finiteStone=350*counts['md.stone'])
    (path/'geography.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(f'{title}: {w}x{h}, {len(actors)} actors, {trees} trees, {stones} deposits, {len(bridge_specs)} bridges, {reachable/total_land:.2%} accessible')
    return report


def main():
    terrain_art()
    path=MOD/'temperat.yaml'; text=path.read_text(encoding='utf-8')
    # Idempotent installation; generation of the other art imports the same block.
    import re
    for ident in (WATER,BRIDGE,FOREST):
        text=re.sub(rf'\tTemplate@{ident}:\n.*?(?=\tTemplate@|\Z)', '', text, flags=re.S)
    text=text.replace('Templates:\n','Templates:\n'+terrain_templates(),1)
    path.write_text(text,encoding='utf-8',newline='\n')
    reports=[generate(spec) for spec in SPECS]
    out=ROOT/'analysis/medieval/large-maps';out.mkdir(parents=True,exist_ok=True)
    (out/'generation.json').write_text(json.dumps(reports,indent=2),encoding='utf-8')


if __name__ == '__main__':
    main()
