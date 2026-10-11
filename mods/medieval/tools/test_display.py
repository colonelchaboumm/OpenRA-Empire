"""Native display persistence and zoom checks in an isolated user profile."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import time

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'analysis/medieval/display-zoom'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--binary', default='bin-medieval-display')
    parser.add_argument('--steps', nargs='+', type=int, default=list(range(5)))
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    profile = OUT / 'profile'
    profile.mkdir(exist_ok=True)
    user_support = Path(os.environ['APPDATA']) / 'OpenRA'
    if 0 in args.steps:
        settings = (user_support / 'settings.yaml').read_text(encoding='utf-8-sig')
        graphics = 'Graphics:\n\tMode: Windowed\n\tWindowedSize: 1280,800\n\tFullscreenSize: 0,0\n\tUIScale: 1\n\tVSync: False\n'
        settings = re.sub(r'^Graphics:\n(?:\t[^\n]*\n|\n)*', '', settings, flags=re.M)
        (profile / 'settings.yaml').write_text(settings.rstrip() + '\n\n' + graphics, encoding='utf-8', newline='\n')
    for name in ('launch-medieval.cmd', 'launch-medieval-multiplayer.cmd'):
        launch = (ROOT / name).read_text()
        assert 'Graphics.Mode=' not in launch and 'Graphics.WindowedSize=' not in launch, name
    package = ROOT / 'mods/medieval-display'
    package.mkdir(exist_ok=True)
    manifest = (ROOT / 'mods/medieval/mod.yaml').read_text(encoding='utf-8')
    manifest = manifest.replace('\t\t$medieval: medieval\n', '\t\t$medieval: medieval\n\t\t^EngineDir|mods/medieval-display: medieval-display\n')
    manifest = manifest.replace('\nSequences:\n', '\tmedieval-display|tests.yaml\n\nSequences:\n')
    content = Path(os.path.relpath(user_support / 'Content/ra/v2', ROOT)).as_posix()
    manifest = manifest.replace('~^SupportDir|Content/ra/v2/', '~^EngineDir|' + content + '/')
    (package / 'mod.yaml').write_text(manifest, encoding='utf-8', newline='\n')
    for src in (ROOT / 'bin-medieval').iterdir():
        dst = ROOT / args.binary / src.name
        if src.is_file() and not dst.exists():
            shutil.copy2(src, dst)
    results = []
    for step in args.steps:
        (package / 'tests.yaml').write_text(f'World:\n\tMedievalDisplayValidation:\n\t\tStep: {step}\n', encoding='utf-8', newline='\n')
        game_args = ['Engine.EngineDir=..', 'Game.Mod=medieval-display', 'Launch.Map=' + ('terres-sans-fin' if step == 4 else 'village'),
                     'Engine.SupportDir=' + str(profile), 'Sound.Mute=True', 'Debug.SyncCheckUnsyncedCode=True']
        command = [str(ROOT / '.tools/dotnet/dotnet.exe'), str(ROOT / args.binary / 'OpenRA.dll'), '--', *game_args]
        report = OUT / f'native/step-{step}/result.json'
        started = time.time()
        print(f'START display step {step}', flush=True)
        with (OUT / f'step-{step}.log').open('w', encoding='utf-8') as log:
            process = subprocess.Popen(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
            try:
                code = process.wait(timeout=240)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
                raise RuntimeError(f'Display step {step} timed out')
        fresh = report.exists() and report.stat().st_mtime >= started - 1
        data = json.loads(report.read_text()) if fresh else None
        result = dict(step=step, exitCode=code, fresh=fresh, passed=code == 0 and fresh and data.get('passed') is True,
                      seconds=round(time.time()-started, 1), result=data)
        results.append(result)
        (OUT / 'test-results.json').write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding='utf-8')
        print(('PASS' if result['passed'] else 'FAIL') + f' display step {step}: ' + str(result['seconds']) + 's', flush=True)
        if not result['passed']:
            raise SystemExit(1)


if __name__ == '__main__':
    main()
