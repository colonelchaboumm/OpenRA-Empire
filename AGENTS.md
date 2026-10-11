# OpenRA Empire — publication des mises à jour

## Consigne permanente du propriétaire

L'utilisateur a demandé le 11 octobre 2026 de publier sur GitHub après chaque
modification du jeu que nous terminons et validons ensemble. Cette autorisation
vaut pour les prochaines tâches du projet, sauf instruction contraire explicite.
Il ne demande pas de surveillance périodique des modifications manuelles.

## Fin d'une modification du jeu

- Après une modification terminée du moteur, des règles, des cartes, des
  graphismes, de l'audio ou du lanceur distribué, effectuer les vérifications
  adaptées puis publier une seule nouvelle version avant la réponse finale.
- Utiliser depuis la racine `py -3 distribution/release.py publish`. Ce script
  exécute les tests, construit le paquet Windows autonome, incrémente la version,
  publie les sources et prépare le patch pour les joueurs. Ne pas remplacer une
  version déjà publique. Le dépôt autorisé est
  `https://github.com/colonelchaboumm/OpenRA-Empire`.
- Inclure des notes de mise à jour en français décrivant les changements livrés :
  écrire un fichier dans `.distribution`, puis ajouter `--notes CHEMIN` à la
  commande de publication.
- Vérifier que la Release est publique, que les fichiers sont disponibles et que
  les contrôles GitHub associés réussissent. Donner le numéro et le lien de la
  version publiée dans la réponse finale. Ne pas redemander systématiquement
  l'autorisation de publier : elle a déjà été donnée.
- En cas d'échec, corriger dans le périmètre de la tâche et reprendre la
  publication ; si un blocage persiste, expliquer ce qui manque sans prétendre
  que la nouvelle version est disponible.

## Périmètre

Une question, une analyse sans modification ou une modification uniquement de
documentation ou de consignes ne nécessite pas de nouvelle version du jeu.
Ne pas publier pendant une modification inachevée ni lancer plusieurs
publications simultanément. Si une autre tâche modifie encore des fichiers
inclus dans le paquet, coordonner la fin des changements avant de figer la
version à publier. Ne jamais inclure de profils, sauvegardes, identifiants ou
journaux personnels dans une Release.

La procédure détaillée se trouve dans `distribution/README.md`. La copie Git de
publication est `.distribution/repository` ; la racine de développement peut
ne pas être un dépôt Git.
