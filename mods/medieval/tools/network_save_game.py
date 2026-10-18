"""Host a game, save, terminate every process, and resume twice over real TCP.

Uses isolated profiles and the test-only CPU renderer. Never touches player saves.
Build Medieval.NetworkTest and Medieval.NetworkScenario before running this tool.
"""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import shutil
import socket
import subprocess
import time

ROOT = Path(__file__).resolve().parents[3]


def run(binary_name, map_id, output_name, use_menu=False, pause=False):
    binary = ROOT/'bin-medieval-network-test'
    shutil.copytree(ROOT/binary_name, binary, dirs_exist_ok=True)
    mod = ROOT/'mods/medieval-network-audit'
    mod.mkdir(exist_ok=True)
    files = [mod/'mod.yaml', mod/'rules.yaml']
    before = {p: p.read_bytes() if p.exists() else None for p in files}
    output = ROOT/output_name/map_id
    output.mkdir(parents=True, exist_ok=True)
    manifest = (ROOT/'mods/medieval/mod.yaml').read_text(encoding='utf-8')
    manifest = re.sub(r'LoadScreen:.*?(?=\n\S)', 'LoadScreen: MultiplayerSaveLoadScreen\n', manifest, flags=re.S)
    manifest = manifest.replace('OpenRA.Mods.Medieval.dll\n', 'OpenRA.Mods.Medieval.dll, OpenRA.Mods.NetworkTest.dll\n', 1)
    manifest = manifest.replace('\tmedieval|seasons.yaml\n', '\tmedieval|seasons.yaml\n\tnetworkaudit|rules.yaml\n', 1)
    manifest = manifest.replace('\t\t$medieval: medieval\n', '\t\t$medieval: medieval\n\t\t^EngineDir|mods/medieval-network-audit: networkaudit\n')
    content = Path(os.path.relpath(Path(os.environ['APPDATA'])/'OpenRA/Content/ra/v2', ROOT)).as_posix()
    manifest = manifest.replace('~^SupportDir|Content/ra/v2/', '~^EngineDir|'+content+'/')
    manifest = manifest.replace('\t\t\tTimestep: 40', '\t\t\tTimestep: 4')
    path = ROOT/'mods/medieval/maps'/map_id
    count = (path/'map.yaml').read_text().count('\t\tPlayable: True')
    uid = hashlib.sha1(b''.join(p.read_bytes() for p in sorted(path.iterdir())
        if p.suffix in ('.yaml', '.bin', '.lua') or p.name == 'map.png')).hexdigest()
    all_reports = []
    try:
        files[0].write_text(manifest, encoding='utf-8', newline='\n')
        files[1].write_text('World:\n\tMultiplayerSaveAudit:\n', encoding='utf-8', newline='\n')
        for phase, resume, save in [('original', '', 'first.orasav'), ('resume', 'first.orasav', 'second.orasav'), ('resume-again', 'second.orasav', 'third.orasav')]:
            with socket.socket() as s:
                s.bind(('127.0.0.1', 0))
                port = s.getsockname()[1]
            phase_root = output/phase
            phase_root.mkdir(exist_ok=True)
            processes, logs, profiles = [], [], []
            started = time.time()
            try:
                order = [0, *range(1, count)] if not resume else [0, *reversed(range(1, count))]
                for nation in order:
                    profile = phase_root/f'client-{nation}'
                    profile.mkdir(exist_ok=True)
                    result = profile/'result.json'
                    if result.exists(): result.rename(profile/f'result-{time.time_ns()}.json')
                    (profile/'finish.txt').unlink(missing_ok=True)
                    if nation == 0 and resume:
                        previous = output/('original' if phase == 'resume' else 'resume')/'client-0'/'Saves'
                        shutil.copytree(previous, profile/'Saves', dirs_exist_ok=True)
                    log = (phase_root/f'client-{nation}.log').open('w', encoding='utf-8')
                    logs.append(log)
                    args = [str(ROOT/'.tools/dotnet/dotnet.exe'), str(binary/'OpenRA.dll'), '--',
                        'Engine.EngineDir=..', 'Game.Mod=medieval-network-audit', 'Engine.SupportDir='+str(profile),
                        f'Player.Name=Nation {nation}', 'Game.Platform=NetworkTest', f'Launch.Connect=127.0.0.1:{port}',
                        'Server.Map='+uid, f'Server.ListenPort={port}', 'Server.Name=Multiplayer save test',
                        'Server.AdvertiseOnline=False', 'Server.AdvertiseOnLocalNetwork=False',
                        'Server.EnableGeoIP=False', 'Server.QueryMapRepository=False', 'Server.FloodLimitJoinCooldown=0',
                        f'Audit.Host={nation == 0}', f'Audit.Slot=Bourg{nation}', 'Audit.Resume='+resume, 'Audit.SaveName='+save,
                        f'Audit.UseMenu={use_menu}',
                        f'Audit.Pause={pause}',
                        'Graphics.Mode=Windowed', 'Graphics.WindowedSize=1280,800', 'Graphics.MaxFramerate=5',
                        'Graphics.VSync=False', 'Sound.Mute=True', 'Debug.SyncCheckUnsyncedCode=True']
                    p = subprocess.Popen(args, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
                    processes.append(p)
                    profiles.append(profile)
                    # Wait for a completed lobby join so initial slot ownership is reproducible.
                    joined = profile/'joined.txt'
                    for _ in range(900):
                        if p.poll() is not None: raise RuntimeError(f'{phase}: client {nation} exited; inspect log')
                        if joined.exists() and joined.stat().st_mtime >= started: break
                        time.sleep(.1)
                    else: raise RuntimeError(f'{phase}: client {nation} failed to join')
                reports = []
                while not all((p/'result.json').exists() for p in profiles):
                    if time.time()-started > 240: raise TimeoutError(f'{phase}: clients did not finish')
                    if any(p.poll() is not None for p in processes): raise RuntimeError(f'{phase}: client exited early')
                    time.sleep(.1)
                for profile in profiles: (profile/'finish.txt').write_text('done', encoding='utf-8')
                for p, profile in zip(processes, profiles):
                    code = p.wait(timeout=max(1, 240-(time.time()-started)))
                    data = json.loads((profile/'result.json').read_text(encoding='utf-8'))
                    assert code == 0 and data['passed'], (phase, profile, code, data)
                    reports.append(data)
                assert all(r['checkpoints'] == reports[0]['checkpoints'] for r in reports), 'Clients diverged'
                assert list((phase_root/'client-0'/'Saves').rglob(save)), 'Save file missing'
                assert not list(phase_root.rglob('unauthorized.orasav')), 'Guest created an unauthorized save'
                all_reports.append(dict(phase=phase, seconds=round(time.time()-started, 1), reports=reports))
                print(phase, 'passed', round(time.time()-started, 1), 'seconds', flush=True)
            finally:
                for p in reversed(processes):
                    if p.poll() is None: p.kill()
                    p.wait()
                for log in logs: log.close()
        baseline = all_reports[0]['reports'][0]['checkpoints']
        assert all(p['reports'][0]['checkpoints'] == baseline for p in all_reports), 'Restoration differs from uninterrupted game'
        report = dict(passed=True, map=map_id, players=count, phases=all_reports)
        (output/'result.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        print('Two save/load cycles match the uninterrupted game.', flush=True)
    finally:
        for p, data in before.items():
            if data is None: p.unlink(missing_ok=True)
            else: p.write_bytes(data)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--binary', default='bin-medieval-mp-save')
    parser.add_argument('--map', default='deux-bourgs', choices=['deux-bourgs', 'trois-bourgs'])
    parser.add_argument('--output-root', default='analysis/medieval/multiplayer-save')
    parser.add_argument('--use-menu', action='store_true', help='Host resumes through the real load browser and create-server UI')
    parser.add_argument('--pause', action='store_true', help='Save during a synchronized pause and verify that it is restored')
    args = parser.parse_args()
    run(args.binary, args.map, args.output_root, args.use_menu, args.pause)
