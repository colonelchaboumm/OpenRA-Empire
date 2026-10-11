"""Generate in an isolated --output mod, then publish only after matching binaries are installed."""
from pathlib import Path
import argparse
import re
import wilderness_art
from wilderness_art import save
from isometric_art import agricultural_zone
from camping_art import sheepzone
from animation_art import unit_frame, UNIT_MODES
from village_life_art import DECORS, furnishing, livestock, near_fence

MOD=Path(__file__).resolve().parents[1]

def main(output=None, menu=True):
    target=Path(output) if output else MOD
    wilderness_art.MOD=target
    seq=[];rules=['md.pasture:','\tMedievalLivestock:','', 'md.sheeppasture:','\tMedievalLivestock:', '\t\tImage: md.livestock-sheep','', 'md.stonepath:', '\tMedievalDecoration:', '\t\tGround: True','']
    for kind in ('cow','sheep'):
        save('livestock-'+kind,[livestock(kind,f,s,m) for m in (False,True) for f in range(8) for s in range(8)])
        seq += [f'md.livestock-{kind}:','\tidle:',f'\t\tFilename: medieval|art/livestock-{kind}.png','\t\tOffset: 0, -16','\t\tLength: 128','']
    for kind in ('pasture','sheeppasture'):
        for w in range(2,11):
            for h in range(2,11):
                name=kind+('' if (w,h)==(2,2) else f'.{w}x{h}')
                frames=[sheepzone(w,h,s,f,True) if kind=='sheeppasture' else agricultural_zone(w,h,s,True,f,True) for s in range(4) for f in range(8 if s<3 else 1)]
                save(name,frames)
                fence=f'{kind}-fence.{w}x{h}';save(fence,[near_fence(w,h,kind=='sheeppasture')])
                seq += [f'md.{name}:','\tfence:',f'\t\tFilename: medieval|art/{fence}.png','\t\tOffset: 0, -6','\t\tZOffset: 0','']
    save('farmer',[unit_frame('farmer',f,s,m) for m in UNIT_MODES for f in range(8) for s in range(8)])
    for name,(label,size,wood,stone,work,solid,height) in DECORS.items():
        save(name,[furnishing(name),furnishing(name,True)])
        rules += [f'md.{name}:','\tInherits: ^MDDECORATION','\tTooltip:',f'\t\tName: medieval-{name}',
                  '\tBuilding:',f'\t\tDimensions: {size},{size}',f'\t\tFootprint: {" ".join(("x" if solid else "=")*size for _ in range(size))}',
                  '\tIsometricSelectable:',f'\t\tHeight: {height}',f'\t\tDecorationHeight: {max(4,height)}',
                  '\tMedievalBuilding:',f'\t\tCost: {wood}, 0, 0, {stone}',f'\t\tWork: {work}']
        if not solid:rules+=['\tMedievalDecoration:','\t\tGround: True']
        rules+=['']
        seq += [f'md.{name}:','\tDefaults:',f'\t\tFilename: medieval|art/{name}.png','\t\tOffset: 0, -32']
        if not solid:seq+=['\t\tZOffset: -8c0']
        seq+=['\tidle:','\tconstruction:','\t\tStart: 1','']
    (target/'village-life.yaml').write_text('\n'.join(rules),encoding='utf-8',newline='\n')
    (target/'sequences/village-life.yaml').write_text('\n'.join(seq),encoding='utf-8',newline='\n')
    (target/'fluent/village-life.ftl').write_text('\n'.join(f'medieval-{n} = {v[0]}' for n,v in DECORS.items())+'\n',encoding='utf-8',newline='\n')
    if menu:
        from construction_menu_content import main as make_menu
        make_menu(target,wire=False)
    manifests=[target/'mod.yaml'] if output else list(MOD.parent.glob('medieval*/mod.yaml'))
    for manifest in manifests:
        text=manifest.read_text(encoding='utf-8')
        for prefix,suffix in (('','yaml'),('sequences/','yaml'),('fluent/','ftl')):
            entry=f'\tmedieval|{prefix}village-life.{suffix}'
            if entry not in text:
                anchor=next((f'\tmedieval|{prefix}{name}.{suffix}' for name in ('commander','camping') if f'\tmedieval|{prefix}{name}.{suffix}' in text),None)
                if anchor is None:raise ValueError(f'Missing content anchor in {manifest}: {prefix}')
                text=text.replace(anchor,anchor+'\n'+entry,1)
        manifest.write_text(text,encoding='utf-8',newline='\n')
    if output:
        # The package alias stays 'medieval'; all reads resolve to this separate content copy.
        preview=MOD.parent/'medieval-life-preview';preview.mkdir(exist_ok=True)
        text=(target/'mod.yaml').read_text(encoding='utf-8').replace('$medieval: medieval',f'${target.name}: medieval')
        text=text.replace('\tmedieval|village-life.yaml','\tmedieval|village-life.yaml\n\tmedieval|village-life-tests.yaml',1).replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4')
        (preview/'mod.yaml').write_text(text,encoding='utf-8',newline='\n')
    else:
        validation=MOD.parent/'medieval-village-life';validation.mkdir(exist_ok=True)
        text=(target/'mod.yaml').read_text(encoding='utf-8')
        text=text.replace('\tmedieval|village-life.yaml','\tmedieval|village-life.yaml\n\tmedieval|village-life-tests.yaml',1).replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4')
        (validation/'mod.yaml').write_text(text,encoding='utf-8',newline='\n')
    print('Village life content generated in '+str(target))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path);args=parser.parse_args();main(args.output)
