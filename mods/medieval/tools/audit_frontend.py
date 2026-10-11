"""Visit native menus in isolated profiles; run graphical processes serially."""
from pathlib import Path
import argparse
import json
import os
import shutil
import subprocess
import time

ROOT = Path(__file__).resolve().parents[3]


def run(binary, size, output):
    out = output / size.replace(',', 'x')
    profile = out / 'profile'
    profile.mkdir(parents=True, exist_ok=True)
    support = Path(os.environ['APPDATA']) / 'OpenRA'
    shutil.copy2(support / 'settings.yaml', profile / 'settings.yaml')
    text = (ROOT / 'mods/medieval/mod.yaml').read_text(encoding='utf-8')
    content = Path(os.path.relpath(support / 'Content/ra/v2', ROOT)).as_posix()
    text = text.replace('~^SupportDir|Content/ra/v2/', '~^EngineDir|' + content + '/')
    package = ROOT / 'mods/medieval-frontend-audit'
    package.mkdir(exist_ok=True)
    (package / 'rules.yaml').write_text('World:\n\tMedievalFrontendGameValidation:\n', encoding='utf-8')
    text = text.replace('Rules:\n', 'Rules:\n\tmedieval-frontend-audit|rules.yaml\n', 1)
    text = text.replace('\t\t$medieval: medieval\n', '\t\t$medieval: medieval\n\t\t$medieval-frontend-audit: medieval-frontend-audit\n', 1)
    (package / 'mod.yaml').write_text(text, encoding='utf-8', newline='\n')
    args = [str(ROOT / '.tools/dotnet/dotnet.exe'), str(ROOT / binary / 'OpenRA.dll'), '--',
            'Engine.EngineDir=..', 'Engine.SupportDir=' + str(profile), 'Game.Mod=medieval-frontend-audit',
            'Graphics.Mode=Windowed', 'Graphics.WindowedSize=' + size, 'Graphics.UIScale=1', 'Graphics.VSync=False',
            'Sound.Mute=True', 'Debug.CheckVersion=False', 'Game.FetchNews=False',
            'Game.ViewportEdgeScroll=False',
            'Game.IntroductionPromptVersion=2', 'Debug.SystemInformationVersionPrompt=6',
            'Medieval.AuditMenu=True', 'Medieval.AuditOutput=' + str(out)]
    started = time.time()
    print('START frontend ' + size, flush=True)
    with (out / 'native.log').open('w', encoding='utf-8') as log:
        p = subprocess.Popen(args, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
        try:
            code = p.wait(timeout=150)
        except subprocess.TimeoutExpired:
            p.kill()  # Only the test process created here.
            p.wait()
            code = 'timeout'
    report = out / 'result.json'
    fresh = report.exists() and report.stat().st_mtime >= started - 1
    result = json.loads(report.read_text(encoding='utf-8')) if fresh else None
    passed = code == 0 and fresh and result['passed']
    print(('PASS ' if passed else 'FAIL ') + size + ' exit=' + str(code), flush=True)
    return dict(size=size, passed=passed, exitCode=code, fresh=fresh, seconds=round(time.time()-started, 1), result=result)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--binary', default='bin-medieval-finishing')
    parser.add_argument('--sizes', nargs='+', default=['1280,800', '1024,600'])
    parser.add_argument('--output-root', default='analysis/medieval/finishing/frontend')
    opts = parser.parse_args()
    output = (ROOT / opts.output_root).resolve()
    results = [run(opts.binary, size, output) for size in opts.sizes]
    (output / 'summary.json').write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding='utf-8')
    raise SystemExit(0 if all(r['passed'] for r in results) else 1)
