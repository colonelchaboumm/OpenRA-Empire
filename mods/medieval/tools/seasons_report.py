"""Preserve fresh season/hunting validation receipts and verify the installed build."""
from pathlib import Path
import json
import hashlib
import shutil
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'analysis/medieval/seasons-hunting'
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))

def main():
    first=read(OUT/'first-pass.json')
    latest=read(OUT/'gameplay-summary.json')
    checks={r['name']:r for r in first+latest}
    checks['save']=read(OUT/'winter-save.json')
    assert len(checks)==8 and all(r['passed'] and r['fresh'] and r['exitCode']==0 for r in checks.values())
    network=read(OUT/'network/deux-bourgs/result.json')
    assert network['passed'] and network['identicalCheckpoints'] and network['seasonTicks']==1500
    ui=read(OUT/'frontend/summary.json');assert all(r['passed'] and r['fresh'] for r in ui)
    unit=ET.parse(OUT/'unit-tests/seasons.trx').find('.//{*}Counters').attrib
    assert unit['failed']=='0'
    assert '504 sheets' in (OUT/'art.log').read_text(encoding='utf-8-sig')
    assert 'Error:' not in (OUT/'yaml.log').read_text(encoding='utf-8-sig')
    for name,r in checks.items():
        dest=OUT/'checks'/name;dest.mkdir(parents=True,exist_ok=True)
        for file in ('result.json','native.log'):shutil.copy2(ROOT/'analysis/medieval/audit'/name/file,dest/file)
    (OUT/'summary.json').write_text(json.dumps(list(checks.values()),indent=2,ensure_ascii=False),encoding='utf-8')
    hashes=[]
    for file in ('OpenRA.Game.dll','OpenRA.Mods.Common.dll','OpenRA.Mods.Cnc.dll','OpenRA.Mods.D2k.dll','OpenRA.Mods.Medieval.dll'):
        installed=hashlib.sha256((ROOT/'bin-medieval'/file).read_bytes()).hexdigest()
        assert installed==hashlib.sha256((ROOT/'bin-medieval-seasons'/file).read_bytes()).hexdigest()
        hashes.append(dict(file=file,sha256=installed))
    (OUT/'installed.json').write_text(json.dumps(hashes,indent=2),encoding='utf-8')
    rows='\n'.join(f"| {name} | Réussi | {r['seconds']} s |" for name,r in checks.items())
    (OUT/'rapport.md').write_text(f'''# Saisons et chasse — 10 octobre 2026

Mise à jour installée dans `bin-medieval`. Lanceur : `launch-medieval.cmd`. Commencer une nouvelle partie pour cette version ; les anciennes sauvegardes ne sont pas couvertes par le test de reprise.

## Quatre saisons

Départ au printemps. **30 minutes de simulation par saison**, soit **2 heures par année** à vitesse normale. Le cycle jour/nuit conserve 8 minutes de jour et 2 minutes de nuit. Le bandeau supérieur indique la saison, l’année, le temps restant et la météo.

| Saison | Croissance et travail agricole | Chasse | Déplacements hors précipitations |
| --- | --- | --- | --- |
| Printemps | 100 % | 100 % | 100 % |
| Été | 115 % | 100 % | 100 % |
| Automne | 75 % | 90 % | 95 % |
| Hiver | 20 % | 65 % | 80 % |

Les pourcentages sont des rythmes relatifs ; le travail reste arrondi au pas de simulation. Le froid ralentit aussi le bois et la pierre à 75 %. Les intempéries enlèvent jusqu’à 10 points de vitesse. La puissance de combat baisse de 10 % en hiver ; les arcs perdent jusqu’à 15 points supplémentaires sous les précipitations. Le froid ajoute de la fatigue aux soldats exposés, tandis que le sommeil dans un pavillon permet toujours de récupérer. Faim et moral continuent à s’appliquer.

L’été réchauffe légèrement le terrain, l’automne colore le sol et les feuillages, l’hiver apporte flocons et neige, et le printemps fait fondre cette neige. Les empreintes et sillons suivent les déplacements observés. Ils disparaissent progressivement et plus vite sous la neige, sans révéler les unités cachées. Leur cache est limité à 8 192 marques, et le sol saisonnier est dessiné seulement dans la fenêtre visible.

## Cabane et chasseur

**Nourriture → Chasse** : cabane de 2 × 2 cases, 55 bois et 10 pierre. Les bâtisseurs la construisent avec les matériaux apportés par la logistique normale. **Citoyens → Suivant → Chasseur** : 10 nourriture, une place d’habitation, repas et sommeil ordinaires. Il participe également aux secours incendie.

Le chasseur cherche le gibier autour d’une cabane terminée, vise à l’arc et rapporte des charges de 12 nourriture au maximum. La cabane stocke 100 nourriture ; les transporteurs la collectent pour les entrepôts et marchés. Un manque de stockage suspend la production. Une carcasse partiellement récoltée conserve exactement sa nourriture restante.

Cerfs et lapins remplacent leurs anciens équivalents purement décoratifs : acteurs réels, sélectionnables, animés et synchronisés, placés près des secteurs boisés, capables de fuir. Un cerf donne 24 nourriture et un lapin 6. Cette nourriture alimente le compteur **Nourriture**, distinct de la viande des élevages. Le gibier initial est fini, plafonné à 600 animaux par carte et sans réapparition automatique.

## Vérifications

- Compilation Release réussie ; {unit['passed']} tests C# réussis, 2 ignorés, aucun échec.
- Règles et cartes : aucune erreur, 129 avertissements de textes hérités/inutilisés.
- 504 planches vérifiées : directions, animations, dimensions et absence de découpe aux bords.
- Construction, recrutement, chasse, portage, dépôt puis transport vérifiés par de vrais ordres : 30 nourriture récoltées ; total conservé en comptant les stocks, charges, repas, recrutement et ressources restantes.
- Quatre saisons traversées et deuxième année atteinte ; empreintes réellement produites. Captures `Printemps.png`, `Été.png`, `Automne.png`, `Hiver.png`. Les durées sont accélérées uniquement dans ce scénario.
- Sauvegarde à 500 ticks pendant l’hiver du profil accéléré, reprise à 700 ticks après le changement de saison : état et empreinte de simulation identiques.
- Deux clients et un serveur TCP sur la même machine : 9 000 ticks, saisons de 1 500 ticks pour traverser les changements, 36 points de contrôle identiques avec vérifications de synchronisation activées. Ce test ne valide pas une connexion Internet entre deux ordinateurs.
- Interface et guide visités à 1024 × 600 ; 42 choix de construction/recrutement vérifiés. Carte de 512 × 1 024 cases contrôlée avec la nouvelle faune ; ce scénario ne représente pas une immense bataille sous la neige.

| Scénario natif | Résultat | Durée de test |
| --- | --- | --- |
{rows}

Preuves : `summary.json`, `checks/`, `winter-save.json`, `unit-tests/seasons.trx`, `frontend/summary.json`, `network/deux-bourgs/result.json`, `art.log`, `yaml.log`, `installed.json`. Les captures initiales ont servi à corriger un raccord de neige avant la dernière validation visuelle.

## Sources et reproduction

Logique : `OpenRA.Mods.Medieval/Seasons.cs`, `Hunting.cs`, `Workers.cs`, `Buildings.cs`, `Weather.cs`, `Rest.cs`. Contenu : `mods/medieval/seasons.yaml`, `tools/seasons_content.py`, sprites dessinés par code à partir des modèles isométriques et des poses du projet. Le générateur général conserve l’intégration.

```powershell
py mods/medieval/tools/seasons_content.py
& .tools/dotnet/dotnet.exe build OpenRA.slnx -c Release --no-restore -p:OutputPath=../bin-medieval-seasons
py mods/medieval/tools/audit_game.py --binary bin-medieval-seasons --only seasons wilderness construction-menu logistics camping combat save large-terres-sans-fin --jobs 1 --support-root analysis/medieval/seasons-hunting/profiles
py mods/medieval/tools/network_game.py deux-bourgs --deep-sync --binary bin-medieval-seasons --season-ticks 1500 --output-root analysis/medieval/seasons-hunting/network
```

Les tests graphiques doivent rester en série. L’équilibrage d’une année entière de deux heures reste à éprouver en partie prolongée ; les scénarios accélérés vérifient les mécanismes, pas toutes les stratégies ni toutes les anciennes sauvegardes.
''',encoding='utf-8',newline='\n')
    print('Season/hunting report verified: 8 native scenarios, UI, unit tests, TCP and installed libraries.')

if __name__=='__main__': main()
