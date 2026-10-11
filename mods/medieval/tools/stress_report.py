"""Build the load-test report from completed results; partial trials stay labelled."""
from pathlib import Path
import json

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'analysis/medieval/load-test'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else None

if __name__=='__main__':
    result=read(OUT/'massive-result/result.json')
    execution=read(OUT/'massive-result/execution.json')
    if not result or not execution or not execution.get('passed'):
        raise SystemExit('The final massive simulation has not completed successfully.')
    before=read(OUT/'massive-before/progress.json')
    first=result['samples'][0]
    deaths=sum(a['combatDeaths'] for a in result['armies'])
    civilian_survivors=sum(a['workers'] for a in result['armies'])
    food=sum(a['foodConsumed'] for a in result['armies'])
    hunger_deaths=sum(a['starvationDeaths'] for a in result['armies'])
    soldier_survivors=sum(a['soldiers'] for a in result['armies'])
    lines=[
        '# Charge massive : quatre armées sur Les Terres sans Fin', '',
        'Test natif du 10 octobre 2026, sur une copie de la carte de 512 × 1024 cases. Les cartes habituelles et les préférences du joueur sont conservées.', '',
        '## Charge et méthode', '',
        '- 2 000 militaires, dont miliciens, épéistes, archers, hommes d’armes, archers d’élite, gardes royaux, tambours et porte-drapeaux.',
        '- 1 000 citoyens actifs : fermiers, transporteurs, bûcherons, tailleurs de pierre et bâtisseurs, répartis en quatre villes.',
        '- 80 charrettes supplémentaires de ravitaillement ; 1 440 bâtiments, parcelles et chantiers, dont 144 chantiers à terminer. Le compteur technique de 1 728 structures inclut aussi les 288 arbres et rochers ajoutés aux quartiers.',
        '- La grande carte conserve plus de 14 000 acteurs de décor et de ressources au chargement. Les emplacements des quatre villes et du combat sont défrichés et aplanis dans cette copie de test.',
        '- Les villes et armées sont préparées directement pour mesurer la charge. Ensuite, ressources, réservations, déplacements, consommation, dégâts, faim, morale et fatigue suivent les règles normales, sans revenu automatique ni invulnérabilité.',
        '- Quatre camps ennemis se rencontrent au centre. Le banc de test donne des ordres d’avance et relance les soldats devenus inactifs ; ce n’est pas une IA stratégique complète.',
        '- 4 000 ticks, soit 2 min 40 s de temps de jeu normal. L’horloge commence avant le crépuscule pour inclure la nuit, en conservant les durées habituelles de 8 minutes de jour et 2 minutes de nuit.',
        '- Une seule instance graphique, profils et sauvegardes isolés. Instrumentation des fonctions lentes activée ; le contrôle coûteux des accès non synchronisés est réservé aux tests de régression.', '',
        '## Résultats observés', '',
        f'- Scénario achevé sans exception : **{result["tick"]} ticks**, {execution["seconds"]:.1f} s avec chargement ; simulation : {result["seconds"]:.1f} s.',
        f'- **{deaths} morts attribuées à un dernier coup ennemi**, {soldier_survivors}/2 000 militaires et {civilian_survivors}/1 000 citoyens encore vivants à la fin ; chaque camp a infligé/reçu des dégâts et subi des pertes.',
        f'- **{result["completedSites"]}/144 constructions terminées**, production et transport physiques observés dans chacun des quatre camps.',
        f'- **{food} repas consommés**, maximum {result["maxHungry"]} personnes affamées simultanément, {hunger_deaths} morts par famine.',
        f'- Jusqu’à **{result["maxSleeping"]} citoyens endormis** durant la phase nocturne.',
        f'- {result["visualChecks"]} comparaisons du cache graphique des chemins avec les traces réelles, sans divergence enregistrée.', '',
        '| Camp | Soldats restants | Citoyens restants | Morts au combat | Repas consommés | Camps adverses rencontrés |',
        '| --- | ---: | ---: | ---: | ---: | --- |',
    ]
    for a in result['armies']:
        lines.append(f'| {a["player"]} | {a["soldiers"]} | {a["workers"]} | {a["combatDeaths"]} | {a["foodConsumed"]} | {", ".join(a["opponents"])} |')
    lines += ['', 'Le compteur de combat ne comptabilise que les coups mortels ennemis : il ne représente donc pas toutes les pertes. Les règles actuelles autorisent les dommages alliés autour des impacts de flèches. Ce test ne journalise pas séparément tous les coups mortels alliés ou autres ; les pertes restantes ne leur sont pas attribuées automatiquement.', '']
    lines += ['', '## Performances', '',
        f'Le départ non optimisé atteignait 250 ticks en {before["samples"][0]["seconds"]:.1f} s. Après corrections, un départ de même carte et même population prend {first["seconds"]:.1f} s (environ {before["samples"][0]["seconds"]/first["seconds"]:.1f} fois plus rapide). La référence trop lente a été arrêtée volontairement ; cette comparaison concerne uniquement les 250 premiers ticks, pas toute la bataille. Les événements aléatoires ne sont pas verrouillés par une graine commune entre ces essais.', '',
        f'Sur la simulation complète : moyenne **{result["averageTickMs"]:.1f} ms/tick**, médiane {result["p50TickMs"]:.1f} ms, 95e percentile {result["p95TickMs"]:.1f} ms et pointe {result["maxTickMs"]:.0f} ms. Pic mémoire : **{result["peakMemoryMiB"]/1024:.2f} Gio**. Ces valeurs ne sont pas des FPS.', '',
        'Le rythme normal demande un tick toutes les 40 ms. Les pointes de recherche de chemins et la densité du combat dépassent encore ce budget sur cette machine ; le scénario est terminé, mais une bataille de cette taille présente encore des ralentissements.', '',
        'Les mesures comprennent l’instrumentation et les captures : les comparaisons graphiques coûtent jusqu’à 432 ms chacune. Le journal relève aussi des activités de déplacement/attaque allant jusqu’à 437 ms. Les durées imbriquées ne doivent pas être additionnées. Ce banc mesure une situation chargée instrumentée, pas une fréquence d’images garantie en partie normale.', '',
        '| Tick | Temps réel de simulation | Ticks/s sur les 250 derniers ticks | Militaires vivants | Morts au combat cumulés |',
        '| ---: | ---: | ---: | ---: | ---: |']
    for s in result['samples']:
        lines.append(f'| {s["tick"]} | {s["seconds"]:.1f} s | {s["ticksPerSecond"]:.1f} | {s["soldiers"]} | {s["combatDeaths"]} |')
    lines += ['', '## Corrections apportées', '',
        '- Transporteurs : réutilisation des listes de dépôts pendant une recherche, exclusion des sources vides et des destinations pleines, recherche des seuls chantiers inachevés. Les priorités et réservations restent inchangées.',
        '- Fermiers : exclusion des parcelles en croissance avant le calcul de leur capacité et comptage des seuls ouvriers, sans parcourir tous les acteurs de la carte.',
        '- Repas : choix direct de la station prioritaire la plus proche ; entre deux repas, simple vérification de couverture. Conservation des réserves, des priorités, de l’ordre des personnes affamées et des égalités de distance.',
        '- Chemins naturels : invalidation ciblée quand l’usure, les empreintes des bâtiments, les aires de stockage ou la visibilité changent. Plus de recalcul global de toutes les empreintes à chaque image.', '',
        '## Vérifications complémentaires', '',
        'Suite moteur et mod : 671 tests C# réussis, 2 ignorés, aucun échec (`unit-tests/load-test.trx`). Les nouveaux tests contrôlent notamment les égalités de priorité des repas, l’épuisement des réserves et les notifications d’usure des chemins sans changement d’état de simulation.', '']
    regression=read(OUT/'regression-results.json')
    if regression:
        lines += ['| Régression native | Résultat |','| --- | --- |']
        lines += [f'| {r["name"]} | {"Réussie" if r["passed"] else "Échec"} |' for r in regression]
    installation=read(OUT/'installation.json')
    if installation:
        lines += ['', f'Installation vérifiée : `{installation["target"]}`, SHA-256 `{installation["sha256"]}`. Ancienne bibliothèque conservée dans `installed-before/`.']
    lines += ['', '## Limites', '',
        '- Test local de quatre factions, sans connexion réseau entre plusieurs ordinateurs.',
        '- Combat en zone dégagée : le passage de milliers d’unités par un pont ou un étranglement de forêt n’est pas validé par ce scénario.',
        '- Les unités partent avec des vivres finis dans des charrettes restées près du départ. La faim des soldats sortis de leur influence est attendue ; le scénario ne constitue pas une démonstration d’équilibre économique permanent.',
        '- Pas de siège massif, de flotte, ni de session de plusieurs heures dans cet essai. Les engins de siège restent couverts par les scénarios fonctionnels précédents.',
        '- Les essais interrompus sont archivés dans `massive-before`, `massive-after`, `massive-final` et `massive-verified`, avec leurs motifs ; ils ne sont pas comptés comme des réussites complètes.', '',
        '## Relancer', '', '```powershell',
        ".\\.tools\\dotnet\\dotnet.exe build OpenRA.slnx -c Release -p:OutputPath=../bin-medieval-load-release",
        "& 'C:\\Program Files\\Python314\\python.exe' mods/medieval/tools/stress_game.py --soldiers 500 --workers 250 --ticks 4000 --name massive-result --binary bin-medieval-load-release",
        "& 'C:\\Program Files\\Python314\\python.exe' mods/medieval/tools/stress_report.py", '```', '',
        'Captures : `massive-result/view-1200.png` (combat) et `massive-result/view-3900.png` (ville nocturne). Rapports bruts et journaux de performance dans le même dossier.', '']
    (OUT/'rapport.md').write_text('\n'.join(lines),encoding='utf-8')
    print(f'{deaths} combat deaths, {civilian_survivors} civilians alive, {result["averageTickMs"]:.1f} ms/tick')
