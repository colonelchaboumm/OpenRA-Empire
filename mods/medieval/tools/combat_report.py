"""Summarize completed massive-combat benchmarks and their functional checks."""
from pathlib import Path
import json
import re

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT/'analysis/medieval/combat-optimization'
LOAD = ROOT/'analysis/medieval/load-test'

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def phase(result, start=500, end=1500):
    samples = {s['tick']: s for s in result['samples']}
    return samples[end]['seconds'] - samples[start]['seconds']

def slow_moves(folder):
    records = []
    for line in (folder/'profile/Logs/perf.log').read_text().splitlines():
        match = re.match(r'\s*(\d+) ms \[(\d+)\] Activity: MoveWithinRange$', line)
        if match and 500 <= int(match[2]) <= 1500:
            records.append(int(match[1]))
    return records

if __name__ == '__main__':
    before_dir = LOAD/'massive-result'
    after_dir = LOAD/'combat-final'
    before, after = (read(p/'result.json') for p in (before_dir, after_dir))
    if not all(read(p/'execution.json')['passed'] and read(p/'result.json')['passed'] for p in (before_dir, after_dir)):
        raise SystemExit('Both complete scenarios must pass before making a comparison.')
    old_phase, new_phase = phase(before), phase(after)
    old_slow, new_slow = slow_moves(before_dir), slow_moves(after_dir)
    peak_after = f'{max(new_slow)} ms' if new_slow else 'aucun au-dessus du seuil de 10 ms'
    regressions = read(OUT/'regression-results.json') if (OUT/'regression-results.json').exists() else []
    install = read(OUT/'installation.json') if (OUT/'installation.json').exists() else None
    lines = [
        '# Optimisation des batailles massives — 10 octobre 2026', '',
        '## Résultat mesuré', '',
        'Scénario natif : copie des Terres sans Fin (512 × 1 024), quatre camps ennemis, 2 000 militaires, 1 000 citoyens et 80 charrettes. Les villes continuent à produire, transporter, construire et se nourrir pendant le combat.', '',
        '| Mesure | Avant | Après |', '| --- | ---: | ---: |',
        f'| Phase de mêlée, ticks 500 à 1 500 | {old_phase:.1f} s | {new_phase:.1f} s |',
        f'| Simulation entière, 4 000 ticks | {before["seconds"]:.1f} s | {after["seconds"]:.1f} s |',
        f'| Temps moyen par tick | {before["averageTickMs"]:.1f} ms | {after["averageTickMs"]:.1f} ms |',
        f'| 95e percentile | {before["p95TickMs"]:.1f} ms | {after["p95TickMs"]:.1f} ms |',
        f'| Approche de cible la plus lente journalisée, ticks 500 à 1 500 | {max(old_slow, default=0)} ms | {peak_after} |',
        f'| Pic mémoire du processus | {before["peakMemoryMiB"]/1024:.2f} Gio | {after["peakMemoryMiB"]/1024:.2f} Gio |', '',
        f'La phase de mêlée prend **{(1-new_phase/old_phase)*100:.1f} % de temps en moins**. La simulation complète prend {(1-after["seconds"]/before["seconds"])*100:.1f} % de temps en moins. Il s’agit de temps de calcul et de progression réelle, pas de FPS.', '',
        'Les deux essais ont la même carte, les mêmes effectifs, positions initiales et ordres de bataille. Ce ne sont pas des relectures identiques : les événements aléatoires ne sont pas verrouillés et les nouveaux chemins peuvent modifier le déroulement du combat. Le second essai ajoute cinq vérifications de chemins avant le démarrage du chronomètre. La moyenne globale inclut aussi le travail civil, les captures et les comparaisons graphiques.', '',
        '## Changements', '',
        '- Recherche de positions de combat : exploration depuis le soldat, limitée à 64 cellules examinées pour les destinations proches. Une région fermée est reconnue sans explorer toute la carte. Une limite atteinte déclenche la recherche complète habituelle, afin de conserver les grands détours.',
        '- La recherche courte mémorise uniquement les cellules rencontrées. Elle ne remet plus à zéro un tableau couvrant toute la carte à chaque demande.',
        '- Les positions de destination déjà connues comme bloquées sont exclues de la recherche complète.',
        '- Choix automatique des cibles : les vérifications d’armes sont évitées pour une cible qui ne peut plus battre la priorité et la distance de la meilleure cible déjà trouvée. Les égalités conservent leur ordre initial.',
        '- Les décisions utilisent un nombre fixe de cellules, jamais un délai réel. Dégâts, cadence de tir, portée, collisions, faim, fatigue et moral conservent leurs règles.', '',
        '## Validation', '',
        f'- Bataille complète réussie sans exception ; {sum(a["combatDeaths"] for a in after["armies"])} coups mortels ennemis enregistrés, {sum(a["workers"] for a in after["armies"])}/1 000 citoyens survivants, {after["completedSites"]} constructions terminées.',
        f'- {after["pathingChecks"]} contrôles natifs : accès proche, encerclement, détour autour d’un mur de 81 cases, sortie d’une case encombrée, rejet d’une destination bloquée avec conservation d’une autre accessible.',
        f'- Recherche locale : {after["localTargetPaths"]} chemins trouvés, {after["localTargetBlocked"]} blocages conclusifs et {after["localTargetFallbacks"]} reprises par la recherche complète. Ces compteurs incluent les cinq contrôles préparatoires.',
        '- 676 tests C# réussis, 2 ignorés. Cinq nouveaux tests contrôlent la limite de recherche, la reprise, l’épuisement exact du budget et le choix du trajet le moins coûteux.', '',
    ]
    if regressions:
        lines += ['| Scénario natif avec contrôles de synchronisation | Résultat |', '| --- | --- |']
        lines += [f'| {r["name"]} | {"Réussi" if r["passed"] else "ÉCHEC"} |' for r in regressions]
    if install:
        lines += ['', '## Installation', '', 'Les bibliothèques ci-dessous sont identiques aux copies testées. Les anciennes versions sont conservées dans `installed-before/`.', '']
        lines += [f'- `{item["name"]}` — SHA-256 `{item["sha256"]}`.' for item in install['files']]
        lines += ['', 'Le lanceur habituel utilise cette version ; les préférences du joueur sont conservées.']
    lines += ['', '## Limites', '',
        'Le rythme normal demande 25 ticks/s (40 ms par tick). De très grands rassemblements et déplacements simultanés peuvent encore ralentir le jeu. Les chiffres concernent cette machine, une bataille locale et 2 min 40 s de temps simulé ; ils ne garantissent pas une fréquence d’images constante, une session de plusieurs heures ni un multijoueur entre plusieurs ordinateurs.', '',
        'La copie de test dégage les quatre villes et l’arène. Le scénario complémentaire de grande carte teste les ponts avec ses unités dédiées, sans valider une armée de 2 000 soldats dans un unique étranglement.', '',
        '## Reproduire', '', '```powershell',
        '.\\.tools\\dotnet\\dotnet.exe build OpenRA.slnx -c Release -p:OutputPath=../bin-medieval-combat-release',
        "& 'C:\\Program Files\\Python314\\python.exe' mods/medieval/tools/stress_game.py --soldiers 500 --workers 250 --ticks 4000 --name combat-final --binary bin-medieval-combat-release",
        "& 'C:\\Program Files\\Python314\\python.exe' mods/medieval/tools/combat_report.py", '```', '',
        'Résultats bruts : `../load-test/combat-final/result.json`. Première variante avec tableaux de carte : `../load-test/combat-local/`. Référence précédente : `../load-test/massive-result/`. Captures : `../load-test/combat-final/view-1200.png` et `view-3900.png`.', '']
    (OUT/'rapport.md').write_text('\n'.join(lines), encoding='utf-8')
    print(f'Melee: {old_phase:.1f}s -> {new_phase:.1f}s ({(1-new_phase/old_phase)*100:.1f}% less); total: {after["seconds"]:.1f}s')
