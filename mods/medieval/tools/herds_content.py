"""Install real livestock rules and controls together with their matching game assembly."""
from pathlib import Path
import argparse
import re

MOD = Path(__file__).resolve().parents[1]
RULES = '''World:
\tMedievalWildHerds:

Player:
\tMedievalAnimalOrders:

md.breeder:
\tMedievalHerding:
\t\tBusyCondition:

md.shepherd:
\tMedievalHerding:
\t\tBusyCondition:

^MDFIGHTER:
\tMedievalHerding:
\tAutoTarget:
\t\tRequiresCondition: !sleeping && !herding && !siege-crew && !siege-capturing
\tAttackFrontal:
\t\tPauseOnCondition: sleeping || herding || siege-crew || siege-capturing

md.commander:
\tAutoTarget:
\t\tRequiresCondition: !sleeping && !commander-working && !herding
\tAttackFrontal:
\t\tPauseOnCondition: sleeping || commander-working || herding

^MDANIMAL:
\tMedievalActorAudio:
\tBodyOrientation:
\t\tQuantizedFacings: 8
\tSelectable:
\t\tBounds: 1100, 850, 0, -220
\t\tPriority: 15
\tSelectionDecorations:
\tTooltip:
\t\tName: medieval-cow
\tHealth:
\t\tHP: 3500
\tArmor:
\t\tType: None
\tTargetable:
\t\tTargetTypes: GroundActor, Infantry
\tHitShape:
\t\tType: Circle
\t\t\tRadius: 230
\tMedievalAnimalMobile:
\t\tLocomotor: foot
\t\tSpeed: 58
\t\tAlwaysTurnInPlace: True
\t\tVoice:
\tMedievalAnimal:
\tAppearsOnRadar:
\tHiddenUnderFog:
\t\tType: CenterPosition
\tMapEditorData:
\t\tCategories: Medieval

md.cow:
\tInherits: ^MDANIMAL

md.sheep:
\tInherits: ^MDANIMAL
\tTooltip:
\t\tName: medieval-sheep
\tMedievalAnimal:
\t\tImage: md.livestock-sheep
\tHealth:
\t\tHP: 2200
'''

def chrome(text):
    if 'MedievalHerdLogic' not in text:
        text = text.replace('MedievalPatrolLogic', 'MedievalPatrolLogic, MedievalHerdLogic', 1)
    text = re.sub(r'^\t\tBackground@MD_HERD_PANEL:\n(?:\t{3,}[^\n]*\n|\n)*', '', text, flags=re.M)
    lines = ['\t\tBackground@MD_HERD_PANEL:', '\t\t\tX: 12', '\t\t\tY: 155', '\t\t\tWidth: 410', '\t\t\tHeight: 240', '\t\t\tBackground: md-panel', '\t\t\tChildren:']
    for kind, name, y, height in [('Label','TITLE',8,20),('Label','STATUS',34,42),('Button','TRANSFER',83,28),('Button','CANCEL',116,27),('Label','HINT',154,76)]:
        lines += [f'\t\t\t\t{kind}@MD_HERD_{name}:','\t\t\t\t\tX: 9',f'\t\t\t\t\tY: {y}','\t\t\t\t\tWidth: 392',f'\t\t\t\t\tHeight: {height}','\t\t\t\t\tFont: TinyBold']
        if kind == 'Button': lines += ['\t\t\t\t\tBackground: md-button']
        else: lines += ['\t\t\t\t\tWordWrap: True','\t\t\t\t\tVAlign: Top']
    return text.rstrip() + '\n' + '\n'.join(lines) + '\n'

def wire(text, prefix='medieval'):
    for anchor, entry in [('medieval|patrol.yaml', f'{prefix}|herds.yaml'), ('medieval|fluent/camping.ftl', f'{prefix}|fluent/herds.ftl')]:
        if '\t'+entry not in text:
            if '\t'+anchor not in text: raise ValueError('Missing manifest anchor: '+anchor)
            text = text.replace('\t'+anchor, '\t'+anchor+'\n\t'+entry, 1)
    return text

def main(output=None):
    target = Path(output) if output else MOD
    (target/'chrome').mkdir(parents=True, exist_ok=True)
    (target/'fluent').mkdir(exist_ok=True)
    (target/'herds.yaml').write_text(RULES, encoding='utf-8', newline='\n')
    (target/'fluent/herds.ftl').write_text('medieval-cow = Bovin\nmedieval-sheep = Mouton\n', encoding='utf-8', newline='\n')
    (target/'chrome/ingame-player.yaml').write_text(chrome((MOD/'chrome/ingame-player.yaml').read_text(encoding='utf-8')), encoding='utf-8', newline='\n')
    manifest = (MOD/'mod.yaml').read_text(encoding='utf-8')
    if output:
        manifest = manifest.replace('\t\t$medieval: medieval', f'\t\t$medieval: medieval\n\t\t^EngineDir|mods/{target.name}: herds-preview')
        manifest = wire(manifest, 'herds-preview').replace('medieval|chrome/ingame-player.yaml', 'herds-preview|chrome/ingame-player.yaml')
        folder = MOD.parent/'medieval-herds-preview'
    else:
        for path in MOD.parent.glob('medieval*/mod.yaml'):
            content = path.read_text(encoding='utf-8')
            if '\t\t$medieval: medieval' not in content or 'herds-preview|' in content or 'patrol-preview|' in content: continue
            path.write_text(wire(content), encoding='utf-8', newline='\n')
        manifest = wire(manifest)
        folder = MOD.parent/'medieval-herds'
    folder.mkdir(exist_ok=True)
    manifest = manifest.replace('\tmedieval|patrol.yaml', '\tmedieval|patrol.yaml\n\tmedieval|herds-tests.yaml', 1).replace('\t\t\tTimestep: 40','\t\t\tTimestep: 4')
    (folder/'mod.yaml').write_text(manifest, encoding='utf-8', newline='\n')
    spawn_folder = MOD.parent/('medieval-herds-spawn-preview' if output else 'medieval-herds-spawn')
    spawn_folder.mkdir(exist_ok=True)
    (spawn_folder/'mod.yaml').write_text(manifest.replace('medieval|herds-tests.yaml','medieval|herds-spawn-tests.yaml'), encoding='utf-8', newline='\n')
    print('Herd content ready: '+str(target))

if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--output', type=Path); args = parser.parse_args(); main(args.output)
