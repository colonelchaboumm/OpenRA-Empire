"""Replace playable settlement actors with one provisioned founding caravan.

The prebuilt 'village' map remains a regression fixture, outside the lobby;
'fondation' is the normal small starting map. Neutral resources are preserved.
"""
from pathlib import Path
import re
import shutil

MOD = Path(__file__).resolve().parents[1]


def apply(path):
    text = (path/'map.yaml').read_text(encoding='utf-8')
    header, rest = text.split('Actors:\n', 1)
    actor_text, rules = rest.split('Rules:\n', 1)
    blocks = re.findall(r'\t[^\t\n][^\n]*\n(?:\t\t[^\n]*\n)*', actor_text)
    starts, kept = {}, []
    for block in blocks:
        owner = re.search(r'\t\tOwner: (.+)', block).group(1)
        if owner in ('Neutral', 'Creeps'):
            kept.append(block)
        elif ': md.warehouse\n' in block or ': md.caravan\n' in block:
            x,y = map(int,re.search(r'\t\tLocation: (.+)',block).group(1).split(','))
            starts[owner] = (x,y) if ': md.caravan\n' in block else (x+1,y+1)
    for owner,(x,y) in starts.items():
        kept.append(f'\tFounders{owner}: md.caravan\n\t\tOwner: {owner}\n\t\tLocation: {x},{y}\n')
    assert starts, path
    (path/'map.yaml').write_text(header+'Actors:\n'+''.join(kept)+'Rules:\n'+rules,encoding='utf-8',newline='\n')


def main():
    # Preserve a like-for-like 16,137-actor performance fixture before migration.
    benchmark = MOD/'maps/large-map-benchmark'
    if not benchmark.exists():
        shutil.copytree(MOD/'maps/terres-sans-fin', benchmark)
        yaml=(benchmark/'map.yaml').read_text(encoding='utf-8').replace('Visibility: Lobby','Visibility: MissionSelector')
        yaml=yaml.replace('Les Terres sans Fin - immense','Validation performances - grande carte')
        (benchmark/'map.yaml').write_text(yaml,encoding='utf-8',newline='\n')
        import json
        path=benchmark/'geography.json'; geo=json.loads(path.read_text(encoding='utf-8'));geo['folder']='large-map-benchmark'
        path.write_text(json.dumps(geo,indent=2),encoding='utf-8')
    village=MOD/'maps/village'; foundation=MOD/'maps/fondation';foundation.mkdir(exist_ok=True)
    shutil.copyfile(village/'map.bin',foundation/'map.bin')
    text=(village/'map.yaml').read_text(encoding='utf-8').replace('Visibility: MissionSelector','Visibility: Lobby')
    text=text.replace('Le Bourg - economie medievale','Le Bourg - fondation par caravane')
    (foundation/'map.yaml').write_text(text,encoding='utf-8',newline='\n');apply(foundation)
    text=(village/'map.yaml').read_text(encoding='utf-8').replace('Visibility: Lobby','Visibility: MissionSelector')
    (village/'map.yaml').write_text(text,encoding='utf-8',newline='\n')
    for name in ('deux-bourgs','trois-bourgs','grands-meandres','marches-boisees','terres-sans-fin'):
        apply(MOD/'maps'/name)
    from multiplayer_nature import populate
    for i,name in enumerate(('deux-bourgs','trois-bourgs')):
        populate(MOD/'maps'/name, 64217+i)


if __name__ == '__main__': main()
