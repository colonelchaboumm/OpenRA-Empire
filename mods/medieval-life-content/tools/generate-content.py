"""Compile the medieval artwork, mod wiring and the village test map.
Requires Pillow. Gameplay C# and rules.yaml are maintained separately.
"""
from pathlib import Path
import math
import random
import struct
from PIL import Image, ImageDraw, PngImagePlugin

MOD = Path(__file__).resolve().parents[1]
ROOT = MOD.parents[1]
for directory in ('art', 'sequences', 'chrome', 'fluent', 'maps/village', 'maps/menu'):
    (MOD / directory).mkdir(parents=True, exist_ok=True)

def save_sheet(name, frames):
    if name=='trebuchet':
        from trebuchet_art import save_sheet as save_trebuchet_sheet
        save_trebuchet_sheet(MOD/'art/trebuchet.png',frames)
        return
    w, h = frames[0].size
    sheet = Image.new('RGBA', (w * len(frames), h))
    for i, frame in enumerate(frames): sheet.alpha_composite(frame, (w * i, 0))
    meta = PngImagePlugin.PngInfo()
    meta.add_text('FrameSize', f'{w},{h}')
    meta.add_text('FrameAmount', str(len(frames)))
    sheet.save(MOD / 'art' / f'{name}.png', pnginfo=meta)

# Painted buildings and code-native pixel animation share one art compiler.
from medieval_art import (ROLE_COLORS, ui_texture, site, resource_icon, pile)
from animation_art import UNIT_FRAMES, UNIT_MODES, ZONE_FRAMES, MILL_FRAMES, unit_frame, cart_frame, farm_frames
from stockyard_art import floor_frames, stock_frames, MAX_STACK
from building_animation_art import building_frames, BUILDING_FRAMES, ANIMATED_BUILDINGS
from decoration_art import NAMES as DECORATIONS, frames as decoration_frames, rules as decoration_rules, sequences as decoration_sequences
from fortification_art import NAMES as FORTIFICATIONS, frames as fortification_frames, sequences as fortification_sequences, arrows
from military_art import equipment_icons
from siege_art import NAMES as SIEGE, MODES as SIEGE_MODES, frames as siege_frames, workshop_frames, sequences as siege_sequences, stone as siege_stone
from drum_audio import write_drum
from village_audio import main as write_village_audio
from house_art import COUNT as HOUSE_STYLES, frames_and_metadata as house_frames, rules as house_rules, contact_sheet as house_contact_sheet
from isometric_art import (SIZES, OFFSETS, NATURE_SIZES, FOOTPRINTS, painted_sprite, terrain_frames,
    agricultural_zone, stump, rubble, construction, trail_frames)

for name in ('grass', 'path'):
    save_sheet(name, terrain_frames(name == 'path'))
save_sheet('trails',trail_frames())
save_sheet('stockyard-floor',floor_frames())
save_sheet('stockyard-stock',stock_frames())
ui_texture().save(MOD/'art/ui.png')
chrome=''
for suffix,state in [('',0),('-hover',1),('-pressed',2),('-disabled',3),('-highlighted',1),('-highlighted-hover',1),('-highlighted-pressed',2),('-highlighted-disabled',3)]:
    chrome+=f'md-button{suffix}:\n\tImage: medieval|art/ui.png\n\tPanelRegion: {state*24}, 0, 3, 3, 18, 18, 3, 3\n\n'
chrome+='md-panel:\n\tImage: medieval|art/ui.png\n\tPanelRegion: 48, 0, 3, 3, 18, 18, 3, 3\n'

sizes=SIZES
for name in DECORATIONS: save_sheet(name,decoration_frames(name))
for name in FORTIFICATIONS: save_sheet(name,fortification_frames(name))
save_sheet('arrow',arrows())
save_sheet('equipment',equipment_icons())
save_sheet('siegeworkshop',workshop_frames())
for name in SIEGE_MODES: save_sheet(name,siege_frames(name))
save_sheet('siegestone',[siege_stone()])
(MOD/'decorations.yaml').write_text(decoration_rules(),encoding='utf-8',newline='\n')
for name,size in sizes.items():
    if name=='house':
        frames,metadata=house_frames()
        save_sheet(name,frames)
        (MOD/'house-styles.yaml').write_text(house_rules(metadata),encoding='utf-8',newline='\n')
        house_contact_sheet(frames,metadata).save(MOD/'art/house-styles-preview.png')
    else:save_sheet(name,(farm_frames() if name=='farm' else building_frames(name) if name in ANIMATED_BUILDINGS else [painted_sprite(name,size)])+[construction(name)])
zone_variants=[]
zone_rules=[]
for kind in ('field','pasture'):
    for width in range(2,11):
        for height in range(2,11):
            name=kind if (width,height)==(2,2) else f'{kind}.{width}x{height}'
            save_sheet(name,[agricultural_zone(width,height,s,kind=='pasture',f) for s in range(4) for f in range(ZONE_FRAMES if s<3 else 1)])
            if (width,height)==(2,2):continue
            zone_variants.append(name)
            area=width*height;perimeter=2*(width+height)
            footprint=[''.join('x' if kind=='pasture' and (x in (0,width-1) or y in (0,height-1)) and (x,y)!=(width//2,height-1) else '=' for x in range(width)) for y in range(height)]
            wood=math.ceil(perimeter*25/8) if kind=='pasture' else math.ceil(area*15/4)
            stone=math.ceil(perimeter*5/8) if kind=='pasture' else 0
            worker_capacity=math.ceil(area/(8 if kind=='pasture' else 4))
            yield_amount=max(1,area//4)*30 if kind=='pasture' else area*10
            zone_rules += [f'md.{name}:',f'\tInherits: md.{kind}','\tBuilding:',f'\t\tDimensions: {width},{height}',f'\t\tFootprint: {" ".join(footprint)}',
                '\tMedievalBuilding:',f'\t\tCost: {wood}, 0, 0, {stone}',f'\t\tWork: {area*3}',
                '\tMedievalSource:',f'\t\tAmount: {yield_amount}',f'\t\tCultivationWork: {worker_capacity}','']
(MOD/'agriculture.yaml').write_text('\n'.join(zone_rules),encoding='utf-8',newline='\n')

for name,size in NATURE_SIZES.items():
    sprite=painted_sprite(name,size)
    frames=[sprite,stump()] if name in ('tree','pine','birch') else [sprite,Image.new('RGBA',size)] if name=='stone' else [sprite]
    save_sheet(name,frames)
save_sheet('stump',[stump()])
save_sheet('rubble',[rubble()])
save_sheet('salvage',[site((32,38))])
colors=ROLE_COLORS
write_drum(MOD/'audio/drum.wav')
write_village_audio()
for name in colors:
    frames=[]
    for mode in UNIT_MODES:
        for facing in range(8):
            for step in range(UNIT_FRAMES):
                frames.append(cart_frame(facing,step,mode) if name=='supplycart' else unit_frame(name,facing,step,mode))
    save_sheet(name,frames)
save_sheet('resources',[resource_icon(r) for r in range(4)])
save_sheet('piles',[pile(r,level) for r in range(4) for level in range(1,5)])
chrome+='\nmd-resources:\n\tImage: medieval|art/resources.png\n\tRegions:\n'
for r,name in enumerate(('wood','food','meat','stone')): chrome+=f'\t\t{name}: {r*16}, 0, 16, 16\n'
icon_names=list(sizes)+['field','pasture']+list(colors)+list(DECORATIONS)+list(FORTIFICATIONS)+list(SIEGE)
# Chrome textures are uploaded directly and require power-of-two dimensions.
icons=Image.new('RGBA',(1 << (len(icon_names)*32-1).bit_length(),32))
chrome+='\nmd-icons:\n\tImage: medieval|art/icons.png\n\tRegions:\n'
for i,name in enumerate(icon_names):
    sheet=Image.open(MOD/'art'/f'{name}.png')
    fw,fh=map(int,sheet.info['FrameSize'].split(','))
    # Use a south-east facing character in recruitment portraits.
    portrait=5*UNIT_FRAMES if name in colors else 0
    frame=sheet.crop((fw*portrait,0,fw*(portrait+1),fh))
    frame=frame.crop(frame.getbbox());frame.thumbnail((30,30),Image.Resampling.LANCZOS)
    icons.alpha_composite(frame,(i*32+(32-frame.width)//2,(32-frame.height)//2))
    chrome+=f'\t\t{name}: {i*32}, 0, 32, 32\n'
icons.save(MOD/'art/icons.png')
(MOD/'chrome.yaml').write_text(chrome,encoding='utf-8')

seq=decoration_sequences()+fortification_sequences()+siege_sequences()
seq+=['md.equipment:','\tDefaults:','\t\tFilename: medieval|art/equipment.png']
for i,name in enumerate(('bow','sword','armor')):seq += [f'\t{name}:',f'\t\tStart: {i}']
seq+=['']
for name in list(sizes)+['field','pasture']+list(NATURE_SIZES)+['stump','rubble','salvage']+list(colors):
    seq += [f'md.{name}:','\tDefaults:',f'\t\tFilename: medieval|art/{name}.png']
    if name in OFFSETS: seq += [f'\t\tOffset: {OFFSETS[name][0]}, {OFFSETS[name][1]}']
    elif name in colors: seq += ['\t\tOffset: 0, -12']
    elif name in ('field','pasture'): seq += ['\t\tOffset: 0, -6','\t\tZOffset: -8c0']
    if name in colors:
        for index,mode in enumerate(UNIT_MODES):
            seq += [f'\t{mode}:',f'\t\tStart: {index*8*UNIT_FRAMES}','\t\tFacings: 8',f'\t\tLength: {UNIT_FRAMES}',f'\t\tTick: {60 if mode=="attack" else 120}']
    else:
        seq += ['\tidle:']
        if name=='house':seq += [f'\t\tLength: {HOUSE_STYLES}']
        if name=='farm':seq += [f'\t\tLength: {MILL_FRAMES}','\t\tTick: 120']
        if name in ANIMATED_BUILDINGS:seq += ['\tworking:','\t\tStart: 1',f'\t\tLength: {BUILDING_FRAMES}','\t\tTick: 120']
        if name in sizes: seq+=['\tconstruction:',f'\t\tStart: {HOUSE_STYLES if name=="house" else MILL_FRAMES if name=="farm" else BUILDING_FRAMES+1 if name in ANIMATED_BUILDINGS else 1}']
        if name in ('field','pasture'):seq += [f'\t\tLength: {ZONE_FRAMES}','\t\tTick: 180','\tgrowing:',f'\t\tStart: {ZONE_FRAMES}',f'\t\tLength: {ZONE_FRAMES}','\t\tTick: 180','\tempty:',f'\t\tStart: {ZONE_FRAMES*2}',f'\t\tLength: {ZONE_FRAMES}','\t\tTick: 180','\tconstruction:',f'\t\tStart: {ZONE_FRAMES*3}']
        if name in ('tree','pine','birch','stone'):seq+=['\tgrowing:','\t\tStart: 1','\tempty:','\t\tStart: 1']
    seq.append('')
for name in zone_variants:
    seq += [f'md.{name}:','\tDefaults:',f'\t\tFilename: medieval|art/{name}.png','\t\tOffset: 0, -6','\t\tZOffset: -8c0','\tidle:',f'\t\tLength: {ZONE_FRAMES}','\t\tTick: 180','\tgrowing:',f'\t\tStart: {ZONE_FRAMES}',f'\t\tLength: {ZONE_FRAMES}','\t\tTick: 180','\tempty:',f'\t\tStart: {ZONE_FRAMES*2}',f'\t\tLength: {ZONE_FRAMES}','\t\tTick: 180','\tconstruction:',f'\t\tStart: {ZONE_FRAMES*3}','']
seq += ['md.trails:', '\tidle:', '\t\tFilename: medieval|art/trails.png', '\t\tLength: 48', '']
seq += ['md.stockyard-floor:', '\tidle:', '\t\tFilename: medieval|art/stockyard-floor.png', '\t\tLength: 4', '']
seq += ['md.stockyard-stock:', '\tDefaults:', '\t\tFilename: medieval|art/stockyard-stock.png', '\t\tOffset: 0, -16']
for r,name in enumerate(('wood','food','meat','stone')):seq += [f'\t{name}:',f'\t\tStart: {r*(MAX_STACK+1)}',f'\t\tLength: {MAX_STACK+1}']
seq.append('')
seq += ['md.resources:', '\tDefaults:', '\t\tFilename: medieval|art/resources.png']
for r,name in enumerate(('wood','food','meat','stone')): seq += [f'\t{name}:',f'\t\tStart: {r}']
seq += ['', 'md.piles:', '\tDefaults:', '\t\tFilename: medieval|art/piles.png']
for r,name in enumerate(('wood','food','meat','stone')): seq += [f'\t{name}:',f'\t\tStart: {r*4}', '\t\tLength: 4']
seq.append('')
(MOD/'sequences/medieval.yaml').write_text('\n'.join(seq),encoding='utf-8')

manifest=(ROOT/'mods/ra/mod.yaml').read_text(encoding='utf-8')
manifest=manifest.replace('Title: mod-title','Title: medieval-title').replace('WindowTitle: mod-windowtitle','WindowTitle: medieval-title')
manifest=manifest.replace('\t\t$ra: ra','\t\t$ra: ra\n\t\t$medieval: medieval')
manifest=manifest.replace('\tra|maps: System','\tmedieval|maps: System').replace('maps/ra/','maps/medieval/')
manifest=manifest.replace('\tra|rules/map-generators.yaml','\tra|rules/map-generators.yaml\n\tmedieval|rules.yaml\n\tmedieval|agriculture.yaml\n\tmedieval|decorations.yaml\n\tmedieval|house-styles.yaml\n\tmedieval|fortifications.yaml')
manifest=manifest.replace('Weapons:\n','Weapons:\n\tmedieval|weapons.yaml\n')
manifest=manifest.replace('Music:\n\tra|audio/music.yaml','Music:\n')
manifest=manifest.replace('\tmedieval|fortifications.yaml','\tmedieval|fortifications.yaml\n\tmedieval|military.yaml')
manifest=manifest.replace('\tmedieval|military.yaml','\tmedieval|military.yaml\n\tmedieval|siege.yaml')
manifest=manifest.replace('\tra|sequences/decorations.yaml','\tra|sequences/decorations.yaml\n\tmedieval|sequences/medieval.yaml')
manifest=manifest.replace('OpenRA.Mods.Cnc.dll\n','OpenRA.Mods.Cnc.dll, OpenRA.Mods.Medieval.dll\n')
manifest=manifest.replace('\tra|chrome/ingame-player.yaml','\tmedieval|chrome/ingame-player.yaml')
manifest=manifest.replace('Chrome:\n\tra|chrome.yaml','Chrome:\n\tra|chrome.yaml\n\tmedieval|chrome.yaml')
manifest=manifest.replace('\tra|fluent/rules.ftl','\tra|fluent/rules.ftl\n\tmedieval|fluent/medieval.ftl')
manifest=manifest.replace('SpriteFormats: ShpD2,','SpriteFormats: PngSheet, ShpD2,')
manifest=manifest.replace('\tra|tilesets/temperat.yaml','\tmedieval|temperat.yaml')
manifest=manifest.replace('SupportsMapsFrom: ra','SupportsMapsFrom: medieval')
manifest=manifest.replace('MapGrid:\n\tType: Rectangular','MapGrid:\n\tType: RectangularIsometric\n\tSubCellOffsets: 0,0,0, -362,0,0, 0,362,0, 362,0,0\n\tDefaultSubCell: 2')
# RA campaigns depend on RA rules and must not be offered as medieval missions.
manifest=manifest.replace('Missions:\n\tra|missions.yaml\n','')
(MOD/'mod.yaml').write_text(manifest,encoding='utf-8')
terrain=(ROOT/'mods/ra/tilesets/temperat.yaml').read_text(encoding='utf-8').replace('Images: clear1.tem','Images: medieval|art/grass.png')
terrain=terrain.replace('General:\n','General:\n\tTileSize: 48, 24\n',1)
road_template='\tTemplate@60000:\n\t\tId: 60000\n\t\tImages: medieval|art/path.png\n\t\tSize: 1,1\n\t\tPickAny: True\n\t\tCategories: Road\n\t\tTiles:\n'
road_template+=''.join(f'\t\t\t{i}: Road\n' for i in range(16))
terrain=terrain.replace('Templates:\n','Templates:\n'+road_template)
from large_maps import terrain_art as large_map_art, terrain_templates as large_map_templates
large_map_art()
terrain=terrain.replace('Templates:\n','Templates:\n'+large_map_templates(),1)
(MOD/'temperat.yaml').write_text(terrain,encoding='utf-8')
testmod=ROOT/'mods/medieval-test'
testmod.mkdir(exist_ok=True)
test_manifest=manifest.replace('\tmedieval|rules.yaml','\tmedieval|rules.yaml\n\tmedieval|tests.yaml').replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4')
(testmod/'mod.yaml').write_text(test_manifest,encoding='utf-8',newline='\n')
visualmod=ROOT/'mods/medieval-visual'
visualmod.mkdir(exist_ok=True)
(visualmod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|visual-tests.yaml'),encoding='utf-8',newline='\n')
placementmod=ROOT/'mods/medieval-placement'
placementmod.mkdir(exist_ok=True)
(placementmod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|placement-tests.yaml'),encoding='utf-8',newline='\n')
stallmod=ROOT/'mods/medieval-stalls'
stallmod.mkdir(exist_ok=True)
(stallmod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|stall-tests.yaml'),encoding='utf-8',newline='\n')
logisticsmod=ROOT/'mods/medieval-logistics'
logisticsmod.mkdir(exist_ok=True)
(logisticsmod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|logistics-tests.yaml'),encoding='utf-8',newline='\n')
messengermod=ROOT/'mods/medieval-messenger'
messengermod.mkdir(exist_ok=True)
(messengermod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|messenger-tests.yaml'),encoding='utf-8',newline='\n')
localitymod=ROOT/'mods/medieval-localities'
localitymod.mkdir(exist_ok=True)
(localitymod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|locality-tests.yaml'),encoding='utf-8',newline='\n')
siegemod=ROOT/'mods/medieval-siege'
siegemod.mkdir(exist_ok=True)
(siegemod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|siege-tests.yaml'),encoding='utf-8',newline='\n')
ambientmod=ROOT/'mods/medieval-ambience'
ambientmod.mkdir(exist_ok=True)
(ambientmod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|ambience-tests.yaml'),encoding='utf-8',newline='\n')
animationmod=ROOT/'mods/medieval-animation'
animationmod.mkdir(exist_ok=True)
(animationmod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|animation-tests.yaml'),encoding='utf-8',newline='\n')
buildingmod=ROOT/'mods/medieval-building-animation'
buildingmod.mkdir(exist_ok=True)
(buildingmod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|building-animation-tests.yaml'),encoding='utf-8',newline='\n')
decormod=ROOT/'mods/medieval-decoration'
decormod.mkdir(exist_ok=True)
(decormod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|decoration-tests.yaml'),encoding='utf-8',newline='\n')
housemod=ROOT/'mods/medieval-houses'
housemod.mkdir(exist_ok=True)
(housemod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|house-tests.yaml'),encoding='utf-8',newline='\n')
fortmod=ROOT/'mods/medieval-fortifications'
fortmod.mkdir(exist_ok=True)
(fortmod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|fortification-tests.yaml'),encoding='utf-8',newline='\n')
militarymod=ROOT/'mods/medieval-military'
militarymod.mkdir(exist_ok=True)
(militarymod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|military-tests.yaml'),encoding='utf-8',newline='\n')
moralemod=ROOT/'mods/medieval-morale'
moralemod.mkdir(exist_ok=True)
(moralemod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|morale-tests.yaml'),encoding='utf-8',newline='\n')
yardmod=ROOT/'mods/medieval-stockyard'
yardmod.mkdir(exist_ok=True)
(yardmod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|stockyard-tests.yaml'),encoding='utf-8',newline='\n')
naturalmod=ROOT/'mods/medieval-natural'
naturalmod.mkdir(exist_ok=True)
(naturalmod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|natural-tests.yaml'),encoding='utf-8',newline='\n')
trademod=ROOT/'mods/medieval-trade'
trademod.mkdir(exist_ok=True)
(trademod/'mod.yaml').write_text(test_manifest.replace('medieval|tests.yaml','medieval|trade-tests.yaml'),encoding='utf-8',newline='\n')

ui=(ROOT/'mods/ra/chrome/ingame-player.yaml').read_text(encoding='utf-8')
ui=ui.replace('Logic: LoadIngameChatLogic','Logic: LoadIngameChatLogic, MedievalEconomyLogic, MedievalMessengerLogic, MedievalLocalityLogic, MedievalSiegeLogic',1)
lines=['\t\tBackground@MD_PANEL:','\t\t\tX: WINDOW_WIDTH - 250','\t\t\tY: 274','\t\t\tWidth: 238','\t\t\tHeight: 510','\t\t\tBackground: md-panel','\t\t\tChildren:']
def label(id,x,y,w,text=None):
    lines.extend([f'\t\t\t\tLabel@{id}:',f'\t\t\t\t\tX: {x}',f'\t\t\t\t\tY: {y}',f'\t\t\t\t\tWidth: {w}','\t\t\t\t\tHeight: 20','\t\t\t\t\tFont: TinyBold'])
    if text:lines.append(f'\t\t\t\t\tText: {text}')
def button(id,x,y,text=None,width=112,height=26):
    lines.extend([f'\t\t\t\tButton@{id}:',f'\t\t\t\t\tX: {x}',f'\t\t\t\t\tY: {y}',f'\t\t\t\t\tWidth: {width}',f'\t\t\t\t\tHeight: {height}','\t\t\t\t\tFont: TinyBold'])
    lines.append('\t\t\t\t\tBackground: md-button')
    if id.startswith(('MD_BUILD_','MD_RECRUIT_','MD_CART_','MD_YARD_','MD_STALL_','MD_TRADE','MD_CONVOY_')):lines.append('\t\t\t\t\tTooltipContainer: TOOLTIP_CONTAINER')
    if text:lines.append(f'\t\t\t\t\tText: {text}')

def wrapped_label(id,x,y,w,height):
    label(id,x,y,w)
    lines[-2]=f'\t\t\t\t\tHeight: {height}'
    lines.extend(['\t\t\t\t\tWordWrap: True','\t\t\t\t\tVAlign: Top'])
def picture_button(id,x,y,name,width=112,height=36,caption=None):
    lines.extend([f'\t\t\t\tButton@{id}:',f'\t\t\t\t\tX: {x}',f'\t\t\t\t\tY: {y}',
        f'\t\t\t\t\tWidth: {width}',f'\t\t\t\t\tHeight: {height}','\t\t\t\t\tBackground: md-button',
        '\t\t\t\t\tTooltipContainer: TOOLTIP_CONTAINER','\t\t\t\t\tChildren:',
        f'\t\t\t\t\t\tImage@{id}_ICON:', '\t\t\t\t\t\t\tX: 2', '\t\t\t\t\t\t\tY: 2',
        '\t\t\t\t\t\t\tWidth: 32','\t\t\t\t\t\t\tHeight: 32',
        '\t\t\t\t\t\t\tImageCollection: md-icons',f'\t\t\t\t\t\t\tImageName: {name}'])
    for suffix,yy in (('NAME',2),('COST',17)):
        lines.extend([f'\t\t\t\t\t\tLabel@{id}_{suffix}:','\t\t\t\t\t\t\tX: 35',f'\t\t\t\t\t\t\tY: {yy}',
            f'\t\t\t\t\t\t\tWidth: {width-39}','\t\t\t\t\t\t\tHeight: 16','\t\t\t\t\t\t\tFont: TinyBold',
            '\t\t\t\t\t\t\tTextColor: '+('EEE9D4' if suffix=='NAME' else 'C8AD74')])

for r,name in enumerate(('wood','food','meat','stone')):
    x,y=7+r%2*117,4+r//2*20
    lines.extend([f'\t\t\t\tImage@MD_RESOURCE_ICON_{r}:',f'\t\t\t\t\tX: {x}',f'\t\t\t\t\tY: {y+2}',
        '\t\t\t\t\tWidth: 16','\t\t\t\t\tHeight: 16','\t\t\t\t\tImageCollection: md-resources',f'\t\t\t\t\tImageName: {name}'])
    label(f'MD_RESOURCE_{r}',x+19,y,94)
label('MD_POP',7,46,126)
button('MD_FORT_OPEN',139,46,width=92,height=20)
for i,name in enumerate(('house','warehouse','sawmill','quarry','farm','field','ranch','pasture','market')):
    picture_button(f'MD_BUILD_{i}',5+i%2*116,70+i//2*38,name)
picture_button('MD_DECOR_OPEN',121,222,'well')
label('MD_RECRUIT_COST',7,260,225)
for i,name in enumerate(('builder','hauler','lumberjack','stonecutter','farmer','breeder')):
    picture_button(f'MD_RECRUIT_{i}',5+i%2*116,282+i//2*36,name,height=34)
label('MD_MILITIA_COST',7,391,225)
label('MD_ARMS_COST',7,408,225)
for i in range(6,8):button(f'MD_RECRUIT_{i}',5+i%2*116,430)
button('MD_CART_CREATE',5,457)
button('MD_CART_RELOAD',121,457)
button('MD_RESUME',5,483,'medieval-resume')
button('MD_CANCEL',121,483,'medieval-cancel')
ui=ui.rstrip()+'\n'+'\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_FOOD_PANEL:','\t\t\tX: 12','\t\t\tY: 12','\t\t\tWidth: 410','\t\t\tHeight: 132','\t\t\tBackground: md-panel','\t\t\tChildren:']
for i,id in enumerate(('MD_FOOD_TITLE','MD_FOOD_FLOW','MD_FOOD_BALANCE','MD_FOOD_RESERVE','MD_FOOD_STATUS','MD_FOOD_DEATHS')):
    label(id,9,5+i*20,394)
ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_CART_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 176','\t\t\tBackground: md-panel','\t\t\tChildren:']
label('MD_CART_TITLE',9,4,394)
for i,r in enumerate((1,0,3)):button(f'MD_CART_CARGO_{r}',9+i*132,28)
label('MD_CART_STOCK',9,58,394)
label('MD_CART_HINT',9,82,394)
button('MD_CONVOY_ASSIGN',9,108,width=190)
button('MD_CONVOY_STOP',207,108,width=190)
label('MD_CONVOY_STATUS',9,144,394)
ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_YARD_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 158','\t\t\tBackground: md-panel','\t\t\tChildren:']
label('MD_YARD_TITLE',9,4,394)
button('MD_YARD_PLACE',9,28,width=190)
button('MD_YARD_REMOVE',207,28,width=190)
label('MD_YARD_STOCK',9,58,394)
label('MD_YARD_MORE',9,78,394)
label('MD_YARD_EQUIPMENT',9,100,394)
label('MD_YARD_HINT',9,127,394)
ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_LOCALITY_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 120','\t\t\tBackground: md-panel','\t\t\tChildren:']
label('MD_LOCALITY_TITLE',9,5,392)
lines.extend(['\t\t\t\tTextField@MD_LOCALITY_INPUT:','\t\t\t\t\tX: 9','\t\t\t\t\tY: 31','\t\t\t\t\tWidth: 282','\t\t\t\t\tHeight: 28','\t\t\t\t\tFont: TinyBold','\t\t\t\t\tMaxLength: 32'])
button('MD_LOCALITY_RENAME',299,31,width=102,height=28)
label('MD_LOCALITY_STATS',9,66,392)
wrapped_label('MD_LOCALITY_HINT',9,90,392,26)
ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_TRADE_PANEL:','\t\t\tX: 12','\t\t\tY: 284','\t\t\tWidth: 410','\t\t\tHeight: 326','\t\t\tBackground: md-panel','\t\t\tChildren:']
label('MD_TRADE_TITLE',9,4,394)
button('MD_TRADE_LINK',9,28,width=190)
button('MD_TRADE_UNLINK',207,28,width=190)
wrapped_label('MD_TRADE_PARTNER',9,58,394,38)
label('MD_TRADE_EXPORT_LABEL',9,100,394)
for i,r in enumerate((1,0,3,2)):button(f'MD_TRADE_EXPORT_{r}',9+i*98,122,width=94)
label('MD_TRADE_IMPORTS',9,154,394)
button('MD_TRADE_CREATE',9,178,width=190)
button('MD_TRADE_OTHER',207,178,width=190)
label('MD_TRADE_STATUS',9,210,394)
label('MD_TRADE_HINT',9,232,394)
label('MD_TRADE_EQUIPMENT',9,255,394)
picture_button('MD_BUILD_9',9,283,'marketstall',width=392)
ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_STALL_PANEL:','\t\t\tX: 12','\t\t\tY: 284','\t\t\tWidth: 410','\t\t\tHeight: 260','\t\t\tBackground: md-panel','\t\t\tChildren:']
for i,id in enumerate(('MD_STALL_TITLE','MD_STALL_LINK','MD_STALL_STOCK','MD_STALL_DEMAND','MD_STALL_HINT')):
    label(id,9,4+i*24,392)
label('MD_STALL_EQUIPMENT',9,124,392)
button('MD_STALL_RELINK',9,162,width=392)
button('MD_STALL_YARD_PLACE',9,196,width=190)
button('MD_STALL_YARD_REMOVE',207,196,width=190)
label('MD_STALL_YARD_HINT',9,230,392)
ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_TRADER_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 216','\t\t\tBackground: md-panel','\t\t\tChildren:']
label('MD_TRADER_TITLE',9,4,394)
button('MD_TRADER_ASSIGN',9,28,width=190)
button('MD_TRADER_PAUSE',207,28,width=190)
button('MD_TRADER_RECOVER',9,60,width=388)
wrapped_label('MD_TRADER_ROUTE',9,96,394,38)
label('MD_TRADER_STOCK',9,138,394)
wrapped_label('MD_TRADER_STATUS',9,164,394,44)
ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_DECOR_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 250','\t\t\tBackground: md-panel','\t\t\tChildren:']
label('MD_DECOR_TITLE',9,4,260)
button('MD_DECOR_CLOSE',310,4,width=90)
for i,name in enumerate(('well','gardenfence','gardengate','flowerbed','banner','stonepath')):
    picture_button(f'MD_DECOR_{i}',9+i%2*198,36+i//2*40,name,width=190)
button('MD_DECOR_ROTATE',9,161,width=240)
button('MD_DECOR_REMOVE',255,161,width=146)
label('MD_DECOR_HINT',9,197,394)
label('MD_DECOR_HINT2',9,219,394)
ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_FORT_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 280','\t\t\tBackground: md-panel','\t\t\tChildren:']
label('MD_FORT_TITLE',9,4,280)
button('MD_FORT_CLOSE',310,4,width=90)
for i,name in enumerate(('palisade','woodgate','woodtower')):
    picture_button(f'MD_FORT_{i}',9,36+i*40,name,width=392)
picture_button('MD_SIEGE_BUILD',9,156,'siegeworkshop',width=392)
button('MD_FORT_ROTATE',9,201,width=392)
label('MD_FORT_HINT',9,233,394)
label('MD_FORT_HINT2',9,255,394)
ui+='\n'.join(lines)+'\n'
for prefix,height in (('MD_FORGE',342),('MD_BARRACKS',534)):
    lines=[f'\t\tBackground@{prefix}_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410',f'\t\t\tHeight: {height}','\t\t\tBackground: md-panel','\t\t\tChildren:']
    label(prefix+'_TITLE',9,4,392)
    button(prefix+'_STAFF',9,30,width=392)
    if prefix=='MD_FORGE':
        for r in range(3):
            y=70+r*56
            button(f'MD_FORGE_MINUS_{r}',9,y,width=30)
            label(f'MD_FORGE_TARGET_{r}',47,y,310)
            button(f'MD_FORGE_PLUS_{r}',367,y,width=34)
            label(f'MD_FORGE_RECIPE_{r}',47,y+25,348)
        label(prefix+'_STATUS',9,244,392);label(prefix+'_WORKER',9,266,392)
        label(prefix+'_INPUTS',9,288,392);label(prefix+'_HINT',9,313,392)
    else:
        for i,name in enumerate(('militia','archer','swordsman','manatarms','elitearcher','royalguard','drummer')):
            picture_button(f'MD_BARRACKS_RECRUIT_{i}',9,66+i*42,name,width=392,height=40)
        button(prefix+'_MESSENGER',9,362,width=392,height=32)
        button(prefix+'_CANCEL',9,400,width=392)
        label(prefix+'_STATUS',9,434,392);label(prefix+'_WORKER',9,456,392)
        label(prefix+'_INPUTS',9,478,392);label(prefix+'_HINT',9,506,392)
    ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_MAIL_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 452','\t\t\tBackground: md-panel','\t\t\tChildren:']
def mail_label(id,y,height):
    label('MD_MAIL_'+id,9,y,392)
    lines[-2]=f'\t\t\t\t\tHeight: {height}'
    lines.extend(['\t\t\t\t\tWordWrap: True','\t\t\t\t\tVAlign: Top'])
mail_label('TITLE',6,20);mail_label('STATUS',30,36);mail_label('ROUTE',70,20);mail_label('BODY',96,102)
mail_label('INPUT_LABEL',204,20)
lines.extend(['\t\t\t\tTextField@MD_MAIL_INPUT:','\t\t\t\t\tX: 9','\t\t\t\t\tY: 228','\t\t\t\t\tWidth: 392','\t\t\t\t\tHeight: 28','\t\t\t\t\tFont: TinyBold','\t\t\t\t\tMaxLength: 320'])
for row,pair in enumerate((('SEND','READ'),('REPLY','RETURN'),('RECALL','RESUME'))):
    for col,name in enumerate(pair):button('MD_MAIL_'+name,9+col*198,266+row*34,width=194)
button('MD_MAIL_CLEAR',9,368,width=392)
mail_label('HINT',404,40)
ui+='\n'.join(lines)+'\n'
ui+='\t\tButton@MD_MAIL_ALERT:\n\t\t\tX: 440\n\t\t\tY: 12\n\t\t\tWidth: 360\n\t\t\tHeight: 28\n\t\t\tFont: TinyBold\n\t\t\tBackground: md-button\n'
ui+='\t\tBackground@MD_STATUS_PANEL:\n\t\t\tX: 8\n\t\t\tY: WINDOW_HEIGHT - 100\n\t\t\tWidth: WINDOW_WIDTH - 270\n\t\t\tHeight: 49\n\t\t\tBackground: md-panel\n\t\t\tChildren:\n'
for id,y in [('MD_MESSAGE',3),('MD_SELECTION',25)]:
    ui+=f'\t\t\t\tLabel@{id}:\n\t\t\t\t\tX: 7\n\t\t\t\t\tY: {y}\n\t\t\t\t\tWidth: PARENT_WIDTH - 14\n\t\t\t\t\tHeight: 20\n\t\t\t\t\tFont: TinyBold\n'
ui+='\t\tButton@MD_PLACES_OPEN:\n\t\t\tX: 440\n\t\t\tY: 48\n\t\t\tWidth: 180\n\t\t\tHeight: 28\n\t\t\tFont: TinyBold\n\t\t\tBackground: md-button\n'
lines=['\t\tBackground@MD_PLACES_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 516','\t\t\tBackground: md-panel','\t\t\tChildren:']
label('MD_PLACES_TITLE',9,5,315)
button('MD_PLACES_CLOSE',330,4,width=71)
button('MD_PLACES_TOWNS',9,37,width=190);button('MD_PLACES_ROUTES',207,37,width=194)
for i in range(6):
    y=74+i*60
    wrapped_label(f'MD_PLACES_NAME_{i}',9,y,317,32)
    label(f'MD_PLACES_DETAIL_{i}',9,y+33,317)
    button(f'MD_PLACES_VIEW_{i}',334,y+4,width=67,height=46)
button('MD_PLACES_PREVIOUS',9,440,width=94);button('MD_PLACES_NEXT',307,440,width=94)
label('MD_PLACES_COUNT',113,443,190)
wrapped_label('MD_PLACES_HINT',9,479,392,32)
ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_SIEGE_WORKSHOP:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 348','\t\t\tBackground: md-panel','\t\t\tChildren:']
label('MD_SIEGE_TITLE',9,5,392)
for i,name in enumerate(('ram','trebuchet','stonecart')):
    picture_button(f'MD_SIEGE_PRODUCT_{i}',9,33+i*44,name,width=392,height=40)
button('MD_SIEGE_CANCEL',9,171,width=392)
wrapped_label('MD_SIEGE_STATUS',9,205,392,36)
wrapped_label('MD_SIEGE_INPUTS',9,246,392,36)
wrapped_label('MD_SIEGE_HINT',9,289,392,52)
ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_SIEGE_UNIT:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 264','\t\t\tBackground: md-panel','\t\t\tChildren:']
label('MD_ENGINE_TITLE',9,5,392)
wrapped_label('MD_ENGINE_STATUS',9,32,392,38)
button('MD_ENGINE_ACTION',9,80,width=392)
button('MD_ENGINE_RELEASE',9,114,width=392)
wrapped_label('MD_ENGINE_AMMO',9,149,392,34)
wrapped_label('MD_ENGINE_HINT',9,191,392,64)
ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_STONE_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 282','\t\t\tBackground: md-panel','\t\t\tChildren:']
label('MD_STONE_TITLE',9,5,392)
button('MD_STONE_ASSIGN',9,34,width=392)
button('MD_STONE_PAUSE',9,68,width=392)
button('MD_STONE_RECOVER',9,102,width=392)
wrapped_label('MD_STONE_STATUS',9,138,392,36)
label('MD_STONE_STOCK',9,180,392)
wrapped_label('MD_STONE_HINT',9,214,392,60)
ui+='\n'.join(lines)+'\n'
lines=['\t\tBackground@MD_CREW_PANEL:','\t\t\tX: 12','\t\t\tY: 154','\t\t\tWidth: 410','\t\t\tHeight: 77','\t\t\tBackground: md-panel','\t\t\tChildren:']
label('MD_CREW_TITLE',9,5,392)
button('MD_CREW_LEAVE',9,34,width=392)
ui+='\n'.join(lines)+'\n'
(MOD/'chrome/ingame-player.yaml').write_text(ui,encoding='utf-8')
names={'house':'Maison','warehouse':'Entrepôt','sawmill':'Scierie','quarry':'Carrière','farm':'Ferme céréalière','field':'Parcelle cultivée','ranch':'Ferme bovine','pasture':'Enclos bovin','tree':'Arbre exploitable','stone':'Gisement de pierre','salvage':'Matériaux récupérables','builder':'Bâtisseur','hauler':'Transporteur','lumberjack':'Bûcheron','stonecutter':'Tailleur de pierre','farmer':'Fermier','breeder':'Éleveur','militia':'Milicien','manatarms':"Homme d’armes"}
names.update({'market':'Marché','marketstall':'Étal de marché','supplycart':'Charrette de transport','tradecart':'Charrette marchande'})
names.update(DECORATIONS)
names.update(FORTIFICATIONS)
names.update(SIEGE)
names.update(forge='Forge',barracks='Caserne',blacksmith='Forgeron',recruiter='Recruteur',archer='Archer',swordsman='Épéiste',elitearcher="Archer d’élite",royalguard='Garde royal',drummer='Tambour militaire',messenger='Messager')
ftl='medieval-title = Les Terres du Bourg\nmedieval-resume = Au travail\nmedieval-cancel = Annuler chantier\n'
ftl+=''.join(f'medieval-{key} = {value}\n' for key,value in names.items())
ftl+='medieval-mail-arrived = Un messager attend devant votre caserne. Sélectionnez-le pour lire ou refuser le courrier.\nmedieval-mail-returned = Votre messager est revenu à sa caserne. Sélectionnez-le pour consulter le résultat.\n'
(MOD/'fluent/medieval.ftl').write_text(ftl,encoding='utf-8')

N=64;H=128;rng=random.Random(41)
data=struct.pack('<BHHIII',2,N,H,17,0,17+3*N*H)
# The village starts on grass: residents create desire paths by walking.
for u in range(N):
    for v in range(H):
        data+=struct.pack('<HB',255,rng.randrange(16))
data+=bytes(2*N*H)
actors=[]
placements=[]
def actor(name,x,y,owner='Village'):
    placements.append((name,x,y))
    actors.append(f'\tActor{len(actors)}: md.{name}\n\t\tOwner: {owner}\n\t\tLocation: {x+32},{y-32}\n')
    if name == 'warehouse': actors[-1] += '\t\tMedievalStock: 180, 100, 80, 100\n'
    if name == 'market': actors[-1] += '\t\tMedievalStock: 0, 40, 0, 0\n'
for name,x,y in [('warehouse',28,28),('house',32,29),('sawmill',18,25),('quarry',40,26),('farm',24,38),('ranch',35,38),('field',23,43),('field',27,43),('pasture',35,43),('pasture',39,43),('builder',28,33),('builder',30,33),('hauler',26,32),('hauler',27,33),('lumberjack',19,29),('stonecutter',40,30),('farmer',25,42),('breeder',37,42)]:actor(name,x,y)
actor('market',28,34)
for i,(x,y) in enumerate([(14,17),(17,17),(20,18),(13,21),(16,21),(14,25),(10,18),(11,24),(18,14),(22,15),(9,28),(12,31)]):actor(('tree','pine','birch')[i%3],x,y,'Neutral')
for x,y in [(44,19),(47,20),(44,23),(49,24),(46,16)]:actor('stone',x,y,'Neutral')
for i,(x,y) in enumerate([(12,16),(16,15),(10,22),(15,28),(21,18),(23,16),(46,22),(43,18),(49,26),(22,39),(22,46),(32,46),(38,48),(44,44),(36,25),(16,35),(45,35)]):
    actor(('shrub','grasses','flowers')[i%3],x,y,'Neutral')
# Small irregular meadows and woodland edges, with clear space around the
# working village. These plants are non-blocking and hide below later builds.
occupied={(x,y) for _,x,y in placements}
reserved=set()
for name,x,y in placements:
    if name in FOOTPRINTS or name in ('field','pasture'):
        w,h=FOOTPRINTS.get(name,(2,2))
        reserved.update((xx,yy) for xx in range(x-1,x+w+1) for yy in range(y-1,y+h+1))
meadow=random.Random(8031)
for cx,cy,radius,count in [(12,19,5,18),(19,16,5,16),(22,33,4,12),(18,41,5,18),
        (27,49,5,18),(35,48,4,16),(43,45,5,18),(44,32,4,12),(46,21,4,12),(32,23,4,12)]:
    made=0
    for _ in range(count*8):
        x=round(meadow.gauss(cx,radius*.55));y=round(meadow.gauss(cy,radius*.55))
        if not (5<=x<=55 and 8<=y<=55) or (x,y) in occupied or (x,y) in reserved:continue
        actor(meadow.choice(('grasses','grasses','flowers','flowers','shrub')),x,y,'Neutral')
        occupied.add((x,y));made+=1
        if made==count:break
for i,(x,y) in enumerate([(8,16),(10,13),(14,11),(17,10),(21,12),(24,14),(7,21),(8,25)]):
    if (x,y) not in occupied: actor(('tree','birch','pine')[i%3],x,y,'Neutral')
header='''MapFormat: 12
RequiresMod: medieval
Title: Le Bourg - economie medievale
Author: Projet medieval
Tileset: TEMPERAT
MapSize: 64,128
Bounds: 2,2,60,124
Visibility: Lobby
Categories: Conquest
Players:
\tPlayerReference@Neutral:
\t\tName: Neutral
\t\tOwnsWorld: True
\t\tNonCombatant: True
\t\tFaction: england
\tPlayerReference@Creeps:
\t\tName: Creeps
\t\tNonCombatant: True
\t\tFaction: england
\tPlayerReference@Village:
\t\tName: Village
\t\tPlayable: True
\t\tAllowBots: False
\t\tRequired: True
\t\tLockFaction: True
\t\tFaction: england
\t\tColor: 6C91B3
Actors:
\tSpawn: mpspawn
\t\tOwner: Neutral
\t\tLocation: 61,-3
'''
# Shroud is disabled only for the teaching map; normal exploration/placement remains in engine.
rules='''Rules:
\tPlayer:
\t\tShroud:
\t\t\tFogCheckboxEnabled: False
\t\t\tExploredMapCheckboxEnabled: True
'''
for folder in ('village','menu'):
    text=header+''.join(actors)+rules
    if folder=='menu':text=text.replace('Visibility: Lobby','Visibility: Shellmap').replace('\t\tPlayable: True\n','').replace('\t\tRequired: True\n','')
    (MOD/'maps'/folder/'map.yaml').write_text(text,encoding='utf-8')
    (MOD/'maps'/folder/'map.bin').write_bytes(data)

from messenger_maps import write_maps
write_maps(MOD, data, header, rules)
from large_maps import generate as generate_large_map, SPECS as LARGE_MAP_SPECS
for spec in LARGE_MAP_SPECS:
    generate_large_map(spec)
large_map_testmod=ROOT/'mods/medieval-large-maps'
large_map_testmod.mkdir(exist_ok=True)
(large_map_testmod/'mod.yaml').write_text(manifest.replace('\tmedieval|rules.yaml',
    '\tmedieval|rules.yaml\n\tmedieval|large-map-tests.yaml'),encoding='utf-8',newline='\n')
from caravan_art import main as generate_caravan
from founding_start import main as generate_founding_starts
from founding_content import main as wire_founding_content
generate_caravan()
generate_founding_starts()
wire_founding_content()
from heraldry_art import main as generate_heraldry
from heraldry_content import main as wire_heraldry
generate_heraldry(regenerate_existing=False)
wire_heraldry()
from wilderness_art import main as generate_wilderness
from wilderness_content import main as wire_wilderness
generate_wilderness()
wire_wilderness()
from camping_art import main as draw_camping
from camping_content import main as wire_camping
draw_camping()
wire_camping()
from construction_menu_content import main as wire_construction_menu
wire_construction_menu()
from commander_content import main as wire_commander
wire_commander()

# Contact sheet for reviewing the compiled sprites at native game scale.
names.update(pine='Conifère',birch='Bouleau',shrub='Buisson',grasses='Herbes',flowers='Fleurs')
preview=Image.new('RGB',(960,math.ceil(len(names)/5)*206),'#586546');pd=ImageDraw.Draw(preview)
for i,name in enumerate(names):
    sheet=Image.open(MOD/'art'/f'{"supplycart" if name=="tradecart" else name}.png');fw=int(sheet.info['FrameSize'].split(',')[0]);frame=sheet.crop((0,0,fw,sheet.height))
    if name in SIEGE: frame.thumbnail((184,184),Image.Resampling.LANCZOS);fw=frame.width
    col,row=i%5,i//5;x=col*192;y=row*206
    preview.paste(frame,(x+(184-fw)//2,y+5),frame);pd.text((x+4,y+187),names[name],fill='#eee4ce')
preview.save(MOD/'art/contact-sheet.png')
# MiniYaml's canonical writer uses LF, including on Windows.
for path in list(MOD.rglob('*.yaml')) + list(MOD.rglob('*.ftl')):
    path.write_text(path.read_text(encoding='utf-8'), encoding='utf-8', newline='\n')
print(f'Generated medieval mod wiring, {len(list((MOD/'art').glob('*.png')))} art files, interface, village and menu maps.')
