"""Archive the completed finishing audit; never count earlier or partial runs."""
from pathlib import Path
import hashlib
import json
import shutil
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'analysis/medieval/finishing'


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def main():
    gameplay = read(OUT / 'gameplay-summary.json')
    frontend = read(OUT / 'frontend/summary.json')
    network = read(OUT / 'network-herds-fix/deux-bourgs/result.json')
    geography = read(OUT / 'geography.json')
    installed = read(OUT / 'installed.json')
    assert len(gameplay) == 40 and all(r['fresh'] and r['passed'] for r in gameplay), 'Gameplay audit incomplete or failed'
    assert len(frontend) == 2 and all(r['fresh'] and r['passed'] for r in frontend)
    assert network['passed'] and network['identicalCheckpoints']
    assert all(r['passed'] and all(c['sectors'] == c['total'] for c in r['coverage'].values()) for r in geography)
    for entry in installed:
        actual = hashlib.sha256((ROOT/'bin-medieval'/entry['file']).read_bytes()).hexdigest().upper()
        assert actual == entry['sha256'], 'Installed library changed after audit'
    counters = ET.parse(OUT/'unit-tests/finishing.trx').find('.//{*}Counters').attrib
    assert counters['failed'] == '0'
    for r in gameplay:
        dest = OUT/'gameplay'/r['name']; dest.mkdir(parents=True, exist_ok=True)
        source = ROOT/'analysis/medieval/audit'/r['name']
        for file in ('result.json', 'native.log'):
            shutil.copy2(source/file, dest/file)
    lint = (OUT/'yaml.log').read_text(encoding='utf-8-sig')
    assert 'Error:' not in lint
    assert 'Art validation passed: 502' in (OUT/'art.log').read_text(encoding='utf-8-sig')
    assert 'Audio validation passed: 84' in (OUT/'audio-assets.log').read_text(encoding='utf-8-sig')
    warnings = [line for line in lint.splitlines() if line.startswith('Warnings:')][-1]
    rows = '\n'.join(f"| {r['name']} | {r['map']} | Réussi |" for r in gameplay)
    lines = f'''# Finition des Terres du Bourg — 10 octobre 2026

L’illustration de chargement et les menus médiévaux sont installés dans le lanceur habituel. Cet audit regroupe les nouveaux contrôles de la version installée ; les anciens rapports restent des archives de leurs versions respectives.

## Accueil et navigation

- Illustration originale d’un bourg au crépuscule : marché, fermes, élevages, rivière, pont, forêt et activité militaire. Affichage sans déformation ; texte et commandes restent rendus par le moteur.
- Accueil « Les Terres du Bourg », boutons vert sombre et or, chargement avec indicateur d’activité sans pourcentage fictif.
- `launch-medieval.cmd` ouvre désormais l’accueil. **Jouer en solo → Fonder mon premier bourg** lance la caravane ; **Choisir une région** ouvre un salon sur une carte solo compatible.
- Guide intégré en cinq pages : départ, économie, village, armée et multijoueur.
- Principaux menus traduits, salons et sauvegardes harmonisés, lecteur de musique vide et campagne inexistante retirés du parcours.
- Paramètres audio adaptés à l’ambiance et aux bruitages, conformément au choix du joueur.
- Le pays se personnalise au marché : le salon n’affiche plus un drapeau et un nom de faction d’Alerte Rouge.
- Résumé du domaine dans le menu de pause : pays, logements, population et besoins alimentaires ; retrait de l’objectif générique « détruire toute opposition » alors qu’aucune victoire automatique n’est définie.

## Défauts et améliorations traités

| Observation | Correction |
| --- | --- |
| L’ancien lanceur contournait l’accueil. | Entrée par le nouveau menu avec accès explicites au solo et au réseau. |
| Le choix par défaut du salon pouvait être la carte la plus récente, nécessitant plusieurs humains. | La création d’un salon solo commence sur Fondation. |
| Aucun aperçu de la carte Fondation dans le sélecteur. | Aperçu généré par le moteur ; données du terrain conservées. |
| Libellés français trop longs dans certains boutons. | Intitulés raccourcis ou boutons de sauvegarde agrandis, contrôlés aux deux résolutions. |
| La caravane initiale n’était pas présélectionnée. | Centrage et sélection au nouveau départ, sans modifier la caméra restaurée des sauvegardes ni les replays. |
| Habillage et options sans rapport avec le mod. | Suppression des logos, drapeaux de faction, objectifs et accès musicaux concernés ; panneaux cohérents. |
| Vache bloquée derrière son éleveur lors de l’arrivée au portillon. | La distance de suivi permet une case voisine libre au lieu d’exiger d’occuper celle du guide. Un cas de régression force cette configuration et vérifie l’entrée réelle dans l’enclos. |

Le déplacement de caméra constaté sur certaines premières captures venait aussi du défilement automatique avec le curseur au bord de la fenêtre. Les profils de test le désactivent désormais ; les préférences habituelles du joueur restent intactes.

## Résultats actuels

| Contrôle | Résultat et preuve |
| --- | --- |
| Compilation Release | 0 erreur, 0 avertissement de compilation ; `build.log` |
| Tests C# | {counters['passed']} réussis, 2 ignorés, 0 échec ; `unit-tests/finishing.trx` |
| Interface native | 2 parcours complets réussis : 1280 × 800 et 1024 × 600 ; `frontend/summary.json` |
| Règles, interfaces et cartes | 0 erreur de lint ; {warnings} (textes hérités/inutilisés) ; `yaml.log` |
| Graphismes et sons | 502 planches et 84 effets PCM validés ; `art.log`, `audio-assets.log` |
| Scénarios natifs | 40/40 réussis sur des profils isolés ; `gameplay-summary.json` |
| Sauvegarde/reprise | État et empreinte de simulation identiques à la partie ininterrompue ; `gameplay/save/result.json` |
| Réseau TCP | 2 clients, 9 000 ticks, 36 points de contrôle identiques, vérifications de code visuel activées ; `network-herds-fix/deux-bourgs/result.json` |
| Géographie | 7 cartes vérifiées : départs reliés, bois, pierre et végétation dans tous les secteurs contrôlés ; `geography.json` |
| Installation | Bibliothèques identiques à la compilation testée ; `installed.json` ; anciennes bibliothèques dans `installed-before/` |

Les parcours d’interface ouvrent réellement l’accueil, le solo, les cinq pages du guide, les paramètres, l’audio, les archives, le salon et le choix de carte, puis démarrent une partie, ouvrent la pause et les sauvegardes, et reprennent le jeu. Captures dans `frontend/1280x800/` et `frontend/1024x600/`. Le test de sauvegarde séparé écrit puis recharge réellement une partie.

Le premier passage a détecté un échec de l’élevage, archivé dans `herds-before/`. La distance de suivi a été corrigée ; le parcours complet avec un test supplémentaire d’arrivée au portillon a été relancé, ainsi que les scénarios affectés. `gameplay-summary.json` conserve les derniers résultats vérifiés de chaque scénario. Le contrôle sans GPU intermédiaire (`herds-headless/`) sert à la simulation et n’est pas une validation graphique.

Le réseau utilise un serveur dédié et des processus indépendants sur **un seul ordinateur**. Il exerce la fondation, les constructions, les récoltes/livraisons, le recrutement, les identités de pays et les combats. Il ne prouve pas la connectivité entre deux machines distantes. La géographie utilise des secteurs 8 × 8 sur les continents et 4 × 4 sur les petites cartes ; l’audit précédent a déjà vérifié la répétabilité des graines (`../network-maps/summary.json`).

## Couverture des systèmes

| Scénario | Carte | Résultat |
| --- | --- | --- |
{rows}

Les scénarios couvrent les stocks et réservations, les producteurs et porteurs, les chantiers, marchés et routes, les logements et recrutements, la faim, les élevages, les ressources finies, les armées et engins capturables, le moral, le repos et les rondes, les messages, les incendies, les animations, le placement et les paysages. Ils vérifient des situations déterminées, pas toutes les combinaisons possibles ni des sessions de plusieurs heures.

## Points restant avant une version finale distribuable

1. **IA adverse médiévale** : les emplacements des cartes jouables interdisent les bots. Le solo est actuellement un développement libre. Il faut une IA capable de gérer cette économie et sa logistique avant de proposer de vraies escarmouches contre l’ordinateur.
2. **Conditions de victoire et défaite** : `ConquestVictoryConditions` est désactivé dans `mods/medieval/rules.yaml`. Les unités se battent et détruisent les biens adverses, mais une fin de partie par conquête ne se déclenche pas automatiquement. Le critère doit être défini pour ce jeu avant son activation.
3. **Parties distantes et longues** : vérifier deux ordinateurs, les pare-feu/NAT, les coupures de connexion et les sessions prolongées. Les contrôles actuels portent sur le réseau local à la machine.
4. **Équilibrage** : les mécanismes sont testés, pas l’équilibre définitif entre production, consommation, expansion, troupes d’élite et siège. Un très petit enclos bovin conserve ses deux reproducteurs et ne garantit donc pas un surplus durable de viande.
5. **Distribution et versionnement** : le mod dépend encore des contenus Red Alert installés via OpenRA, et affiche une version de développement. Préparer une version numérotée et un paquet reproductible, puis vérifier l’installation sur une machine propre et la compatibilité des sauvegardes de cette version.
6. **Derniers détails de présentation** : certains réglages avancés, outils et icônes de commande restent hérités du moteur ; la traduction n’est pas exhaustive. Le style des nouvelles illustrations est plus détaillé que les sprites de la partie, qui restent ceux du RTS isométrique.

Les limites de performances des mêlées extrêmes restent celles du rapport `../dense-optimization/rapport.md` : les pics ont été réduits mais pas éliminés. Cet audit ne promet ni un nombre constant d’images par seconde sur toutes les machines ni l’absence de tout bug.

## Fichiers et reproduction

Illustration : `mods/medieval/art/menu/bourg-dusk.png`. Création avec l’outil intégré de génération d’images ; prompt et provenance dans `mods/medieval/art/menu/provenance.md`. L’image originale est conservée, le moteur gère son cadrage. Menus et panneaux reproductibles via `mods/medieval/tools/frontend_content.py`, appelé par le générateur général.

Depuis la racine, avec Python et .NET du projet :

```powershell
& .tools/dotnet/dotnet.exe build OpenRA.slnx -c Release --no-restore -p:OutputPath=../bin-medieval-finishing
py mods/medieval/tools/audit_frontend.py
py mods/medieval/tools/audit_game.py --binary bin-medieval-finishing --jobs 1 --support-root analysis/medieval/finishing/profiles
py mods/medieval/tools/network_game.py deux-bourgs --deep-sync --binary bin-medieval-finishing --output-root analysis/medieval/finishing/network-herds-fix
```

Exécuter les validations graphiques **en série**. Chaque scénario vérifie que son rapport est frais et que son processus se termine normalement. Les anciennes sauvegardes et préférences du joueur ne sont pas utilisées comme destinations de test.
'''
    (OUT/'rapport.md').write_text(lines, encoding='utf-8', newline='\n')
    print('Finishing report archived: 40 gameplay cases, 2 UI resolutions, TCP and geography passed.')


if __name__ == '__main__':
    main()
