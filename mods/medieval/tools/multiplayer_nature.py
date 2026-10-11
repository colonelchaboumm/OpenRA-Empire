"""Seeded nature across the whole small multiplayer map, with clear town starts."""
from collections import Counter
from pathlib import Path
import random
import re
import struct

from large_maps import cpos, mpos, WATER, BRIDGE


def populate(path, seed):
    text = (path/'map.yaml').read_text(encoding='utf-8')
    header, rest = text.split('Actors:\n',1); body,rules=rest.split('Rules:\n',1)
    # Idempotent: replace only this generator's actors, preserving starter kits.
    body=re.sub(r'\tNature\d+:.*?\n(?:\t\t[^\n]*\n)*','',body)
    blocks=re.findall(r'^\t[^\t\n]+: ([^\n]+)\n\t\tOwner: ([^\n]+)\n\t\tLocation: (-?\d+),(-?\d+)',body,re.M)
    starts=[(int(x),int(y)) for name,_,x,y in blocks if name=='md.caravan']
    if not starts: return  # founding_start applies the caravan conversion first.
    data=(path/'map.bin').read_bytes(); _,w,h,offset,_,_=struct.unpack_from('<BHHIII',data)
    rng=random.Random(seed)
    occupied={(int(x),int(y)) for _,_,x,y in blocks}; blocked=set(occupied)
    for name,_,x,y in blocks:
        if name in ('md.tree','md.pine','md.birch','md.stone'):
            for dx in (-1,0,1):
                for dy in (-1,0,1): blocked.add((int(x)+dx,int(y)+dy))
    groves=[(rng.randrange(w),rng.randrange(h),rng.randrange(4,11)) for _ in range(28)]
    groups={'wood':('tree','pine','birch'), 'stone':('stone',), 'vegetation':('flowers','grasses','grasses','shrub')}
    counts=Counter(); sectors=[Counter() for _ in range(16)]; added=[]
    for name,_,x,y in blocks:
        u,v=mpos(int(x),int(y))
        for kind,names in groups.items():
            if name.removeprefix('md.') in names: sectors[v*4//h*4+u*4//w][kind]+=1
    def place(kind,u,v):
        x,y=cpos(u,v)
        if not (3<=u<w-3 and 4<=v<h-4) or (x,y) in occupied: return False
        if struct.unpack_from('<H',data,offset+3*(u*h+v))[0] in (WATER,BRIDGE): return False
        if any(abs(x-sx)<=13 and abs(y-sy)<=13 for sx,sy in starts): return False
        if kind != 'vegetation' and (x,y) in blocked: return False
        name='md.'+rng.choice(groups[kind]); added.append((name,x,y)); occupied.add((x,y))
        if kind!='vegetation':
            for dx in (-1,0,1):
                for dy in (-1,0,1): blocked.add((x+dx,y+dy))
        counts[kind]+=1; sectors[v*4//h*4+u*4//w][kind]+=1
        return True
    for kind,target in (('wood',350),('stone',70),('vegetation',240)):
        for _ in range(target*300):
            if counts[kind]>=target: break
            u,v=rng.randrange(w),rng.randrange(h)
            if kind=='wood' and rng.randrange(8) and not any((u-gx)**2+((v-gy)/2)**2<radius**2 for gx,gy,radius in groves): continue
            place(kind,u,v)
    for i,sector in enumerate(sectors):
        gx,gy=i%4,i//4
        for kind in groups:
            for _ in range(10000):
                if sector[kind]>=3: break
                place(kind,rng.randrange(gx*w//4,(gx+1)*w//4),rng.randrange(gy*h//4,(gy+1)*h//4))
    for i,(name,x,y) in enumerate(added): body+=f'\tNature{i}: {name}\n\t\tOwner: Neutral\n\t\tLocation: {x},{y}\n'
    (path/'map.yaml').write_text(header+'Actors:\n'+body+'Rules:\n'+rules,encoding='utf-8',newline='\n')
    return dict(seed=seed,added=dict(counts))
