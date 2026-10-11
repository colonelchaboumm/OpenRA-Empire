"""Report the dense-melee optimization using serial, uninstrumented native runs."""
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT/'analysis/medieval/dense-optimization'
LOAD = ROOT/'analysis/medieval/load-test'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def phase(result):
    samples = {s['tick']: s for s in result['samples']}
    return samples[1500]['seconds'] - samples[500]['seconds']

if __name__ == '__main__':
    before, after = (read(LOAD/name/'result.json') for name in ('dense-reference', 'dense-spatial'))
    for name in ('dense-reference', 'dense-spatial'):
        if not read(LOAD/name/'execution.json')['passed'] or not read(LOAD/name/'result.json')['passed']:
            raise SystemExit('Both native runs must finish successfully.')
    regressions = read(OUT/'regression-results-spatial.json')
    combat = next(r['result'] for r in regressions if r['name'] == 'combat')
    install = read(OUT/'installation.json') if (OUT/'installation.json').exists() else None
    old, new = phase(before), phase(after)
    metrics = dict(beforeMeleeSeconds=old, afterMeleeSeconds=new, meleeReductionPercent=100*(1-new/old),
                   beforeTotalSeconds=before['seconds'], afterTotalSeconds=after['seconds'],
                   beforeMaxTickMs=before['maxTickMs'], afterMaxTickMs=after['maxTickMs'],
                   maxTickReductionPercent=100*(1-after['maxTickMs']/before['maxTickMs']),
                   meanTickReductionPercent=100*(1-after['averageTickMs']/before['averageTickMs']))
    (OUT/'comparison.json').write_text(json.dumps(metrics, indent=2), encoding='utf-8')
    lines = [
        '# Optimisation des mêlées denses — 10 octobre 2026', '',
        '## Mesures natives', '',
        'Quatre armées ennemies, 2 000 militaires, 1 000 citoyens, 80 charrettes sur une copie de la carte 512 × 1 024. Les villes conservent leur production, leurs transports, leurs chantiers et le cycle jour/nuit.', '',
        '| Mesure | Version précédente | Nouvelle version |', '| --- | ---: | ---: |',
        f'| Mêlée, ticks 500–1 500 | {old:.2f} s | {new:.2f} s |',
        f'| Simulation complète, 4 000 ticks | {before["seconds"]:.2f} s | {after["seconds"]:.2f} s |',
        f'| Temps moyen par tick | {before["averageTickMs"]:.2f} ms | {after["averageTickMs"]:.2f} ms |',
        f'| 95e percentile | {before["p95TickMs"]:.2f} ms | {after["p95TickMs"]:.2f} ms |',
        f'| 99e percentile | {before["p99TickMs"]:.2f} ms | {after["p99TickMs"]:.2f} ms |',
        f'| Plus long tick, hors initialisation | {before["maxTickMs"]:.2f} ms | {after["maxTickMs"]:.2f} ms |',
        f'| Pic de mémoire résidente | {before["peakMemoryMiB"]/1024:.2f} Gio | {after["peakMemoryMiB"]/1024:.2f} Gio |', '',
        f'Le plus gros pic de calcul diminue de **{metrics["maxTickReductionPercent"]:.1f} %**, et le temps moyen de calcul de **{metrics["meanTickReductionPercent"]:.1f} %**. La durée réelle de la phase de mêlée reste pratiquement identique ({old:.2f} contre {new:.2f} secondes) : ce changement réduit surtout les grosses saccades et ne constitue pas un gain sensible de cadence continue. Ces mesures ne sont pas des FPS.', '',
        'Les deux mesures ont été exécutées successivement, avec les mêmes effectifs, positions, ordres et paramètres, sans compilation ni autre test lourd en parallèle. La version précédente a été remesurée dans cette session. La graine aléatoire varie : les batailles ne sont pas des relectures identiques et un seul couple de mesures ne constitue pas une garantie statistique.', '',
        'Les diagnostics `dense-profile` et `dense-local` contiennent les compteurs temporaires. Le premier essai `dense-final` a été perturbé par une compilation concurrente. Ces essais sont exclus de la comparaison ci-dessus. Une première variante `dense-validated` sans les dernières optimisations spatiales ne gagnait que 0,3 % sur la phase dense ; elle n’a pas été installée. La bibliothèque OpenRA.Game.dll des deux essais retenus est identique à celle installée avant cette intervention, sans les compteurs temporaires de profilage.', '',
        '## Modifications', '',
        '- Chemins : les tableaux réutilisés ne nettoient que les cellules visitées. Une grande exploration conserve un nettoyage global rapide. Les couches de recherches simultanées restent indépendantes.',
        '- Index spatial : seules les régions contenant des anciennes positions à retirer sont compactées. Les listes conservent leur ordre pour ne pas modifier les égalités de ciblage et les déclencheurs de proximité.',
        '- Ciblage : dès qu’une cible a la meilleure priorité possible, les candidats plus éloignés sont écartés avant les recherches de propriétés et de visibilité. Les contrôles de visibilité restent obligatoires avant de choisir une cible ; les égalités conservent leur ordre.',
        '- Attaques : les portées des armes actives sont calculées en une passe, sans créer une liste intermédiaire par attaque. Les armes suspendues gardent le comportement précédent.',
        '- Ravitaillement : les marchés et charrettes sont regroupés par secteurs pour chaque allocation. Les unités ne parcourent que les fournisseurs susceptibles de les couvrir. Les distances exactes, priorités, réservations et pénuries sont toujours vérifiées. Petits villages et rayons exceptionnellement grands gardent une recherche linéaire.',
        '- Sons : un bruit déjà limité en fréquence est écarté avant les calculs de caméra et de brouillard. Les sons acceptés et le plafond de voix restent inchangés.',
        '- Chemins naturels : la liste des marcheurs présents est calculée lors de son utilisation, une fois tous les 25 ticks. Les empreintes et leur usure continuent au même rythme.', '',
        '## Vérifications', '',
        '- 683 tests C# réussis, 2 ignorés. Les nouveaux tests comparent repas et stocks avec une allocation exhaustive, sur des positions négatives, des limites de secteurs, des réservations, des pénuries et des charrettes mobiles.',
        '- Réutilisation des données de chemin : vérification après recherches courtes et longues, sur grilles rectangulaires et isométriques, avec préservation de recherches simultanées indépendantes.',
        '- Le contrôle audio utilise la vitesse normale : accélérer la simulation par dix alors que les voix expirent en temps réel saturait artificiellement le mixage. Les oiseaux, bovins, sons de travail et de combat, pause, silence et plafond de 18 voix passent à vitesse normale.',
        f'- Combat natif : {combat["targetingChecks"]} comparaisons avec le choix de cible précédent, dont {combat["matchedTargets"]} avec une cible effectivement choisie, sans divergence.',
        f'- Bataille massive terminée : {sum(a["combatDeaths"] for a in after["armies"])} morts attribuées à un ennemi, {sum(a["workers"] for a in after["armies"])}/1 000 citoyens survivants, {after["completedSites"]} chantiers terminés.',
        f'- {after["spatialChecks"]} comparaisons de l’index avec tous les acteurs positionnés, sans perte ni doublon ; {after["pathingChecks"]} contrôles de chemins et {after["visualChecks"]} comparaisons des traces au sol réussis ; jusqu’à {after["maxSleeping"]} citoyens endormis la nuit.', '',
        '| Régression native avec contrôle de synchronisation | Résultat |', '| --- | --- |',
        *[f'| {r["name"]} | {"Réussi" if r["passed"] else "ÉCHEC"} |' for r in regressions], '',
        '## Installation', '',
    ]
    if install:
        lines += ['La version validée est installée dans `bin-medieval`, utilisée par `launch-medieval.cmd`. Les versions précédentes sont sauvegardées dans `installed-before/` ; les préférences du joueur sont conservées.', '']
        lines += [f'- `{f["name"]}` : SHA-256 `{f["sha256"]}`.' for f in install['files']]
    else:
        lines += ['Installation encore non effectuée.']
    lines += ['', '## Limites et reproduction', '',
        'Les mouvements de milliers de soldats au même instant peuvent encore produire des pointes. La mesure porte sur cette machine et 160 secondes simulées ; elle ne garantit ni 60 FPS ni la fluidité de plusieurs heures de multijoueur. La carte de stress dégage les villes et l’arène ; les ponts sont vérifiés séparément par le test de grande carte.', '',
        '```powershell',
        '.\\.tools\\dotnet\\dotnet.exe build OpenRA.slnx -c Release -p:OutputPath=../bin-medieval-dense-spatial',
        "& 'C:\\Program Files\\Python314\\python.exe' mods/medieval/tools/stress_game.py --name dense-spatial --binary bin-medieval-dense-spatial",
        "& 'C:\\Program Files\\Python314\\python.exe' mods/medieval/tools/dense_report.py", '```', '',
        'Résultats bruts : `../load-test/dense-spatial/`, `../load-test/dense-reference/` et `regression-results-spatial.json`. Sources modifiées : `changes.diff` et `tested-sources.json`.', '']
    (OUT/'rapport.md').write_text('\n'.join(lines), encoding='utf-8')
    print(json.dumps(metrics))
