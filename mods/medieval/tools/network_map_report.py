"""Consolidate fresh network, geography and native bridge results without hiding failures."""
from pathlib import Path
import hashlib
import json
from datetime import datetime, timezone

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'analysis/medieval/network-maps'


def main():
    geography=json.loads((OUT/'generation-audit.json').read_text(encoding='utf-8'))
    networks=[json.loads((OUT/name/'result.json').read_text(encoding='utf-8'))
              for name in ('deux-bourgs','trois-bourgs','terres-sans-fin-2joueurs')]
    graphics=[json.loads((ROOT/'analysis/medieval/audit'/name/'result.json').read_text(encoding='utf-8'))
              for name in ('large-grands-meandres','large-marches-boisees','large-terres-sans-fin')]
    installed=json.loads((OUT/'installed-maps.json').read_text(encoding='utf-8-sig'))
    matches=all(hashlib.sha256((ROOT/'mods/medieval/maps'/f['map']/f['file']).read_bytes()).hexdigest().upper()==f['sha256'] for f in installed)
    lints=[OUT/('lint-huge.log' if name=='terres-sans-fin-3joueurs' else f'lint-{name}.log') for name in [r['map'] for r in geography['maps']]]
    lint_ok=all(p.exists() and 'Testing map:' in p.read_text(encoding='utf-8-sig') and 'Error:' not in p.read_text(encoding='utf-8-sig') for p in lints)
    network_summary=[]
    network_maps_match=True
    for r in networks:
        path=ROOT/'mods/medieval/maps'/r['map']
        uid=hashlib.sha1(b''.join(f.read_bytes() for f in sorted(path.iterdir())
            if f.suffix in ('.yaml','.bin','.lua') or f.name=='map.png')).hexdigest()
        network_maps_match &= uid==r['uid']
        profiles=[(OUT/r['map']/f'client-{i}'/'settings.yaml').read_text(encoding='utf-8') for i in range(r['clients'])]
        network_summary.append(dict(map=r['map'],clients=r['clients'],passed=r['passed'],
            ticks=r['reports'][0]['result']['tick'],checkpoints=len(r['reports'][0]['result']['checkpoints']),
            equalStates=r['identicalCheckpoints'],normalNetworkSync=True,
            extraUnsyncedChecks=all('SyncCheckUnsyncedCode: True' in p for p in profiles),
            weaponAttacks=r['reports'][0]['result'].get('shots'),seconds=r['seconds']))
    report=dict(date=datetime.now(timezone.utc).isoformat(),
        passed=geography['passed'] and matches and network_maps_match and lint_ok and all(r['passed'] for r in networks+graphics),
        geography=geography,network=network_summary,
        nativeBridges=[dict(map=r['map'],passed=r['passed'],ticks=r['result']['tick'],
            crossings=len(r['result']['crossings']),waterBlockingSamples=r['result']['waterCells']) for r in graphics],
        installedMapsMatchAudit=matches,networkMapIdsMatch=network_maps_match,lintPassed=lint_ok,
        limitations=['TCP clients and dedicated server ran on one PC; external PCs, firewall/NAT and Internet latency were not tested.',
                     'Network tests use the installed simulation with a GPU-free test backend; graphical checks run separately on SDL/OpenGL.',
                     'The three-player continent has geography/rule validation; live network continent test used two clients.',
                     'Recorded durations include instrumentation and multiple clients on one PC; they are not game performance/FPS measurements.',
                     'Randomness is applied during map generation, not independently on each client or automatically each new match.'])
    (OUT/'summary.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8')
    (OUT/'graphics-results.json').write_text(json.dumps(graphics,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('geography','limitations')},indent=2),flush=True)
    if not report['passed']: raise SystemExit(1)


if __name__=='__main__': main()
