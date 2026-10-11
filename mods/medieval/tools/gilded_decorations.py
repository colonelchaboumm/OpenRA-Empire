"""Gold-trimmed variants of the editable native isometric village decorations."""
import math
from pathlib import Path
from PIL import Image, ImageDraw
from village_life_art import furnishing
from decoration_art import frames as wooden_frames, point
from wilderness_art import save, MOD

# Name, footprint, wood, stone, gold, work, height, traversable ground.
SPECS = {
    'gildedknight': ('Statue de chevalier dorée',1,8,40,20,22,65,False),
    'gildedlion': ('Lion couronné doré',1,6,32,14,18,40,False),
    'gildedplaza': ('Placette aux incrustations d’or',2,0,24,12,9,0,True),
    'gildedplazalarge': ('Place royale aux motifs d’or',3,0,45,25,16,0,True),
    'gildedfence': ('Grille de fer aux pointes dorées',1,2,4,2,5,24,False),
    'gildedfencey': ('Grille de fer aux pointes dorées',1,2,4,2,5,24,False),
    'gildedgate': ('Portillon de fer doré ouvert',1,4,6,3,7,24,False),
    'gildedgatey': ('Portillon de fer doré ouvert',1,4,6,3,7,24,False),
}
GOLD, LIGHT, SHADE = '#c89934', '#f2d578', '#846128'


def ornament(name, construction=False):
    base = {'gildedknight':'statueknight','gildedlion':'statuelion',
            'gildedplaza':'plaza','gildedplazalarge':'plazalarge'}[name]
    im = furnishing(base, construction)
    if construction: return im
    d = ImageDraw.Draw(im)
    def p(x,y,z=0): return round(96+(x-y)*24), round(120+(x+y)*12-z)
    def gild(points,width=2):
        d.line(points,fill=SHADE,width=width+1)
        d.line([(x,y-1) for x,y in points],fill=GOLD,width=width)
        d.line([(x,y-1) for x,y in points],fill=LIGHT,width=1)
    if 'plaza' in name:
        size = SPECS[name][1]
        # Fine inlays between paving stones, all on the same 2:1 ground plane.
        for inset in (.08,.24):
            r=size/2-inset
            gild([p(-r,-r),p(r,-r),p(r,r),p(-r,r),p(-r,-r)],1)
        radius = .42 if size==2 else .72
        for i in range(8):
            a=i*math.tau/8; b=a+math.pi/8; c=a-math.pi/8
            pts=[p(0,0),p(radius*.35*math.cos(c),radius*.35*math.sin(c)),
                 p(radius*math.cos(a),radius*math.sin(a)),p(radius*.35*math.cos(b),radius*.35*math.sin(b))]
            d.polygon(pts,fill=GOLD if i%2 else '#52615e',outline=SHADE)
            d.line(pts[1:3],fill=LIGHT)
        for sx in (-1,1):
            for sy in (-1,1):
                cx,cy=sx*(size/2-.40),sy*(size/2-.40)
                pts=[p(cx-.12,cy),p(cx,cy-.12),p(cx+.12,cy),p(cx,cy+.12)]
                d.polygon(pts,fill=GOLD,outline=LIGHT)
        d.ellipse((94,118,98,121),fill=LIGHT,outline=SHADE)
        return im
    # Gilded pedestal bands and a bronze dedication plaque.
    for z in (5,16): gild([p(-.28,.28,z),p(.28,.28,z),p(.28,-.28,z)],1)
    d.polygon([p(-.12,.275,7),p(.14,.275,7),p(.14,.275,12),p(-.12,.275,12)],fill=GOLD,outline=SHADE)
    if name=='gildedknight':
        # Stone remains visible: only visor trim, pauldrons, shield and sword hilt are gold.
        gild([(90,48),(94,45),(100,47)],1)
        gild([(91,53),(96,54),(101,52)],1)
        gild([(87,63),(90,59),(94,58)],1);gild([(98,58),(102,61),(103,65)],1)
        gild([(83,68),(91,67),(91,79),(86,85),(82,78),(83,68)],1)
        gild([(86,70),(86,80)],1);gild([(83,75),(90,74)],1)
        gild([(89,75),(102,75)],1);gild([(104,77),(112,77)],1)
        d.ellipse((106,71,110,75),fill=GOLD,outline=LIGHT)
    else:
        # Crown and ceremonial collar on the existing stone lion.
        d.polygon([(83,74),(82,67),(86,70),(89,65),(92,70),(96,66),(95,74)],fill=GOLD,outline=SHADE)
        d.line([(83,73),(94,73)],fill=LIGHT)
        for x,y in ((82,67),(89,65),(96,66)):d.ellipse((x-1,y-1,x+1,y+1),fill=LIGHT)
        gild([(82,84),(89,87),(96,82)],2)
        d.polygon([(90,87),(93,89),(90,92),(88,89)],fill=GOLD,outline=LIGHT)
        gild([(80,98),(85,99)],1);gild([(103,96),(108,96)],1)
    return im


def iron(name, mask=0):
    im=Image.new('RGBA',(96,112));d=ImageDraw.Draw(im)
    along_y=name.endswith('y'); gate=name.startswith('gildedgate')
    def p(t,z=0):return point(0 if along_y else t,t if along_y else 0,z)
    mask=mask or (12 if along_y else 3)
    ends=[point(x,y) for i,(x,y) in enumerate(((.5,0),(-.5,0),(0,.5),(0,-.5))) if mask&(1<<i)]
    segments=[(p(-.5),p(-.22))] if gate else [(point(0,0),e) for e in ends]
    for a,b in sorted(segments,key=lambda pair:pair[1][1]):
        d.line([a,b],fill=(26,30,26,70),width=3)
        for z in (5,14):
            d.line([(a[0],a[1]-z),(b[0],b[1]-z)],fill='#26342f',width=3)
            d.line([(a[0],a[1]-z-1),(b[0],b[1]-z-1)],fill='#8b9990')
        for t in (.25,.65):
            x=round(a[0]+(b[0]-a[0])*t);y=round(a[1]+(b[1]-a[1])*t)
            d.line((x,y-2,x,y-18),fill='#283831',width=2)
            d.line((x,y-3,x,y-16),fill='#95a398')
            d.polygon([(x-2,y-18),(x,y-23),(x+2,y-18),(x,y-16)],fill=GOLD,outline=SHADE)
            d.line((x,y-22,x,y-18),fill=LIGHT)
    posts=[p(-.48),p(.48)] if gate else ends+[point(0,0)]
    for x,y in sorted(posts,key=lambda v:v[1]):
        d.polygon([(x-2,y),(x+2,y+1),(x+2,y-19),(x-2,y-20)],fill='#394941',outline='#213129')
        d.line((x-1,y-17,x-1,y-2),fill='#a0ada0')
        for z in (4,16):d.line((x-2,y-z,x+2,y-z),fill=GOLD,width=2)
        d.polygon([(x-3,y-21),(x,y-25),(x+3,y-21),(x,y-18)],fill=GOLD,outline=SHADE)
        d.line((x,y-24,x,y-20),fill=LIGHT)
    return im


def main():
    rules=[];seq=[];fluent=[]
    for name,(label,size,wood,stone,gold,work,height,ground) in SPECS.items():
        fence='fence' in name;gate='gate' in name
        length=16 if fence else 1
        if fence or gate:
            poses=[iron(name,i) for i in range(length)]
            poses.append(wooden_frames('gardenfence' if fence else 'gardengate')[-1])
        else:poses=[ornament(name),ornament(name,True)]
        save(name,poses)
        footprint=' '.join(('=' if ground or gate else 'x')*size for _ in range(size))
        rules += [f'md.{name}:','\tInherits: ^MDDECORATION','\tTooltip:',f'\t\tName: medieval-{name}',
                  '\tBuilding:',f'\t\tDimensions: {size},{size}',f'\t\tFootprint: {footprint}',
                  '\tIsometricSelectable:',f'\t\tHeight: {height}',f'\t\tDecorationHeight: {max(4,height)}',
                  '\tMedievalBuilding:',f'\t\tCost: {wood}, 0, 0, {stone}, {gold}',f'\t\tWork: {work}']
        if ground:rules+=['\tMedievalDecoration:','\t\tGround: True']
        if fence:rules+=['\tMedievalGardenFence:']
        rules+=['']
        seq += [f'md.{name}:','\tDefaults:',f'\t\tFilename: medieval|art/{name}.png',f'\t\tOffset: 0, {-28 if fence or gate else -32}']
        if ground:seq+=['\t\tZOffset: -8c0']
        seq+=['\tidle:',f'\t\tLength: {length}','\tconstruction:',f'\t\tStart: {length}','']
        fluent.append(f'medieval-{name} = {label}')
    for path,lines in [('gilded-decorations.yaml',rules),('sequences/gilded-decorations.yaml',seq),('fluent/gilded-decorations.ftl',fluent)]:
        (MOD/path).write_text('\n'.join(lines).rstrip()+'\n',encoding='utf-8',newline='\n')
    # All test manifests use the same new assets and gameplay rules as the normal mod.
    for manifest in MOD.parent.glob('medieval*/mod.yaml'):
        text=manifest.read_text(encoding='utf-8-sig')
        for prefix,ext in [('', 'yaml'),('sequences/','yaml'),('fluent/','ftl')]:
            old=f'\tmedieval|{prefix}gold-church.{ext}\n';new=f'\tmedieval|{prefix}gilded-decorations.{ext}\n'
            if old in text and new not in text:text=text.replace(old,old+new,1)
        manifest.write_text(text,encoding='utf-8',newline='\n')
    from construction_menu_content import main as menu
    menu(wire=False)
    print('Generated eight gilded decorations, gold costs, names and paginated catalogue icons.')


if __name__=='__main__':main()
