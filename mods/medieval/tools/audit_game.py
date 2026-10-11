"""Run native validators against the current mod; archive fresh results, never stale PASS files."""
from pathlib import Path
import argparse
import concurrent.futures
import json
import os
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'analysis/medieval/audit'
# name, map, report folder. All manifests inherit today's normal gameplay rules.
CASES = [
    ('textile', 'village', 'textile/native'),
    ('fire-support', 'messenger-test', 'fire-support/native'),
    ('shadows', 'messenger-test', 'shadows/native'),
    ('shadows-large', 'terres-sans-fin', 'shadows/large'),
    ('demolition-area', 'messenger-test', 'demolition-area/native'),
    ('demolition', 'messenger-test', 'demolition/native'),
    ('siege-heraldry', 'messenger-test', 'siege-heraldry/native'),
    ('cart-heraldry', 'village', 'cart-heraldry/native'),
    ('yard-handling', 'village', 'yard-handling/native'),
    ('nature-storage', 'messenger-test', 'nature-storage/native'),
    ('founding', 'fondation', 'founding'),
    ('logistics', 'village', 'logistics-preview'),
    ('stalls', 'village', 'stall-preview'),
    ('stockyard', 'village', 'stockyard-preview'),
    ('trade', 'village', 'trade-preview'),
    ('military', 'village', 'military-preview'),
    ('herds', 'messenger-test', 'herds'),
    ('seasons', 'village', 'seasons-hunting'),
    ('weather-polish', 'village', 'weather-polish'),
    ('storm-refuge', 'village', 'storm-refuge'),
    ('lodge-lights', 'village', 'lodge-lights'),
    ('commander', 'village', 'commander'),
    ('camping', 'village', 'camping'),
    ('localities', 'village', 'locality-preview'),
    ('messenger', 'messenger-test', 'messenger-preview'),
    ('natural', 'village', 'natural-preview'),
    ('morale', 'village', 'morale-preview'),
    ('heraldry', 'village', 'heraldry'),
    ('night-patrol', 'messenger-test', 'night-patrol'),
    ('patrol', 'messenger-test', 'patrol'),
    ('saboteur', 'messenger-test', 'saboteur'),
    ('firefighting', 'messenger-test', 'firefighting'),
    ('siege', 'messenger-test', 'siege-preview'),
    ('siege-capture', 'messenger-test', 'siege-capture'),
    ('siege-shot', 'messenger-test', 'siege-shot'),
    ('ram', 'messenger-test', 'ram-strike'),
    ('fortifications', 'village', 'fortification-preview'),
    ('tower-garrison', 'village', 'tower-garrison/native'),
    ('gold-church', 'village', 'gold-church/native'),
    ('gilded-decorations', 'village', 'gilded-decorations/native'),
    ('architecture', 'village', 'perspective/native'),
    ('houses', 'village', 'house-preview'),
    ('decoration', 'village', 'decoration-preview'),
    ('construction-menu', 'village', 'construction-menu'),
    ('placement', 'village', 'placement-preview/ui-1'),
    ('village-life', 'village', 'village-life'),
    ('building-animation', 'village', 'building-animation-preview'),
    ('audio', 'messenger-test', 'audio'),
    ('wilderness', 'village', 'wilderness/village'),
    ('animation', 'village', 'animation-preview'),
    ('ambience', 'village', 'ambience-preview'),
    ('test', 'village', 'integration'),
    ('combat', 'messenger-test', 'combat'),
    ('save', 'village', 'audit/save'),
    ('large-marches-boisees', 'marches-boisees', 'large-maps/marches-boisees'),
    ('large-grands-meandres', 'grands-meandres', 'large-maps/grands-meandres'),
    ('large-terres-sans-fin', 'terres-sans-fin', 'large-maps/terres-sans-fin'),
    ('large-benchmark', 'large-map-benchmark', 'large-maps/large-map-benchmark'),
]

def rules(text):
    return text.split('\nRules:\n', 1)[1].split('\n\n', 1)[0].strip().splitlines()

def run(case, binary, timeout, support_root=None):
    name, map_id, folder = case
    target = OUT / name
    target.mkdir(parents=True, exist_ok=True)
    normal = (ROOT/'mods/medieval/mod.yaml').read_text(encoding='utf-8')
    source = 'large-maps' if name.startswith('large-') else 'audit-save' if name == 'save' else name
    previous = (ROOT/f'mods/medieval-{source}/mod.yaml').read_text(encoding='utf-8')
    common = {s.strip() for s in rules(normal)}
    extra = [s.strip() for s in rules(previous) if s.strip() not in common]
    assert extra, name
    anchor = '\t' + rules(normal)[-1].strip() + '\n'
    manifest = normal.replace(anchor, anchor + ''.join('\t'+s+'\n' for s in extra), 1)
    for prefix in sorted({s.split('|', 1)[0] for s in extra} - {'medieval'}):
        mount = next(line for line in previous.splitlines() if line.startswith('\t\t') and line.endswith(': ' + prefix))
        manifest = manifest.replace('\t\t$medieval: medieval\n', '\t\t$medieval: medieval\n' + mount + '\n', 1)
    # Voices expire in wall-clock time. Accelerating audio simulation by 10x
    # saturates the voice limit and can suppress perfectly valid ambience cues.
    if name not in ('audio', 'weather-polish'):
        manifest = manifest.replace('\t\t\tTimestep: 40', '\t\t\tTimestep: 4')
    profile = None
    if support_root:
        profile = Path(support_root).resolve()/name
        profile.mkdir(parents=True, exist_ok=True)
        user_support = Path(os.environ['APPDATA'])/'OpenRA'
        shutil.copy2(user_support/'settings.yaml', profile/'settings.yaml')
        content = Path(os.path.relpath(user_support/'Content/ra/v2', ROOT)).as_posix()
        manifest = manifest.replace('~^SupportDir|Content/ra/v2/', '~^EngineDir|'+content+'/')
    mod = 'medieval-audit-' + name
    package = ROOT/'mods'/mod
    package.mkdir(exist_ok=True)
    (package/'mod.yaml').write_text(manifest, encoding='utf-8', newline='\n')
    report = ROOT/'analysis/medieval'/folder/'result.json'
    started = time.time()
    args = [str(ROOT/'.tools/dotnet/dotnet.exe'), str(ROOT/binary/'OpenRA.dll'), '--',
            'Engine.EngineDir=..', 'Game.Mod='+mod, 'Launch.Map='+map_id,
            'Graphics.Mode=Windowed', 'Graphics.WindowedSize=1280,800', 'Graphics.UIScale=1',
            'Graphics.VSync=False', 'Sound.Mute=True', 'Debug.SyncCheckUnsyncedCode=True']
    if profile:
        args.append('Engine.SupportDir='+str(profile))
    print('START '+name, flush=True)
    with (target/'native.log').open('w', encoding='utf-8') as log:
        process = subprocess.Popen(args, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
        try:
            code = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            process.kill()  # Only the specific test process created immediately above.
            process.wait()
            code = 'timeout'
    fresh = report.exists() and report.stat().st_mtime >= started - 1
    data = json.loads(report.read_text(encoding='utf-8')) if fresh else None
    result = dict(name=name, map=map_id, exitCode=code, seconds=round(time.time()-started, 1),
                  fresh=fresh, passed=code == 0 and fresh and data.get('passed') is True,
                  result=data)
    (target/'result.json').write_text(json.dumps(result, indent=2, ensure_ascii=False), encoding='utf-8')
    print(('PASS ' if result['passed'] else 'FAIL ')+name+' '+str(result['seconds'])+'s', flush=True)
    return result

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--only', nargs='+', choices=[c[0] for c in CASES])
    parser.add_argument('--binary', default='bin-medieval-audit')
    parser.add_argument('--jobs', type=int, default=1)
    parser.add_argument('--timeout', type=int, default=300)
    parser.add_argument('--support-root', help='Private Windows test profiles; leaves normal settings and saves untouched')
    opts = parser.parse_args()
    if opts.jobs < 1 or opts.timeout < 1:
        parser.error('--jobs and --timeout must be positive')
    OUT.mkdir(parents=True, exist_ok=True)
    cases = [c for c in CASES if opts.only is None or c[0] in opts.only]
    results = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=opts.jobs) as pool:
        futures = [pool.submit(run, c, opts.binary, opts.timeout, opts.support_root) for c in cases]
        for f in concurrent.futures.as_completed(futures):
            results.append(f.result())
            (OUT/'summary.json').write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding='utf-8')
    print(f'DONE {sum(r["passed"] for r in results)}/{len(results)}', flush=True)
    sys.exit(0 if all(r['passed'] for r in results) else 1)
