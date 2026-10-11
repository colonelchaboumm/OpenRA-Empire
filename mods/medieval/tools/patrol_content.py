"""Wire militia patrols with isolated chrome/rule staging before binary installation."""
from pathlib import Path
import argparse
import re

MOD=Path(__file__).resolve().parents[1]
RULES='''^MDFIGHTER:
\tAutoTarget:
\t\tScanRadius: 6

md.militia:
\tMedievalPatrol:
\tMedievalTorch:
'''

def chrome(text):
    if 'MedievalPatrolLogic' not in text:
        text=text.replace('MedievalConstructionMenuLogic','MedievalConstructionMenuLogic, MedievalPatrolLogic',1)
    text=re.sub(r'^\t\tBackground@MD_PATROL_PANEL:\n(?:\t{3,}[^\n]*\n|\n)*','',text,flags=re.M)
    lines=['\t\tBackground@MD_PATROL_PANEL:','\t\t\tX: 12','\t\t\tY: 398','\t\t\tWidth: 410','\t\t\tHeight: 240','\t\t\tBackground: md-panel','\t\t\tChildren:']
    def item(kind,name,x,y,w,h,extra=()):
        lines.extend([f'\t\t\t\t{kind}@MD_PATROL_{name}:',f'\t\t\t\t\tX: {x}',f'\t\t\t\t\tY: {y}',f'\t\t\t\t\tWidth: {w}',f'\t\t\t\t\tHeight: {h}','\t\t\t\t\tFont: TinyBold'])
        if kind=='Button':lines.append('\t\t\t\t\tBackground: md-button')
        lines.extend('\t\t\t\t\t'+v for v in extra)
    item('Label','TITLE',9,8,392,20)
    for i in range(3):item('Button',f'STANCE_{i}',9+i*132,34,128,28,['TooltipContainer: TOOLTIP_CONTAINER','TooltipTemplate: BUTTON_WITH_DESC_HIGHLIGHT_TOOLTIP'])
    item('Label','HINT',9,72,392,38,['WordWrap: True','VAlign: Top'])
    item('Button','DRAW',9,118,193,28);item('Button','START',207,118,194,28)
    item('Button','NIGHT',9,152,193,26,['TooltipContainer: TOOLTIP_CONTAINER','TooltipTemplate: BUTTON_WITH_DESC_HIGHLIGHT_TOOLTIP'])
    item('Button','STOP',207,152,194,26)
    item('Label','STATUS',9,187,392,44,['WordWrap: True','VAlign: Top'])
    return text.rstrip()+'\n'+'\n'.join(lines)+'\n'

def main(output=None):
    target=Path(output) if output else MOD
    (target/'chrome').mkdir(parents=True,exist_ok=True)
    (target/'patrol.yaml').write_text(RULES,encoding='utf-8',newline='\n')
    (target/'chrome/ingame-player.yaml').write_text(chrome((MOD/'chrome/ingame-player.yaml').read_text(encoding='utf-8')),encoding='utf-8',newline='\n')
    manifest=(MOD/'mod.yaml').read_text(encoding='utf-8')
    if '\tmedieval|patrol.yaml' not in manifest:
        manifest=manifest.replace('\tmedieval|village-life.yaml','\tmedieval|village-life.yaml\n\tmedieval|patrol.yaml',1)
    if output:
        manifest=manifest.replace('\t\t$medieval: medieval',f'\t\t$medieval: medieval\n\t\t^EngineDir|mods/{target.name}: patrol-preview')
        manifest=manifest.replace('medieval|chrome/ingame-player.yaml','patrol-preview|chrome/ingame-player.yaml').replace('medieval|patrol.yaml','patrol-preview|patrol.yaml')
        folder=MOD.parent/'medieval-patrol-preview'
    else:
        for path in MOD.parent.glob('medieval*/mod.yaml'):
            text=path.read_text(encoding='utf-8')
            if '\t\t$medieval: medieval' not in text:continue
            if 'medieval|patrol.yaml' not in text and 'patrol-preview|patrol.yaml' not in text:
                text=text.replace('\tmedieval|village-life.yaml','\tmedieval|village-life.yaml\n\tmedieval|patrol.yaml',1)
                path.write_text(text,encoding='utf-8',newline='\n')
        folder=MOD.parent/'medieval-patrol'
    folder.mkdir(exist_ok=True)
    anchor='patrol-preview|patrol.yaml' if output else 'medieval|patrol.yaml'
    manifest=manifest.replace('\t'+anchor,'\t'+anchor+'\n\tmedieval|patrol-tests.yaml',1).replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4')
    (folder/'mod.yaml').write_text(manifest,encoding='utf-8',newline='\n')
    night_folder=MOD.parent/('medieval-night-patrol-preview' if output else 'medieval-night-patrol')
    night_folder.mkdir(exist_ok=True)
    (night_folder/'mod.yaml').write_text(manifest.replace('medieval|patrol-tests.yaml','medieval|night-patrol-tests.yaml'),encoding='utf-8',newline='\n')
    print('Patrol content ready: '+str(target))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path);args=parser.parse_args();main(args.output)
