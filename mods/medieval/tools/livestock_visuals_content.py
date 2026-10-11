"""Generate livestock art separately, with a preview mod before live installation."""
from pathlib import Path
import argparse
import shutil
from PIL import Image, ImageDraw
import wilderness_art
from livestock_art import livestock, KINDS, MODES
from animation_art import unit_frame, HERD_MODES, UNIT_MODES

MOD = Path(__file__).resolve().parents[1]
GUIDES = ('breeder','shepherd','militia','swordsman','manatarms','archer','elitearcher','royalguard','commander')


def wire_live():
    for manifest in MOD.parent.glob('medieval*/mod.yaml'):
        text=manifest.read_text(encoding='utf-8')
        if '$medieval: medieval' not in text or 'preview|' in text: continue
        entry='\tmedieval|sequences/livestock-visuals.yaml'
        if entry not in text:
            text=text.replace('\tmedieval|sequences/village-life.yaml','\tmedieval|sequences/village-life.yaml\n'+entry)
            manifest.write_text(text,encoding='utf-8',newline='\n')


def install(staged):
    """Publish exactly the art that passed preview validation, without regenerating it."""
    names=['herd-'+k for k in KINDS]+['herding-'+g for g in GUIDES]+['shepherd']
    for name in names:
        if not (staged/'art'/f'{name}.png').is_file(): raise FileNotFoundError(name)
    seq=(staged/'sequences/livestock-visuals.yaml').read_text(encoding='utf-8').replace('herd-art-preview|','medieval|')
    for name in names: shutil.copy2(staged/'art'/f'{name}.png',MOD/'art'/f'{name}.png')
    (MOD/'sequences/livestock-visuals.yaml').write_text(seq,encoding='utf-8',newline='\n')
    wire_live()
    print('Validated livestock art installed in '+str(MOD))


def main(output=None):
    target=Path(output) if output else MOD
    for folder in ('art','sequences'): (target/folder).mkdir(parents=True,exist_ok=True)
    wilderness_art.MOD=target
    package='herd-art-preview' if output else 'medieval'
    seq=[]; preview=Image.new('RGB',(768,440),'#526247'); d=ImageDraw.Draw(preview)
    for row,kind in enumerate(KINDS):
        frames=[livestock(kind,f,s,m) for m in MODES for f in range(8) for s in range(8)]
        wilderness_art.save('herd-'+kind,frames)
        image={'calf':'cow-young','lamb':'sheep-young'}.get(kind,kind)
        seq += [f'md.livestock-{image}:','\tidle:',f'\t\tFilename: {package}|art/herd-{kind}.png','\t\tOffset: 0, -16','\t\tLength: 320','']
        d.text((8,row*90+4),{'cow':'Vache','calf':'Veau','sheep':'Mouton','lamb':'Agneau'}[kind],fill='#f1e8ce')
        for f in range(8):
            pose=frames[64+f*8+2].resize((96,84),Image.Resampling.NEAREST)
            preview.paste(pose,(f*96,row*90+8),pose)
    for name in GUIDES:
        frames=[unit_frame(name,f,s,m) for m in HERD_MODES for f in range(8) for s in range(8)]
        wilderness_art.save('herding-'+name,frames)
        seq += [f'md.{name}:']
        if name=='shepherd':
            wilderness_art.save(name,[unit_frame(name,f,s,m) for m in UNIT_MODES for f in range(8) for s in range(8)])
            seq += ['\tDefaults:',f'\t\tFilename: {package}|art/shepherd.png']
        for i,m in enumerate(HERD_MODES):
            seq += [f'\t{m}:',f'\t\tFilename: {package}|art/herding-{name}.png', '\t\tOffset: 0, -12', f'\t\tStart: {i*64}','\t\tLength: 8','\t\tFacings: 8','\t\tTick: 120']
        seq += ['']
    for s in range(8):
        p=unit_frame('shepherd',6,s,'catch').resize((84,78),Image.Resampling.NEAREST)
        preview.paste(p,(s*96,358),p)
    (target/'sequences/livestock-visuals.yaml').write_text('\n'.join(seq),encoding='utf-8',newline='\n')
    out=MOD.parents[1]/'analysis/medieval/livestock-visuals';out.mkdir(parents=True,exist_ok=True)
    preview.save(out/'designs.png')
    # Animated review uses the very same individual frames that are packed for OpenRA.
    movie=[]
    for step in range(24):
        canvas=Image.new('RGB',(640,384),'#526247');draw=ImageDraw.Draw(canvas)
        for row,kind in enumerate(KINDS):
            draw.text((8,row*96+4),kind,fill='#eee0bf')
            for col,mode in enumerate(MODES[:4]):
                pose=livestock(kind,0,step%8,mode).resize((128,112),Image.Resampling.NEAREST)
                canvas.paste(pose,(col*160+20,row*96-8),pose)
        movie.append(canvas)
    movie[0].save(out/'animation-review.gif',save_all=True,append_images=movie[1:],duration=120,loop=0)
    if output:
        text=(MOD/'mod.yaml').read_text(encoding='utf-8')
        text=text.replace('\tmedieval|sequences/livestock-visuals.yaml\n','')
        text=text.replace('\t\t$medieval: medieval',f'\t\t$medieval: medieval\n\t\t^EngineDir|mods/{target.name}: {package}')
        text=text.replace('\tmedieval|sequences/village-life.yaml',f'\tmedieval|sequences/village-life.yaml\n\t{package}|sequences/livestock-visuals.yaml')
        text=text.replace('\tmedieval|patrol.yaml','\tmedieval|patrol.yaml\n\tmedieval|herds-tests.yaml').replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4')
        folder=MOD.parent/'medieval-livestock-art-preview';folder.mkdir(exist_ok=True)
        (folder/'mod.yaml').write_text(text,encoding='utf-8',newline='\n')
    else:
        wire_live()
    print('Livestock visuals generated in '+str(target))


if __name__=='__main__':
    parser=argparse.ArgumentParser();group=parser.add_mutually_exclusive_group()
    group.add_argument('--output',type=Path);group.add_argument('--install-from',type=Path);args=parser.parse_args()
    if args.install_from:install(args.install_from)
    else:main(args.output)
