"""Validate packed animation frames, all zone sizes and GPU chrome constraints."""
from pathlib import Path
from PIL import Image, ImageChops
from medieval_art import ROLE_COLORS
from animation_art import UNIT_FRAMES, UNIT_MODES, ZONE_FRAMES, MILL_FRAMES
from stockyard_art import MAX_STACK
from building_animation_art import BUILDING_FRAMES, ANIMATED_BUILDINGS
from decoration_art import NAMES as DECORATIONS, FRAME as DECORATION_FRAME
from house_art import COUNT as HOUSE_STYLES, frames_and_metadata as house_frames
from fortification_art import NAMES as FORTIFICATIONS, FRAME as FORT_FRAME, TOWER_FRAME, GATE_FRAME, GATE_FRAMES
from isometric_art import SIZES, NATURE_SIZES, zone_size
from siege_art import MODES as SIEGE_MODES, FRAME as SIEGE_FRAME, WORKSHOP_FRAMES, TREBUCHET_FRAME, sequence_length
from camp_building_art import CAMP_BUILDING_FRAMES

MOD=Path(__file__).resolve().parents[1]
checked=0

def frames(name, count, size=None):
    global checked
    image=Image.open(MOD/'art'/f'{name}.png')
    frame_size=tuple(map(int,image.info['FrameSize'].split(',')))
    assert image.mode=='RGBA',name
    assert int(image.info['FrameAmount'])==count,name
    if size: assert frame_size==size,name
    checked+=1
    if 'Frame[0]' in image.info:
        result=[]
        for i in range(count):
            region,offset=image.info[f'Frame[{i}]'].split(';')
            x,y,w,h=map(int,region.split(','));dx,dy=map(float,offset.split(','))
            assert x>=0 and y>=0 and x+w<=image.width and y+h<=image.height,name
            left=frame_size[0]/2+dx-w/2;top=frame_size[1]/2+dy-h/2
            assert left.is_integer() and top.is_integer(),f'{name}: subpixel anchor drift'
            pose=Image.new('RGBA',frame_size)
            pose.paste(image.crop((x,y,x+w,y+h)),(int(left),int(top)))
            result.append(pose)
        return result
    fw,fh=frame_size;cols=image.width//fw
    assert cols>0 and image.width%fw==0 and image.height==fh*((count+cols-1)//cols),name
    return [image.crop(((i%cols)*fw,(i//cols)*fh,(i%cols+1)*fw,(i//cols+1)*fh)) for i in range(count)]

for name in ('sheepfold','tentweaver','militarytent'):
    poses=frames(name,9 if name=='militarytent' else CAMP_BUILDING_FRAMES+2,(184,184))
    if name!='militarytent':
        assert len({s.tobytes() for s in poses[1:-1]})==CAMP_BUILDING_FRAMES,f'{name}: every articulated pose must differ'
        assert all(s.crop((0,0,184,108)).tobytes()==poses[0].crop((0,0,184,108)).tobytes() for s in poses[1:-1]),f'{name}: roof must remain stationary'
    for sprite in poses:
        b=sprite.getbbox()
        assert b and min(b[0],b[1])>0 and b[2]<184 and b[3]<184, f'{name}: clipped building'
frames('torch',2,(64,72));frames('campgoods',2,(16,16))
frames('construction-icons',48,(32,34))
for name in ('shepherd','weaver'):frames(name,640,(56,52))
for sprite in frames('commander',640,(56,52)):
    b=sprite.getbbox()
    assert b and b[0]>0 and b[1]>0 and b[2]<56 and b[3]<52,'Commander: clipped animation'
for width in range(2,11):
    for height in range(2,11):
        name='sheeppasture'+('' if (width,height)==(2,2) else f'.{width}x{height}')
        poses=frames(name,25,zone_size(width,height))
        assert poses[0].tobytes()!=poses[1].tobytes(), f'{name}: sheep must animate'
        for sprite in poses:
            b=sprite.getbbox()
            assert b and b[0]>0 and b[1]>0 and b[2]<sprite.width and b[3]<sprite.height, f'{name}: clipped sheep or fence'

for name,size in (('rabbit',(56,48)),('deer',(80,80))):
    sprites=frames('wild-'+name,192,size)
    for sprite in sprites:
        b=sprite.getbbox()
        assert b and b[0]>0 and b[1]>0 and b[2]<size[0] and b[3]<size[1],f'{name}: clipped wildlife frame'
    for facing in range(8):
        assert len({s.tobytes() for s in sprites[96+facing*12:108+facing*12]})>=8,f'{name}: running gait must articulate'
frames('wild-undergrowth',48,(64,48))

workshop=frames('siegeworkshop',2+10*WORKSHOP_FRAMES,(224,224))
for i,sprite in enumerate(workshop):
    b=sprite.getbbox()
    assert b and b[0]>0 and b[1]>0 and b[2]<224 and b[3]<224, f'Workshop frame {i} clipped: {b}'
for sequence in range(10):
    cycle=workshop[2+sequence*WORKSHOP_FRAMES:2+(sequence+1)*WORKSHOP_FRAMES]
    assert len({s.tobytes() for s in cycle})>=12, f'Workshop cycle {sequence} must articulate'
    assert all(s.crop((0,0,224,119)).tobytes()==workshop[0].crop((0,0,224,119)).tobytes() for s in cycle), 'Workshop roof must remain perfectly stationary'
frames('siegestone',1,(16,16))
for name,modes in SIEGE_MODES.items():
    size=TREBUCHET_FRAME if name=='trebuchet' else SIEGE_FRAME
    sprites=frames(name,sum(sequence_length(name,mode)*8 for mode in modes),size)
    for i,sprite in enumerate(sprites):
        b=sprite.getbbox()
        assert b and b[0]>0 and b[1]>0 and b[2]<size[0] and b[3]<size[1], f'{name}:{i} clipped siege frame {b}'
    for mode in ('run','fire','assemble'):
        if mode not in modes: continue
        for facing in range(8):
            length=sequence_length(name,mode)
            start=sum(sequence_length(name,m)*8 for m in modes[:modes.index(mode)])+facing*length
            assert len({s.tobytes() for s in sprites[start:start+length]})>=(40 if mode=='assemble' and name=='trebuchet' else 4),f'{name}/{mode}/{facing}: mechanism must animate'
    if name=='trebuchet':
        from trebuchet_art import frame as native_pose
        index=0
        for mode in modes:
            for facing in range(8):
                for step in range(sequence_length(name,mode)):
                    assert sprites[index].tobytes()==native_pose(facing,step,mode).tobytes(), 'Packed atlas must preserve every pixel and anchor'
                    index+=1
        def pose(mode,facing,step):
            return sprites[sum(sequence_length(name,m)*8 for m in modes[:modes.index(mode)])+facing*sequence_length(name,mode)+step]
        for facing in range(8):
            assert pose('assemble',facing,0).tobytes()==pose('idle',facing,0).tobytes(), 'Assembly must start exactly on the loaded wagon'
            assert pose('assemble',facing,47).tobytes()==pose('deployed',facing,0).tobytes(), 'Assembly must end exactly on the ready trebuchet'

for emblem in range(6):
    sprites=frames(f'heraldic-standardbearer-{emblem}',128,(88,80))
    assert len({s.tobytes() for s in sprites[:8]})>=4,'Standard must wave at rest'
    for sprite in sprites:
        b=sprite.getbbox()
        assert b and b[0]>0 and b[1]>0 and b[2]<88 and b[3]<80,'Clipped standard bearer'
    assert len({s.tobytes() for s in frames(f'heraldic-banner-{emblem}',13,(96,112))[:12]})==12,'Decorative standard must wave'
assert len({s.tobytes() for s in frames('heraldry-icons',6,(64,48))})==6,'Every emblem must be distinct'

for name in FORTIFICATIONS:
    count=2 if name=='woodtower' else GATE_FRAMES+1 if name.startswith('woodgate') else 17
    sprites=frames(name,count,TOWER_FRAME if name=='woodtower' else GATE_FRAME if name.startswith('woodgate') else FORT_FRAME)
    for sprite in sprites:
        b=sprite.getbbox()
        assert b and b[0]>0 and b[1]>0 and b[2]<sprite.width and b[3]<sprite.height,f'{name}: clipped fortification'
    if name.startswith('woodgate'): assert len({s.tobytes() for s in sprites[:-1]})==GATE_FRAMES,'Gate must have distinct opening poses'
    if name.startswith('palisade'): assert len({s.tobytes() for s in sprites[:-1]})==15,'Palisade must cover every connected corner and junction'
assert len({s.tobytes() for s in frames('arrow',16,(24,24))})==16,'Arrows must support all projectile directions'
for name in DECORATIONS:
    sprites=frames(name,13 if name=='banner' else 17 if name.startswith('gardenfence') else 2,DECORATION_FRAME)
    for sprite in sprites:
        b=sprite.getbbox()
        assert b and b[0]>0 and b[1]>0 and b[2]<96 and b[3]<112,f'{name}: clipped decoration'
    if name=='banner': assert len({s.tobytes() for s in sprites[:-1]})==12,'Flag animation must have twelve distinct poses'
    if name.startswith('gardenfence'): assert len({s.tobytes() for s in sprites[:-1]})==15,'All fifteen fence connection masks must be distinct'
for name,size in SIZES.items():
    sprites=frames(name,HOUSE_STYLES+1 if name=='house' else MILL_FRAMES+1 if name=='farm' else BUILDING_FRAMES+2 if name in ANIMATED_BUILDINGS else 2,size)
    for frame in sprites: assert frame.getbbox(),name
    if name=='house':
        assert len({s.tobytes() for s in sprites[:-1]})==HOUSE_STYLES,'House styles must be distinct'
        metadata=house_frames()[1]
        for sprite,m in zip(sprites,metadata):
            bounds=sprite.getchannel('A').point(lambda a:255 if a>12 else 0).getbbox()
            assert bounds[0]>0 and bounds[1]>0 and bounds[2]<size[0] and bounds[3]<size[1],'Clipped house'
            x,y=m['chimney'][0]+92,m['chimney'][1]+140
            assert sprite.getpixel((x,y))[3]>128, f"{m['name']}: chimney mouth must sit on the building"
        assert sprites[1].getbbox()[3]-sprites[1].getbbox()[1] < sprites[2].getbbox()[3]-sprites[2].getbbox()[1],'Tall house must be visibly taller than cottage'
    if name in ANIMATED_BUILDINGS:
        assert len({p.tobytes() for p in sprites[1:-1]})>=16,f'{name}: mechanism lacks distinct poses'
        for sprite in sprites[:-1]:
            b=sprite.getchannel('A').getbbox()
            assert b[0]>0 and b[1]>0 and b[2]<size[0] and b[3]<size[1],f'{name}: clipped motion'
            # Every wall, roof, ground anchor and foundation outside the mechanism is stationary.
            box=ImageChops.difference(sprites[0],sprite).getbbox(alpha_only=False)
            limit={'sawmill':(45,106,94,151),'quarry':(27,71,79,135),'market':(48,112,76,141),'marketstall':(48,98,132,157)}[name]
            assert not box or all((box[0]>=limit[0],box[1]>=limit[1],box[2]<=limit[2],box[3]<=limit[3])),f'{name}: building moves outside mechanism: {box}'
for name in ROLE_COLORS:
    sprites=frames(name,len(UNIT_MODES)*8*UNIT_FRAMES)
    assert len({sprites[f*UNIT_FRAMES].tobytes() for f in range(8)})==8,f'{name}: missing direction'
    for mode in ('run','carry-run') if name=='supplycart' else ('run','work','harvest','carry-run','attack'):
        start=UNIT_MODES.index(mode)*8*UNIT_FRAMES
        for facing in range(8):
            poses=sprites[start+facing*UNIT_FRAMES:start+(facing+1)*UNIT_FRAMES]
            assert len({p.tobytes() for p in poses})>=4,f'{name}/{mode}/{facing}: animation lacks articulated poses'
    for i,sprite in enumerate(sprites):
        bounds=sprite.getchannel('A').getbbox()
        assert bounds and bounds[0]>0 and bounds[1]>0 and bounds[2]<sprite.width and bounds[3]<sprite.height, f'{name}:{i} clipped at frame edge: {bounds}'
for kind in ('field','pasture'):
    for w in range(2,11):
        for h in range(2,11):
            name=kind if (w,h)==(2,2) else f'{kind}.{w}x{h}'
            sprites=frames(name,ZONE_FRAMES*3+1,zone_size(w,h))
            assert sprites[0].tobytes()!=sprites[-1].tobytes(),name
            assert len({p.tobytes() for p in sprites[:ZONE_FRAMES]})>2,f'{name}: static crops/cattle'
            for sprite in sprites:
                bbox=sprite.getbbox()
                assert bbox and bbox[0]>0 and bbox[1]>0 and bbox[2]<sprite.width and bbox[3]<sprite.height, f'{name}: cropped fence/crop {bbox}'
frames('resources',4,(16,16))
frames('equipment',3,(16,16))
frames('piles',16,(32,28))
frames('stump',1,(96,116))
frames('rubble',1,(72,56))
frames('stockyard-floor',4,(48,24))
yard=frames('stockyard-stock',4*(MAX_STACK+1),(64,72))
for resource in range(4):
    piles=yard[resource*(MAX_STACK+1):(resource+1)*(MAX_STACK+1)]
    assert not piles[0].getbbox(), 'Empty exterior storage must have no goods'
    assert len({p.tobytes() for p in piles})==MAX_STACK+1, 'Every stock quantity must have its own visible pile'
    for p in piles[1:]:
        b=p.getbbox()
        assert b and b[0]>0 and b[1]>0 and b[2]<64 and b[3]<72, f'Exterior pile clipped: {resource}, {b}'
trails=frames('trails',48,(32,20))
for frame in trails:
    bbox=frame.getbbox()
    assert bbox and bbox[0]>0 and bbox[1]>0 and bbox[2]<32 and bbox[3]<20, 'Trail stamp clipped'
assert trails[-1].getchannel('A').getextrema()[1] > trails[0].getchannel('A').getextrema()[1], 'Trail wear must become visible progressively'
for name,size in NATURE_SIZES.items():
    assert frames(name,2 if name in ('tree','pine','birch','stone') else 1,size)[0].getbbox(),name
for name,count,size in (('salvage',1,(32,38)),('grass',16,(48,24)),('path',16,(48,24))):
    assert frames(name,count,size)[0].getbbox(),name
# Isometric terrain must tessellate exactly, including odd staggered rows.
tile=Image.open(MOD/'art/grass.png').crop((0,0,48,24)).getchannel('A')
for y in range(48,96):
    for x in range(96,144):
        coverage=0
        for v in range(2,10):
            for u in range(0,5):
                px=x-u*48-(v%2)*24;py=y-v*12
                if 0<=px<48 and 0<=py<24: coverage+=tile.getpixel((px,py))//255
        assert coverage==1, f'Terrain gap or overlap at {x},{y}: {coverage}'
for name in ('ui','icons','resources'):
    image=Image.open(MOD/'art'/f'{name}.png')
    assert all(n>0 and n&(n-1)==0 for n in image.size),f'{name}: GPU texture must have power-of-two dimensions'
print(f'Art validation passed: {checked} sheets; 81 sizes per agricultural zone; 8 directions per unit; no clipped animation frames; GPU chrome dimensions valid.')
