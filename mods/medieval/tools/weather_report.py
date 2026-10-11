"""Archive fresh weather validation and verify that the launcher uses the tested DLLs."""
from pathlib import Path
import hashlib
import json
import shutil
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'analysis/medieval/weather-polish'
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))

def main():
    summary=read(ROOT/'analysis/medieval/audit/summary.json')
    assert {r['name'] for r in summary}=={'weather-polish','seasons','audio','save'}
    assert all(r['fresh'] and r['passed'] and r['exitCode']==0 for r in summary)
    weather=read(OUT/'result.json'); assert weather['passed'] and weather['decoded']==12
    network=read(OUT/'network/deux-bourgs/result.json'); assert network['passed'] and network['identicalCheckpoints']
    sounds=read(OUT/'audio-assets.json'); assert sounds['passed'] and sounds['files']==96
    unit=ET.parse(OUT/'unit-tests/weather.trx').find('.//{*}Counters').attrib
    assert unit['failed']=='0' and unit['passed']=='691'
    yaml=(OUT/'yaml.log').read_text(encoding='utf-8-sig')
    assert 'Testing mod: Les Terres du Bourg' in yaml and 'Error:' not in yaml and 'Warnings: 129' in yaml
    for result in summary:
        source=ROOT/'analysis/medieval/audit'/result['name']; dest=OUT/'checks'/result['name']; dest.mkdir(parents=True,exist_ok=True)
        for name in ('result.json','native.log'): shutil.copy2(source/name,dest/name)
    (OUT/'summary.json').write_text(json.dumps(summary,indent=2,ensure_ascii=False),encoding='utf-8')
    hashes=[]
    for name in ('OpenRA.Game.dll','OpenRA.Mods.Common.dll','OpenRA.Mods.Cnc.dll','OpenRA.Mods.D2k.dll','OpenRA.Mods.Medieval.dll'):
        actual=hashlib.sha256((ROOT/'bin-medieval'/name).read_bytes()).hexdigest()
        assert actual==hashlib.sha256((ROOT/'bin-medieval-weather'/name).read_bytes()).hexdigest(),name
        hashes.append(dict(file=name,sha256=actual))
    (OUT/'installed.json').write_text(json.dumps(hashes,indent=2),encoding='utf-8')
    rows='\n'.join(f"| {r['name']} | Réussi | {r['seconds']} s |" for r in summary)
    (OUT/'rapport.md').write_text(f'''# Neige sur les bâtiments et ambiance des orages

Mise à jour installée le 10 octobre 2026 dans `bin-medieval`, utilisée par `launch-medieval.cmd`. Le cycle reste de **30 minutes par saison**.

## Graphismes

- Couches de neige progressives sur les six variantes de maisons, entrepôt, scierie, ferme, élevages, forge, caserne, marchés, tisseur, cabane du chasseur, atelier de siège et pavillons. Les textures restent visibles sous la neige ; les cheminées, ouvertures et mécanismes restent dégagés.
- Bourrelets sur les arêtes et petits glaçons au bas des toits. Fonte au printemps, en même temps que la neige au sol.
- Neige sur les clôtures de jardin, portillons, palissades, portes et enclos de taille variable. La neige suit chaque pose des battants ; l’ouverture reste libre. Le sol des enclos est enneigé.
- Pendant les fortes averses : assombrissement du ciel, éclair ramifié et courte lueur bleutée. Le bandeau météo indique « Orage ». Les flocons remplacent ces effets en hiver.

![Village enneigé](neige-village.png)

![Orage en jeu](orage.png)

## Audio

Douze sons supplémentaires : quatre pas dans la neige, deux roulements de charrette dans la neige, trois grondements de tonnerre et trois exclamations françaises. Les pas sont déclenchés par la distance réellement parcourue sur le sol enneigé, et non par une boucle permanente. Les surfaces protégées par le système de chemins conservent leurs sons ordinaires.

Le tonnerre suit l’éclair avec un délai de 0,5 à 2,2 secondes. Un habitant visible, éveillé et civil peut réagir brièvement. Une seule voix parlée occupe le canal à la fois ; les réactions sont espacées. Le moteur conserve son plafond de 18 sons simultanés dans le mixeur du village. La pause arrête les sons, et les options habituelles de volume/sourdine s’appliquent. Aucune musique ajoutée.

Les sons de neige et de tonnerre sont synthétisés localement par `weather_audio.py`. Les exclamations sont des voix françaises synthétiques produites localement par `weather_voices.ps1`, puis livrées comme fichiers WAV : aucune connexion ou synthèse vocale n’est nécessaire pendant le jeu. Provenance et caractéristiques dans `mods/medieval/audio/weather*-manifest.json`.

## Vérifications

Compilation Release : aucune erreur ni avertissement. **691 tests unitaires réussis**, deux ignorés. Règles et cartes : aucune erreur, 129 avertissements de textes hérités/inutilisés. Les **96 sons** livrés ont passé les contrôles PCM mono 22 050 Hz/16 bits, de saturation, de fondu et de variantes distinctes.

| Scénario natif | Résultat | Durée |
| --- | --- | --- |
{rows}

Le scénario météo a décodé les 12 nouveaux fichiers avec le moteur, observé {weather['snowyBuildings']} bâtiments ou clôtures enneigés et {weather['gateFrames']} positions de porte. Il a déclenché le tonnerre, une réaction d’habitant et les sons de neige par de véritables déplacements. Le pic audio est de {weather['PeakVoices']} voix. Les contrôles couvrent également la pause et l’arrêt des orages lors du passage à la neige.

Le scénario des saisons confirme la construction, la chasse, le transport, la conservation des ressources, le cycle saisonnier et les empreintes. Le scénario audio vérifie aussi les bruitages antérieurs. La sauvegarde/reprise pendant l’hiver donne un état identique à la simulation continue.

Multijoueur : deux processus clients et un serveur TCP local, 9 000 ticks, 36 états de contrôle identiques, vérification de synchronisation renforcée. Les saisons sont accélérées seulement dans ce test. Cela ne constitue pas un essai sur deux ordinateurs par Internet.

Les surfaces enneigées sont calculées pour les bâtiments affichés et mises en cache par pose ; les particules et les traces gardent leurs limites existantes. Les effets d’orage sont visuels et sonores : ils ne provoquent pas d’incendie ni de dégâts et ne changent pas les ordres. Les anciennes sauvegardes antérieures aux nouvelles règles ne font pas partie de cette validation.

## Reproduction et preuves

```powershell
& .tools/dotnet/dotnet.exe build OpenRA.slnx -c Release --no-restore -p:OutputPath=../bin-medieval-weather
py mods/medieval/tools/weather_audio.py
& mods/medieval/tools/weather_voices.ps1
py mods/medieval/tools/verify-audio.py
py mods/medieval/tools/audit_game.py --binary bin-medieval-weather --only weather-polish seasons audio save --jobs 1 --support-root analysis/medieval/weather-polish/profiles
py mods/medieval/tools/network_game.py deux-bourgs --deep-sync --binary bin-medieval-weather --season-ticks 1500 --output-root analysis/medieval/weather-polish/network
```

Garder les tests graphiques en série. Preuves : `summary.json`, `checks/`, `result.json`, `audio-assets.json`, `unit-tests/weather.trx`, `network/deux-bourgs/result.json`, `yaml.log`, `build.log`, `installed.json`. Les empreintes SHA-256 confirment que le lanceur utilise les bibliothèques testées.
''',encoding='utf-8',newline='\n')
    print('Weather report verified: four native scenarios, 691 unit tests, 96 audio files, TCP sync, installed libraries.')

if __name__=='__main__': main()
