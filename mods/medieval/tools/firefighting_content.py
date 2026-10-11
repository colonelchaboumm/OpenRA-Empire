"""Fire response rules and water-carrying animations; supports an isolated preview."""
from pathlib import Path
import argparse
import shutil
from PIL import Image, ImageDraw
import wilderness_art
from animation_art import unit_frame

MOD=Path(__file__).resolve().parents[1]
PEOPLE=('builder','hauler','lumberjack','stonecutter','farmer','breeder','blacksmith','recruiter','shepherd','weaver','militia')
MODES=('water-empty-stand','water-empty-run','water-stand','water-run','water-fill','water-pour')
RULES='World:\n\tMedievalFireResponse:\n\nmd.well:\n\tMedievalWell:\n\n'
for name in PEOPLE:
    RULES+=f'md.{name}:\n\tMedievalFirefighter:\n'
    if name=='militia':
        RULES+='\t\tBusyCondition: firefighting\n\tAutoTarget:\n\t\tRequiresCondition: !sleeping && !herding && !firefighting && !siege-crew && !siege-capturing\n\tAttackFrontal:\n\t\tPauseOnCondition: sleeping || herding || firefighting || siege-crew || siege-capturing\n'
    RULES+='\n'
RULES+='md.saboteur:\n\tMedievalTorch:\n\t\tScalePercent: 35\n'
TESTS='World:\n\tMedievalFirefightingValidation:\n\tMedievalWildHerds:\n\t\tSpawnAnimals: False\n\tMedievalDayNight:\n\t\tDayTicks: 1000\n\t\tNightTicks: 6000\n\tMedievalWeather:\n\t\tFirstRainTicks: 30000\n\n^MDWORKER:\n\tMedievalNeedsFood:\n\t\tMealInterval: 30000\n\t\tStarvationDelay: 60000\n'

def wire(text,package='medieval'):
    for anchor,entry in [('saboteur.yaml','firefighting.yaml'),('sequences/saboteur.yaml','sequences/firefighting.yaml')]:
        text=text.replace('\tmedieval|'+entry+'\n','')
        text=text.replace('\tmedieval|'+anchor+'\n','\tmedieval|'+anchor+'\n\t'+package+'|'+entry+'\n',1)
    return text

def install(source):
    files=['firefighting.yaml','firefighting-tests.yaml','sequences/firefighting.yaml']+[f'art/water-{name}.png' for name in PEOPLE]
    if source.resolve()!=MOD.resolve():
        for name in files:
            if name.endswith('.png'): shutil.copy2(source/name,MOD/name)
            else: (MOD/name).write_text((source/name).read_text(encoding='utf-8').replace('fire-preview|','medieval|'),encoding='utf-8',newline='\n')
    for manifest in MOD.parent.glob('medieval*/mod.yaml'):
        text=manifest.read_text(encoding='utf-8')
        if '$medieval: medieval' in text and 'preview|' not in text: manifest.write_text(wire(text),encoding='utf-8',newline='\n')
    test=MOD.parent/'medieval-firefighting';test.mkdir(exist_ok=True)
    (test/'mod.yaml').write_text(wire((MOD/'mod.yaml').read_text(encoding='utf-8')).replace('\tmedieval|firefighting.yaml','\tmedieval|firefighting.yaml\n\tmedieval|firefighting-tests.yaml').replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4'),encoding='utf-8',newline='\n')

def main(output=None):
    target=output or MOD;package='fire-preview' if output else 'medieval'
    for folder in ('art','sequences'): (target/folder).mkdir(parents=True,exist_ok=True)
    wilderness_art.MOD=target;seq=[]
    for name in PEOPLE:
        frames=[unit_frame(name,f,s,m) for m in MODES for f in range(8) for s in range(8)]
        wilderness_art.save('water-'+name,frames)
        seq.append('md.'+name+':')
        for i,mode in enumerate(MODES):
            seq += [f'\t{mode}:',f'\t\tFilename: {package}|art/water-{name}.png','\t\tOffset: 0, -12',f'\t\tStart: {i*64}','\t\tLength: 8','\t\tFacings: 8','\t\tTick: 120']
        seq.append('')
    (target/'firefighting.yaml').write_text(RULES,encoding='utf-8',newline='\n')
    (target/'firefighting-tests.yaml').write_text(TESTS,encoding='utf-8',newline='\n')
    (target/'sequences/firefighting.yaml').write_text('\n'.join(seq),encoding='utf-8',newline='\n')
    if output:
        text=wire((MOD/'mod.yaml').read_text(encoding='utf-8'),package)
        text=text.replace('\t\t$medieval: medieval',f'\t\t$medieval: medieval\n\t\t^EngineDir|mods/{target.name}: {package}')
        text=text.replace('\tfire-preview|firefighting.yaml','\tfire-preview|firefighting.yaml\n\tfire-preview|firefighting-tests.yaml')
        test=MOD.parent/'medieval-firefighting-preview';test.mkdir(exist_ok=True)
        (test/'mod.yaml').write_text(text.replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4'),encoding='utf-8',newline='\n')
    else: install(MOD)
    out=MOD.parents[1]/'analysis/medieval/firefighting';out.mkdir(parents=True,exist_ok=True)
    preview=Image.new('RGB',(624,336),'#536347');d=ImageDraw.Draw(preview)
    for row,mode in enumerate(MODES):
        d.text((3,row*56+3),mode,fill='#eee5ce')
        for f in range(8):
            pose=unit_frame('farmer' if row<4 else 'militia',f,4,mode);preview.paste(pose,(152+f*58,row*56),pose)
    preview.save(out/'poses.png')
    print('Firefighting content ready: '+str(target))

if __name__=='__main__':
    parser=argparse.ArgumentParser();group=parser.add_mutually_exclusive_group();group.add_argument('--output',type=Path);group.add_argument('--install-from',type=Path);args=parser.parse_args()
    if args.install_from: install(args.install_from)
    else: main(args.output)
