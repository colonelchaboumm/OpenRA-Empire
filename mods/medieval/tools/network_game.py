"""Real TCP server + independent engine processes; test-only CPU/no-GPU platform.

Build tools/Medieval.NetworkTest first. No normal settings/saves are touched.
The render backend is a stub, so this verifies network simulation, not graphics.
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


def run(map_id, timeout=300, deep_sync=False, source_binary='bin-medieval', output_root='analysis/medieval/network-maps', season_ticks=None, storm_refuge=False):
    output = ROOT/output_root/map_id
    output.mkdir(parents=True, exist_ok=True)
    binary = ROOT/'bin-medieval-network-test'
    assert (binary/'OpenRA.Platforms.NetworkTest.dll').exists(), 'Build the test platform first'
    # Keep the separately compiled test plugins, and test the exact chosen build.
    shutil.copytree(ROOT/source_binary, binary, dirs_exist_ok=True)
    manifest = (ROOT/'mods/medieval/mod.yaml').read_text(encoding='utf-8')
    manifest = re.sub(r'LoadScreen:.*?(?=\n\S)', 'LoadScreen: NetworkAuditLoadScreen\n', manifest, flags=re.S)
    manifest = manifest.replace('OpenRA.Mods.Medieval.dll\n', 'OpenRA.Mods.Medieval.dll, OpenRA.Mods.NetworkTest.dll\n', 1)
    anchor = '\tmedieval|seasons.yaml\n' if '\tmedieval|seasons.yaml\n' in manifest else '\tmedieval|firefighting.yaml\n'
    manifest = manifest.replace(anchor, anchor+'\tnetworkaudit|rules.yaml\n', 1)
    manifest = manifest.replace('\t\t$medieval: medieval\n', '\t\t$medieval: medieval\n\t\t^EngineDir|mods/medieval-network-audit: networkaudit\n')
    content = Path(os.path.relpath(Path(os.environ['APPDATA'])/'OpenRA/Content/ra/v2', ROOT)).as_posix()
    manifest = manifest.replace('~^SupportDir|Content/ra/v2/', '~^EngineDir|'+content+'/')
    manifest = manifest.replace('\t\t\tTimestep: 40', '\t\t\tTimestep: 4')
    mod = ROOT/'mods/medieval-network-audit'
    mod.mkdir(exist_ok=True)
    (mod/'mod.yaml').write_text(manifest, encoding='utf-8', newline='\n')
    seasons = '' if season_ticks is None else f'\tMedievalSeasons:\n\t\tSeasonTicks: {season_ticks}\n'
    storm = '\t\tStormRefuge: True\n\tMedievalWeather:\n\t\tFirstRainTicks: 2000\n\t\tRainTicks: 1600\n\t\tMinimumRainPermille: 850\n' if storm_refuge else ''
    (mod/'rules.yaml').write_text('World:\n\tNetworkAudit:\n'+storm+seasons+'md.militia:\n\tMedievalCombatProbe:\n', encoding='utf-8', newline='\n')
    path = ROOT/'mods/medieval/maps'/map_id
    yaml = (path/'map.yaml').read_text(encoding='utf-8')
    count = yaml.count('\t\tPlayable: True')
    uid = hashlib.sha1(b''.join(f.read_bytes() for f in sorted(path.iterdir())
        if f.suffix in ('.yaml', '.bin', '.lua') or f.name == 'map.png')).hexdigest()
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0)); port = s.getsockname()[1]
    common = ['Engine.EngineDir=..', 'Game.Mod=medieval-network-audit']
    dotnet = str(ROOT/'.tools/dotnet/dotnet.exe')
    (output/'server').mkdir(exist_ok=True)
    server_args = [dotnet, str(binary/'OpenRA.Server.dll'), *common, 'Engine.SupportDir='+str(output/'server'),
                   'Server.Map='+uid, 'Server.ListenPort='+str(port), 'Server.Name=Local network audit',
                   'Server.AdvertiseOnline=False', 'Server.AdvertiseOnLocalNetwork=False',
                   'Server.EnableGeoIP=False', 'Server.QueryMapRepository=False', 'Server.RecordReplays=True',
                   'Server.EnableSyncReports=False', 'Server.FloodLimitJoinCooldown=0']
    processes, logs, clients = [], [], []
    started = time.time()
    try:
        log = (output/'server.log').open('w', encoding='utf-8'); logs.append(log)
        server = subprocess.Popen(server_args, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT); processes.append(server)
        # Wait for the listener, not an arbitrary multi-second startup sleep.
        for _ in range(600):
            if server.poll() is not None: raise RuntimeError('Server exited; see server.log')
            try:
                with socket.create_connection(('127.0.0.1', port), timeout=.2): break
            except OSError: time.sleep(.1)
        else: raise RuntimeError('Server did not start listening')
        for i in range(count):
            profile = output/f'client-{i}'
            profile.mkdir(exist_ok=True)
            result = profile/'result.json'
            if result.exists(): result.rename(profile/f'result-{time.time_ns()}.json')
            log = (output/f'client-{i}.log').open('w', encoding='utf-8'); logs.append(log)
            args = [dotnet, str(binary/'OpenRA.dll'), '--', *common, 'Engine.SupportDir='+str(profile),
                    f'Player.Name=Audit {i+1}', 'Game.Platform=NetworkTest', f'Launch.Connect=127.0.0.1:{port}',
                    'Graphics.Mode=Windowed', 'Graphics.WindowedSize=1280,800', 'Graphics.MaxFramerate=5',
                    'Graphics.VSync=False', 'Sound.Mute=True', 'Debug.SyncCheckUnsyncedCode='+str(deep_sync)]
            p = subprocess.Popen(args, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
            clients.append((p, profile)); processes.append(p)
        reports = []
        for p, profile in clients:
            code = p.wait(timeout=max(1, timeout-(time.time()-started)))
            path = profile/'result.json'
            data = json.loads(path.read_text(encoding='utf-8')) if path.exists() and path.stat().st_mtime >= started else None
            reports.append(dict(exitCode=code, result=data, passed=code == 0 and bool(data and data['passed'])))
        identical = bool(reports[0]['result']) and all(r['result'] and r['result']['checkpoints'] == reports[0]['result']['checkpoints'] for r in reports)
        result = dict(map=map_id, uid=uid, clients=count, seconds=round(time.time()-started, 1), deepSyncChecks=deep_sync, seasonTicks=season_ticks, stormRefuge=storm_refuge,
                      identicalCheckpoints=identical, passed=identical and all(r['passed'] for r in reports), reports=reports)
        (output/'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
        print(json.dumps({k:v for k,v in result.items() if k != 'reports'}), flush=True)
        return result['passed']
    finally:
        for p in reversed(processes):
            if p.poll() is None: p.kill()  # Only processes owned by this run.
            p.wait()
        for log in logs: log.close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('map')
    parser.add_argument('--timeout', type=int, default=300)
    parser.add_argument('--deep-sync', action='store_true', help='Extra before/after hashes around UI code; expensive on continents. Network sync checks always run.')
    parser.add_argument('--binary', default='bin-medieval', help='Game build to validate before installation')
    parser.add_argument('--output-root', default='analysis/medieval/network-maps', help='Separate reports and private profiles for this run')
    parser.add_argument('--season-ticks', type=int, help='Test-only season length, for exercising transitions within the network scenario')
    parser.add_argument('--storm-refuge', action='store_true', help='Test-only houses and heavy rain to exercise citizen refuge across peers')
    args = parser.parse_args()
    if args.season_ticks is not None and args.season_ticks < 1: parser.error('--season-ticks must be positive')
    raise SystemExit(0 if run(args.map, args.timeout, args.deep_sync, args.binary, args.output_root, args.season_ticks, args.storm_refuge) else 1)
