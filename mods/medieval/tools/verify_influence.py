"""Native screenshots and assertions with private settings and an isolated test map."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import time

ROOT = Path(__file__).resolve().parents[3]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--binary', default='bin-medieval-influence')
    opts = parser.parse_args()
    output = ROOT/'analysis/medieval/influence'
    output.mkdir(parents=True, exist_ok=True)
    package = ROOT/'mods/medieval-influence'
    test_map = package/'maps/influence-test'
    test_map.mkdir(parents=True, exist_ok=True)
    for source in (ROOT/'mods/medieval/maps/messenger-test').iterdir():
        if source.is_file():
            shutil.copy2(source, test_map/source.name)
    map_file = test_map/'map.yaml'
    map_file.write_text(map_file.read_text(encoding='utf-8').replace('FogCheckboxEnabled: False', 'FogCheckboxEnabled: True')
                        .replace('ExploredMapCheckboxEnabled: True', 'ExploredMapCheckboxEnabled: False'), encoding='utf-8')
    manifest = (ROOT/'mods/medieval/mod.yaml').read_text(encoding='utf-8')
    manifest = manifest.replace('\nSequences:\n', '\tmedieval|influence-tests.yaml\n\nSequences:\n', 1)
    manifest = manifest.replace('medieval|maps: System', '^EngineDir|mods/medieval-influence/maps: System')
    manifest = manifest.replace('Timestep: 40', 'Timestep: 4')
    support = Path(os.environ['APPDATA'])/'OpenRA'
    content = Path(os.path.relpath(support/'Content/ra/v2', ROOT)).as_posix()
    manifest = manifest.replace('~^SupportDir|Content/ra/v2/', '~^EngineDir|'+content+'/')
    (package/'mod.yaml').write_text(manifest, encoding='utf-8')
    profile = output/'profile'
    profile.mkdir(exist_ok=True)
    (profile/'settings.yaml').write_text('Player:\n\tName: Validation\nSound:\n\tMute: True\n', encoding='utf-8')
    args = [str(ROOT/'.tools/dotnet/dotnet.exe'), str(ROOT/opts.binary/'OpenRA.dll'), '--', 'Engine.EngineDir=..',
            'Engine.SupportDir='+str(profile), 'Game.Mod=medieval-influence', 'Launch.Map=influence-test',
            'Graphics.Mode=Windowed', 'Graphics.WindowedSize=1280,800', 'Graphics.UIScale=1',
            'Graphics.VSync=False', 'Sound.Mute=True', 'Debug.SyncCheckUnsyncedCode=True']
    start = time.time()
    with (output/'native.log').open('w', encoding='utf-8') as log:
        process = subprocess.Popen(args, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
        try:
            while process.poll() is None and time.time() - start < 240:
                time.sleep(1)
                if 'Exception of type' in (output/'native.log').read_text(encoding='utf-8', errors='replace'):
                    process.kill()  # Only our own test process, including its error dialog.
                    process.wait()
                    raise RuntimeError('Native exception; see '+str(output/'native.log'))
            code = process.wait(timeout=1)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
            raise RuntimeError('Native influence scenario timed out.')
    report = output/'result.json'
    if code != 0 or not report.exists() or report.stat().st_mtime < start - 1:
        raise RuntimeError('Native influence scenario failed; see '+str(output/'native.log'))
    result = json.loads(report.read_text(encoding='utf-8'))
    print(json.dumps(result, indent=2))
    if not result['passed']:
        raise RuntimeError('Influence assertions failed.')


if __name__ == '__main__':
    main()
