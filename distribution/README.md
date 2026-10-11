# Distribution Windows et mises a jour

`release.py build` fige les sources dans `.distribution/build-…/source`, teste le
lanceur, execute les tests C#, compile le jeu autonome Windows x64, valide les
regles et genere les archives. Les dossiers de travail, profils, journaux et SDK
locaux sont exclus par une liste explicite de sources.

`release.py publish` execute ces operations puis synchronise les sources dans un
clone de publication (`.distribution/repository`). La copie de developpement
d'origine n'est ni deplacee ni transformee en depot Git. Une Release brouillon est
completee avant de devenir publique. Le compte du joueur n'intervient jamais.

## Publication a la fin des modifications accompagnees

La consigne du proprietaire est enregistree dans `AGENTS.md` a la racine : apres
chaque modification du jeu terminee et validee avec l'assistant, celui-ci lance
la publication et communique le lien de la nouvelle version. Cela ne necessite
pas de cliquer manuellement sur le script. Les simples questions et changements
de documentation ne declenchent pas une nouvelle version du jeu. Aucune
surveillance periodique des fichiers n'est configuree.

## Mise a jour

Le manifeste public `empire-manifest.json` contient les empreintes SHA-256 et
tailles de tous les fichiers, l'archive complete et le patch depuis la version
precedente. Le patch contient les fichiers nouveaux ou modifies. Les fichiers
retires sont deduits du manifeste. Un joueur ayant saute des versions recoit une
chaine de patches ; si elle manque, depasse 30 versions, coute plus que l'archive
complete ou si l'installation est endommagee, le lanceur utilise l'archive complete.

Tous les telechargements passent par HTTPS sur les Releases de ce depot et sont
verifies avant extraction. L'extraction refuse les chemins sortant du dossier,
les liens, les doublons et les fichiers qui ne figurent pas au manifeste.

Le jeu actif n'est jamais modifie : la nouvelle version est preparee dans un
dossier temporaire, verifiee puis activee par remplacement atomique de
`current.json`. Une interruption laisse la version courante intacte. Les anciennes
versions restent presentes pour le retour arriere ; elles peuvent occuper de
l'espace disque. Ne supprimez pas la version courante ni celle indiquee dans
`previous` de `current.json`.

Le petit `Empire.exe` utilise .NET Framework fourni avec Windows pour lancer le
lanceur compile du dossier actif. Le lanceur fait donc lui aussi partie des
mises a jour. Le format de ce point d'entree est stable (schema 1).

## Reprise d'une publication

Si les archives sont deja construites et verifiees :

```powershell
py -3 distribution/release.py publish --prepared .distribution/build-1.0.1-IDENTIFIANT
```

Les sources et archives sont reverifiees avant l'envoi. Si une autre version est
devenue publique entre-temps, reconstruisez avec un nouveau numero. Une version
publique est immutable. Une Release brouillon peut etre completee et publiee.

## Tests

```powershell
powershell -NoProfile -File distribution/tests/Test-Updater.ps1
```

Les scenarios utilisent de petites installations temporaires et simulent les
telechargements : patch, suppression de fichier obsolete, versions sautees,
telechargement coupe, empreinte incorrecte, ZIP malveillant, installation abimee,
retour arriere et conservation des donnees personnelles. Aucun profil reel n'est
utilise. La verification GitHub Actions execute aussi ces tests sous Windows.

## Premiere execution du jeu

Le paquet contient le moteur, ses bibliotheques natives, .NET et les mods utiles.
Les ressources originales Red Alert sont installees par le gestionnaire OpenRA
lorsqu'elles manquent. Les tests locaux des regles utilisent le contenu deja
installe sur la machine de developpement. La compilation ne copie jamais ce
contenu personnel dans le paquet.
