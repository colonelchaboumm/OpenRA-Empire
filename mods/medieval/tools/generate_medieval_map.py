"""Create a new seeded medieval map; all network participants need the same files."""
from pathlib import Path
import argparse
from tempfile import TemporaryDirectory
from large_maps import generate, MOD
from audit_geography import audit


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--size',type=int,choices=(256,384,512),default=512)
    parser.add_argument('--seed',type=int,required=True)
    parser.add_argument('--players',type=int,choices=(1,2,3),default=2)
    parser.add_argument('--output',type=Path,default=MOD/'maps')
    args=parser.parse_args()
    folder=f'continent-{args.size}-{args.seed}'
    suffix=f'-{args.players}joueurs' if args.players>1 else ''
    args.output=args.output.resolve()
    destination=args.output/(folder+suffix)
    if destination.exists(): parser.error(f'La carte existe deja : {destination}')
    target={256:3200,384:7000,512:12000}[args.size]
    args.output.mkdir(parents=True,exist_ok=True)
    # Validate outside the lobby's map folder, then publish the complete map.
    with TemporaryDirectory(prefix='medieval-generation-',dir=args.output.parent) as temporary:
        staging=Path(temporary).resolve()
        assert staging.parent==args.output.parent
        generate((folder,f'Continent {args.seed}',args.size,args.seed,target,2),staging,args.players)
        candidate=staging/destination.name
        result=audit(candidate)
        for kind,coverage in result['coverage'].items():
            if coverage['sectors']!=coverage['total']: result['errors'].append('Repartition incomplete : '+kind)
        if result['errors']: raise RuntimeError(result['errors'])
        assert candidate.resolve().is_relative_to(staging) and destination.parent==args.output
        if destination.exists(): raise FileExistsError(destination)
        candidate.rename(destination)
    print(f'Carte creee : {destination}. Copier ce dossier chez tous les joueurs, puis relancer le salon.')


if __name__=='__main__': main()
