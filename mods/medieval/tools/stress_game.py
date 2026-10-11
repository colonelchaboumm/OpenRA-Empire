"""Opt-in four-army load test, with a private map and settings profile."""
from pathlib import Path
import argparse
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT/'analysis/medieval/load-test'
TOWNS = [(250, -60), (480, -60), (250, 175), (480, 175)]
ARENA = (360, 55, 115, 110)

def cleared(x, y):
    return any(a-3 <= x < a+w+3 and b-3 <= y < b+h+3 for a,b,w,h in
               [*( (a,b,100,100) for a,b in TOWNS), ARENA])

def prepare(soldiers, workers, ticks, name):
    target = OUT/name
    target.mkdir(parents=True, exist_ok=True)
    package = ROOT/'mods/medieval-stress'
    package.mkdir(exist_ok=True)
    original = ROOT/'mods/medieval/maps/terres-sans-fin'
    mapdir = package/'maps/stress-immense'
    mapdir.mkdir(parents=True, exist_ok=True)
    data = bytearray((original/'map.bin').read_bytes())
    _, width, height, terrain, _, _ = struct.unpack_from('<BHHIII', data)
    for u in range(width):
        for v in range(height):
            y = v//2-u
            x = v-y
            if cleared(x,y):
                struct.pack_into('<HB',data,terrain+3*(u*height+v),255,(x*3+y*7)%16)
    (mapdir/'map.bin').write_bytes(data)
    yaml = (original/'map.yaml').read_text(encoding='utf-8')
    head, tail = yaml.split('Actors:\n',1)
    actors, rules = tail.split('Rules:\n',1)
    kept=[]
    for block in re.split(r'(?=^\t[^\t\n][^\n]*:\s*md\.)', actors, flags=re.M):
        loc=re.search(r'Location: (-?\d+),(-?\d+)',block)
        if ': md.' in block and (': md.caravan' in block or loc and cleared(*map(int,loc.groups()))):
            continue
        kept.append(block)
    colors=['CE5959','71AB73','C6A65E']
    for i,color in enumerate(colors,1):
        head += f'\tPlayerReference@Army{i}:\n\t\tName: Army{i}\n\t\tFaction: england\n\t\tColor: {color}\n\t\tEnemies: Village, ' + ', '.join(f'Army{k}' for k in range(1,4) if k!=i) + '\n'
    head=head.replace('Title: Les Terres sans Fin - immense','Title: Audit - quatre armees sur carte immense').replace('Visibility: Lobby','Visibility: MissionSelector')
    (mapdir/'map.yaml').write_text(head+'Actors:\n'+''.join(kept)+'Rules:\n'+rules,encoding='utf-8')
    (mapdir/'map.png').write_bytes((original/'map.png').read_bytes())
    support=Path(os.environ['APPDATA'])/'OpenRA'
    profile=target/'profile'
    profile.mkdir(exist_ok=True)
    shutil.copy2(support/'settings.yaml',profile/'settings.yaml')
    normal=(ROOT/'mods/medieval/mod.yaml').read_text(encoding='utf-8')
    manifest=normal.replace('\t\t$medieval: medieval\n','\t\t$medieval: medieval\n\t\t^EngineDir|mods/medieval-stress: stress\n')
    content=Path(os.path.relpath(support/'Content/ra/v2',ROOT)).as_posix()
    manifest=manifest.replace('~^SupportDir|Content/ra/v2/', '~^EngineDir|'+content+'/')
    manifest=manifest.replace('\tmedieval|firefighting.yaml\n','\tmedieval|firefighting.yaml\n\tstress|tests.yaml\n')
    manifest=manifest.replace('MapFolders:\n','MapFolders:\n\tstress|maps: System\n')
    (package/'mod.yaml').write_text(manifest,encoding='utf-8')
    (package/'tests.yaml').write_text(f'World:\n\tMedievalStressValidation:\n\t\tSoldiersPerArmy: {soldiers}\n\t\tWorkersPerArmy: {workers}\n\t\tDuration: {ticks}\n\t\tOutput: {name}\n\n^MDWORKER:\n\tMedievalStressProbe:\n\n^MDBUILDING:\n\tMedievalStressProbe:\n',encoding='utf-8')
    return target,profile

if __name__=='__main__':
    p=argparse.ArgumentParser()
    p.add_argument('--soldiers',type=int,default=500)
    p.add_argument('--workers',type=int,default=250)
    p.add_argument('--ticks',type=int,default=4000)
    p.add_argument('--name',default='massive')
    p.add_argument('--binary',default='bin-medieval-load')
    p.add_argument('--timeout',type=int,default=1200)
    p.add_argument('--sync',action='store_true')
    a=p.parse_args()
    if not re.fullmatch('[a-z0-9-]+',a.name) or not 1<=a.soldiers<=500 or not 1<=a.workers<=250 or a.ticks<1000:
        p.error('Expected a simple run name, 1..500 soldiers, 1..250 workers per army and at least 1000 ticks.')
    target,profile=prepare(a.soldiers,a.workers,a.ticks,a.name)
    started=time.time()
    cmd=[str(ROOT/'.tools/dotnet/dotnet.exe'),str(ROOT/a.binary/'OpenRA.dll'),'--','Engine.EngineDir=..',
         'Engine.SupportDir='+str(profile),'Game.Mod=medieval-stress','Launch.Map=stress-immense',
         'Graphics.Mode=Windowed','Graphics.WindowedSize=1280,800','Graphics.UIScale=1','Graphics.MaxFramerate=60',
         'Graphics.VSync=False','Sound.Mute=True','Debug.SyncCheckUnsyncedCode='+str(a.sync),
         'Debug.EnableSimulationPerfLogging=True','Debug.LongTickThresholdMs=10']
    with (target/'native.log').open('w',encoding='utf-8') as log:
        process=subprocess.Popen(cmd,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT)
        try: code=process.wait(timeout=a.timeout)
        except subprocess.TimeoutExpired:
            process.kill(); process.wait(); code='timeout'
    report=target/'result.json'
    fresh=report.exists() and report.stat().st_mtime>=started
    result=json.loads(report.read_text(encoding='utf-8')) if fresh else None
    run=dict(exitCode=code,seconds=round(time.time()-started,1),fresh=fresh,passed=code==0 and fresh and result.get('passed') is True)
    (target/'execution.json').write_text(json.dumps(run,indent=2),encoding='utf-8')
    print(json.dumps(run),flush=True)
    sys.exit(0 if run['passed'] else 1)
