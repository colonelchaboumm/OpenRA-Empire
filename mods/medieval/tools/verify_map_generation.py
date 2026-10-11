"""Generate into an isolated staging area, audit installed candidates and new seeds."""
from pathlib import Path
import hashlib
import json
import shutil
from large_maps import generate, SPECS, MOD, ROOT
from multiplayer_nature import populate
from audit_geography import audit

OUT=ROOT/'analysis/medieval/network-maps'
STAGED=OUT/'staged'


def digest(path):
    return hashlib.sha256((path/'map.yaml').read_bytes()+(path/'map.bin').read_bytes()).hexdigest()


def main():
    STAGED.mkdir(parents=True,exist_ok=True)
    results=[]
    for spec in SPECS:
        generate(spec,STAGED)
        results.append(audit(STAGED/spec[0]))
    for players in (2,3):
        spec=SPECS[-1]
        generate(spec,STAGED,players)
        results.append(audit(STAGED/f'{spec[0]}-{players}joueurs'))
    for i,name in enumerate(('deux-bourgs','trois-bourgs')):
        path=STAGED/name
        shutil.copytree(OUT/'before'/name,path,dirs_exist_ok=True)
        populate(path,64217+i)
        result=audit(path,4); results.append(result)
        first=digest(path);populate(path,64217+i)
        assert first==digest(path), 'Small nature generator must be idempotent'
    seeds=[]
    seed_hashes=[]
    for size,players,seed in ((256,2,101),(256,2,102),(256,3,202),(384,2,303),(512,3,404)):
        spec=(f'seed-{seed}',f'Validation {seed}',size,seed,{256:3200,384:7000,512:12000}[size],2)
        generate(spec,OUT/'seed-tests',players)
        path=OUT/'seed-tests'/f'seed-{seed}-{players}joueurs'
        result=audit(path);first=digest(path)
        generate(spec,OUT/'seed-repeat',players)
        repeat=OUT/'seed-repeat'/path.name
        result['deterministic']=first==digest(repeat)
        seed_hashes.append(first)
        assert result['deterministic'], 'Same seed must yield identical map bytes'
        seeds.append(result)
    for result in results+seeds:
        for kind,coverage in result['coverage'].items():
            if coverage['sectors']!=coverage['total']: result['errors'].append('Missing '+kind+' in some sectors')
        starts=result['starts']
        # Identical starter reserves and nearby resource kits on huge maps.
        if result['width']>=256 and len(starts)>1:
            for kind in ('woodWithin24','stoneWithin24'):
                if len({s[kind] for s in starts})!=1: result['errors'].append('Unequal nearby '+kind)
        result['passed']=not result['errors']
    assert seed_hashes[0] != seed_hashes[1], 'Different seeds must change the generated landscape'
    report=dict(passed=all(r['passed'] for r in results+seeds),differentSeedsDiffer=True,maps=results,seeds=seeds)
    (OUT/'generation-audit.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    for r in results+seeds: print(r['map'],r['passed'],r['errors'],flush=True)
    if not report['passed']: raise SystemExit(1)


if __name__=='__main__': main()
