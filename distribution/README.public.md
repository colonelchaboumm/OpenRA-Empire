# OpenRA Empire — Les Terres du Bourg

Jeu de strategie medievale construit sur OpenRA : fondez votre bourg, developpez
les chaines de production et commandez vos troupes.

## Jouer sous Windows 64 bits

1. Ouvrez [la derniere version](https://github.com/colonelchaboumm/OpenRA-Empire/releases/latest).
2. Telechargez **OpenRA-Empire-…-Windows-x64.zip**.
3. Extrayez **tout** le ZIP dans un dossier de votre choix, par exemple Documents.
4. Double-cliquez sur **Empire.exe**. Vous pouvez creer un raccourci vers ce fichier.

Le lanceur recherche les mises a jour et propose **Mettre a jour et jouer**.
Une installation existante reste jouable sans connexion. .NET est inclus dans
le jeu ; aucun SDK, Git ou compte GitHub n'est necessaire pour les joueurs.
Au premier lancement, le gestionnaire OpenRA peut demander de telecharger les
ressources Red Alert necessaires. Elles ne sont pas redistribuees dans ce depot.

Les reglages et sauvegardes sont stockes par OpenRA en dehors des versions du jeu
(habituellement `%APPDATA%\OpenRA`, ou l'ancien dossier Documents\OpenRA).
Une mise a jour ne les supprime pas. Une evolution du jeu peut cependant rendre
une ancienne sauvegarde incompatible. **Revenir a la version precedente** permet
de relancer l'ancienne version. Tous les joueurs doivent utiliser la meme version
pour une partie multijoueur.

Les autres fichiers des Releases (`full`, `patch`, manifeste, sources) servent au
lanceur ou aux developpeurs. Pour jouer, seul le ZIP Windows est necessaire.

## Construire et publier

Le SDK .NET 10, Python 3.11 ou plus recent, Git et GitHub CLI sont necessaires.
Pour publier dans ce depot, connectez GitHub CLI avec un compte ayant l'acces
en ecriture : `gh auth login`.

```powershell
# Tester et fabriquer les archives localement
py -3 distribution/release.py build --version 1.0.1

# Tester, construire puis publier la prochaine version
py -3 distribution/release.py publish

# Version et notes explicites
py -3 distribution/release.py publish --version 1.1.0 --notes nouveautes.md
```

Sous Windows, **Publier-une-mise-a-jour.cmd** lance le meme processus avec un
numero de version incremente automatiquement. Un snapshot des sources est pris
avant compilation, les tests sont executes, puis les sources et les archives
sont publiees. Aucune version publique existante n'est remplacee.

Consultez [le guide de distribution](distribution/README.md) pour les details,
la reprise apres erreur et les verifications.

## Licence et credits

Moteur OpenRA et code distribues sous **GPL-3.0-or-later** : voir [COPYING](COPYING)
et [AUTHORS](AUTHORS). Les sources correspondant a chaque version sont incluses
dans les Releases. Les ressources des jeux Command & Conquer d'origine restent
soumises a leurs propres conditions et sont gerees par l'installateur OpenRA.
Les credits et provenances des illustrations du mod se trouvent dans
`mods/medieval/art` et `mods/medieval/art-source`.

[Documentation OpenRA d'origine](README.OpenRA.md) ·
[Guide du mod](mods/medieval/README.md)
