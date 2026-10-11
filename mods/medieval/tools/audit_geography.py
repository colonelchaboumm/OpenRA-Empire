"""Audit the actual YAML/binary maps, including isometric coordinates and coverage."""
from collections import Counter, deque
from pathlib import Path
import argparse
import json
import re
import struct

from large_maps import cpos, mpos, WATER, BRIDGE

KINDS = {'md.tree': 'wood', 'md.pine': 'wood', 'md.birch': 'wood', 'md.stone': 'stone',
         'md.flowers': 'vegetation', 'md.grasses': 'vegetation', 'md.shrub': 'vegetation'}


def audit(path, grid=8):
    text = (path/'map.yaml').read_text(encoding='utf-8')
    data = (path/'map.bin').read_bytes()
    version, w, h, offset, _, _ = struct.unpack_from('<BHHIII', data)
    assert version == 2
    left, top, bw, bh = map(int, re.search(r'^Bounds: (.+)', text, re.M)[1].split(','))
    sectors = [Counter() for _ in range(grid * grid)]
    def sector(u, v): return sectors[min(grid-1, v * grid // h) * grid + min(grid-1, u * grid // w)]
    def inside(u, v): return left <= u < left + bw and top <= v < top + bh
    walkable = bytearray(w*h)
    for u in range(w):
        for v in range(h):
            tile = struct.unpack_from('<H', data, offset+3*(u*h+v))[0]
            if inside(u, v) and tile != WATER:
                walkable[v*w+u] = 1; sector(u, v)['land'] += 1
    actors = re.findall(r'^\t[^\t\n]+: (md\.[^\n]+|mpspawn)\n\t\tOwner: ([^\n]+)\n\t\tLocation: (-?\d+),(-?\d+)', text.split('Actors:\n')[1], re.M)
    occupied, starts, resources = set(), [], []
    errors = []
    for name, owner, xx, yy in actors:
        x, y = int(xx), int(yy); u, v = mpos(x, y)
        if not inside(u, v): errors.append(f'Outside bounds: {name} {x},{y}'); continue
        tile = struct.unpack_from('<H', data, offset+3*(u*h+v))[0]
        if tile == WATER: errors.append(f'On water: {name} {x},{y}')
        if name in KINDS:
            if (x,y) in occupied: errors.append(f'Overlapping nature: {name} {x},{y}')
            occupied.add((x,y)); sector(u,v)[KINDS[name]] += 1
            if KINDS[name] != 'vegetation':
                resources.append((x,y,KINDS[name])); walkable[v*w+u] = 0
                if tile == BRIDGE: errors.append('Resource obstructs bridge')
        if name == 'md.caravan': starts.append((owner,x,y))
    reached = bytearray(w*h)
    if starts:
        _, x, y = starts[0]; u,v = mpos(x,y); reached[v*w+u] = 1; queue=deque([(x,y)])
        while queue:
            x,y=queue.popleft()
            for dx,dy in ((-1,0),(1,0),(0,-1),(0,1)):
                nx,ny=x+dx,y+dy; u,v=mpos(nx,ny)
                if inside(u,v) and walkable[v*w+u] and not reached[v*w+u]:
                    reached[v*w+u]=1; queue.append((nx,ny))
    total = sum(walkable); accessible = sum(reached)
    if starts and accessible / total < .995: errors.append(f'Isolated land: {total-accessible} cells')
    near = []
    for owner,x,y in starts:
        u,v=mpos(x,y)
        if not reached[v*w+u]: errors.append('Disconnected player start: '+owner)
        entry = dict(player=owner, start=[x,y])
        for kind in ('wood','stone'):
            distances = sorted((xx-x)**2+(yy-y)**2 for xx,yy,k in resources if k==kind)
            entry[kind+'Within24'] = sum(d <= 24**2 for d in distances)
            entry[kind+'Nearest'] = round(distances[0]**.5, 1) if distances else None
        near.append(entry)
    # A sector with >=20% land should have all types. Open clearing/start areas
    # are allowed within it; pure water sectors are deliberately excluded.
    relevant = [s for s in sectors if s['land'] >= w*h/grid**2*.2]
    coverage = {kind: dict(sectors=sum(s[kind] > 0 for s in relevant), total=len(relevant),
                          minimum=min(s[kind] for s in relevant), count=sum(s[kind] for s in sectors))
                for kind in ('wood','stone','vegetation')}
    return dict(map=path.name, width=w, height=h, starts=near, land=total, reachable=accessible,
                coverage=coverage, sectors=[dict(s) for s in sectors], errors=errors, passed=not errors)


if __name__ == '__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('maps', nargs='+', type=Path); parser.add_argument('--output', required=True, type=Path)
    args=parser.parse_args(); result=[audit(p) for p in args.maps]
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,indent=2),encoding='utf-8')
    for r in result: print(r['map'], 'PASS' if r['passed'] else r['errors'], r['coverage'])
    raise SystemExit(0 if all(r['passed'] for r in result) else 1)
