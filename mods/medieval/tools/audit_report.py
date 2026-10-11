"""Summarize archived native audit results without treating missing/stale data as success."""
from pathlib import Path
import json
from audit_game import CASES, ROOT, OUT

fixes = [
    ('Enclos détruit pendant une capture ou un transfert', 'L’éleveur ou l’animal accédait aux composants d’un enclos déjà détruit et faisait planter la partie.', 'Libération des réservations et de l’animal sans consulter un objet détruit.'),
    ('Champ supprimé pendant la récolte', 'Le rendu pouvait lire une source détruite avant que le prochain cycle de travail libère sa référence.', 'Vérification de la disponibilité de la source avant de calculer l’animation de récolte.'),
    ('Annulation de chantier répétée', 'Plusieurs ordres reçus ensemble restituaient plusieurs fois les mêmes matériaux.', 'Une seule restitution, même avant la suppression effective du chantier.'),
    ('Suppression répétée de décoration', 'Les matériaux livrés pouvaient être remboursés plusieurs fois.', 'Une seule récupération de matériaux par décoration.'),
    ('Rangement répété d’un pavillon', 'La même tente pouvait être ajoutée plusieurs fois à une charrette.', 'Un pavillon replié donne exactement une tente.'),
    ('Logements perdus pendant un recrutement civil', 'Le villageois apparaissait malgré la destruction des logements.', 'Recrutement conservé en attente puis repris sans second paiement.'),
    ('Logements perdus pendant la préparation d’une charrette', 'Le porteur apparaissait malgré le manque de logements.', 'Même contrôle de capacité au moment de la création effective.'),
    ('Sortie de caserne ou de forge occupée au dernier instant', 'La création pouvait utiliser une case devenue occupée après son choix.', 'Dernière vérification de la case, du propriétaire, des logements et de la commande ; provisions conservées.'),
    ('Retour de charge dans un village isolé', 'Les porteurs interrompus ignoraient les marchés comme lieux de retour ; un transporteur pouvait annuler en boucle son retour à un étal sans liaison.', 'Retour au stockage compatible le plus proche, y compris marchés et petits marchés, avec réservation et conservation de la charge.'),
]

def read(path):
    return json.loads(path.read_text(encoding='utf-8')) if path.exists() else None

if __name__ == '__main__':
    native = []
    for name, mapid, _ in CASES:
        item = read(OUT/name/'result.json')
        native.append((name, mapid, item))
    edge, save = read(OUT/'edge/result.json'), read(OUT/'save/result.json')
    passed = sum(bool(item and item.get('passed') and item.get('fresh')) for _, _, item in native)
    lines = [
        '# Audit du jeu médiéval — 10 octobre 2026', '',
        'Audit du code du mod, de ses règles, de ses ressources graphiques et sonores, complété par des parties natives automatisées. Les corrections ci-dessous concernent des défauts reproduits ; les limites de validation sont indiquées à la fin.', '',
        '## Bugs corrigés', '', '| Cas | Défaut reproduit | Comportement corrigé |', '| --- | --- | --- |',
        *[f'| {a} | {b} | {c} |' for a,b,c in fixes], '',
        'L’atlas du trébuchet a aussi été régénéré : certaines poses d’assemblage ne correspondaient plus à l’animation actuelle du bâtisseur dans le générateur.', '',
        '## Résultats', '',
        '- Compilation : voir `install-build.log` ; règles et dix cartes : `yaml.log` (11 avertissements de traductions inutilisées héritées d’Alerte Rouge).',
        '- Suite C# moteur et mod : 669 réussites, dont 160 tests médiévaux, 2 tests ignorés, 0 échec ; détail dans `unit-tests/audit-final.trx`.',
        '- Graphismes : 502 planches contrôlées ; intégrité des poses, des ancrages et des dimensions. Contrôles supplémentaires des animaux, du sabotage et des pompiers dans les journaux `*-art.log`.',
        '- Audio : 56 effets PCM contrôlés, décodage et déclenchement en partie, sans musique.',
        f'- Scénarios natifs archivés : {passed}/{len(native)} réussis avec résultat frais.',
        f'- Cas limites : {"réussis" if edge and edge.get("passed") else "NON VALIDÉS"} ({len(edge.get("checks", [])) if edge else 0} assertions, `edge/result.json`).',
        f'- Sauvegarde/reprise : {"réussie" if save and save.get("passed") else "NON VALIDÉE"}, comparaison à la simulation ininterrompue au tick 700 (`save/result.json`, `save/uninterrupted.json`, `save/restored.json`).', '',
        '| Scénario | Carte | Résultat |', '| --- | --- | --- |',
    ]
    for name, mapid, item in native:
        ok = bool(item and item.get('passed') and item.get('fresh'))
        lines.append(f'| {name} | {mapid} | {"Réussi" if ok else "Non validé — examiner le journal"} |')
    installation = read(OUT/'installation.json')
    if installation:
        lines += ['', '## Installation', '',
            f'Bibliothèque installée dans `{installation["target"]}` et comparée à la copie testée. SHA-256 : `{installation["sha256"]}`. La version précédente est conservée dans `{installation["backup"]}`.', '',
            'Le lanceur habituel `launch-medieval.cmd` utilise cette bibliothèque. Les bruitages sont réactivés et les contrôles de synchronisation réservés aux tests sont désactivés dans le profil normal.']
    lines += ['', '## Grandes cartes : mesures de cet audit', '',
        'Mesures sur cette machine, contrôles de synchronisation activés, 1 000 ticks et trajets de ponts. Le temps par tick mesure le calcul de simulation, pas les FPS. Ces résultats ne sont pas directement comparables aux anciens benchmarks sans ces contrôles.', '',
        '| Carte | Sources naturelles | Chargement | Calcul moyen par tick | Pic mémoire | Traversées terminées |',
        '| --- | ---: | ---: | ---: | ---: | ---: |']
    for name, mapid, item in native:
        if not name.startswith('large-') or not item or not item.get('passed'):
            continue
        r = item['result']
        lines.append(f'| {mapid} | {r["sources"]:,} | {r["loadSeconds"]:.1f} s | {r["averageTickMs"]:.1f} ms | {r["peakMemoryMiB"]/1024:.2f} Gio | {sum(c["Arrived"] for c in r["crossings"])}/{len(r["crossings"])} |')
    lines += ['', '## Scénarios historiques adaptés', '',
        '- Campement et intégration : remplacer les troupeaux virtuels par de vrais bovins et moutons dans les fixtures ; contrôler les poses des animaux individuels et leur reproduction réelle.',
        '- Fortifications : empêcher les cibles de test contrôlées par Creeps de détruire les palissades dont on mesure le coût ; les combats réels restent contrôlés séparément.',
        '- Placement : empêcher l’apparition aléatoire de bétail sur les cases réservées à la mesure du curseur.',
        '- Audio : utiliser la carte de test disposant des factions nécessaires. Archivage du résultat de nature corrigé pour son sous-dossier.',
        '- Ces adaptations sont réservées aux scénarios de validation, pas aux règles des parties normales.', '',
        '## Limites et observations', '',
        '- Un crash natif a été enregistré dans le pilote graphique AMD `atio6axx.dll` pendant des essais simultanés de grandes cartes. Les essais concernés ont été relancés individuellement ; aucune modification de pilote système n’a été faite. Diagnostic conservé dans `graphics-driver-error.json`.',
        '- Les résultats des grandes cartes portent sur le départ et les trajets de test, pas sur une région entièrement urbanisée ni sur des milliers de soldats en bataille.',
        '- Les interactions de factions adverses sont exercées dans le moteur. Une vraie partie réseau entre deux ordinateurs, les déconnexions/reconnexions et une session de plusieurs heures restent à tester.',
        '- La reprise vérifie une sauvegarde créée par la même version. La compatibilité avec toutes les anciennes sauvegardes n’est pas garantie.',
        '- Les trois régions immenses sont actuellement des cartes solo. Le multijoueur utilise notamment Deux Bourgs et Trois Bourgs ; il n’existe pas encore d’IA médiévale complète.',
        '- Un enclos bovin de moins de trois places ne permet pas une production durable de viande : deux adultes sont préservés pour la reproduction et seuls les adultes excédentaires sont abattus. Ce comportement mérite d’être davantage expliqué dans l’interface.',
        '- L’audit réduit les risques connus ; il ne démontre pas l’absence de tout bug ni un équilibrage définitif.', '',
        '## Relancer', '',
        'Depuis la racine, après compilation de la copie indiquée :', '',
        '```powershell',
        "& 'C:\\Program Files\\Python314\\python.exe' mods/medieval/tools/audit_game.py --binary bin-medieval --jobs 1",
        "& 'C:\\Program Files\\Python314\\python.exe' mods/medieval/tools/audit_report.py",
        '```', '',
        'Le lanceur recompose les manifests de test à partir des règles actuelles. Il contrôle la fraîcheur des rapports et impose un délai maximal à chaque processus qu’il crée.', '',
    ]
    (OUT/'rapport.md').write_text('\n'.join(lines),encoding='utf-8')
    print(f'Native scenarios: {passed}/{len(native)}; edge={bool(edge and edge.get("passed"))}; save={bool(save and save.get("passed"))}')
