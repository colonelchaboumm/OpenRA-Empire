"""Run barter/correspondence validators with private profiles and fresh reports."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import time

ROOT = Path(__file__).resolve().parents[3]
CASES = {
    'fleet': ('fleet-tests.yaml', 'messenger-test', 'barter/fleet'),
    'barter': ('barter-tests.yaml', 'messenger-test', 'barter/native'),
    'messenger': ('messenger-tests.yaml', 'messenger-test', 'messenger-preview'),
}

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--binary', default='bin-medieval')
    parser.add_argument('--case', choices=CASES, default='barter')
    parser.add_argument('--size', choices=('1280,800', '1024,768'), default='1280,800')
    parser.add_argument('--timeout', type=int, default=240)
    args = parser.parse_args()
    rule, map_id, report_folder = CASES[args.case]
    output = ROOT/'.distribution/barter-tests'/f'{args.case}-{args.size.replace(",", "x")}'
    package, profile = output/'mod', output/'profile'
    package.mkdir(parents=True, exist_ok=True)
    profile.mkdir(exist_ok=True)
    manifest = (ROOT/'mods/medieval/mod.yaml').read_text(encoding='utf-8')
    manifest = manifest.replace('\nSequences:\n', f'\tmedieval|{rule}\n\nSequences:\n', 1)
    manifest = manifest.replace('Timestep: 40', 'Timestep: 4')
    content = Path(os.path.relpath(Path(os.environ['APPDATA'])/'OpenRA/Content/ra/v2', ROOT)).as_posix()
    manifest = manifest.replace('~^SupportDir|Content/ra/v2/', '~^EngineDir|'+content+'/')
    (package/'mod.yaml').write_text(manifest, encoding='utf-8')
    (profile/'settings.yaml').write_text('Player:\n\tName: Validation\nSound:\n\tMute: True\n', encoding='utf-8')
    command = [str(ROOT/'.tools/dotnet/dotnet.exe'), str(ROOT/args.binary/'OpenRA.dll'), '--',
               'Engine.EngineDir='+str(ROOT), 'Engine.SupportDir='+str(profile), 'Game.Mod='+str(package),
               'Launch.Map='+map_id, 'Graphics.Mode=Windowed', 'Graphics.WindowedSize='+args.size,
               'Graphics.UIScale=1', 'Graphics.VSync=False', 'Sound.Mute=True', 'Debug.SyncCheckUnsyncedCode=True']
    started = time.time()
    with (output/'native.log').open('w', encoding='utf-8') as log:
        process = subprocess.Popen(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
        try:
            code = process.wait(timeout=args.timeout)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
            raise
    report = ROOT/'analysis/medieval'/report_folder/'result.json'
    if code != 0 or not report.exists() or report.stat().st_mtime < started:
        raise RuntimeError('Native run failed: '+str(output/'native.log'))
    result = json.loads(report.read_text(encoding='utf-8'))
    (output/'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(dict(case=args.case, passed=result['passed'], errors=result['errors'], seconds=round(time.time()-started, 1)), indent=2))
    if not result['passed']:
        raise RuntimeError('Scenario failed')
