# Les Terres du Bourg — prototype jouable OpenRA

Le mod intègre une économie médiévale physique dans le moteur OpenRA de ce dossier. Le joueur développe les chaînes de nourriture, viande, bois, pierre et or, recrute les métiers économiques, fabrique équipements et tentes, forme des combattants et des unités de soutien, pose des chantiers et agrandit les exploitations. Des marchés approvisionnés et des charrettes assurent le ravitaillement à proximité. Les règles et les sprites de ce prototype sont propres au mod ; le moteur, des éléments d’interface et les contenus de base proviennent de Red Alert.

## Or, église et prêtres

Le clocher s’anime et sonne à la fin de la construction, au recrutement d’un prêtre, à l’aube et à la tombée de la nuit. Les cloches en bronze et leurs battants oscillent nettement derrière les arches, en rythme avec le son. La croix dorée du toit porte un reflet doux et un scintillement occasionnel. Les vitraux prennent une lueur chaude la nuit et pendant les soins. Le prêtre se tourne vers le blessé et enchaîne seize poses dans huit directions : lecture du livre, levée de la croix, bénédiction et retour au repos. Une petite croix lumineuse montante et une lueur au sol signalent les soins reçus. Ces gestes s’arrêtent après le rétablissement. À la mine d’or, les livraisons déclenchent le treuil, le tri du minerai, quelques poussières et leurs bruitages. Les sons restent locaux et respectent les réglages audio et la pause.

Dans **Ressources**, construire une **mine d’or** (70 bois, 30 pierre), puis recruter un **mineur d’or** dans **Citoyens** (seconde page, 10 nourriture). Les gisements aurifères sont répartis à proximité des groupes de pierre au lancement des cartes. Chaque gisement contient 300 or : les mineurs travaillent à la pioche comme les tailleurs de pierre, portent leur récolte à la mine et les transporteurs livrent ensuite les réserves. Les roches se réduisent puis disparaissent lorsqu’elles sont épuisées.

L’or figure dans le compteur, les stocks des dépôts et marchés, les aires de stockage extérieures, les chargements de charrettes et les exportations des routes commerciales. Les représentations extérieures montrent le stock du bâtiment : elles ne créent aucune ressource supplémentaire. Les bâtisseurs transportent aussi l’or jusqu’aux chantiers, avec approvisionnement possible par convoi.

Dans **Ville**, l’**église** coûte **120 bois, 100 pierre et 60 or**. Sa croix au sommet du clocher est dorée. Sélectionner l’église terminée pour recruter jusqu’à **trois prêtres**, à **30 nourriture** chacun. Chaque prêtre occupe une place de logement, mange et dort comme un citoyen. À proximité, un prêtre éveillé et nourri apporte **+10 moral** aux troupes du même joueur dans un rayon de **5 cases** ; plusieurs prêtres ne cumulent pas ce bonus.

Pour soigner, sélectionner les citoyens ou soldats blessés et faire un **clic droit sur l’église**. Ils rejoignent son pourtour et attendent leur tour. Un prêtre affecté à cette église, présent à moins de 4 cases, éveillé et nourri soigne jusqu’à **trois blessés à la fois**, à **5 % de santé maximale par seconde**. Sans prêtre disponible, les soins attendent. Une unité rétablie reprend son activité ; un ordre de déplacement interrompt les soins. Les engins de siège et la caravane de fondation ne sont pas réparés par les prêtres.

## Lancer

Double-cliquer sur **`launch-medieval.cmd` à la racine du projet**. Le lanceur ouvre l’accueil médiéval en fenêtre 1280 × 800. Choisir **Jouer en solo → Fonder mon premier bourg** pour partir avec une caravane, ou **Choisir une région** pour sélectionner une carte dans le salon. **Multijoueur** permet de créer ou rejoindre une partie. Le **Guide du bourg** explique les ressources, les habitants, l’armée et le réseau. Le SDK .NET 10 nécessaire est installé localement dans `.tools/dotnet` sur cette copie ; il ne remplace pas le SDK système. Les fichiers compilés du mod sont dans `bin-medieval`. Fermer la partie précédente puis relancer pour charger une mise à jour.

L’accueil, le chargement, le salon et les panneaux utilisent une illustration du bourg et un thème vert sombre et or. Les principaux menus sont en français ; certains réglages avancés et outils du moteur restent en anglais. Le choix du nom, des couleurs et de l’emblème du pays se fait depuis le marché, en partie. Il n’y a pas de musique : les réglages audio concernent l’ambiance et les bruitages. Le solo reste un développement libre sans IA médiévale adverse ; la victoire automatique par conquête est encore désactivée.

L’illustration originale et son prompt se trouvent dans `art/menu/provenance.md`. Le générateur `tools/frontend_content.py` conserve les menus lors de la régénération du contenu. Le parcours natif des menus se vérifie avec `tools/audit_frontend.py` sur une compilation de test ; il utilise des profils isolés et ne touche pas aux préférences ni aux sauvegardes habituelles.

Le contenu Red Alert doit être installé dans le répertoire de contenu OpenRA. Il est déjà présent sur cette machine. Sur une autre machine, lancer le gestionnaire de contenu Red Alert d’OpenRA avant ce mod. Les ressources du jeu d’origine ne sont pas recopiées dans ce mod.

## Jouer

### Saisons et chasse

Les fenêtres des **six styles de maisons** s’éclairent progressivement au crépuscule et s’éteignent à l’aube. Leur lumière chaude suit les fenêtres de chaque façade, avec une légère variation d’intensité. Les chantiers restent éteints ; les lumières respectent le brouillard de guerre. Cet éclairage est visuel et ne consomme aucune ressource.

La **cabane du chasseur** possède une nouvelle façade en bois, un toit de bardeaux, un porche et une enseigne à bois de cerf, avec arc, carquois, bûches et peau sur séchoir. Son icône, sa cheminée et la neige sur le toit suivent le nouveau dessin. Sa surface reste de 2 × 2 cases et ses coûts sont conservés. Illustration source et prompts : `art-source/huntinglodge-painted.png`, `art-source/huntinglodge-prompts.json` (outil intégré `image_gen`). Voir les [captures et vérifications](../../analysis/medieval/lodge-lights/rapport.md).

Une nouvelle partie commence au **printemps**. Printemps, été, automne et hiver durent chacun **30 minutes de simulation**, soit deux heures par année et trois cycles jour/nuit par saison à vitesse normale. La pause suspend cette progression. La saison, l’année, le temps restant et la météo sont affichés en haut de l’écran.

La croissance et le rythme de travail agricole suivent les saisons : printemps 100 %, été 115 %, automne 75 %, hiver 20 %. Ce sont des vitesses relatives, pas des ressources gratuites. Les réserves déjà récoltées restent disponibles. Les bûcherons et tailleurs de pierre travaillent à 75 % en hiver ; les chasseurs à 90 % en automne et 65 % en hiver.

Les déplacements ralentissent de 5 % en automne et 20 % en hiver, avec jusqu’à 10 points supplémentaires pendant les précipitations. En hiver, la puissance de combat diminue de 10 % ; les archers subissent jusqu’à 15 points supplémentaires sous les intempéries. Le froid augmente la fatigue des soldats exposés. Le repos dans un pavillon protège du froid, de la pluie et de la neige. Ces effets se combinent aux règles existantes de faim, moral et fatigue.

L’automne colore le sol et les feuillages ; l’hiver remplace la pluie par des flocons et accumule de la neige, qui fond au printemps. Les citoyens, soldats, animaux et charrettes laissent des empreintes ou des sillons. Les traces s’effacent, plus vite pendant une chute de neige. Elles sont visuelles, plafonnées et limitées aux mouvements observés : elles ne révèlent pas les unités cachées par le brouillard.

La neige s’accumule aussi sur les toitures des six maisons, entrepôts, ateliers, fermes, élevages, marchés, cabanes et pavillons. Des bourrelets et de petits glaçons soulignent les bords sans masquer entièrement les textures. Les clôtures, palissades et portes reçoivent des chapeaux de neige ; ceux des portes suivent leurs battants. Les enclos de taille variable ont également leur sol et leurs barrières enneigés. La fonte suit celle du sol au printemps.

Les averses fortes peuvent devenir des **orages** : ciel assombri, éclairs ramifiés et brève illumination du paysage, puis tonnerre décalé de 0,5 à 2,2 secondes. Un habitant visible et éveillé peut réagir par une courte exclamation française. Ces réactions sont espacées et partagent la limite des autres voix. La foudre ne cause pas de dégâts. La neige hivernale remplace les orages.

Au **tonnerre**, les citoyens interrompent leur travail et courent vers une maison disponible à 165 % de leur vitesse habituelle, en conservant les ralentissements saisonniers. Ils privilégient leur dernière maison, respectent ses places disponibles et restent à l’intérieur jusqu’à huit secondes d’accalmie. Ils reprennent ensuite leur travail, avec leur chargement conservé ; les transferts d’animaux interrompus restent en attente. Sans maison accessible ou place libre, ils restent dehors et réessaient. Les soldats restent à leur poste et les secours incendie ont priorité. Un ordre manuel ou une sortie demandée depuis la maison garde la priorité pendant trente secondes. Le refuge dépend de la simulation : il fonctionne aussi avec le son coupé et en multijoueur. Voir les [vérifications du refuge et des enclos enneigés](../../analysis/medieval/storm-refuge/rapport.md).

Sur un sol enneigé, les déplacements déclenchent quatre variantes de pas craquants et deux variantes de roues dans la neige. Le tonnerre dispose de trois variantes et les habitants de trois exclamations. Tous suivent le réglage des effets sonores, la mise en sourdine et la pause ; aucune musique n’est ajoutée. Détails et captures : [neige et orages](../../analysis/medieval/weather-polish/rapport.md).

**Nourriture → Chasse** construit la **cabane du chasseur** (2 × 2 cases, 55 bois et 10 pierre). **Citoyens → Suivant → Chasseur** recrute un chasseur pour 10 nourriture. Il occupe un logement, mange et dort comme les autres citoyens. Il recherche automatiquement le gibier proche d’une cabane terminée, chasse à l’arc puis rapporte physiquement sa nourriture à la cabane. Les transporteurs l’emportent vers les entrepôts et marchés. La cabane contient au maximum 100 nourriture ; un stock plein suspend la chasse.

Les cerfs et lapins sont désormais des acteurs synchronisés et sélectionnables : 24 nourriture pour un cerf et 6 pour un lapin. Ils se déplacent dans les bois et fuient les citoyens proches. Leur population initiale dépend des secteurs forestiers, avec un plafond de 600 animaux sur un continent ; ils ne réapparaissent pas après récolte. Les oiseaux et le petit décor végétal restent de l’ambiance. Les nouvelles règles s’appliquent aux nouvelles parties ; la compatibilité d’une sauvegarde produite avant cette mise à jour n’est pas garantie.

Sources : `Seasons.cs`, `Hunting.cs`, `seasons.yaml`, `tools/seasons_content.py`. Le scénario `medieval-seasons` accélère les saisons uniquement pour contrôler un cycle complet, une construction et un recrutement par ordres, la chasse, les livraisons, la conservation de nourriture et les empreintes. Voir le [rapport des vérifications et de l’installation](../../analysis/medieval/seasons-hunting/rapport.md).

### Menu de construction par catégories

Le panneau de droite comporte six onglets, avec les icônes et le coût de chaque choix :

- **Ville** : maison, entrepôt, grand marché et petit marché.
- **Nourriture** : ferme, cultures, élevage bovin, enclos bovin, bergerie, enclos de moutons et cabane du chasseur.
- **Ressources** : scierie et carrière.
- **Militaire** : caserne, forge, atelier de siège, tisseur de tentes, palissade, porte et tour en bois.
- **Décorations** : puits, clôture, portillon, fleurs, drapeau, chemin de pierre, torche, deux places pavées, deux statues et deux arbres sur pavés. Les boutons **Suivant / Précédent** donnent accès aux deux pages.
- **Citoyens** : bâtisseur, transporteur, bûcheron, tailleur de pierre, fermier, éleveur bovin, berger, tisseur et chasseur (page suivante). Les forgerons se recrutent depuis la forge et les soldats depuis la caserne.

**Tourner** reste disponible pour les portes et clôtures ; **Retirer décor** se trouve dans Décorations. Les zones agricoles se tracent toujours en deux clics. Passer à un autre onglet quitte l'outil de pose actif.

La bergerie utilise une chaumière à colombages et soubassement de pierre, avec un abri latéral. Les tentes déployées sont des **pavillons médiévaux à toit pointu**, toile rayée, rideaux et fanion animés. La couleur des bandes et du fanion suit celle du pays, y compris après un changement au marché. L'emprise reste de 2 × 2 cases et la capacité de six soldats.

Sources : `ConstructionMenu.cs`, `tools/construction_menu_content.py`, `tools/camping_art.py`. La source de la bergerie, générée avec l'outil intégré `image_gen`, est `art-source/sheepfold-isometric.png` ; ses prompts et sa provenance sont consignés dans `art-source/sheepfold-generation.json`. La génération du contenu reconstruit les 96 emplacements d'icônes à partir du catalogue C#.

Le scénario natif `medieval-construction-menu`, sur `village`, vérifie les 42 choix, leurs textes, les pages, les rotations, le placement des bâtiments, l'enclos variable et les recrutements. Dernier résultat : `analysis/medieval/seasons-hunting/checks/construction-menu/`.

### Bergerie et tisseur animés

Le **tisseur de tentes** possède maintenant un atelier à colombages, toiture d'ardoise, soubassement de pierre, rouleaux de toile et enseigne de pavillon. Sa façade ouverte montre un métier à tisser avec une navette qui traverse les fils, un cadre mobile, une pédale, une manivelle et les gestes de l'artisan. Le métier fonctionne uniquement lorsque le tisseur affecté travaille réellement à la fabrication ; il s'arrête pendant les trajets, le manque de matériaux, le repos ou après l'atteinte du stock cible.

Les deux moutons visibles dans la **bergerie** respirent, tournent la tête, clignent des yeux et remuent les oreilles à des rythmes différents. Ils restent calmes la nuit et reprennent leur animation au matin. Ce sont les moutons décoratifs du bâtiment ; la capacité et la production des enclos restent liées aux animaux des zones d'élevage.

Chaque bâtiment dispose de **32 poses animées**, d'une pose au repos et de sa construction. Les murs et toitures restent parfaitement fixes. La pause fige les animations, qui n'ajoutent ni ressources ni citoyens. Les icônes du menu ont également été mises à jour.

Sources graphiques réalisées avec l'outil intégré `image_gen` : `art-source/tentweaver-isometric.png` et `art-source/sheepfold-animation-base.png`. Prompts : `art-source/camp-workshops-generation.json`. Les éléments articulés et l'assemblage des images sont dans `tools/camp_building_art.py`, appelé par `tools/camping_art.py`. Aperçu animé : `analysis/medieval/camp-buildings/animation.gif`.

### Jour, nuit, moutons et campements

À vitesse normale, le cycle dure **8 minutes de jour puis 2 minutes de nuit**, avec une transition progressive de la lumière. Le compteur en haut de l’écran indique le temps restant et la météo. La pluie utilise désormais un calendrier commun à la simulation ; les gouttes restent un effet local à la caméra.

La nuit, les ouvriers rejoignent une maison terminée disponible, puis y dorment : 8 lits par maison, 12 places de secours dans l’entrepôt et 3 au marché fondateur. Les lits sont réservés pendant le trajet. Les charges transportées, les matériaux livrés et le travail déjà accompli sont conservés. Le matin, le travail automatique reprend. Les dormeurs continuent à occuper leur logement et à consommer de la nourriture : leurs maisons doivent donc rester ravitaillées. Un ordre manuel permet une intervention nocturne ; le retour automatique au lit est alors différé de 30 secondes.

Les constructions se trouvent dans leurs onglets ; le bouton **Campements & repos** regroupe le stock cible, le chargement, le déploiement et le repos :

1. Dans **Nourriture**, construire une **bergerie** (70 bois, 15 pierre), tracer ses enclos de 2 à 10 cases par côté, puis recruter des **bergers** dans **Citoyens** (10 nourriture chacun, un logement). Un mouton par 4 cases, avec un minimum d’un ; un éleveur par 8 cases, arrondi au supérieur. La tonte fournit la laine, les moutons restent dans leur enclos.
2. Dans **Militaire**, construire le **tisseur de tentes militaires** (80 bois, 20 pierre) et recruter son **tisseur** dans **Citoyens** (10 nourriture, un logement). Il apporte physiquement **8 laine et 4 bois** par tente. Le stock cible se règle entre 0 et 20 en sélectionnant le bâtiment et en ouvrant **Campements & repos** ; les tentes en cours de transport comptent aussi dans cet objectif. Les transporteurs et le tisseur livrent les stocks aux dépôts ou marchés.
3. Sélectionner une **charrette de ravitaillement dans la zone d’un grand marché**, ouvrir ce menu et cocher **Charger des tentes**. Les transporteurs lui apportent jusqu’à **4 tentes**, en complément de ses autres chargements.
4. Recruter un **commandant à la caserne** : 40 nourriture, 15 viande, une épée et une armure, apportées par le recruteur. Il occupe un logement et consomme deux unités de nourriture par minute. Arrêter une charrette chargée à moins de 8 cases de lui, sélectionner le commandant, puis cliquer **Installer un pavillon depuis la charrette** et choisir un terrain libre à moins de 5 cases de celle-ci. Il rejoint la charrette, prend réellement une tente, la porte puis monte le pavillon sur place avec une animation de marteau. Chaque pavillon offre **6 lits**, sans logements permanents supplémentaires.
5. Chaque pavillon montre un **cercle de 6 cases de rayon**, doré tant que le repos n’a pas été ordonné. Sélectionner le commandant et cliquer **Repos du camp** : les pavillons du pays à moins de 12 cases de lui passent au vert. Les soldats dans leurs cercles rejoignent un lit disponible ; les équipages concernés quittent leur engin. Les soldats inactifs arrivant ensuite dans un cercle vert rejoignent aussi le camp, selon les places libres. Deux cercles superposés ne doublent pas les lits. Les ennemis et les citoyens ne prennent pas ces places militaires.
6. **Fin du repos**, depuis le commandant, désactive les cercles des pavillons à portée et réveille leurs occupants. Les soldats attendent cet ordre même après récupération complète ; la nuit seule ne les envoie plus dans un camp inactif. Un ordre individuel de déplacement ou de combat interrompt toujours le repos et diffère le retour automatique de 30 secondes. Si le commandant dort, sélectionner son pavillon, ouvrir **Campements & repos**, puis **Sélectionner le commandant** pour retrouver ses commandes.
7. Pour récupérer le campement, garer une charrette contre une tente, sélectionner celle-ci et cliquer **Replier le pavillon sélectionné**. Les occupants sortent avant le rangement ; une seule tente revient au stock. Un emplacement libre dans la charrette est nécessaire. Les maisons conservent leur commande **Réveiller les occupants**.

Un montage interrompu avant le chargement libère la réservation dans la charrette. Après le chargement, le commandant garde sa tente et peut reprendre le placement à moins de 8 cases de lui. Un emplacement devenu occupé n’efface pas cette tente. Plusieurs commandants ne peuvent pas prendre la même tente. La mort du porteur fait perdre son chargement, comme pour les autres unités de transport. Le commandant porte une cape aux couleurs du pays, une armure et un casque à plumet ; il conserve les mécanismes de faim, de fatigue et de moral des soldats.

La marche, la nuit et la pluie font monter la fatigue militaire. À fatigue maximale, la vitesse descend à 65 % et les dégâts à 75 %, avant les autres effets de faim et de moral. Une tente protège de la pluie et restaure 4 points de fatigue par seconde ; les soldats au repos en plein air récupèrent lentement lorsqu’il fait jour et sec. Un déplacement ou un ordre de combat interrompt le repos. La fatigue ne tue pas directement.

Les **torches décoratives** (5 bois, 1 pierre), accessibles aussi depuis **Décorations**, illuminent leurs alentours au crépuscule et la nuit. Les **tambours et porte-drapeaux portent également un flambeau**. L’éclairage ne révèle pas le brouillard de guerre. Les stocks de laine et de tentes sont visibles près des bâtiments et dans les charrettes ; les compteurs sélectionnés indiquent leurs quantités exactes.

Sources : `camping.yaml`, `commander.yaml`, `sheep-enclosures.yaml`, `tools/camping_art.py`, `tools/camping_content.py`, `tools/commander_content.py`, et les fichiers C# `DayNight`, `Rest`, `CampGoods`, `Camping`, `Commander`, `CampInterface`. Le scénario `medieval-camping` utilise un cycle accéléré réservé aux tests et produit son rapport et ses captures dans `analysis/medieval/camping/`. Le scénario `medieval-commander` vérifie le recrutement et ses livraisons, l’attente de viande, les interruptions avant et après chargement, la concurrence entre commandants, les lits et les commandes de repos dans `analysis/medieval/commander/`. Les valeurs normales restent 8 minutes / 2 minutes dans le mod jouable.

### Paysages vivants et averses

Les cartes accueillent maintenant des **lapins et des cerfs animés dans huit directions**. Les lapins bondissent puis s'arrêtent ; les cerfs marchent, broutent et partent au galop lorsqu'un citoyen ou un soldat approche. Ils restent sur la terre ferme et contournent les emprises des bâtiments, cultures, enclos et stocks extérieurs. Cette faune sert à l'ambiance du paysage.

Des **fougères, herbes hautes, fleurs sauvages, mousses et petits champignons** enrichissent les clairières. Ces touffes suivent le vent, s'effacent sous les constructions et laissent apparaître les sentiers usés par les habitants. Les oiseaux sont plus grands, avec des ailes articulées, des plumes visibles, des phases de vol plané et des ombres mobiles.

Des **averses automatiques** apparaissent progressivement : premières gouttes après environ **44 secondes** à vitesse normale, puis épisodes de quelques dizaines de secondes, séparés par des éclaircies d'environ deux minutes. Le vent incline les gouttes, des éclaboussures apparaissent au sol et des rides se forment sur l'eau. Le paysage s'assombrit légèrement ; le volume de la pluie suit son intensité et les réglages audio. La pause suspend les animations et coupe le son de pluie.

Le décor est calculé autour de la caméra, avec au maximum **16 animaux, 360 touffes supplémentaires, 8 oiseaux, 72 particules végétales et 440 gouttes**. Le terrain existant, la nourriture et les ressources restent gérés par les systèmes économiques habituels. Les nouveaux sprites et leurs règles sont conservés par `tools/wilderness_art.py`, `tools/wilderness_content.py` et `wilderness.yaml` lors de la régénération.

Validation : **133 tests C#**, **242 planches graphiques**, **56 fichiers sonores**, et deux scénarios natifs de 1 600 ticks sur le village et la carte de **512 × 1024 cases**. Les contrôles couvrent la course, la fuite, les obstacles, les déplacements de caméra, les limites de population visuelle et les cycles pluie/éclaircie. Captures et rapports : `analysis/medieval/wilderness/`.

### Nom du pays, emblème, couleurs et porte-drapeau

Sélectionner un **marché terminé**, grand marché fondateur compris, ou un **petit marché**, puis cliquer **Pays, emblème & couleur**. Saisir le **nom du pays** (32 caractères maximum), choisir **Lion, Aigle, Soleil, Tour, Cerf ou Fleur de lys**, sélectionner une couleur proposée ou saisir son code **#RRGGBB**, puis **Appliquer à l'empire**. Le panneau montre un aperçu avant validation. Le nom, l'emblème et la couleur sont appliqués ensemble à tout l'empire ; chaque joueur conserve son propre choix. Le nom est modifiable depuis n'importe lequel de vos marchés et reste distinct des noms des villes et villages. Les noms vides sont refusés. L'identité commence avec le nom « Mon pays », la couleur du salon et l'emblème du lion.

Les **drapeaux décoratifs et les étendards portés** reprennent l'emblème et la couleur. Les vêtements des citoyens, les tuniques, les tabards et bandes de bouclier des soldats en armure, ainsi que les toiles des grands et petits marchés, utilisent la couleur choisie. Le changement affecte les acteurs existants et les futurs recrutements/constructions. Les textures, ombres, peaux, outils, bois et métal nu sont conservés. Les couleurs du radar et des relations diplomatiques restent celles du moteur.

La caserne peut former un **porte-drapeau pour 25 nourriture**, sans arme ni armure, après l'arrivée physique des provisions apportées par le recruteur. C'est une unité de soutien sans attaque, qui occupe **un logement** et consomme **deux nourritures par minute**. Elle marche dans huit directions et porte un étendard animé.

- Dans un rayon de **cinq cases**, son étendard apporte **+15 de moral aux alliés**. Plusieurs porte-drapeaux ne cumulent pas ce bonus.
- Le soutien se combine avec le tambour, avec un **plafond global de +30**. Le tambour conserve son effet d'intimidation habituel ; le porte-drapeau n'intimide pas les ennemis.
- La faim réduit son bonus à **+7** ; la famine le supprime. Quitter la portée ou mourir supprime le soutien, puis le moral des troupes évolue progressivement selon les règles habituelles.
- L'identité visuelle ne change ni les coûts, ni les statistiques, ni les alliances. Les drapeaux décoratifs restent des décorations.

Les **charrettes de ravitaillement, de commerce et de pierres**, ainsi que la **caravane de départ**, portent aussi la couleur du pays : tissus bordés d’or sur les caisses, bandes colorées sur la bâche de la caravane et **petit drapeau animé avec l’emblème national**. Les attaches suivent leurs huit directions et le mouvement du véhicule. Changer l’identité au marché actualise immédiatement les véhicules existants et futurs ; un changement de propriétaire adopte l’identité du nouveau pays. Les étiquettes de chargement restent au-dessus des fanions. Ces ornements ne donnent aucun bonus de moral et ne modifient pas le chargement.

Les couleurs utilisent des palettes GPU partagées par joueur et mises à jour seulement après un changement. Les commandes d'identité sont synchronisées et vérifient que le marché appartient au joueur qui les envoie. Les règles, les six emblèmes et le panneau sont conservés par `heraldry.yaml`, `tools/heraldry_art.py` et `tools/heraldry_content.py` lors de la régénération du mod.

Validation : **133 tests C#** et **242 planches graphiques**. Le scénario natif `medieval-heraldry` utilise les vrais boutons, recrute avec transport de nourriture, vérifie logement et repas, noms et couleurs distincts de deux propriétaires, renommage du pays depuis un petit marché, conservation du nom de ville, refus des noms vides et des ordres sur un marché adverse, actualisation des drapeaux existants, palettes effectivement employées par le rendu, cumul du soutien, changement de propriétaire, sortie de portée et mort. Rapport et captures : `analysis/medieval/heraldry/`. Ce scénario local vérifie les ordres et la synchronisation du moteur ; il ne remplace pas un essai réseau entre deux ordinateurs.

### Fonder sa première ville

Chaque joueur commence avec **une caravane : 280 bois, 120 nourriture, 100 pierre et aucune viande**. Elle abrite deux bâtisseurs et un transporteur, soit trois habitants qui consomment ensemble six nourritures par minute pendant le voyage. Elle ravitaille aussi les personnes à proximité avec ses provisions réelles.

Déplacer la caravane vers un terrain libre, la sélectionner puis cliquer **Fonder le marché**. Les six cases de son emprise de **3 × 2** deviennent vertes lorsque l'installation est possible ; il faut également de la place autour pour les trois fondateurs. Le bouton de déploiement habituel fonctionne aussi. L'installation prend trois secondes à vitesse normale ; Stop ou un déplacement l'interrompt.

Le marché reçoit uniquement les ressources encore présentes dans la caravane. Les trois fondateurs apparaissent à côté et commencent leur travail. Les blessures et la faim sont conservées ; le déploiement ne duplique pas les stocks. Le camp du marché offre trois logements et le marché peut être nommé et relié aux routes commerciales comme les autres.

**Construire d'abord un entrepôt**, qui ouvre le recrutement et apporte douze logements. Construire ensuite une scierie et recruter un bûcheron, puis une carrière et un tailleur de pierre, une ferme avec sa zone de cultures et un fermier. Les réserves permettent d'amorcer ces chaînes sans production préalable de bois ni de pierre. Les cultures doivent ensuite fournir les repas et financer les recrutements ; la viande viendra de l'élevage.

Ce départ s'applique à la carte normale, aux trois cartes immenses et aux cartes à deux ou trois joueurs. L'ancien village préconstruit reste uniquement une carte de validation, masquée dans le salon. Les fichiers `founding.yaml`, `tools/caravan_art.py`, `tools/founding_start.py` et `tools/founding_content.py` conservent les règles, les sprites et les départs lors d'une régénération.

Le scénario natif `medieval-founding` vérifie un emplacement bloqué, l'annulation, le déplacement, les commandes répétées, le transfert exact des stocks et la construction d'un premier village avec récolte et livraison réelles. Rapport et captures : `analysis/medieval/founding/`.

### Villes, villages et noms des routes

Un **petit marché construit à l'écart des localités existantes fonde un village**, une fois le chantier terminé. Un marché situé dans la zone d'une localité existante la rejoint. Cette zone réunit les rayons de ses marchés : **16 cases pour un grand marché, 6 pour un petit marché**, même lorsqu'ils attendent des provisions. L'aperçu de construction indique si le marché rejoindra une localité ou en créera une nouvelle.

Sélectionner un marché puis saisir le **nom de la ville ou du village** dans le champ au-dessus de ses commandes. Valider avec **Renommer** ou Entrée. Le nom peut contenir 32 caractères ; les noms vides et les doublons parmi vos localités sont refusés. Les marchés d'une même localité partagent ce nom. Les noms restent distincts des bâtiments, des stocks et des routes : renommer pendant un transport conserve les marchandises, les réservations et la destination.

Le nom s'affiche sur la carte au-dessus du marché principal de la localité. Un village qui reçoit un grand marché dans sa zone devient une ville et conserve son nom. Si le marché fondateur disparaît, les autres marchés de cette localité conservent le nom ; une capture rattache le marché au registre de son nouveau propriétaire. Les localités déjà nommées ne fusionnent pas automatiquement lorsque leurs zones finissent par se recouvrir.

Le bouton **Villes & routes** ouvre deux listes avec pagination :

- **Localités** : villes et villages, avec leurs grands et petits marchés. **Voir** sélectionne le marché principal et centre la caméra pour le renommer ou le gérer.
- **Routes & relais** : liaisons commerciales entre grands marchés et approvisionnements des petits marchés, avec les noms des deux extrémités. **Voir** ouvre les commandes du marché concerné. Pour changer le fournisseur d'un village, sélectionner son petit marché puis **Choisir un autre grand marché**.

Les grands marchés utilisent toujours leurs charrettes marchandes ; les petits marchés reçoivent les livraisons physiques des transporteurs selon les besoins du voisinage. Les bâtiments et les habitants du nouveau village se développent avec les constructions et recrutements du joueur.

La suite compte désormais **133 tests C#**. Le scénario `medieval-localities` construit un village avec un vrai bâtisseur, vérifie les noms partagés, les doublons, la capture et la perte du marché fondateur, puis renomme une localité pendant un transport commercial réel. Il contrôle aussi la conservation des ressources et les boutons de la liste paginée. Captures et rapport : `analysis/medieval/locality-preview/`.

```powershell
& .\.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-localities Launch.Map=village Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.UIScale=1 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
```

### Messagers et correspondance

La caserne peut former un **messager pour 20 nourriture**, sans arme ni armure, après recrutement de son recruteur. Il occupe un logement et consomme deux unités de nourriture par minute, avec les mêmes règles de ravitaillement et de famine que les citoyens. Sa cape violette et sa sacoche le distinguent des soldats.

1. Sélectionner le messager **à côté d'une de vos casernes** et saisir un texte, jusqu'à 320 caractères.
2. Cliquer sur **Envoyer : choisir la caserne**, puis sur une caserne visible d'un autre joueur. Il rejoint physiquement cette caserne et affiche une enveloppe tant qu'il porte le courrier.
3. À l'arrivée, le destinataire reçoit une alerte et peut sélectionner le messager : **Lire le courrier**, puis **Répondre et renvoyer**, ou **Refuser et renvoyer** sans ouvrir la lettre. Après lecture, il peut aussi le renvoyer sans réponse.
4. Le même messager revient à sa caserne d'origine. Son propriétaire peut ouvrir la réponse à son retour, classer le courrier et préparer un nouvel envoi.

Le messager reste contrôlé par son propriétaire. **Stop** ou un déplacement manuel interrompt son trajet ; **Reprendre le trajet** ou **Rappeler le messager** permet de poursuivre sa mission ou de le faire rentrer. Une caserne destinataire détruite ou capturée déclenche son retour. Si sa caserne d'origine a disparu, il reste contrôlable avec son courrier et peut rejoindre une autre caserne.

Il est vulnérable : sélectionner un soldat et lui donner l'ordre d'attaquer le messager permet de le tuer, aussi bien devant la caserne que pendant son trajet, y compris pour un troisième joueur. Les défenses ennemies peuvent également l'attaquer. Sa mort détruit le courrier. Le contenu est masqué dans l'interface aux autres joueurs ; comme les autres données de simulation OpenRA, il est synchronisé entre les clients.

Pour jouer entre humains, lancer **`launch-medieval-multiplayer.cmd`**, ouvrir **Multijoueur**, créer une partie puis choisir **Deux Bourgs** (2 joueurs), **Trois Bourgs** (3 joueurs), ou **Les Terres sans Fin - 2 joueurs / 3 joueurs - immense**. Tous les emplacements de la carte doivent être occupés. Chaque joueur commence avec la même caravane et les mêmes provisions ; la caserne devra être construite et pourvue d'un recruteur. Les deux petites cartes sont explorées et sans brouillard ; les continents sont à explorer, avec les options de brouillard réglables dans le salon. Chaque participant doit utiliser les mêmes fichiers du mod, des cartes et du moteur. Le lanceur solo habituel reste disponible pour la carte d'apprentissage.

### Cartes aléatoires médiévales

Les forêts, gisements et plantes sont tirés au hasard **lors de la génération**, puis enregistrés dans la carte. Une même graine, avec la même version du générateur et de ses dépendances, reproduit exactement le même terrain ; les joueurs partagent ensuite ces fichiers. Lancer une nouvelle partie sur une carte existante ne change pas son paysage. Les petites cartes multijoueurs possèdent maintenant des ressources et de la végétation sur l'ensemble du terrain, avec des clairières pour construire. Sur les continents, un contrôle par secteur complète les zones pauvres en pierre sans supprimer les bosquets et les espaces ouverts. Les départs des continents multijoueurs ont les mêmes ressources proches.

Pour créer un autre continent, depuis la racine du projet, avec Python et Pillow installés :

```powershell
py mods/medieval/tools/generate_medieval_map.py --size 512 --players 3 --seed 12345
```

Tailles disponibles : `256`, `384`, `512` (la hauteur du fichier isométrique vaut deux fois la largeur). Joueurs : `1`, `2`, `3`. Changer `--seed` change le paysage. Le générateur refuse d'écraser un dossier existant. Copier le dossier créé dans `mods/medieval/maps/` chez **tous les participants**, puis relancer le jeu. Les générateurs Red Alert du salon ont été retirés car leurs minerais et leurs départs ne correspondent pas à ce mod.

Les cartes précédentes sont conservées dans `analysis/medieval/network-maps/before/`. Les cartes corrigées ont un nouvel identifiant : les anciennes replays/sauvegardes qui référencent un ancien identifiant peuvent nécessiter la restauration de leur carte d'origine.

Le bouton **Défenses**, à côté du nombre d’habitants, ouvre le catalogue des fortifications en bois. Les palissades se tracent avec deux clics : début puis fin, jusqu’à 64 cases par ligne. Le tracé suit l’axe isométrique dominant ; les angles, jonctions et extrémités se raccordent automatiquement. Retracer une portion existante ne crée ni doublon ni surcoût. Le prix total des nouveaux segments apparaît dans l’aperçu. Clic droit termine la pose.

| Ouvrage | Coût | Emprise | Fonction |
| --- | ---: | --- | --- |
| Palissade en bois | 8 bois par case | 1 × 1 | Barrière solide et destructible, 18 000 points de vie |
| Porte en bois | 30 bois | 3 × 1 ou 1 × 3 | Deux battants pivotent latéralement pour les alliés puis se referment après leur passage, 24 000 points de vie |
| Tour en bois | 100 bois | 2 × 2 | Défense automatique par flèches à 7 cases, vision à 9 cases, 45 000 points de vie |

**Laisser trois cases consécutives vides dans la palissade pour poser la porte.** Le bouton « Tourner la porte / palissade » change son axe de raccord et fait pivoter l’emprise de 3 × 1 à 1 × 3 ; les unités traversent perpendiculairement à cet axe. L’aperçu et le chantier couvrent les trois cases. Les deux battants pivotent sur leurs gonds à hauteur constante, dégageant un passage large pour les citoyens, soldats et charrettes. Les ennemis ne peuvent pas ouvrir une porte fermée. Une porte momentanément ouverte reste physiquement traversable. Les bâtisseurs transportent le bois depuis un dépôt ou une charrette à portée et réalisent le chantier. Une tour en chantier ne tire pas. Le cercle de portée apparaît à son placement et à sa sélection. Les flèches suivent un arc visible ; les armures réduisent leurs dégâts et une cible mobile peut les esquiver. Les tours sont des bâtiments de défense autonomes dans ce prototype, sans garnison de citoyens à affecter. Les chantiers peuvent être annulés avec récupération des matériaux déjà livrés.

Les palissades, portes à battants et flèches sont dessinées sur la grille du moteur par `tools/fortification_art.py`. L’illustration originale de la tour, produite avec l’outil intégré imagegen, est conservée dans `art-source/woodtower-isometric.png` ; son prompt et sa provenance figurent dans `art-source/woodtower-isometric.json`.

Le village utilise désormais une véritable grille **isométrique 2:1** (cases de 48 × 24 pixels). Les bâtiments, chemins, cultures, clôtures et aperçus suivent la même projection. Les zones restent des rectangles de cases, affichés en parallélogrammes à l’écran. Les arbres comprennent chênes, conifères et bouleaux ; les buissons, herbes et fleurs sont des décors traversables.

Le village commence sur l’herbe, sans routes rectilignes prétracées. Les déplacements des citoyens et soldats tassent progressivement le sol : les passages répétés font apparaître des sentiers de terre, avec des bords irréguliers et des embranchements correspondant aux trajets réels. Les charrettes marquent deux fois plus le terrain. Rester immobile ne crée aucune trace. Les bâtiments, cultures et enclos sont protégés. Après trois minutes sans passage, l’herbe commence à récupérer lentement (première réduction d’usure trente secondes plus tard). Ces sentiers sont visuels : ils ne coûtent aucune ressource et ne modifient pas la vitesse des unités.

Des prairies fleuries et des lisières plus fournies entourent le village. Les herbes, fleurs, buissons et arbres oscillent doucement, chacun avec un décalage différent, sous un vent qui change progressivement de force et de direction. Des pétales, graines et feuilles se détachent des végétaux visibles ; les rafales en emportent davantage. Des oiseaux passent seuls ou par petits groupes, avec des ailes animées et une ombre discrète au sol. Ces effets s’arrêtent lorsque le jeu est en pause. Les petites plantes restent traversables et disparaissent sous les nouvelles constructions ; les arbres coupés ne continuent pas à onduler ni à émettre des feuilles.

Les **transporteurs déposent directement dans les aires extérieures**, au tas correspondant à leur marchandise. Les **bâtisseurs y prennent leurs matériaux** avant de rejoindre le chantier ; les forgerons, recruteurs et tisseurs suivent aussi ce fonctionnement pour leurs stocks. Les emplacements se remplissent un à un, avec une priorité aux zones extérieures. Le surplus intérieur reste accessible au bâtiment.

Chaque prise ou dépôt comporte un geste d’environ **une seconde**, ralenti par la faim : l’ouvrier se tourne, se penche, prend ou pose sa charge puis se relève. La charge suit ses mains, les matériaux lourds soulèvent une légère poussière et un petit **− / +** indique la quantité transférée. **Le tas et l’inventaire changent au contact**, sans stock fictif pendant le trajet. Une charge répartie sur deux tas nécessite deux arrêts. Si la zone est déplacée avant le geste, l’ouvrier rejoint le nouvel emplacement ; si l’accès est bloqué, il conserve sa charge. Un arrêt manuel avant la prise ne retire rien ; après une prise partielle, les marchandises déjà portées restent sur l’ouvrier et les autres réservations sont libérées. Une zone en feu ne reçoit pas de manutention.

Les unités possèdent huit poses par cycle et huit directions. La marche suit la distance réellement parcourue, y compris lorsque la faim ralentit l’unité. Jambes, genoux, bras, outils et torse bougent séparément. Au travail, l’ouvrier se tourne vers sa cible : le bûcheron frappe à la hache, le tailleur attaque la pierre à la pioche, le bâtisseur frappe au marteau, le fermier prépare puis récolte le champ et l’éleveur nourrit ses bêtes. De petits éclats accompagnent les coups. Les charges sont tenues devant le corps et le chargement/déchargement dispose d’une courte pose dédiée. Les soldats ont un cycle de frappe plus complet ; les charrettes présentent des roues tournantes et un pousseur aux bras tendus. Les gestes de travail cessent lors d’un ordre manuel.

Le moulin tourne sur son axe, plus ou moins vite selon le vent. Les épis ondulent dans les parcelles et les bovins bougent la tête et la queue avec des décalages individuels. Les clôtures, fondations et emprises restent immobiles. Les séquences de chantier sont séparées des animations des bâtiments terminés.

La **scierie** fait tourner sa lame et projette de petits copeaux après une livraison réelle de bois. À la **carrière**, le treuil s’enroule, le câble se tend et la grue soulève puis redescend un bloc, avec une légère oscillation. Les livraisons entretiennent l’activité ; les mécanismes terminent leur cycle puis se reposent si les livraisons cessent. Les matériaux d’un chantier ne les déclenchent pas. Ces gestes représentent la manutention et ne produisent ni ne consomment de ressources supplémentaires.

Un **vendeur** se tient derrière l’étal gauche du **marché** : il range les produits, se penche et présente un article. Les fruits et les planches du comptoir masquent le bas de son corps. Les **maisons terminées** émettent une fumée légère depuis leur cheminée ; les volutes montent, dérivent avec le vent puis se dissipent. Chaque maison a son propre décalage, avec huit volutes maximum. Les chantiers ne fument pas. Les bâtiments restent ancrés et tous ces effets s’arrêtent à la pause.

Les mécanismes et le vendeur ont chacun 24 poses. Le fichier `tools/building_animation_art.py` dessine ces pièces animées, à partir d’une base fixe enregistrée dans `art-source/buildings-animation-base.png`. Cette base a été retouchée avec l’outil intégré imagegen pour enlever les anciennes pièces mobiles de la scierie et de la grue ; les autres bâtiments gardent leurs illustrations existantes. La consigne et la provenance figurent dans `art-source/buildings-animation-base.json`.

Les **ressources naturelles s’épuisent définitivement** : chaque arbre contient **200 bois**, chaque gisement **350 pierre**. Sélectionner une source affiche sa réserve restante et sa quantité initiale. Le premier prélèvement du bûcheron déclenche une chute de 1,6 seconde, avec inclinaison progressive, impact, poussière et éclats. Les trois essences sont concernées. Le tronc reste au sol et raccourcit à mesure que le bois est emporté. Les rochers réduisent leur volume à chaque prélèvement, en laissant apparaître les gravats. L’animation ne crée aucune ressource.

Une fois le stock naturel vide, il reste une souche ou quelques gravats traversables ; l’emprise devient disponible deux secondes après la fin de l’exploitation et de la chute éventuelle. Les ouvriers cherchent automatiquement une autre source à portée. Les arbres abattus cessent d’osciller et d’émettre des feuilles ; les restes sont masqués sous les constructions et les aires de stockage. Le bois et la pierre ne repoussent pas. Les cultures et élevages sont des productions entretenues : leurs récoltes restent limitées par cycle et nécessitent le travail des fermiers ou éleveurs pour se renouveler.

La carte normale commence avec la caravane décrite ci-dessus. Elle est explorée et le brouillard désactivé pour faciliter l'apprentissage ; les grandes régions demandent de l'exploration.

1. Choisir **Maison**, **Scierie**, **Carrière**, **Ferme**, **Élevage**, **Entrepôt** ou **Marché** dans le panneau de droite, puis cliquer sur le terrain. Le contour vert indique une emprise valide ; le contour rouge indique un obstacle ou une zone interdite. Le placement du marché prévisualise aussi son rayon d’influence. Le clic droit annule le placement.
2. Les bâtisseurs choisissent, pour chaque matériau, le stock disponible le plus proche du chantier parmi les entrepôts, grands marchés, petits marchés et charrettes arrêtées à portée. Ils apportent physiquement leurs charges au chantier. Les matériaux et le travail doivent être terminés pour que le bâtiment fonctionne. Les cercles bleus indiquent la portée de construction : 20 cases pour un entrepôt, 16 pour un grand marché, 6 pour un étal et 8 pour une charrette.
3. Utiliser **Cultures** près d’une ferme terminée, ou **Enclos** près d’un élevage terminé. Cliquer sur un premier coin, puis sur le coin opposé pour choisir un rectangle de **2 à 10 cases par côté**. La prévisualisation indique sa taille, son coût et ses capacités ; le clic droit annule. Chaque zone devient un chantier. Le bord le plus proche doit se trouver à huit cases maximum du bâtiment agricole correspondant.
4. Recruter les six métiers économiques dans le panneau. Un villageois coûte **10 nourriture**, demande quatre secondes à vitesse normale et occupe une place d’habitation. Les boutons **Forge** et **Caserne** construisent les bâtiments militaires ; leurs artisans se recrutent ensuite en sélectionnant le bâtiment terminé. Les soldats sont commandés dans la caserne et attendent leurs provisions et équipements (détails ci-dessous). Une maison fournit huit places ; un entrepôt en fournit douze. Chaque caserne et marché dispose de sa propre formation, en parallèle de la file des villageois. Les recrutements en cours réservent déjà leur place d’habitation.
5. Les ouvriers cherchent automatiquement un bâtiment de travail et une source accessibles. Un ordre manuel de déplacement suspend leur travail automatique. Sélectionner les ouvriers puis cliquer sur **Au travail** pour le reprendre.
6. Sélectionner un chantier puis **Annuler chantier** pour l’annuler. Ses matériaux déposés restent dans un dépôt de récupération ; les transporteurs peuvent les rapporter. Les charges encore portées sont redirigées vers un stockage valide.
7. Les soldats se commandent sur la carte : déplacement, attaque d’une cible ennemie et déplacement offensif. Le milicien combat à la masse, l’archer tire des flèches à cinq cases, l’épéiste combat à l’épée et l’homme d’armes ajoute une armure et davantage de points de vie. Ils utilisent les ordres et le déplacement du moteur. La carte d’essai n’a pas d’armée ennemie ; une attaque forcée permet d’essayer les coups sur une cible neutre.
8. Sélectionner un **marché terminé**, puis **Charrette 20N** pour créer une charrette et son pousseur. Sélectionner la charrette dans la zone d’un marché, puis cocher **Nourriture**, **Bois** et/ou **Pierre** dans le panneau de chargement. Attendre les livraisons, puis la déplacer auprès des unités ou d’un chantier éloigné. **Recharger** la fait revenir physiquement au marché pour les ressources cochées ; elle y reste après le chargement. Pour plusieurs voyages automatiques, utiliser **Approvisionner chantier**, puis cliquer le chantier.
9. Sélectionner un **entrepôt terminé**, puis **Tracer une aire**. Cliquer sur deux coins opposés pour dessiner une zone extérieure de **4 à 10 cases par côté**, à six cases maximum de l’entrepôt. Les quatre compartiments montrent ses sacs de blé, bûches, blocs de pierre et caisses de viande. **Redessiner l’aire** permet de la déplacer ou de la redimensionner ; **Retirer l’aire** rend le terrain disponible. Le clic droit annule le tracé.
10. Pour commercer avec un comptoir éloigné, terminer son entrepôt et son marché. Sélectionner le marché de départ, **Relier un marché**, puis cliquer l’autre marché. Choisir les ressources exportées depuis chaque extrémité, puis recruter une **Charrette marchande 20N**. Elle assure les allers-retours ; les transporteurs relient les marchés aux dépôts locaux.

Seule la caravane apporte les provisions initiales. Un nouvel entrepôt ou marché construit est vide ; le marché fondateur récupère les réserves restantes lors du déploiement.

Les **cinq ressources restent séparées** : nourriture issue des cultures et de la chasse, viande issue de l’élevage, bois, pierre et or. La viande ne remplace pas la nourriture. Les constructions demandent du bois et de la pierre, et l’église demande aussi de l’or, apportés par les bâtisseurs ; les villageois et miliciens coûtent seulement de la nourriture ; les hommes d’armes coûtent en plus de la viande. Les équipements fabriqués possèdent leurs propres stocks. Un recrutement refusé ne prélève aucune ressource. Les paiements des citoyens et charrettes peuvent réunir les stocks libres des entrepôts et marchés terminés ; ils ne dépensent ni les réservations, ni les ressources en transit, ni le chargement des charrettes. Les provisions militaires sont transportées physiquement depuis les dépôts vers la caserne.

## Forge, équipements et caserne

Construire une **Forge** (80 bois, 20 pierre ; 3 × 2 cases) et une **Caserne** (100 bois, 40 pierre ; 3 × 3 cases). Les bâtisseurs réalisent ces deux chantiers avec les matériaux des stocks à portée. Sélectionner la forge terminée pour recruter **jusqu’à trois forgerons**, à 10 nourriture chacun ; la caserne emploie un recruteur. Les trois forgerons partagent la collecte, le travail sur l’équipement et les livraisons. Leur travail à l’atelier s’additionne : davantage de forgerons présents accélèrent la fabrication, selon le ravitaillement et les trajets. Chaque artisan utilise un logement, mange deux unités de nourriture par minute, et subit la faim hors d’un marché, d’un étal ou d’une charrette approvisionnée.

La forge permet de régler séparément, avec **− / +**, un **stock cible d’arcs, d’épées et d’armures** entre 0 et 100. Les objectifs commencent à zéro et sont communs à toutes les forges du joueur. Le forgeron prend le bois et la pierre au petit marché relié le plus proche, si la forge se trouve dans son rayon de six cases ; sinon, il cherche un stockage approvisionné. Les transporteurs approvisionnent ce petit marché selon la recette en cours. Chaque équipement terminé est porté au **stockage disposant de place le plus proche de la forge** : dépôt, grand marché ou petit marché. Chaque fabrication demande environ 13 secondes de travail pour un artisan à vitesse normale, en plus des déplacements ; la faim ralentit le travail.

| Équipement | Bois | Pierre |
| --- | ---: | ---: |
| Arc | 6 | 2 |
| Épée | 4 | 6 |
| Armure | 6 | 10 |

La production reprend dès que les équipements libres et déjà en fabrication ne suffisent plus à l’objectif. Plusieurs forges et leurs artisans tiennent compte des fabrications et livraisons en cours pour ne pas lancer le même besoin deux fois. Une commande déjà commencée est terminée même si l’objectif baisse ; zéro empêche les nouvelles fabrications de ce produit. Les biens rendus après une annulation peuvent temporairement dépasser l’objectif, qui est un seuil de réapprovisionnement et non une limite de stockage. La capacité intérieure de base est de **100** unités par équipement au dépôt, **60** au grand marché et **20** au petit marché. Les aires extérieures ajoutent leur capacité à celle des dépôts et petits marchés. Leurs panneaux et icônes affichent les quantités réellement livrées ; des piles d’équipements apparaissent à leur pied et se vident lors des prélèvements. Les réservations ne créent aucun objet supplémentaire. Le panneau de la forge indique les quantités libres de l’ensemble des dépôts et marchés, hors réservations.

Dans la caserne, commander un soldat réserve immédiatement une place de logement. Le recruteur récupère physiquement son équipement, sa nourriture et, si nécessaire, sa viande dans les dépôts ou marchés approvisionnés, en choisissant le plus proche de la caserne. **La formation commence seulement lorsque tout est arrivé.** Elle demande environ 16 secondes de travail à vitesse normale, en plus des trajets. Si les stocks manquent d’un élément, la commande attend son réapprovisionnement. La caserne affiche les livraisons et l’activité du recruteur.

| Soldat | Nourriture | Viande | Équipement |
| --- | ---: | ---: | --- |
| Milicien | 20 | 0 | Aucun équipement fabriqué |
| Archer | 20 | 0 | 1 arc |
| Épéiste | 25 | 0 | 1 épée |
| Homme d’armes — élite | 30 | 10 | 1 épée + 1 armure |
| Archer d’élite | 35 | 15 | 1 arc + 1 armure |
| Garde royal — élite | 40 | 20 | 1 épée + 1 armure |
| Tambour militaire — soutien | 25 | 0 | Aucun équipement fabriqué |
| Porte-drapeau — soutien | 25 | 0 | Aucun équipement fabriqué |

Les sept troupes disposent de portraits dans la caserne ; les unités avancées portent la mention dorée **ÉLITE**. Les prix indiquent explicitement la nourriture, la viande et les équipements. L’archer d’élite utilise un arc long à six cases, inflige davantage de dégâts et porte une armure légère ; sa capuche, son carquois et ses épaulières le distinguent de l’archer. Le garde royal combat à l’épée, avec une armure lourde, un grand bouclier, un casque à plumet et une cape bleue. Il résiste mieux que l’homme d’armes, frappe plus fort et se déplace légèrement plus lentement. Les deux nouvelles unités ont les mêmes huit directions et cycles animés que les autres citoyens.

**La viande est obligatoire pour chacune des trois unités d’élite.** Même avec toute la nourriture et tout l’équipement déjà livrés, la formation reste à zéro si la viande manque. La caserne affiche la quantité attendue ; le recruteur va la chercher au dépôt dès qu’elle devient disponible. La viande est consommée une seule fois lors de la création effective du soldat.

Chaque soldat occupe **un logement** et consomme ensuite **deux nourritures par minute**, avec exactement les mêmes zones de ravitaillement, ralentissement et famine que les citoyens. Il faut maintenir la production alimentaire pour les artisans, l’armée existante et les prochaines recrues. La viande sert au recrutement avancé ; elle ne remplace pas les repas. Si des logements sont détruits pendant une formation, celle-ci attend une capacité suffisante. Une sortie bloquée retarde la création sans consommer le kit.

**Annuler la formation** libère la place réservée. Les équipements récupérés sont rapportés au dépôt ou marché disponible le plus proche par le recruteur ; la nourriture déjà livrée reste disponible dans la caserne pour une prochaine commande. Les ressources et équipements ne sont pas dépensés avant la sortie effective du soldat. Un ordre de déplacement manuel suspend l’artisan ; **Au travail** le remet à son poste. Après perte de son atelier, un artisan disponible cherche un bâtiment du même métier ayant une place libre.

Les illustrations isométriques originales sont `art-source/forge-isometric.png` et `art-source/barracks-isometric.png`, créées avec l’outil intégré imagegen. Les prompts et leur référence sont conservés dans `art-source/military-buildings.json`. Les artisans et soldats ont des silhouettes, outils et animations propres, dans les huit directions.

Le scénario **medieval-military** construit les deux bâtiments avec les vrais boutons et bâtisseurs, recrute les artisans, règle les trois objectifs, ajoute une deuxième forge, forme les six combattants et le tambour, teste le tir de l’archer et annule une formation pendant le transport d’un équipement. Pour chacune des trois troupes d’élite, il retire temporairement toute la viande du dépôt, attend que nourriture et équipements soient livrés, vérifie que la formation reste bloquée, puis remet la viande et observe sa livraison réelle et la création du soldat. Il contrôle les stocks, les recettes, les réservations, seize logements occupés, les repas et les 45 viandes consommées au total. Rapports et captures : `analysis/medieval/military-preview/`.

```powershell
& .\.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-military Launch.Map=village Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
```

## Moral des troupes, tambour et porte-drapeau

### Attitudes de combat et rondes de milice

Sélectionner un ou plusieurs combattants pour afficher le panneau **Troupes**, sous les commandes d’équipage ou du commandant. Les trois boutons règlent leur comportement automatique :

- **Offensive** : engage les ennemis visibles détectés à proximité (six cases) et les poursuit.
- **Défensive** : combat les ennemis à portée de son arme, sans quitter sa position pour les poursuivre.
- **Neutre** : n’attaque et ne riposte pas automatiquement. Un ordre d’attaque manuel reste possible. Cette attitude ne change pas les relations diplomatiques et n’empêche pas l’adversaire d’attaquer.

Pour organiser une ronde, sélectionner les **miliciens**, cliquer **Tracer une ronde**, poser **2 à 16 points de passage** sur le terrain exploré, puis cliquer **Enregistrer ronde**. Le mode **Nuit auto**, activé par défaut, mémorise le circuit : les miliciens commencent automatiquement au crépuscule, s’arrêtent à l’aube, puis reprennent le même parcours la nuit suivante sans nouvel ordre. Le départ coïncide avec l’allumage des torches, pendant les trente dernières secondes du jour normal. Enregistrer une ronde pendant la nuit la lance immédiatement, sauf si le commandant a ordonné le repos.

Cliquer sur **Mode : nuit auto** permet de passer au mode **en continu**, pour patrouiller aussi pendant le jour. Le tracé en pointillés reste visible à la sélection : gris en attente du crépuscule, vert en service, avec le point courant en doré. Un clic droit pendant le tracé l’annule. Un point inaccessible après une construction est sauté, puis réessayé au tour suivant. Les miliciens sans parcours attribué n’inventent pas de ronde.

La ronde respecte l’attitude choisie : offensive avec poursuite et reprise du circuit après le combat, défensive avec engagement uniquement à portée, ou neutre sans attaque automatique. Les boutons d’attitude s’appliquent à toute la sélection. Les nouvelles rondes sont réservées à la milice ; les attitudes sont également accessibles aux autres combattants.

**Supprimer ronde**, un ordre de déplacement, d’attaque ou d’arrêt efface le circuit et annule les départs automatiques suivants. Une affectation à un engin de siège remplace également la ronde ; enregistrer une nouvelle ronde libère le milicien de l’engin. Un garde qui se repose librement dans un pavillon se réveille automatiquement au crépuscule pour son service. Le repos explicitement ordonné par le commandant reste prioritaire : le parcours est conservé et reprend après l’ordre de réveil, si l’horaire de ronde le permet. Les gardes continuent de consommer de la nourriture et de se fatiguer pendant leurs déplacements.

Les miliciens portent une **torche qui s’allume automatiquement au crépuscule**, éclaire localement la nuit et s’éteint au retour du jour. Les torches sont masquées lorsque les miliciens dorment. Cette lumière ne révèle pas le brouillard de guerre. Les tambours, porte-drapeaux et torches décoratives conservent leur éclairage.

Sources : `Patrol.cs`, `PatrolInterface.cs`, `DayNight.cs`, `tools/patrol_content.py`. Ce dernier accepte `--output mods/medieval-patrol-content` pour préparer le panneau et les règles dans une copie isolée. Le scénario `medieval-patrol`, sur `messenger-test`, vérifie les boutons, le parcours groupé, les combats, la reprise du circuit, les interruptions, le repos, les torches et la pause. Résultats et captures : `analysis/medieval/patrol/`.

Le scénario `medieval-night-patrol`, sur `village`, vérifie deux crépuscules et deux aubes successifs, la conservation du parcours, le réveil automatique, la priorité des ordres du commandant, l’annulation des prochaines nuits et le mode continu. Résultats et captures : `analysis/medieval/night-patrol/`.

Les six combattants, le tambour et le porte-drapeau ont un moral de **0 à 100**, initialement **70**. Sélectionner une troupe affiche sa jauge au-dessus de sa tête, son moral et le pourcentage réel de dégâts dans les informations de sélection. Le moral rejoint progressivement son niveau cible : deux points perdus ou un point récupéré par seconde. Il influence les dégâts, sans changer les points de vie ni provoquer de fuite automatique.

| Situation durable, sans autre influence | Moral cible | Dégâts |
| --- | ---: | ---: |
| Nourri, sans musique | 70 | 100 % |
| Faim | 40 | 85 % |
| Famine prolongée | 20 | 75 % |
| Nourri, tambour allié proche | 90 | 110 % |
| Nourri, tambour ennemi proche | 60 | 95 % |

Le **Tambour militaire** se recrute dans la caserne pour **25 nourriture**, occupe un logement et consomme deux nourritures par minute. C’est un soutien sans attaque directe. Il joue automatiquement à l’arrêt et en marchant ; ses baguettes et ses bras sont animés. Un battement sonore original suit le musicien, respecte le volume des effets et le mode muet, et reste inaudible dans le brouillard. Un seul tambour par groupe proche est entendu pour limiter la superposition des sons.

Sélectionner le tambour montre son **rayon de cinq cases**. Un musicien bien nourri ajoute **20 points de moral** à ses alliés et en retire **10 aux ennemis**, sans effet sur les neutres. Ces effets déplacent le moral cible : leur effet sur les dégâts arrive progressivement. Ils ne se cumulent pas entre plusieurs tambours du même camp ; un soutien allié et une intimidation ennemie peuvent agir ensemble. L’influence suit la position et le propriétaire du musicien. Quand il s’éloigne ou meurt, les troupes reviennent progressivement au niveau dicté par les influences restantes.

Un tambour affamé joue plus lentement et n’apporte que **+10 aux alliés / −5 aux ennemis**. En famine, il cesse de jouer et d’influencer les troupes. La musique ne nourrit personne et n’empêche pas les dégâts ni la mort par famine. Les soldats conservent le ralentissement physique dû à la faim, mais leur baisse de dégâts passe désormais par le moral : l’ancien malus de dégâts alimentaire ne se cumule pas avec lui. Après un repas, le moral se rétablit progressivement.

Le scénario **medieval-morale** vérifie des dégâts réels de 110 % pour un allié encouragé et 95 % pour un ennemi intimidé, l’absence de cumul, les neutres, le déplacement et la capture du musicien, la récupération après un repas, le tambour affamé puis silencieux, et les huit poses d’animation. Captures et rapport : `analysis/medieval/morale-preview/`. Dix tests C# couvrent également les niveaux, les vitesses de récupération, le cumul et les relations entre camps.

```powershell
& .\.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-morale Launch.Map=village Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
```

## Ressources et métiers

| Ressource | Métier | Circuit réel |
| --- | --- | --- |
| Bois | Bûcheron | Arbre → scierie → transporteur → entrepôt |
| Nourriture (cultures) | Fermier | Travail du champ → croissance → récolte → grange → transporteur → entrepôt |
| Viande | Éleveur | Travail de l’enclos → croissance bovine → production → élevage → transporteur → entrepôt |
| Pierre | Tailleur de pierre | Gisement → carrière → transporteur → entrepôt |
| Matériaux de construction | Bâtisseur | Entrepôt à portée ou charrette arrêtée → charge portée → chantier → travail de construction |
| Matériaux en expédition | Transporteur, charrette, bâtisseur | Dépôt → transporteur → charrette dans une zone de marché → déplacement vers le chantier → bâtisseur |
| Ravitaillement | Transporteur, puis charrette | Ferme ou entrepôt → charge portée → marché → repas locaux ou chargement d’une charrette |
| Commerce entre comptoirs | Transporteurs et charrette marchande | Dépôt local → marché → charrette marchande → autre marché → dépôt local, ou repas pour la nourriture |

Les arbres et les gisements sont finis. Les cultures démarrent sans rendement disponible : les ouvriers doivent d’abord préparer la zone, puis attendre la croissance. Une grande zone demande davantage de préparation ; plusieurs ouvriers peuvent y contribuer. Après épuisement d’un rendement, un nouveau cycle doit être préparé. Les enclos commencent vides : leur cheptel provient désormais de captures et de naissances d’animaux individuels.

## Zones de culture et enclos dessinés

La largeur et la hauteur se choisissent indépendamment, dans les deux sens de tracé. Une zone ne peut pas recouvrir un bâtiment, une autre zone, un arbre, un gisement ou un terrain interdit, ni sortir de la carte. Les cases occupées par des unités peuvent aussi empêcher la validation. Le rectangle vert est valide ; le rouge indique qu’il faut ajuster le coin ou choisir un autre emplacement. Le bâtiment agricole reste séparé de la zone : il sert à déposer la production.

| Surface | Fermiers dans une culture | Éleveurs dans un enclos | Vaches dans un enclos |
| --- | ---: | ---: | ---: |
| 2 × 2 = 4 cases | 1 | 1 | 1 |
| 4 × 4 = 16 cases | 4 | 2 | 4 |
| 6 × 6 = 36 cases | 9 | 5 | 9 |
| 10 × 10 = 100 cases | 25 | 13 | 25 |

Réglages actuels : une place de fermier pour quatre cases, une place d’éleveur pour huit cases, arrondies au supérieur ; une place de bétail pour quatre cases, arrondie à l’inférieur avec un minimum de une. Ce sont des capacités, pas des animaux ou ouvriers gratuits. Recruter les ouvriers et capturer les premiers animaux reste nécessaire. Les bovins et moutons n’occupent pas les logements des citoyens.

Les ouvriers s’affectent automatiquement aux zones ayant une place libre. La limite inclut ceux qui vont y travailler et ceux qui rapportent leur récolte. Si toutes les places sont prises, les nouveaux ouvriers attendent ; ils prennent une place dès qu’elle se libère. Entre deux tâches, un ouvrier peut aider une autre zone disponible pendant la croissance de la première. Supprimer une zone, perdre son bâtiment agricole ou arrêter un ouvrier libère les affectations concernées. La sélection d’une zone affiche sa taille, les ouvriers affectés, la capacité en vaches et la progression de préparation ou de croissance.

Les fermiers se répartissent désormais **à l’intérieur des cultures** : chacun rejoint une case distincte, se tourne vers son rang, coupe les épis et rassemble une gerbe. Ils changent d’emplacement entre leurs cycles de travail et rapportent toujours physiquement leur chargement à la ferme. Les rangs bloqués sont réévalués sans autoriser la récolte à distance.

Les **vaches et les moutons circulent dans leurs enclos**, tournent, remuent les pattes puis s’arrêtent pour brouter. Chaque animal est un acteur sélectionnable, avec son identité, son âge, son propriétaire et sa position réelle. Le nombre affiché correspond au cheptel effectivement présent, dans la limite de la surface du parc. Les mouvements se figent avec la pause.

Les vaches, veaux, moutons et agneaux possèdent chacun leur propre silhouette dans huit directions. Les veaux ont un corps plus fin, une tête proportionnellement plus grande et pas de cornes ; les agneaux ont une toison courte et de grandes oreilles. Le repos, la marche, le broutage, l'attente attachée et la marche en longe utilisent des poses distinctes. Les pattes suivent la distance réellement parcourue ; la respiration, les oreilles et la queue accompagnent les pauses. Le berger porte un capuchon et un bâton recourbé lorsqu'il conduit son animal.

La capture montre le guide qui prépare sa corde, tend les bras puis attache l'animal. Les gestes suivent la progression réelle de la capture, y compris si le travail est ralenti. Pendant le retour, une longe souple relie sa main au museau ; elle se détend à l'arrêt et disparaît à la livraison ou à l'interruption. Les soldats qui volent du bétail utilisent également ces gestes.

Les sources graphiques éditables sont `tools/livestock_art.py` et `tools/animation_art.py`. `tools/livestock_visuals_content.py` produit les planches et les séquences ; `--output mods/medieval-livestock-art-content` prépare une prévisualisation isolée. `tools/verify-livestock-visuals.py` vérifie les 1 280 poses animales et 1 728 poses de conduite, leurs ancrages, proportions et cadrages. Le générateur général réintègre automatiquement ces graphismes. Aperçus et bilan : `analysis/medieval/livestock-visuals/`.

### Captures, reproduction, transferts et vols de bétail

Une nouvelle partie contient des groupes de bovins et de moutons libres, neutres, autour des départs et dans la région. Leur population initiale est finie, indépendante de la caméra et plafonnée sur les grandes cartes. Explorer permet de les repérer. Les lapins, cerfs et oiseaux restent de la faune décorative.

Construire un **élevage bovin** avec son enclos, ou une **bergerie** avec un enclos de moutons, puis recruter l’ouvrier correspondant. En journée, les ouvriers cherchent automatiquement les animaux libres visibles à moins de 32 cases du parc pour y établir un couple. Ils capturent puis ramènent chaque animal séparément, en marchant avec lui. Pour choisir un animal précis ou compléter davantage le troupeau, sélectionner l’animal, cliquer **Capturer vers un enclos**, puis choisir son parc. Un clic droit sur l’animal avec le berger ou l’éleveur sélectionné fonctionne aussi.

À partir de **deux adultes de la même espèce**, un enclos rattaché à son bâtiment agricole peut donner naissance à un petit toutes les 60 secondes. Le veau ou l’agneau est plus petit et devient adulte après 30 secondes. La reproduction s’arrête sans place libre ; les animaux attendus en transfert réservent leur place et empêchent tout dépassement de capacité. Une zone de 2 × 2 ne contient qu’un animal, donc ne permet pas de reproduction. Prévoir au moins huit cases pour un couple et douze pour un élevage bovin productif.

La **tonte** fournit huit unités de laine par mouton adulte, puis un délai de 40 secondes avant la prochaine tonte, sans tuer l’animal. La **viande** provient de l’abattage d’un bovin adulte en surplus : 30 viande par animal, en conservant au moins deux adultes disponibles pour la reproduction. Les ouvriers récoltent et rapportent physiquement ces produits au bâtiment agricole ; les transporteurs poursuivent ensuite la chaîne habituelle. Aucun enclos vide ne produit de laine ou de viande.

Pour déplacer une bête déjà possédée, la sélectionner et cliquer **Transférer vers un autre enclos**, puis cliquer un enclos terminé de la même espèce ayant une place libre. L’éleveur ou le berger disponible vient la chercher à l’entrée du premier parc et l’accompagne jusqu’au second. Une demande reste en attente si aucun ouvrier n’est disponible ; les nouvelles missions automatiques attendent le jour. **Annuler le transfert** ou un ordre manuel au guide interrompt la conduite et libère la place réservée. Une bête déjà sortie reste vivante sur place.

Un **soldat adverse peut voler un animal** : sélectionner le soldat et faire un clic droit sur la bête. Il doit atteindre l’animal et passer trois secondes au contact pour en prendre possession, puis l’escorter jusqu’à un enclos libre de son pays. À défaut, il le ramène près d’un marché ou dépôt, où un éleveur pourra prendre le relais. Un autre adversaire peut l’intercepter pendant le trajet. La mort du guide libère l’animal sur place et la réservation ; elle ne crée aucun duplicata. Ctrl + clic droit conserve l’ordre d’attaque.

Le panneau de l’enclos affiche le nombre réel d’animaux, la capacité, les arrivées réservées et l’état de reproduction. Sources : `Animals.cs`, `Herding.cs`, `Livestock.cs`, `HerdInterface.cs`, `tools/herds_content.py`. Ce générateur accepte `--output mods/medieval-herds-content` pour préparer les règles et le panneau avant installation des DLL correspondantes. Le scénario `medieval-herds` sur `messenger-test` vérifie les captures, naissances, plafonds, transferts via l’interface, vols, interceptions, interruptions et livraisons de produits ; `medieval-herds-spawn` contrôle le peuplement initial sur les cartes à caravane. Résultats : `analysis/medieval/herds/`.

Les coûts suivent la taille : une culture demande 15 bois par quatre cases, arrondis au supérieur. Pour un enclos, le coût suit la longueur de clôture : 25 bois et 5 pierre pour huit segments de périmètre, avec arrondi au supérieur pour chaque ressource. Une culture de 4 × 4 coûte donc **60 bois** ; un enclos de 4 × 4 coûte **50 bois et 10 pierre**. Les bâtisseurs apportent réellement les matériaux et réalisent un travail proportionnel à la surface. Les cercles de construction des entrepôts et charrettes restent applicables.

La clôture entoure tout l’enclos, avec une entrée au milieu du côté sud. Les cases du périmètre bloquent le passage, à l’exception de cette entrée ; l’intérieur est praticable. Le nombre de vaches dessinées suit la capacité. Les rendements augmentent aussi : 10 nourriture par case de culture, et 30 viande par place bovine à chaque cycle complet. Plus de surface exige donc des ouvriers et des transports adaptés. Pour agrandir une exploitation déjà créée, tracer une zone supplémentaire ; cette version ne redimensionne pas un enclos existant.

Les producteurs portent jusqu’à 10 unités ; un transporteur en porte 20 ; un bâtisseur en porte 10. Les transporteurs équilibrent le remplissage des différentes ressources, puis privilégient les trajets proches. Un marché ayant moins de 20 nourriture présentes ou attendues est prioritaire ; les chargements demandés pour les charrettes ont ensuite une priorité accrue. Un porteur à pied transporte un seul matériau à la fois ; la charrette accepte un chargement mixte.

## Saboteur et incendies

Les **puits** du menu Décorations fournissent désormais l'eau des secours. Lorsqu'un bâtiment ou une aire de stockage brûle, jusqu'à quatre citoyens ou miliciens proches interrompent leur tâche, vont au puits, remplissent leur seau et le versent au contact du feu. Le puits doit être terminé, appartenir au même pays, rester accessible et se trouver à moins de 16 cases du foyer ; les intervenants sont appelés dans un rayon de 24 cases. Sans puits utilisable, ils ne peuvent pas créer d'eau à distance.

Une recharge prend environ 1,5 seconde et le versement une seconde, hors trajet et effets de la faim. Chaque seau retire six secondes au feu restant : les flammes diminuent et les pertes cessent dès l'extinction. Les habitants et les miliciens **se réveillent même la nuit** pour intervenir, puis reprennent leur travail, leur lit ou leur ronde mémorisée. Leurs marchandises transportées sont conservées. Un ordre manuel de déplacement ou d'arrêt reste prioritaire et suspend leur mobilisation automatique pendant 30 secondes.

Le saboteur porte une **toute petite torche la nuit**, avec une flamme et un halo réduits à 35 % de ceux de la milice. Elle reste invisible pour un adversaire qui n'a pas détecté le saboteur et s'éteint lorsqu'il dort.

Le **saboteur** se recrute en sélectionnant une caserne terminée disposant d'un recruteur. Il coûte **35 nourriture, 10 viande et une épée**. Le recruteur apporte ces fournitures avant la formation. Il occupe un logement, mange et subit la faim, la fatigue et les effets du moral comme les autres soldats.

Il porte une cape sombre et avance plus lentement que la milice (vitesse 44 contre 64). Il reste caché aux adversaires tant qu'il n'agit pas et qu'aucun milicien ne le détecte à **quatre cases**. Son propriétaire le voit légèrement estompé. La milice endormie ne détecte pas ; en ronde, elle peut le repérer et l'engager selon sa posture habituelle. Le saboteur possède une dague peu puissante et seulement 4 500 points de vie : il est vulnérable au combat direct. Sa posture initiale est neutre, pour éviter une attaque automatique pendant une infiltration.

Sélectionner le saboteur puis choisir dans son panneau :

- **Incendier un bâtiment / une réserve** : cliquer un bâtiment adverse terminé ou une case de son aire de stockage extérieure. Le saboteur rejoint la cible et prépare son feu pendant **10 secondes** au contact. Un clic droit sur un bâtiment adverse donne également cet ordre ; Ctrl + clic droit conserve l'attaque normale à la dague.
- **Abattre un animal adverse** : cliquer une vache ou un mouton adverse. Il rejoint l'entrée de l'enclos ou s'approche de l'animal, puis agit pendant **8 secondes**. L'animal disparaît réellement du troupeau, sans générer de viande pour le saboteur.
- **Voler et ramener un animal** : cliquer l'animal. Après **8 secondes** de capture, il le conduit en longe vers un enclos compatible libre de son pays, ou près de son marché/dépôt s'il n'y a pas d'enclos. Le vol reprend les réservations de places, interceptions et transferts ordinaires du bétail.

La faim peut prolonger ces durées. Le saboteur se dévoile pendant la préparation, l'abattage et la conduite en longe, puis retrouve sa discrétion après **5 secondes** sans action, hors de la détection des miliciens. Une action interrompue avant sa fin ne déclenche aucun effet. **Interrompre**, un déplacement, un ordre de combat, le repos ou sa mort annulent son travail et libèrent ses réservations. Une cible détruite ou devenue indisponible annule aussi le travail.

Un incendie dure **30 secondes**, avec flammes, fumée et crépitements. Sur un bâtiment, il retire 2 % de ses points de vie maximum par seconde et consume progressivement son bois, sa nourriture, sa viande, sa laine, ses tentes et ses arcs. La pierre, les épées et les armures sont préservées. Les marchandises déjà réservées à un transport conservent leur contrat ; celles qui sont encore en transit ne brûlent pas à distance. Sur une aire extérieure, seuls les biens combustibles affectés à la zone visée sont touchés, sans infliger à distance des dégâts au bâtiment ni puiser dans les autres zones. Les compteurs et piles reflètent les pertes réelles. Plusieurs saboteurs prolongent un même feu sans multiplier simultanément ses dégâts. Un feu déjà allumé continue après le départ ou la mort du saboteur, puis s'éteint ; il ne se propage pas automatiquement.

Sources : `Saboteur.cs`, `SaboteurInterface.cs`, `tools/saboteur_content.py`. Le générateur accepte `--output mods/medieval-saboteur-content`, puis `--install-from` pour publier les fichiers vérifiés avec la DLL correspondante. `tools/verify-saboteur.py` contrôle les sprites et l'idempotence des panneaux ; le scénario `medieval-saboteur` sur `messenger-test` vérifie le recrutement avec livraison, la furtivité, les commandes de sabotage, le bétail, les stocks extérieurs et l'interception par la milice. Résultats dans `analysis/medieval/saboteur/`.

## Marchés et ravitaillement mobile

Un marché coûte **80 bois et 30 pierre**, occupe **3 × 2 cases**, stocke **200 nourriture, 120 bois, 120 pierre et 120 viande** et couvre un rayon de **16 cases** depuis son centre. Il utilise les transporteurs déjà recrutés pour faire venir la nourriture des entrepôts ou des dépôts agricoles locaux. Sans transporteur, les stocks ne se déplacent pas. Un marché en chantier ou sans nourriture disponible ne ravitaille personne.

Une charrette coûte **20 nourriture**, inclut un pousseur et occupe **une place d’habitation**. Ce pousseur mange comme les autres citoyens. Elle possède trois capacités indépendantes de **60 nourriture, 60 bois et 60 pierre**, soit jusqu’à 180 ressources si les trois chargements sont activés. Elle apparaît vide, avec seulement la nourriture cochée par défaut. La viande reste exclue. Son rayon de ravitaillement est de **4 cases** et suit sa position, même pendant le déplacement.

Les trois boutons de chargement sont utilisables dans le rayon de **16 cases d’un marché terminé du même propriétaire**, même si le marché manque momentanément de nourriture. Plusieurs ressources peuvent être cochées. Décocher suspend les nouvelles livraisons de cette ressource et conserve tout ce qui est déjà chargé. La charrette doit rester arrêtée dans cette zone pour recevoir les transporteurs. Ils prennent la nourriture dans les marchés et le bois ou la pierre dans les entrepôts ou dépôts de production. Au contact direct d’un marché, la nourriture cochée peut aussi être chargée à raison de 20 par seconde. Chaque transfert retire exactement la même quantité à sa source.

Le bouton **Recharger** fait rejoindre une case voisine accessible d’un marché, en privilégiant un marché ayant de la nourriture. Les choix de chargement sont conservés. Si la charrette part, quitte la zone ou désactive une ressource avant sa livraison, la réservation est annulée ; un transporteur déjà chargé rapporte ses marchandises à un stockage disponible. Aucun matériau n’apparaît dans la charrette à distance.

Sélectionner un marché ou une charrette affiche sa zone : **verte lorsqu’un stock libre permet les repas, rouge lorsqu’elle est inactive ou vide**. Les unités consomment automatiquement à l’intérieur ; elles ne doivent pas rejoindre le bâtiment lui-même. Un marché est utilisé avant une charrette lorsqu’ils couvrent la même personne, afin de préserver les provisions mobiles. Les zones qui se chevauchent ne facturent jamais deux repas. Les sources d’un autre propriétaire ne nourrissent pas les unités du joueur.

Le stock exact est visible au-dessus du marché ou de la charrette et dans les détails de sélection. Une charrette vide reste mobile, mais ne ravitaille plus. Un poste de travail éloigné ou une expédition militaire demande donc un marché supplémentaire ou une charrette approvisionnée.

## Petits étals reliés au grand marché

Sélectionner un **grand marché**, puis **Étal de marché** en bas de son panneau. Chaque étal coûte **25 bois et 5 pierre**, occupe **2 × 1 cases** et doit être construit par les bâtisseurs. Une fois terminé, il se rattache au grand marché allié terminé le plus proche. Plusieurs étals peuvent dépendre du même marché, sans occuper sa liaison commerciale avec un autre grand marché.

Dans un rayon de **6 cases**, l’étal nourrit les citoyens, artisans et soldats et fournit le bois et la pierre aux bâtisseurs et aux forges. Il peut conserver **80 nourriture, 60 bois et 60 pierre**. Son vendeur est animé ; ses piles et compteurs suivent les stocks réellement livrés. L’étal ne produit aucune ressource et ne stocke pas la viande, qui reste nécessaire au recrutement des unités d’élite dans le circuit existant.

Les **transporteurs existants**, en mode automatique, apportent les ressources du dépôt au grand marché, puis du grand marché aux étals. La nourriture demandée correspond à environ deux minutes de consommation des personnes présentes, avec un petit minimum de huit portions. Les matériaux sont demandés selon les besoins encore non approvisionnés des chantiers voisins et des fabrications en cours dans les forges avec un artisan. Une forge dépend d’un seul étal à la fois. Les réservations évitent que plusieurs porteurs livrent deux fois la même demande. Sans habitants, chantier ou fabrication à approvisionner, aucun nouveau stock n’est demandé ; les ressources déjà livrées restent disponibles.

Sélectionner l’étal pour voir son marché, son stock et les quantités à livrer. **Choisir un autre grand marché** permet de changer le rattachement. Si le marché disparaît, l’étal cherche automatiquement un autre grand marché allié. Les transporteurs libèrent les réservations de l’ancienne liaison et conservent toute cargaison déjà chargée. Il faut suffisamment de transporteurs disponibles et des accès praticables pour maintenir les livraisons.

Le bouton **Tracer une aire** de l’étal permet de définir une zone de **2 à 10 cases par côté**, à six cases maximum du bâtiment. Les sacs de nourriture, bûches et pierres correspondent à son propre stock livré. **Redessiner l’aire** et **Retirer l’aire** conservent les ressources et réservations. L’aperçu montre seulement le sol ; après validation, les marchandises et compteurs sont affichés sur l’aire, sans les répéter devant le bâtiment.

Le scénario `medieval-stalls` construit réellement un étal via son bouton, livre les trois ressources, nourrit un villageois et un soldat hors portée du grand marché, construit une maison hors portée du dépôt puis vérifie la conservation des ressources, l’arrêt des demandes de matériaux, le changement manuel de marché et le remplacement d’un marché détruit. Il vérifie aussi le repas d’un forgeron à l’étal, la collecte physique de bois et pierre et la livraison de l’arc terminé dans un stockage public. Captures et rapport : `analysis/medieval/stall-preview/`. Sept tests C# vérifient les demandes et réservations.

Le scénario **medieval-logistics** vérifie le choix du stock le plus proche pour deux chantiers, trois recrutements de forgerons et le refus d’un quatrième sans paiement. Il compare le travail réel à un et trois artisans (345 puis 130 ticks dans ce scénario), contrôle les objectifs communs, la livraison des trois équipements au petit marché, leurs objets et compteurs visibles, puis la récupération d’une épée et d’une armure par le recruteur et la formation d’un homme d’armes avec viande. Il contrôle la conservation de chaque ressource, y compris les recettes, repas et recrutements. Rapport et captures : `analysis/medieval/logistics-preview/`.

```powershell
& .\.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-logistics Launch.Map=village Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
```

## Construction hors du village

Les entrepôts terminés approvisionnent les chantiers dont le centre est situé à **20 cases maximum**. En dehors de cette portée, un chantier reste posé mais attend une source locale. Amener une charrette chargée et l’arrêter à **8 cases maximum du centre du chantier** : les bâtisseurs viennent y chercher les matériaux, par charges de 10, puis les apportent au chantier avant de construire. Une charrette n’apporte pas instantanément son contenu au bâtiment.

Les bâtisseurs choisissent le stock à portée **le plus proche du chantier** qui possède encore la ressource nécessaire, sans priorité donnée aux entrepôts. Grands marchés et petits marchés participent à cette sélection, ainsi que les charrettes arrêtées. Les transporteurs apportent aux grands marchés les matériaux manquants pour les chantiers voisins. Les bâtisseurs réservent leurs charges pour éviter les doubles prélèvements. Si la charrette bouge avant le prélèvement, ils libèrent leurs réservations et recherchent une source valide. Un bâtisseur déjà chargé peut terminer sa livraison. Après une annulation de chantier, il peut rapporter sa charge dans une charrette proche ayant de la place.

Les **cercles bleus** des entrepôts et charrettes montrent cette portée quand on les sélectionne ou qu’on place un bâtiment. Le cercle **vert ou rouge de 4 cases** reste celui des repas. Pour nourrir les bâtisseurs en même temps que les approvisionner, conserver de la nourriture à bord et garer la charrette suffisamment près de leur lieu de travail. Les réserves de construction et les repas restent des consommations séparées.

## Repas, croissance du village et famine

Chaque villageois et chaque soldat mange **1 nourriture toutes les 30 secondes**, soit **2 nourriture par minute**, en plus du coût de son recrutement. Une nouvelle recrue commence son propre délai au moment de son apparition. Les ouvriers inactifs et les soldats immobiles mangent aussi. Les morts cessent de consommer et de compter dans les besoins. Les durées suivent le temps de simulation : la pause les suspend et la vitesse de jeu les accélère ou les ralentit ensemble.

Les repas prélèvent uniquement la nourriture libre d’un **marché terminé ou d’une charrette à portée**, appartenant au même propriétaire. La viande reste distincte et n’est pas consommée comme ration. La nourriture dans un entrepôt ou une ferme, réservée ou en cours de transport ne nourrit pas directement les habitants. Les personnes privées de ravitaillement depuis le plus longtemps sont prioritaires. Entrer et sortir d’une zone ne remet pas à zéro l’horloge des repas à payer.

Le panneau **Ravitaillement** affiche les récoltes, les livraisons, les besoins actuels, le solde, la nourriture libre dans les marchés et charrettes et le nombre de personnes couvertes. Une livraison est comptée à la première entrée de nourriture de production dans un entrepôt ou un marché ; les transferts entre entrepôt, marché et charrette ne comptent pas à nouveau comme production. Les débits sont mesurés sur la dernière minute de jeu ; avant une minute, ils sont extrapolés depuis le début de la partie. Ils représentent l’activité observée, pas un rendement garanti. Une récolte irrégulière ou un entrepôt plein peuvent donc faire varier le solde. Le solde correspond aux livraisons moins les besoins permanents, sans déduire les achats ponctuels de recrues. L’infobulle d’une recrue indique ses besoins supplémentaires et le solde projeté après son recrutement. Un solde positif ne garantit pas que chaque personne soit à portée d’une réserve : il faut aussi surveiller la couverture.

Exemple : **20 habitants ont besoin de 40 nourriture/minute**. Avec 50 nourriture livrées par minute, le village conserve 10 nourriture/minute pour constituer une réserve ou payer des recrutements. Cinq habitants supplémentaires absorbent tout ce surplus ; recruter davantage exige d’augmenter la production et le transport. Agrandir les parcelles, recruter des fermiers, ouvrir de nouvelles fermes et assurer les livraisons permet d’accompagner cette croissance.

Lorsqu’une personne quitte toute zone approvisionnée, ou lorsque les stocks qui la couvrent sont épuisés, elle dispose de **30 secondes avant la faim**. Ensuite, le travail et la construction passent à **50 %**, les déplacements à **75 %**, et le moral des troupes diminue progressivement avec leurs dégâts. Les producteurs continuent à travailler pour permettre un retour à l’équilibre. Après **2 minutes sans ravitaillement**, chaque personne perd **5 % de ses points de vie maximum toutes les 5 secondes**, jusqu’à mourir si le ravitaillement ne revient pas. L’armure ne protège pas de la famine. Une personne initialement en pleine santé meurt après environ 3 minutes 35 sans ravitaillement ; une personne déjà blessée peut mourir plus tôt.

Revenir dans une zone approvisionnée permet de prendre automatiquement le repas attendu : la faim et ses ralentissements disparaissent, les dégâts de famine s’arrêtent et le moral remonte progressivement. Ce repas ne soigne pas les blessures déjà subies et ne réclame pas rétroactivement tous les repas manqués. Les alertes signalent les unités hors couverture, les réserves faibles, les personnes affamées, le risque de mort et le nombre de décès par famine. Une unité affamée porte aussi une indication sur la carte ; sa sélection précise son état.

Les quantités et délais sont des valeurs de départ réglables dans `MedievalNeedsFood` de `rules.yaml` (25 ticks = 1 seconde à vitesse normale).

## Stocks visibles

Les quatre nombres du panneau représentent les quantités **libres dans les entrepôts et marchés terminés**, utilisables pour le recrutement. Les stocks des lieux de production, les réservations et les charges en déplacement, y compris celles des charrettes, sont exclus de ces compteurs. Le panneau Ravitaillement présente séparément les réserves des marchés et charrettes utilisables pour les repas à proximité.

Chaque bâtiment affiche son contenu sur la carte. Des marques empilées évoluent avec le remplissage. Sélectionner un dépôt ou un entrepôt affiche les quantités exactes, les capacités, les réservations de sortie et les livraisons attendues. Les porteurs affichent le type et la quantité de leur charge. Les chantiers affichent leurs matériaux livrés, puis le travail effectué.

Sélectionner un **entrepôt ou un petit marché**, choisir **Type**, puis **Nouvelle zone** et **Tracer la nouvelle zone**. Cliquer ses deux coins sur le terrain. Le bouton Zone permet de parcourir les réserves existantes pour les redessiner, les réaménager ou les retirer. Chaque bâtiment accepte **huit zones** : mixte, bois, céréales, viande, pierre, or, armes et armures, laine et tentes.

La surface ajoute une **capacité réelle**, en plus de la réserve intérieure de base : **100 matériaux par case**, **16 équipements**, **40 laine** ou **8 tentes** par case affectée à la marchandise. Les zones spécialisées vont de 2 à 10 cases par côté ; la réserve mixte commence à 4 × 4 au dépôt, 2 × 2 au petit marché. Les types regroupés répartissent leurs cases entre leurs marchandises. La prévisualisation indique les capacités avant validation.

Les réserves extérieures sont remplies dans leur ordre de création, puis le surplus reste à l’intérieur. Les objets et compteurs de chaque zone représentent uniquement sa part du stock ; ceux du bâtiment représentent le reste. Leur somme correspond toujours à **l’inventaire unique réel**. Les marchandises réservées restent visibles jusqu’au prélèvement ; les livraisons attendues apparaissent seulement au déchargement. Une grande zone peut donc accueillir davantage de marchandises sans en créer lors du tracé.

Tracer ou réaménager est gratuit. Réduire ou retirer une zone est refusé si les biens présents et les arrivages réservés ne tiennent plus dans la capacité restante. Le terrain reste traversable, mais ne peut pas recevoir de bâtiment ou de culture. Les petites plantes sont masquées sous les palettes ; arbres, rochers, bâtiments et autres zones empêchent le placement.

**Un saboteur peut incendier une réserve extérieure.** Le feu détruit les biens combustibles affectés à cette zone et réduit sa capacité du même montant : les autres réserves ne viennent pas remplir le foyer. Bois, céréales, viande, arcs, laine et tentes peuvent brûler ; pierre, or, épées et armures restent intacts. Les contrats de transport déjà réservés sont préservés. Impossible de déplacer ou retirer une réserve pendant le feu. Après extinction, **Redessiner / réaménager** restaure sa capacité, mais ne restitue aucune marchandise détruite.

Une réservation ne déplace aucune ressource. Le prélèvement retire exactement la quantité chargée ; la destination n’est créditée qu’à l’arrivée. Les capacités d’accueil sont réservées à l’avance. Un trajet bloqué ne téléporte pas les matériaux. Un porteur conserve sa charge s’il n’a plus de destination valide ; la mort du porteur ou la destruction d’un stock fait perdre son contenu.

## Refonte graphique — octobre 2026

L’identité visuelle reste celle d’un RTS classique sur une grille de 24 pixels : vue plongeante, silhouettes nettes, personnages compacts, ombres au sol et accents bleus. Les sept bâtiments se distinguent par leurs matériaux et leurs équipements : maison à colombages, entrepôt en pierre, scierie ouverte, atelier de tailleur avec grue, moulin et grange céréalière, grange bovine, marché à auvents. Les images dépassent parfois légèrement leur emprise pour représenter les toitures ; les collisions et coûts sont inchangés.

Le terrain utilise seize variantes d’herbe et seize raccords de chemin à bords irréguliers, avec traces de roues. Les enclos ont des barrières ombrées et une porte visible ; les cultures ont des sillons et des épis denses. Le sol agricole passe derrière les ouvriers, y compris dans les grandes zones. Les vaches restent des représentations du cheptel, pas des unités supplémentaires.

Les stocks restent visibles en permanence, sous forme d’icônes, de nombres exacts et de petites jauges de capacité. Les piles de bois, sacs, viande et pierres devant les bâtiments évoluent en quatre niveaux ; leur contenu numérique reste exact à chaque transfert. Les charges portées apparaissent aussi sur les ouvriers. La charrette est dessinée vide quand elle n’a rien chargé. Les détails de réservation et d’arrivage restent dans la sélection.

Le panneau de commandes utilise des boutons illustrés, des coûts séparés et des cadres sombres. Le radar et la barre d’ordres du moteur conservent leur style Alerte Rouge. Le placement montre le bâtiment ou la zone en transparence, avec un contour vert ou rouge selon sa validité. Les graphismes sources et les commandes de compilation sont inclus : `python mods/medieval/tools/generate-content.py`, puis `python mods/medieval/tools/verify-art.py` et `powershell -File build-medieval.ps1`. Aucun appel au générateur d’images n’est nécessaire pour reconstruire les assets.

Le contrôle graphique vérifie les 185 planches du moteur, les huit orientations de chaque personnage, les cadres d’attaque sans découpe, les 81 tailles de chaque type de zone et les dimensions des textures d’interface. `art/contact-sheet.png` permet d’examiner les sprites à leur taille native.

## Contenu et réglages

Le mod comprend six ouvriers, deux combattants, une charrette avec son pousseur, neuf familles de bâtiments ou zones, des arbres, des gisements et un dépôt de récupération. Les proportions utilisent la grille de 24 pixels : maisons 2 × 2 cases, scierie, carrière et marché 3 × 2, entrepôt et bâtiments agricoles 3 × 3 ; cultures et enclos vont de 2 × 2 à 10 × 10. Les sprites RGBA utilisent une nouvelle planche de bâtiments peints et des animations pixel par pixel compilées par `tools/generate-content.py`. Chaque métier dispose de sa tenue et de son outil ; les déplacements et les attaques possèdent huit orientations. La charrette montre réellement les ressources qu’elle transporte. Les cultures, bovins, clôtures et chantiers ont leurs états propres. Les zones disposent de 81 dimensions par famille, avec des graphismes correspondant exactement à leurs emprises. Le terrain herbeux et les chemins sont également propres au prototype.

- `rules.yaml` : capacités, coûts, vitesses, durées, dimensions et métiers.
- `agriculture.yaml` : variantes de tailles, clôtures, coûts, préparation et rendements ; fichier produit par `tools/generate-content.py`.
- `MedievalAgriculturalArea` dans `rules.yaml` : métiers admis, bâtiment agricole associé, cases par ouvrier et par vache.
- `MedievalRecruitable.Food` et `.Meat` dans `rules.yaml` : prix de recrutement par unité, dans deux stocks indépendants.
- `MedievalNeedsFood` dans `rules.yaml` : fréquence des repas, délai de famine, ralentissements et dégâts.
- `MedievalFoodProvider.Radius`, `MedievalStockpile.Capacity` et `MedievalSupplyCart` dans `rules.yaml` : influence, capacités et chargement ; les valeurs par défaut des traits sont dans `OpenRA.Mods.Medieval/Markets.cs`.
- `MedievalConstructionSupply.Radius` dans `rules.yaml` : portée des entrepôts et des charrettes pour les chantiers.
- `weapons.yaml` : portée, cadence, dégâts et efficacité des armes de mêlée.
- `sequences/medieval.yaml` et `art/` : sprites et séquences compilés.
- `art-source/buildings-isometric.png` et `art-source/nature-isometric.png` : planches actuelles des bâtiments et de la végétation ; sources produites avec le générateur d’images intégré, copiées dans le projet.
- `art-source/ISOMETRIC-PROVENANCE.md` : prompts et références des deux nouvelles planches.
- `art-source/medieval-atlas.png` : ancienne planche conservée comme référence de matériaux.
- `art-source/PROVENANCE.md` : origine et prompt de génération de la planche.
- `tools/medieval_art.py` : importation des cellules de la planche, textures, animations, cultures, clôtures, bovins et icônes.
- `tools/isometric_art.py` : compilateur actuel des bâtiments, végétaux, chemins, cultures et enclos. Ancrages au sol explicites ; projection commune aux quatre états et aux 81 tailles de chaque zone.
- `maps/fondation/` : carte normale avec caravane ; `maps/village/` : village préconstruit réservé aux validations.
- `chrome/ingame-player.yaml` : panneau de gestion.
- `../../OpenRA.Mods.Medieval/` : code C# de la logistique, des bâtiments, des sources et de l’interface.
- `../../OpenRA.Mods.Medieval/Food.cs` et `Markets.cs` : repas à portée, faim, marchés, recrutement et rechargement des charrettes.
- `../../OpenRA.Mods.Medieval/ConstructionSupply.cs` : choix des sources locales et affichage de la portée de construction.
- `../../OpenRA.Mods.Medieval/Agriculture.cs` : tracé des rectangles, capacité des zones et affectations agricoles.
- `../../OpenRA.Mods.Medieval/Trails.cs` : usure du sol par déplacements réels, six niveaux visuels, poids des charrettes et repousse. Les valeurs `RecoveryDelay`, `RecoveryInterval` (en ticks) et `CartWeight` peuvent être renseignées sous `World / MedievalTrails` dans `rules.yaml` ; leurs valeurs par défaut sont 4500, 750 et 2.
- `../../OpenRA.Mods.Medieval/Ambience.cs` : vent, oiseaux et particules de végétation. Le trait `MedievalWindVegetation` règle `Amplitude`, `EmissionHeight`, `Particles` (`Leaf`, `Petal` ou `Seed`) et `HideUnderBuildings` dans `rules.yaml`. Les positions des nouvelles prairies sont générées dans `tools/generate-content.py`.
- `tools/animation_art.py` : poses articulées, huit directions, charrettes et pales du moulin. `UNIT_MODES` définit l’organisation des planches ; `UNIT_FRAMES`, `ZONE_FRAMES` et `MILL_FRAMES` correspondent aux séquences générées.
- `../../OpenRA.Mods.Medieval/Animation.cs` : marche proportionnelle à la distance, choix des gestes selon le travail réel, portage et rotation du moulin suivant le vent.
- `../../OpenRA.Mods.Medieval/NaturalResourceRenderable.cs` : chute autour du pied de l’arbre, troncs débités, diminution des rochers et éclats calculés par le moteur. Les textures peintes existantes sont conservées.
- `MedievalSource` dans `rules.yaml` : `Amount` pour les réserves, `Natural` (`Tree` ou `Stone`) pour le rendu, `FallTicks` (40 par défaut), `ExhaustedTicks` (50) et `RemnantActor` pour les restes traversables. Une source naturelle ne peut pas déclarer `Renewable: True`.
- `../../OpenRA.Mods.Medieval/Stockyard.cs` et `tools/stockyard_art.py` : tracé des aires extérieures, représentation exacte de l’inventaire, palettes et piles de 0 à 100 marchandises par case. `MedievalStockyard.MaxDistance` règle la proximité de l’entrepôt.
- `art-source/farm-animation-base.png` et `art-source/FARM-ANIMATION-PROVENANCE.md` : ferme sans pales fixes et prompt du générateur intégré.

Le générateur régénère les sprites, cartes, manifeste, terrain, interface et variantes de zones agricoles. Il ne faut pas l’exécuter après avoir modifié manuellement ces fichiers sans reporter les changements dans le générateur. Les règles de base de `rules.yaml` et le code C# restent édités séparément.

Les cartes fournies ont été converties au stockage isométrique de 64 × 128 cases. Dans le générateur, les anciennes coordonnées logiques sont translatées en `(x + 32, y - 32)` ; leurs distances, emprises et relations restent conservées. Les cartes carrées de l’ancienne version ne doivent pas être chargées telles quelles. Le terrain médiéval pris en charge est `TEMPERAT` ; les autres climats et les modèles de terrain hérités de Red Alert n’ont pas été convertis.

## Vérification

Depuis la racine, exécuter `powershell -File build-medieval.ps1` pour compiler, contrôler les règles MiniYaml et lancer les tests C# des stocks, paiements, repas et zones de ravitaillement. Ajouter `-Integration` pour lancer une partie automatisée dans le vrai moteur. Ce test ouvre une fenêtre, accélère la simulation uniquement dans le mod `medieval-test`, puis ferme la partie à la fin.

Les contours de placement et les rayons sont dessinés dans la passe d’annotations, après la mise à l’échelle du terrain, afin d’éviter un double zoom. Le scénario `medieval-placement` vérifie neuf outils à trois niveaux de zoom, les tracés dans les deux sens, les changements de caméra et les ordres issus des positions de souris. Pour le lancer : `.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-placement Launch.Map=village Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.UIScale=1 Sound.Mute=True Debug.SyncCheckUnsyncedCode=True`. Refaire avec `Graphics.WindowedSize=1600,1000 Graphics.UIScale=1.25` pour l’interface agrandie. `python mods/medieval/tools/verify-placement.py` contrôle ensuite les pixels verts aux positions attendues dans les captures des deux passages (`analysis/medieval/placement-preview/`).

Les résultats et captures du scénario sont écrits dans `analysis/medieval/integration`. Le scénario contrôle les quatre professions de production, le portage des quatre ressources, le transport par les bâtisseurs, l’achèvement après livraison et travail, les cycles agricoles, l’extension d’un champ et d’un enclos, et l’annulation avec récupération des matériaux. Il vérifie également les coûts des villageois, du milicien et de l’homme d’armes, le refus sans prélèvement lorsque la viande manque, leur prise en compte dans la population et la destruction de deux cibles par leurs attaques de mêlée. Pour l’alimentation, il maintient le village en production pendant que des acteurs de test isolés subissent la faim, les dégâts, un sauvetage par ravitaillement et deux décès. Il vérifie qu’un simple entrepôt, les stocks d’un autre propriétaire et un marché inachevé ne peuvent pas les nourrir. Un soldat quitte ensuite le marché, s’affame, reçoit le ravitaillement d’une charrette, puis perd cette couverture lorsque celle-ci repart. Le retour physique et le rechargement de la charrette ainsi que les livraisons des transporteurs au marché sont contrôlés.

La suite active les véritables boutons de chargement, vérifie les livraisons de 60 bois et 60 pierre dans la charrette, puis pose une maison hors portée de l’entrepôt. Le chantier doit attendre son arrivée. Les bâtisseurs prélèvent et transportent ensuite exactement 60 bois et 20 pierre pour finir la maison. Les commandes de chargement hors zone de marché sont refusées par l’interface et la simulation. Les captures `cart-loading-tested.png` et `remote-construction-tested.png` montrent les choix, les stocks et les cercles de portée.

Les vérifications agricoles utilisent les boutons et les deux clics de tracé, y compris dans le sens inverse, puis contrôlent les chevauchements, la distance aux bâtiments agricoles et les bords de carte. Une culture et un enclos de 4 × 4 sont construits par les bâtisseurs. Des ouvriers supplémentaires vérifient les limites de quatre fermiers et deux éleveurs, la reprise d’une place libérée, la livraison réelle de nourriture/viande et la suppression d’une zone. Le test contrôle aussi la clôture bloquante et son entrée praticable. Les captures `agriculture-zone-preview.png` et `agriculture-zones-tested.png` montrent l’aperçu et les zones terminées. Le scénario couvre 11 000 ticks ; les 37 tests C# vérifient aussi les rectangles, capacités, stocks, réservations, paiements et repas.

La validation MiniYaml signale onze traductions devenues inutilisées dans les fichiers hérités de Red Alert et du moteur. Ce sont des avertissements de texte inutilisé, pas des erreurs de règles.

Le scénario `medieval-building-animation` contrôle les animations des bâtiments sur 1 800 ticks, avec des bûcherons et tailleurs de pierre qui livrent réellement. Il vérifie les 24 poses, le retour au repos après l’arrêt des ouvriers, la conservation des stocks, l’absence de fumée sur les chantiers et l’arrêt des effets à la pause. Captures et rapport : `analysis/medieval/building-animation-preview/`. Le contrôle graphique vérifie aussi que les murs et les fondations restent identiques entre les poses.

`python mods/medieval/tools/verify-art.py` contrôle 190 planches, toutes les tailles agricoles, l’absence de découpe des clôtures et animations, et la couverture exacte des tuiles isométriques. Le mod de contrôle `medieval-visual` exécute une scène de 220 ticks, enregistre quatre captures dans `analysis/medieval/isometric-preview/` et vérifie la conversion aller-retour des clics sur 255 cases. Ses cultures mûres et ses grands enclos sont des fixtures visuelles ; ils ne remplacent pas le scénario économique complet.

Depuis l’ajout des sentiers, la validation couvre **191 planches** et **44 tests C#**. Les sept tests supplémentaires vérifient l’immobilité, les passages répétés, les charrettes, les téléportations, la protection du terrain, la repousse et l’indépendance de l’usure par rapport à la fréquence d’échantillonnage d’un trajet. Le scénario réel enregistre `paths-start.png`, `paths-forming.png` et `paths-developed.png` dans `analysis/medieval/integration/`, puis vérifie que les déplacements ont effectivement produit des chemins visibles.

Avec le décor animé, **48 tests C#** passent. Quatre nouveaux tests contrôlent la limite de particules pendant une longue session, leur expiration, leur origine auprès des plantes, leur ancrage pendant les déplacements de caméra et les variations progressives du vent. Le mod de contrôle `medieval-ambience` exécute 500 ticks dans le moteur et produit des captures ainsi qu’un rapport dans `analysis/medieval/ambience-preview/`. Les effets sont limités à huit oiseaux et 72 particules végétales simultanés et utilisent un générateur aléatoire réservé au décor.

Les animations des métiers ajoutent trois tests de marche, soit **51 tests C#**. La validation graphique contrôle aussi les poses distinctes par action et par direction, les outils levés dans les marges des images et les huit états animés des cultures et du bétail. Le scénario économique vérifie les séquences réellement jouées par chaque métier, les attaques, les huit poses de marche/travail et l’arrêt du geste lors d’un ordre manuel. Le mod `medieval-animation` produit six courtes séries de captures de métiers dans `analysis/medieval/animation-preview/`.

Le stockage extérieur porte la suite à **55 tests C#** et **193 planches contrôlées**. Les tests couvrent les 49 dimensions possibles, la répartition exacte des quatre stocks, les réservations et les livraisons. Le scénario `medieval-stockyard` utilise les véritables boutons et les deux clics de placement, vérifie les obstacles et deux entrepôts indépendants, puis vide, remplit, déplace et retire l’aire. Ses captures et son rapport sont enregistrés dans `analysis/medieval/stockyard-preview/`.

Les ressources naturelles portent la suite à **61 tests C#** et **195 planches contrôlées**. Les nouveaux tests vérifient les réserves finies, la dernière charge partielle, l’attente pendant la chute, l’absence de régénération et le renouvellement des cultures uniquement après travail. Le scénario `medieval-natural` parcourt 2 200 ticks : trois essences chutent, les rochers et troncs se réduisent, les emprises sont libérées et de vrais ouvriers épuisent deux sources chacun, puis livrent exactement 50 bois et 50 pierre. Captures, séquence de chute et rapport : `analysis/medieval/natural-preview/`.

## Comptoirs éloignés et routes commerciales

Un nouvel **entrepôt** peut être placé loin de la ville sur une emprise libre. Il demande **100 bois et 40 pierre** et commence vide après sa construction. La portée de construction reste locale : les bâtisseurs doivent prendre les matériaux dans une charrette arrêtée près du chantier tant qu’aucun entrepôt approvisionné ne le couvre.

1. Poser le chantier d’entrepôt à l’emplacement souhaité. Recruter une charrette au marché de la ville si nécessaire.
2. Sélectionner cette charrette, cliquer **Approvisionner chantier**, puis le chantier d’entrepôt. Le convoi choisit un marché de départ, revient y chercher les matériaux et repart autant de fois que nécessaire. Un entrepôt exige au moins deux charges de bois avec une seule charrette. Des transporteurs et des bâtisseurs disponibles sont nécessaires ; réactiver **Au travail** si ces ouvriers avaient reçu un ordre manuel.
3. Le convoi emporte aussi de la nourriture et attend au moins dix repas avant un départ chargé. Les bâtisseurs prennent les matériaux dans la charrette et construisent réellement le bâtiment. Les choix manuels de chargement sont suspendus pendant cette mission. **Arrêter le convoi** ou un ordre de déplacement restitue le contrôle en conservant la cargaison.
4. Poser un **marché** près du nouvel entrepôt, puis affecter la charrette à ce chantier. Le dépôt nouvellement construit étant vide, la charrette peut encore aller chercher les **80 bois et 30 pierre** nécessaires dans la ville de départ.
5. Une fois les deux marchés terminés, sélectionner celui de la ville, cliquer **Relier un marché**, puis le marché du comptoir. Les deux bâtiments doivent appartenir au joueur. Chaque marché possède une seule liaison ; en choisir une nouvelle remplace l’ancienne.
6. Dans chaque marché, cocher les ressources à **exporter**. Par défaut, le marché de départ expédie nourriture et bois, l’autre viande et pierre. Une ressource circule dans un seul sens : la cocher à l’autre extrémité inverse son sens. **Voir l’autre marché** permet de rejoindre son panneau.
7. Cliquer **Charrette marchande 20N** dans un marché relié. Elle coûte **20 nourriture**, occupe une place d’habitation et transporte jusqu’à **60 unités de chacune des cinq ressources**, or compris. Elle charge, rejoint physiquement l’autre marché, décharge, puis assure le trajet retour. Plusieurs charrettes peuvent desservir une même liaison.

Les transporteurs desservent les marchés depuis les dépôts et productions situés à **16 cases maximum**. Ils emportent aussi le bois, la pierre et la viande importés vers un entrepôt local ; la nourriture reste au marché pour nourrir les habitants. Entre un bâtiment de production et un entrepôt, la portée est de **20 cases**. Prévoir des transporteurs au comptoir ; après un déplacement manuel, leur bouton **Au travail** réactive les tâches automatiques.

Une exportation commerciale laisse **40 nourriture disponibles** au marché de départ. Cette réserve protège contre l’export, mais reste consommable par les habitants et les autres usages habituels. Une destination pleine fait attendre la charrette : sa place est réservée avant le chargement et la cargaison n’est comptée dans le stock du marché qu’à son arrivée. Les quantités transportées sont visibles sur la charrette et dans son panneau.

La charrette marchande sert au commerce ; utiliser la charrette de ravitaillement pour nourrir une expédition ou approvisionner un chantier. Les deux pousseurs mangent comme les autres citoyens. Pour de très longues distances, prévoir un ravitaillement intermédiaire afin d’éviter la faim puis la famine.

Sélectionner un marché relié ou une charrette marchande affiche une liaison dorée entre les marchés. Le déplacement suit les passages accessibles du terrain, et les passages répétés forment naturellement un chemin. La liaison dessinée indique les partenaires commerciaux, pas un tracé de route imposé.

Le panneau de la charrette marchande permet de **mettre en pause**, **reprendre**, **choisir une route** ou **rapatrier la cargaison**. Supprimer une liaison ou perdre un marché interrompt les échanges et conserve les marchandises encore portées. Le rapatriement rejoint le marché disponible le plus proche et attend de la place pour décharger. Une charrette détruite perd sa cargaison.

La suite compte **98 tests C#**, dont sept pour les besoins et réservations des étals, cinq pour les aires de stockage, et quatre pour la viande obligatoire des unités d’élite. Les six tests commerciaux couvrent les quatre stocks, la réserve de nourriture, plusieurs charrettes, les capacités, les interruptions et le déchargement partiel. Le scénario `medieval-trade` construit réellement un entrepôt éloigné et son marché, teste les boutons et les clics de liaison, les échanges aller-retour, puis pause, reprise, rupture et rapatriement. Il contrôle la conservation du bois, de la pierre, de la viande et de la nourriture après déduction des repas et recrutements. Captures et rapport : `analysis/medieval/trade-preview/`.

Pour reproduire ce scénario après `./build-medieval.ps1` :

```powershell
& .\.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-trade Launch.Map=village Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
```

## Maisons de styles variés

Le bouton **Maison** propose désormais six architectures : maison du bourg, petite chaumière, maison à trois niveaux, grande maison à deux ailes, maison avec jardin clôturé, et maison à balcon fleuri. Les volumes, la hauteur, les toitures et les petits extérieurs varient. Chaque maison réserve une **parcelle de 2×2 cases**, coûte **60 bois et 20 pierre**, et fournit **8 places d'habitation une fois terminée**.

Le modèle suivant est choisi dans un ensemble mélangé : les six modèles apparaissent une fois par série de six constructions, sans répétition immédiate entre deux séries. L'ordre dépend de la graine de la partie et du joueur. L'aperçu et la ligne d'information indiquent le modèle avant de poser le chantier ; déplacer la souris, fermer l'outil ou tenter un emplacement invalide ne change pas ce choix. Seule une pose réussie avance le tirage. Annuler ensuite un chantier ne revient pas au modèle précédent. La maison conserve son architecture après achèvement et sa sélection affiche le nom du style.

La zone de sélection suit la hauteur de chaque maison. La fumée part de la cheminée propre au modèle, uniquement après achèvement. Les maisons présentes dès le lancement d'une carte reçoivent aussi une variante stable ; les cartes et scénarios peuvent fixer un modèle avec l'initialiseur `MedievalHouseStyle` (0 à 5).

Les cinq nouvelles illustrations ont été créées avec l'outil intégré `image_gen.imagegen` : `art-source/house-cottage.png`, `house-townhouse.png`, `house-longhouse.png`, `house-courtyard.png` et `house-balcony.png`. Les prompts complets et la référence sont conservés dans `art-source/house-styles-prompts.json`. `tools/house_art.py` recadre et réduit ces sources, mesure leurs points de cheminée, puis produit les six poses et la pose de chantier de `art/house.png`, les règles `house-styles.yaml` et la planche `art/house-styles-preview.png`.

Les quatre nouveaux cas de test contrôlent le tirage reproductible et l'absence de répétitions immédiates. Le scénario `medieval-houses` construit les six modèles avec les vrais ouvriers, compare le style au choix annoncé, contrôle les matériaux, les huit places, les cheminées et les zones de sélection. Rapport et capture : `analysis/medieval/house-preview/`.

```powershell
& .\.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-houses Launch.Map=village Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
```

## Embellir la ville

L’onglet **Décorations** du panneau de droite ouvre un catalogue de trois pages. Les coûts affichés concernent un aménagement complet, ou une case pour les clôtures et chemins tracés. Les bâtisseurs vont chercher les matériaux dans un dépôt, un marché ou une charrette à portée, les apportent au chantier, puis réalisent l’aménagement.

| Décoration | Bois | Pierre | Placement |
| --- | ---: | ---: | --- |
| Puits | 15 | 25 | Un clic ; obstacle décoratif |
| Clôture de jardin | 3 | 0 | Deux clics ; ligne sur un axe de la grille |
| Portillon ouvert | 5 | 0 | Un clic ; orientation réglable ; passage libre |
| Massif de fleurs | 4 | 0 | Un clic ; traversable |
| Drapeau | 6 | 0 | Un clic ; tissu animé ; traversable |
| Chemin de pierre | 0 | 3 | Deux clics ; tracé avec un virage si nécessaire |
| Placette pavée | 0 | 12 | Un clic ; 2 × 2 cases traversables |
| Place pavée | 0 | 27 | Un clic ; 3 × 3 cases traversables, rosace centrale |
| Statue de chevalier | 5 | 30 | Un clic ; socle de pierre, obstacle de 1 case |
| Lion de pierre | 4 | 24 | Un clic ; socle sculpté, obstacle de 1 case |
| Chêne sur pavés | 12 | 6 | Un clic ; arbre décoratif, entourage en pierre |
| Cyprès sur pavés | 10 | 6 | Un clic ; arbre décoratif, entourage en pierre |

Les pages **2 et 3** proposent aussi des ornements dorés. Le chevalier porte des dorures sur le heaume, le bouclier et le socle ; le lion possède une couronne et un collier d’or. Les deux places présentent des incrustations dans les pavés. Les grilles ont des barreaux de fer sombre et des pointes dorées, avec un portillon ouvert assorti.

| Décoration dorée | Bois | Pierre | Or | Taille |
| --- | ---: | ---: | ---: | --- |
| Chevalier or | 8 | 40 | 20 | 1 case |
| Lion doré | 6 | 32 | 14 | 1 case |
| Placette or | 0 | 24 | 12 | 2 × 2 cases |
| Place royale | 0 | 45 | 25 | 3 × 3 cases |
| Grille fer | 2 | 4 | 2 | Par case tracée |
| Portillon or | 4 | 6 | 3 | 1 case, orientation réglable |

**L’or est réellement consommé par la construction** : les bâtisseurs le transportent depuis le stockage jusqu’au chantier. Un chantier sans or reste en attente. Le fer désigne l’apparence des grilles ; leur coût utilise les ressources existantes du jeu. L’aperçu des tracés inclut le coût total en or et les grilles se raccordent automatiquement dans les deux axes. Les statues dorées peuvent être construites sur une place terminée. Leur retrait restitue uniquement les matériaux livrés et conserve le pavage en dessous.

Le générateur `tools/gilded_decorations.py` produit les sprites, règles, traductions et icônes de ces ornements. Le scénario `medieval-gilded-decorations` vérifie le catalogue, un chantier bloqué par manque d’or, son ravitaillement réel, les raccords, le passage du portillon et la conservation des matériaux après retrait. Rapports et captures : `analysis/medieval/gilded-decorations/`.

Les clôtures choisissent leur orientation selon le tracé et raccordent automatiquement les angles et jonctions. Laissez une case pour un portillon : les clôtures bloquent réellement le passage. **Tourner le portillon / clôture** change l'orientation d'un portillon ou d'un segment isolé. Le tracé est limité à 64 cases et l'aperçu affiche son coût total. Une zone occupée rend tout le tracé invalide ; les cases déjà couvertes par le même décor sont conservées sans nouveau chantier. Les pavés recouvrent visuellement les chemins de terre et restent traversables, sans bonus de vitesse.

Après une pose, l’outil reste actif pour aménager plusieurs endroits. **Clic droit** termine la pose ; changer d’onglet ou de page quitte aussi cet outil. **Retirer décor**, puis un clic sur celui-ci, permet de réaménager le terrain. Les matériaux effectivement livrés sont laissés sur place comme matériaux récupérables, même si le chantier n’était pas terminé. Les réservations et chargements en cours sont libérés ou rapportés par les bâtisseurs. Les éléments gratuits d’une carte ne produisent pas de matériaux au démontage.

Le puits est décoratif : il n’ajoute pas une ressource eau ni une nouvelle zone de ravitaillement. Les statues et autres ornements d’une case peuvent se construire sur vos places et chemins pavés terminés, si la case est libre. Leur retrait conserve le pavage en dessous. Un autre bâtiment exige d’abord de retirer la décoration. Les sources, surfaces agricoles et autres obstacles restent protégés contre le chevauchement. Les arbres plantés sont des ornements, pas des sources de bois.

Les sprites et règles sont produits par `tools/decoration_art.py` et `tools/generate-content.py`. Le puits peint est dans `art-source/well-isometric.png`, accompagné du prompt et de la provenance `image_gen.imagegen` dans le fichier JSON voisin. Clôtures, pavage et animation du drapeau sont dessinés par le générateur sur la grille isométrique du jeu.

`tools/village_life_art.py` et `tools/village_life_content.py` reproduisent les six nouveaux aménagements, les animaux et les clôtures au premier plan des 81 dimensions de chaque type d’enclos. L’option `--output` du second script permet de préparer une copie isolée avant d’installer les graphismes avec les DLL correspondantes. Le scénario `medieval-village-life`, sur `village`, vérifie six fermiers, le transport et la conservation de la nourriture, les animaux, la pause, les six constructions réelles, la circulation sur la place, la pose d’une statue par-dessus et son retrait. Rapport et captures : `analysis/medieval/village-life/`.

Le scénario `medieval-decoration` utilise les vrais boutons du catalogue et construit un jardin avec ses ouvriers. Il vérifie les tracés, les angles, le passage effectif d'un soldat par le portillon, l'animation du drapeau, le retrait pendant une livraison et la conservation du bois et de la pierre. Rapport et capture : `analysis/medieval/decoration-preview/`.

```powershell
& .\.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-decoration Launch.Map=village Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
```

## Vérification des fortifications

La vérification graphique couvre **219 planches**, dont les raccords de palissades, les neuf poses des portes à deux battants dans les deux orientations et seize directions de flèches. Les cinq cas C# de fortification contrôlent le tracé dans les deux axes et les deux sens, ses limites et la rotation. Le scénario `medieval-fortifications` utilise les vrais boutons pour construire 25 palissades, une porte large et une tour avec huit bâtisseurs. Il vérifie le coût réel de 330 bois, les doublons, le rejet atomique d’un tracé occupé, le passage d’un soldat et d’une charrette sur plusieurs voies, les raccords aux deux extrémités, la fermeture de la porte, le blocage ennemi sur les trois cases et les tirs automatiques après construction. Rapport et captures de la porte ouverte et fermée : `analysis/medieval/fortification-preview/`.

```powershell
& .\.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-fortifications Launch.Map=village Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
```

## Atelier et engins de siège

Ouvrir **Défenses**, puis **Atelier de siège**. Le bâtiment occupe **4 × 3 cases**. Les bâtisseurs apportent ses matériaux et le construisent. Sélectionner l'atelier terminé permet de commander un engin à la fois ; les bâtisseurs libres vont chercher les ressources dans les stocks ou charrettes à portée, puis le fabriquent. Garder une sortie dégagée. Plusieurs ateliers peuvent travailler en parallèle. Annuler une commande conserve dans l'atelier les matériaux déjà livrés, utilisables par la suivante.

| Construction ou engin | Bois | Pierre | Nourriture | Habitation |
| --- | ---: | ---: | ---: | --- |
| Atelier de siège | 160 | 80 | 0 | Aucune |
| Bélier | 100 | 20 | 0 | Ses quatre soldats existants |
| Trébuchet sur charrette | 150 | 50 | 20 | Une place pour le conducteur |
| Charrette de pierres | 40 | 10 | 20 | Une place pour le conducteur |

### Bélier poussé par les soldats

Recruter les soldats à la caserne, sélectionner le bélier, puis **Appeler 4 soldats proches (10 cases)**. Quatre combattants disponibles le rejoignent à pied et prennent une pose de poussée. Ils restent de vraies unités exposées aux attaques : ils occupent leurs logements, mangent et conservent leur moral. Le bélier attend ses **quatre soldats à moins de deux cases** pour avancer ou attaquer un bâtiment. Une perte ou un retard l'arrête ; appeler un remplaçant permet de repartir. La faim des pousseurs ralentit l'engin et leur moral influe sur ses dégâts.

Donner au bélier un ordre normal de déplacement ou d'attaque. **Libérer les soldats**, ou donner directement un ordre à un soldat affecté, le détache. Le bélier ne crée aucun équipage gratuit et ne consomme pas lui-même de nourriture.

### Trébuchet démontable

Le trébuchet sort **démonté sur sa charrette** et nécessite désormais **1 à 4 soldats affectés**. Sélectionner les soldats, cliquer **Affecter les soldats sélectionnés à un engin**, puis désigner le trébuchet. Son panneau propose aussi **Ajouter un soldat proche (10 cases)**, utilisable jusqu'à quatre fois. Les soldats marchent jusqu'à leurs postes ; seuls ceux présents à proximité contribuent. Sans soldat présent, déplacement, montage, chargement et tir s'arrêtent. L'affectation ne crée aucune unité : les soldats gardent leurs logements, faim, moral et vulnérabilité.

Arrêter l'engin sur un terrain libre de **3 × 3 cases**, puis choisir **Monter le trébuchet**. Avec un soldat nourri, le montage prend **12 secondes** et le démontage **8 secondes**. L'efficacité des manœuvres et du chargement passe à **140 %, 180 %, puis 220 %** avec deux, trois ou quatre soldats nourris ; la cadence de tir et la vitesse de déplacement augmentent aussi. La faim des équipiers réduit leur travail. L'engin ne peut se déplacer ni pendant l'opération ni lorsqu'il est monté. Ses appuis occupent réellement le terrain et empêchent les passages. **Démonter sur la charrette** permet de repartir ; santé, identité, nourriture consommée et pierres restantes sont conservées. Perdre ou libérer tout l'équipage suspend la manœuvre, qui reprend à l'arrivée de remplaçants.

Le montage utilise désormais **48 poses dans chacune des huit orientations** : détachement des charges, déplacement de la charrette, déchargement des longerons et traverses, levage successif des supports en A, pose de l'axe et du bras, installation du contrepoids puis des cordages. Les poutres gardent leur longueur pendant leur déplacement et leur rotation. La charrette vide reste visible à côté du trébuchet et l'opérateur passe d'un poste à l'autre. Le démontage parcourt ces étapes en sens inverse, avec raccord exact vers la charrette chargée. Les poses suivent la progression réelle ; elles ralentissent avec la faim et se figent avec la pause du jeu. Le tir comporte également **32 poses de détente, recul et retour au treuil**, synchronisées avec le départ réel de la pierre.

Le modèle articulé et sa projection sont dans `tools/trebuchet_art.py`. Son exécution compile uniquement la planche du trébuchet et produit `analysis/medieval/trebuchet-stages.png` ainsi que `trebuchet-montage.gif`. Le compilateur général `tools/generate-content.py` appelle le même modèle. Le scénario `medieval-siege` contrôle les 48 poses dans les deux sens, leur ordre, la pause, l'immobilité de l'engin et la conservation de sa santé et de ses pierres.

La planche du trébuchet utilise des régions PNG `Frame[n]` recadrées avec leurs décalages natifs : les marges transparentes sont retirées pour réduire la mémoire au chargement. `tools/verify-art.py` reconstruit chaque pose et compare tous ses pixels au modèle, afin de vérifier que cette optimisation ne décale ni ne coupe l'animation.

Le trébuchet tire de **4 à 16 cases** ; les deux cercles de sélection indiquent ces limites. Sa charrette conserve une réserve de **60 pierres maximum**. Un soldat réserve un projectile de **5 pierres réelles**, rejoint la charrette, le prend, marche avec sa charge visible jusqu'au trébuchet puis le dépose dans la fronde. Les autres soldats aident aux manœuvres et à l'armement du treuil. Le projectile reste visible dans la fronde jusqu'au tir ; réserve, pierre portée et projectile chargé sont des stocks distincts, jamais dupliqués. Le panneau affiche la réserve et la fronde séparément. Sans projectile chargé ou sans équipage, aucun tir ne part. Les projectiles volent en cloche et infligent des dégâts de zone. Conducteurs et soldats mangent près des points de ravitaillement comme les autres citoyens.

Un ordre manuel ou **Quitter l'équipage** libère le soldat. S'il porte une pierre, il la conserve ; après son ordre, il la rapporte physiquement à un stockage disponible, ou peut être réaffecté à un trébuchet. Le démontage conserve également les pierres en cours de transport et dans la fronde. Un soldat tué perd sa charge et libère les réservations de son ancienne tâche.

### Frappe du bélier

Le bélier approche la surface du mur ou de la porte en gardant la place nécessaire à sa poutre. Ses **24 poses de frappe dans huit directions** montrent le recul de la poutre suspendue, l'accélération vers le mur, le choc, le rebond et le retour au repos. Les quatre soldats s'animent pendant la manœuvre. Le cycle dure **32 ticks, soit 1,28 seconde** à vitesse normale ; les dégâts arrivent au contact, au quatorzième tick. Le montant des dégâts et le délai entre deux coups restent identiques.

Au point de contact, des éclats de bois et de pierre jaillissent vers l'extérieur, rebondissent puis disparaissent sous une poussière qui se disperse en **2,8 secondes**. Le bâtiment tremble brièvement. Un arrêt, un déplacement, une capture ou la perte d'un soldat avant le contact annule le coup : aucun dégât retardé ne part d'un bélier abandonné.

`RamStrike.cs` gère l'approche et le coup synchronisé ; `tools/siege_art.py` recompile uniquement les 320 images du bélier lorsqu'il est exécuté directement. Le scénario `medieval-ram` sur `messenger-test` vérifie les frappes contre un entrepôt et une porte depuis deux côtés, les 24 poses, les soldats, un seul dégât par coup, les interruptions et la disparition des effets. Rapport et captures : `analysis/medieval/ram-strike/`.

### Lancer de pierre et impact

La pierre reste attachée à la fronde pendant la montée du bras, puis se détache au huitième tick. Le cycle de tir comporte **32 poses sur 40 ticks** : détente, recul et retour au treuil. Le projectile tourne en vol, laisse une discrète trace de mouvement et projette une ombre au sol qui se resserre pendant la chute. Son point de départ utilise la même projection et la même courbe de mouvement que le modèle articulé, dans les huit directions. Le démontage attend la fin du cycle de tir.

À l'impact, une couronne de poussière s'étale au sol, des éclats de pierre rebondissent et un nuage dense monte avant de se disperser. Un bâtiment qui reçoit réellement les dégâts du trébuchet tremble brièvement, s'éclaircit au choc et projette des débris de pierre et de bois depuis sa toiture. Les poussières et débris disparaissent en **4,4 secondes** à vitesse normale. Leur rendu respecte le brouillard et la pause ; les nuages hors écran ne sont pas dessinés. Ces effets ne modifient ni les stocks, ni le montant des dégâts, ni la portée.

`SiegeProjectile.cs` synchronise la fronde et le projectile natif ; `SiegeImpact.cs` dessine les nuages et débris ; `SiegeDamage.cs` anime les bâtiments touchés. Le scénario `medieval-siege-shot` vérifie le départ depuis la fronde, la montée, la chute, les 32 poses, le choc, un unique dégât par tir, la consommation exacte des cinq pierres et la disparition des effets. Images du moteur et rapport : `analysis/medieval/siege-shot/`.

```powershell
& .\.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-siege-shot Launch.Map=messenger-test Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.UIScale=1 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
```

### Vol des béliers et trébuchets

Sélectionner un ou plusieurs soldats, puis faire **clic droit sur un bélier ou un trébuchet ennemi**. Le curseur de capture remplace celui d'attaque. Le soldat rejoint réellement l'engin et doit rester au contact pendant **150 ticks, soit 6 secondes à vitesse normale**. La progression apparaît dans son panneau et dans la barre de capture de l'engin sélectionné. **Ctrl + clic droit** permet toujours d'attaquer l'engin ; Alt conserve le déplacement. Un ordre d'arrêt, un départ ou la mort du soldat interrompt la tentative et remet sa progression à zéro. Les bâtiments, notamment l'atelier de siège, et les charrettes de ravitaillement ne sont pas capturables par cet ordre.

L'engin passe au camp du vainqueur avec sa santé restante, ses pierres, son projectile chargé et sa progression de montage ou de démontage. L'ancien équipage reste dans son armée et conserve ses éventuelles pierres portées. Ses charrettes de ravitaillement s'arrêtent, gardent leur chargement et libèrent les réservations de livraison. Aucun soldat ni aucune pierre n'est créé par la capture.

Le soldat qui réussit le vol rejoint automatiquement le nouvel équipage ; les autres soldats du même ordre peuvent le compléter, jusqu'à quatre. Le **bélier exige toujours quatre soldats** pour rouler et frapper. Le **trébuchet fonctionne avec un à quatre soldats** : s'il est monté, il faut le démonter avant de le ramener chez soi, puis le remonter pour tirer. Une armée peut reprendre un engin précédemment volé. Un montage interrompu conserve ses appuis et reprend avec le nouvel équipage.

Le scénario natif `medieval-siege-capture`, sur la carte `messenger-test`, contrôle les deux camps avec des ordres sérialisés : interruption, mort du capturant, capture groupée, déplacement et attaque du bélier, tir du trébuchet capturé, démontage, transport, reprise pendant le montage, conservation des ressources des deux camps et nettoyage des appuis après destruction. Rapport : `analysis/medieval/siege-capture/result.json`.

```powershell
& .\.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-siege-capture Launch.Map=messenger-test Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.UIScale=1 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
```

### Ravitaillement en pierres

Fabriquer une **Charrette de pierres** à l'atelier, la sélectionner, puis cliquer **Choisir le trébuchet à ravitailler** et désigner son trébuchet. Elle prélève les pierres au dépôt, grand marché ou petit marché disponible le plus proche, transporte sa charge et alimente la réserve de la charrette du trébuchet. Le dernier trajet jusqu'à la fronde est assuré par un soldat. Le ravitaillement recommence automatiquement tant qu'il est actif. La charge et le stock de l'engin sont visibles et actualisés. Plusieurs charrettes peuvent desservir le même trébuchet : elles réservent la place avant de charger et ne peuvent ni dupliquer les pierres ni dépasser la capacité de la réserve.

**Pause / Reprendre** suspend et réactive les livraisons. **Rapporter les pierres au stockage** rapporte les pierres restantes dans un stockage disponible. Un déplacement manuel met la tournée en pause. Si le trébuchet disparaît ou change de propriétaire, la livraison s'arrête et la charrette garde sa charge pour être réaffectée ou rapatriée. La destruction d'un engin ou d'une charrette fait perdre les pierres qu'il porte.

### Sources et vérification du siège

La perspective de l'atelier a été reprise à partir d'un guide géométrique de **4 × 3 cases**. Son ancrage correspond au centre des fondations, indépendamment de la hauteur du toit ; le chantier, la sélection et les éléments mobiles utilisent ce même repère. Le toit et les murs restent immobiles pendant l'animation.

Dès une commande, un fanion et le crochet du treuil s'animent dans la cour. Dès que les bâtisseurs commencent réellement le travail, une animation montre le treuil, le travail du bois et **trois étapes d'assemblage propres à l'engin commandé**. Si le travail s'interrompt, les éléments déjà assemblés restent visibles mais le mécanisme se fige. Annuler ou terminer la commande rend la cour au repos ; mettre le jeu en pause fige aussi l'animation. Ces effets visuels n'ajoutent aucun citoyen, ne consomment aucune ressource et ne font pas avancer la production.

- `siege.yaml` : acteurs, empreintes, vitesses, conditions, nourriture et capacités ; `weapons.yaml` : tirs et dégâts.
- `OpenRA.Mods.Medieval/Siege*.cs` : équipages, montage, fabrication, transport, interface et scénario de validation.
- `tools/siege_art.py` : mécanismes articulés, huit orientations, roues, poussée, montage et tir ; `art/siegeworkshop.png`, `ram.png`, `trebuchet.png`, `stonecart.png`, `siegestone.png` : planches du moteur.
- `art-source/siege-workshop.png` et `art-source/siege-workshop-prompt.json` : illustration transparente de l'atelier, prompt complet et provenance du générateur intégré `image_gen.imagegen`.
- `art-source/siege-workshop-isometric.png` et `art-source/siege-workshop-isometric.json` : nouvelle source actuellement utilisée, prompts et mesures d'ancrage ; `siege-projection-guide.png` : guide de projection. Les 162 poses de l'atelier sont compilées dans `art/siegeworkshop.png`.

Le scénario natif `medieval-siege` construit réellement l'atelier et les engins, contrôle l'immobilité sans équipage, la sélection groupée de quatre soldats, le montage accéléré et son arrêt après libération de l'équipage. Il vérifie aussi le trajet du porteur avec sa pierre, ses animations, la conservation d'une charge après un ordre manuel, le démontage, le transport et le remontage. Deux charrettes ravitaillent le trébuchet ; portée minimale, projectiles, épuisement des munitions, destruction de la destination, rapatriement et conservation exacte des ressources après fabrication, repas et tirs sont contrôlés. Captures et rapport : `analysis/medieval/siege-preview/`. La compilation générale exécute aussi **133 tests C#**, dont six tests de portage et d'équipage ; le contrôle graphique couvre **239 planches**.

```powershell
& .\.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-siege Launch.Map=village Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.UIScale=1 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
```

## Ambiance et bruitages

Le jeu utilise uniquement de l'ambiance et des bruitages, **sans musique de fond**. La banque compte **84 fichiers WAV** : 74 bruitages de synthèse procédurale et 10 répliques françaises de synthèse vocale. Les vaches, veaux, moutons et agneaux émettent leurs propres cris et des pas de sabots. Le son vient de l'animal réel, libre ou dans un enclos ; un pré vide ne meugle plus. Vent, pluie, marchés, pas et roues accompagnent le village. Les haches, pioches, récoltes, marteaux, enclumes et manipulations de charges suivent le travail réel ; le soufflet de la forge accompagne le forgeron au travail. La scierie et le treuil de la carrière restent silencieux au repos. Les arbres craquent pendant leur chute et les portes grincent pendant leur ouverture ou fermeture.

Les **tambours proches d'un même pays** se renforcent : une unité joue seule, plusieurs produisent un roulement d'ensemble plus puissant. Le gain augmente progressivement et reste plafonné à deux fois le volume d'un tambour, sans multiplier le bonus de moral. Les musiciens affamés jouent moins fort et moins vite ; les dormeurs et les musiciens en famine se taisent. Un seul ensemble est mélangé par battement autour du groupe visible le plus proche du centre de l'écran.

Les combattants poussent de brèves exclamations pendant leurs attaques. Le **porte-drapeau** encourage les soldats alliés présents dans son rayon. Les **miliciens en ronde** commentent leur surveillance, avec des pauses ; ils ne bavardent pas pendant un combat récent, leur sommeil ou une intervention extérieure à la ronde. Une seule réplique joue à la fois. Les phrases sont des voix françaises synthétisées localement, pas des enregistrements de comédiens ; les WAV sont livrés avec le jeu et ne demandent aucune connexion ni moteur vocal chez le joueur.

Au crépuscule, les **grillons et chouettes** remplacent les oiseaux diurnes ; les chouettes proviennent des arbres visibles. La pluie atténue les insectes et suspend les appels d'oiseaux. L'ambiance nocturne s'arrête au retour du jour. Le rythme normal reste huit minutes de jour et deux minutes de nuit.

Les armes, les constructions terminées et les recrutements ont leurs propres sons. Le bélier grince pendant la préparation puis frappe au contact réel ; un coup interrompu ne produit pas de son d'impact. Le trébuchet se fait entendre au lâcher de la fronde, pendant le vol et à la chute de sa pierre. Le volume se règle dans **Paramètres → Audio → Effets sonores**.

Les effets sont spatialisés par le moteur et filtrés selon la caméra et le brouillard. Des variantes limitent la répétition exacte, les demandes simultanées sont espacées et le mixage est limité à 18 effets suivis à la fois, avec priorité aux chocs importants, tambours et voix. Les sons qui sortent de la zone audible sont interrompus ; un saboteur invisible ne se révèle pas par ses pas. La pause coupe les bruitages en cours ; les notifications de construction/recrutement sont réservées au joueur concerné. Ces sons ne modifient ni les ressources, ni les dégâts, ni les générateurs aléatoires de la simulation.

Les sources reproductibles sont `tools/village_audio.py`, `tools/drum_audio.py` et `tools/voice_audio.ps1` / `voice_audio.py` (régénération des répliques avec la voix Windows française Microsoft Paul). `audio/manifest.json` décrit les bruitages ; `audio/voices-manifest.json` conserve les phrases et leur provenance. `tools/verify-audio.py` contrôle les formats PCM, niveaux, fondus et variations. Le scénario natif `medieval-audio`, sur `messenger-test`, vérifie le décodage, les métiers réels, les animaux adultes et jeunes, les livraisons, les cris, les rondes, la nuit, le passage d'un à trois tambours, la pause, le silence hors écran et l'absence de musique. Les scénarios `medieval-ram` et `medieval-siege-shot` contrôlent également les sons des impacts. Rapports : `analysis/medieval/audio/`, `analysis/medieval/audio-expanded/`, `analysis/medieval/ram-strike/` et `analysis/medieval/siege-shot/`.

## Cartes immenses

Lancer **`launch-medieval-immense.cmd`** à la racine, puis choisir 1, 2 ou 3. Les cartes figurent aussi dans le sélecteur de cartes du salon. Le lanceur habituel ouvre désormais l’accueil ; **Jouer en solo → Fonder mon premier bourg** démarre sur la petite carte de fondation par caravane.

| Région | Dimensions de la grille | Surface par rapport au village | Arbres supplémentaires | Gisements supplémentaires | Ponts |
| --- | --- | --- | --- | --- | --- |
| Les Grands Méandres | 256 × 512 | ×16 | 3 200 | 320 | 5 |
| Les Marches Boisées | 384 × 768 | ×36 | 7 000 | 700 | 10 |
| Les Terres sans Fin | 512 × 1 024 | ×64 | 12 000 | 1 200 | 10 |

Chaque carte commence avec une caravane et ses provisions de fondation. Ce sont des régions d'expansion **solo**, avec du brouillard à explorer, des clairières constructibles, des bosquets mixtes, des conifères au nord, des lacs, des rivières sinueuses, de la végétation basse et des pierres isolées ou regroupées. Les règles de faim et de transport restent actives : emmener de la nourriture pour les longues expéditions, puis établir dépôts, petits marchés et routes commerciales entre les nouveaux villages. Les options de brouillard et de carte explorée restent réglables dans le salon.

Les rivières bloquent les déplacements terrestres. Les ponts en bois ont **trois cases de large**, dans la même perspective 2:1 que le terrain, et sont réellement praticables par les citoyens, soldats et charrettes. Ces ponts font partie du terrain : ils ne sont pas destructibles. Le bois et la pierre sont des acteurs de ressources finis, avec les animations habituelles d'abattage et d'épuisement ; les forêts ne sont pas des décors illimités. Les espaces entre les arbres et les abords dégagés des ponts permettent les trajets et l'exploitation.

`tools/large_maps.py` reproduit les trois régions à partir de graines fixes et génère les tuiles d'eau, de berge, de sous-bois et de pont. Il contrôle les coordonnées isométriques, l'absence de ressources sous l'eau et l'accès depuis le village à plus de 99,5 % du terrain praticable et à toutes les entrées des ponts. `map.png` fournit un aperçu dans le salon ; `geography.json` décrit chaque région. Le générateur général conserve aussi ces cartes.

Le scénario natif `medieval-large-maps` charge une carte complète, contrôle les stocks naturels, calcule les itinéraires longue distance et fait traverser chaque pont à un soldat et à une charrette en sens opposés. Il vérifie aussi que les ouvriers continuent d'extraire le bois et la pierre. Il produit des captures et un rapport de chargement/mémoire dans `analysis/medieval/large-maps/<carte>/`.

La validation initiale des trois cartes couvre **trois scénarios de 1 000 ticks**, soit 50 traversées physiques pour les 25 ponts. Les règles de toutes les cartes et les **133 tests C#** passent également.

Les recherches de citoyens, stocks, marchés et chantiers utilisent maintenant les index du moteur. Les bûcherons et tailleurs de pierre interrogent les ressources voisines de leur lieu de travail au lieu de parcourir toute la région. Les arbres intacts évitent les mises à jour de récolte et d'audio inutiles ; les emprises protégées des sentiers se mettent à jour à la création et à la disparition des bâtiments. Le vent et les particules éliminent les végétaux hors écran avant les vérifications de visibilité plus coûteuses. La simulation de la faim, du transport et de l'épuisement des ressources reste active dans toute la région.

Mesure comparative du 9 octobre 2026, sur le même scénario préconstruit de 512 × 1024 cases et 13 223 sources naturelles : le temps moyen de traitement d'un tick passe de **16,29 à 8,92 ms**, soit environ **45 % de calcul en moins**. Les 20 traversées de ponts et l'extraction du bois et de la pierre réussissent. Le chargement mesuré prend 16,8 secondes et le pic de mémoire reste proche de **3,7 Gio**. Cette mesure porte sur le village initial et les unités du scénario, pas sur une région entièrement urbanisée ; elle ne constitue pas une mesure de FPS. La carte de comparaison `large-map-benchmark` conserve le départ préconstruit et reste masquée dans le salon. Rapports : `analysis/medieval/large-maps/before-caravan-optimization.json` et `analysis/medieval/large-maps/large-map-benchmark/result.json`.

```powershell
& 'C:\Program Files\Python314\python.exe' mods/medieval/tools/large_maps.py
& .\.tools\dotnet\dotnet.exe bin-medieval\OpenRA.dll -- Engine.EngineDir=.. Game.Mod=medieval-large-maps Launch.Map=terres-sans-fin Graphics.Mode=Windowed Graphics.WindowedSize=1280,800 Graphics.VSync=False Sound.Mute=True Debug.SyncCheckUnsyncedCode=True
```

## Perspective des bâtiments

Les bâtiments utilisent une correction de projection commune à la grille isométrique 2:1. Chaque modèle possède un repère de fondation et une calibration des deux façades ; les six styles de maisons ont chacun leur réglage. Cette correction concerne les habitations, dépôts, ateliers, bâtiments agricoles, marchés, caserne, atelier de siège, cabane de chasse, mine d'or et église. Les tours, étals, pavillons, clôtures et surfaces au sol déjà dessinés dans les axes de la grille conservent leur projection.

La transformation est appliquée au rendu du bâtiment et à son aperçu de placement. Elle maintient les verticales et accompagne toutes les images de ses animations. Les fenêtres éclairées, sorties de fumée, surfaces enneigées, cloche et mécanismes de la mine suivent les mêmes repères. Les contours de sélection s'adaptent à la hauteur corrigée ; les cases occupées et les trajets des personnages restent ceux des règles de construction.

`ArchitectureProjection.cs` conserve les calibrations à partir des images originales. Le moteur dessine toujours un seul quadrilatère par image : aucune copie de texture ni retouche des sprites sources à chaque image. Le scénario `medieval-architecture` inspecte 25 cas, dont les six maisons et le marché fondateur, et produit des captures de jour, de nuit et sous la neige dans `analysis/medieval/perspective/native/`. Il vérifie aussi la concordance des aperçus et des limites d'affichage. Pour le relancer : `python mods/medieval/tools/audit_game.py --only architecture --binary bin-medieval --jobs 1 --timeout 240`.

## Limites actuelles

Cette version est un prototype jouable avec économie, combattants, unités de soutien et engins de siège. Les coûts et rendements demandent encore un équilibrage, les graphismes ont été retravaillés et les cadres radar/ordres d’Alerte Rouge sont conservés pour garder l’identité RTS classique. Les cartes comprennent le village d'essai, les bourgs multijoueurs, trois grandes régions solo et deux variantes du continent immense pour deux ou trois joueurs. Il n'y a pas encore d'adversaire militaire piloté par l'ordinateur, de factions médiévales distinctes ni de campagnes.

Les habitants occupent des lits la nuit ; les intérieurs des maisons ne sont pas dessinés. Le bétail est individuel et transportable, sans gestion des sexes, péremption ni alimentation animale. L’affectation des ouvriers et les priorités des transporteurs locaux sont automatiques. Les ressources à charger et les exports commerciaux sont sélectionnables, avec une liaison par marché ; les itinéraires à plusieurs escales et les quotas d’export personnalisés ne sont pas disponibles. Une sauvegarde puis sa reprise avec la même version ont été comparées à une simulation ininterrompue, avec un état et une empreinte de synchronisation identiques. Une partie multijoueur entre deux machines, toutes les anciennes sauvegardes et tous les blocages complexes de chemins restent à vérifier.

## Audit général du 10 octobre 2026

Le [bilan de finition des menus et du jeu](../../analysis/medieval/finishing/rapport.md) rassemble les vérifications les plus récentes : nouvelle illustration, parcours d’interface, systèmes de jeu, réseau et géographie. Il distingue les tests réussis des points restant avant une distribution finale. Un blocage d’animal escorté au portillon a été corrigé : la distance de suivi autorise maintenant une case voisine du guide. Un scénario force cette configuration et vérifie l’arrivée effective dans l’enclos.

Les corrections et les résultats détaillés sont regroupés dans [le rapport d’audit](../../analysis/medieval/audit/rapport.md). Il couvre l’économie, les recrutements, les logements, la nourriture, les troupeaux, les marchés, les combats, les campements, les graphismes, le son et les grandes cartes. Les scénarios de validation utilisent les règles actuelles du jeu ; les tests graphiques se lancent un par un pour éviter plusieurs moteurs concurrents sur le pilote graphique.

### Test de charge sur carte immense

Le [rapport de charge](../../analysis/medieval/load-test/rapport.md) décrit un scénario natif terminé sur une copie de **512 × 1 024 cases**, avec **2 000 militaires de quatre camps ennemis, 1 000 citoyens et 80 charrettes**. Les quatre villes travaillent pendant la bataille : 118 chantiers terminés, 1 000 citoyens survivants et jusqu’à 989 citoyens couchés la nuit. Les militaires suivent les règles ordinaires de dégâts, faim, morale et fatigue ; les ravitaillements initiaux sont finis.

Les recherches de tâches des transporteurs et fermiers, l’attribution des repas et l’actualisation graphique des chemins ont été optimisées. Le départ comparable atteint 250 ticks en 25,7 s au lieu de 168,1 s avant ces corrections ; cette comparaison partielle ne mesure pas les performances de toute la bataille. Le scénario complet reste sujet à des ralentissements lors des déplacements dans la mêlée. Ce test local ne valide pas une partie réseau entre plusieurs ordinateurs ni le passage de milliers de soldats sur un pont.

`tools/stress_game.py` prépare et lance cette copie dans un profil isolé ; `tools/stress_report.py` produit le rapport à partir des résultats. Les cartes habituelles et les préférences du joueur sont conservées. La procédure reproductible et les limites de mesure figurent dans le rapport.

### Optimisation des mêlées massives

La recherche de positions de combat commence par une exploration de 64 cellules depuis le soldat. Si cette exploration rencontre un blocage complet, elle évite de parcourir toute la région ; si elle atteint sa limite sans conclusion, le moteur utilise sa recherche complète pour conserver les grands détours. L’état temporaire des petites recherches ne couvre que les cellules rencontrées. Le choix automatique des cibles évite également les vérifications d’armes pour les candidats qui ne peuvent plus dépasser la cible retenue.

Sur le scénario de quatre armées et 3 000 personnages, la phase de mêlée mesurée passe de **86,1 à 66,0 secondes** (environ **23 % de temps en moins**). Le scénario entier passe de 238,0 à 217,9 secondes. Les événements aléatoires et les nouveaux chemins peuvent changer le déroulement des deux batailles ; ces mesures ne représentent pas des FPS garantis. Les règles de combat restent identiques et des ralentissements restent possibles avec des rassemblements extrêmes. Le [rapport comparatif](../../analysis/medieval/combat-optimization/rapport.md) décrit les mesures, les vérifications et les fichiers installés. `tools/combat_report.py` régénère ce rapport.

### Réduction des grosses saccades dans les batailles denses

Une seconde passe évite de remettre à zéro toute la carte pour chaque recherche de chemin et de parcourir tous les secteurs pour mettre à jour les positions. Elle réduit aussi les vérifications de cibles, les allocations d’attaques, les recherches de ravitaillement et les calculs de sons déjà limités en fréquence.

Sur deux nouveaux essais successifs avec l’ancienne puis la nouvelle logique comparées, le plus long pas de calcul passe de **2,08 s à 0,87 s** (environ **58 % de moins**) et le temps de calcul moyen baisse d’environ **4 %**. La durée de la phase dense reste quasiment identique, **62,43 secondes** dans les deux cas : le gain concerne surtout les gros blocages, pas une accélération sensible de toute la mêlée. La graine aléatoire varie entre les essais. Le [rapport des mêlées denses](../../analysis/medieval/dense-optimization/rapport.md) donne les mesures complètes, les contrôles et l’état de l’installation ; `tools/dense_report.py` le régénère.

### Vérification multijoueur et géographique du 10 octobre 2026

Trois parties utilisant un vrai serveur dédié TCP et des processus clients indépendants ont terminé **9 000 ticks chacune** : Deux Bourgs à deux joueurs, Trois Bourgs à trois joueurs, et le continent de 512 × 1 024 cases à deux joueurs. Les 36 points de contrôle de chaque partie donnent les mêmes états chez tous les participants, sans désynchronisation détectée. Les scénarios passent par les ordres réseau pour fonder les marchés, construire, recruter, récolter, livrer, changer l'identité du pays et combattre. Une commande envoyée au marché d'un autre joueur est rejetée. Les deux scénarios instrumentés pour compter les attaques enregistrent chacun 107 coups et 11 morts.

Les sept cartes corrigées passent le contrôle de règles. L'audit des fichiers vérifie les coordonnées isométriques, les départs reliés, les réserves proches identiques sur les continents multijoueurs et l'absence de plantes superposées. Bois, pierre et végétation sont présents dans tous les secteurs contrôlés : grille de 8 × 8 sur les continents, 4 × 4 sur les petites cartes. Cinq graines supplémentaires ont été générées deux fois avec des fichiers identiques ; deux graines différentes avec les mêmes dimensions et joueurs produisent des paysages différents. La commande de génération a également été exécutée, puis son refus d'écraser la carte existante vérifié.

Trois validations graphiques séparées avec SDL/OpenGL ont terminé : **50 traversées physiques de ponts** par soldats et charrettes, 300 vérifications de cellules d'eau bloquantes, et concordance des stocks naturels finis chargés par le moteur. Les captures sont dans `analysis/medieval/large-maps/`.

Les clients TCP ont tourné sur **un même ordinateur**, avec un module d'essai sans GPU ; le rendu graphique a été vérifié séparément. Cela ne valide pas encore deux ordinateurs distants, leurs pare-feu/NAT ou une connexion Internet dégradée. La variante immense à trois joueurs a été vérifiée géographiquement et par le contrôleur de règles ; la partie TCP sur continent utilisait deux clients. Les durées de ces essais incluent les diagnostics et ne mesurent pas les performances normales du jeu.

Le [bilan vérifiable](../../analysis/medieval/network-maps/summary.json) regroupe les résultats et limites. Les empreintes des cartes installées et du moteur sont conservées à côté. Les tests n'installent pas leur module graphique dans `bin-medieval` et utilisent des profils distincts des préférences et sauvegardes du joueur.

Pour reproduire le test TCP, compiler `tools/Medieval.NetworkTest/Medieval.NetworkTest.csproj` puis `tools/Medieval.NetworkScenario/Medieval.NetworkScenario.csproj` en Release avec le SDK local, puis lancer `py mods/medieval/tools/network_game.py deux-bourgs --deep-sync`. Les contrôles réseau ordinaires restent actifs sans `--deep-sync` ; cette option ajoute des vérifications de code visuel coûteuses sur les continents. `tools/verify_map_generation.py` régénère les candidats dans le dossier d'analyse, sans remplacer les cartes jouables. `tools/network_map_report.py` consolide les résultats et refuse un bilan dont les identifiants de cartes ne correspondent pas aux fichiers installés.

### Relief agricole, végétation et fumées

Les cultures suivent les axes isométriques 2:1, avec rebords de terre, extrémités de sillons et épis qui ondulent au vent. Les arbres gardent leurs racines en place : le feuillage fléchit progressivement, puis l’abattage pivote au pied dans la projection du terrain. Les buissons et fleurs utilisent également un ancrage au sol stable. Les colorations saisonnières restent actives.

Les cheminées émettent des volutes arrondies qui montent et dérivent avec le vent. Les incendies combinent plusieurs langues de feu, des braises et une colonne de fumée sombre et dense. Après extinction, les volutes restantes se dissipent progressivement. Les particules sont plafonnées et les détails diminuent au dézoom ; elles ne changent ni l’économie ni le hasard de simulation.

### Ombres dynamiques

Les bâtiments, clôtures, arbres, citoyens, soldats, animaux, charrettes et engins de siège projettent des ombres sur le sol. Leur direction suit la journée ; elles sont plus longues le matin, le soir et en hiver, plus courtes en été. La pluie et la neige qui tombe réduisent leur contraste. Elles disparaissent progressivement au coucher du soleil et laissent place à des ombres lunaires discrètes. Les ombres suivent les poses, le vent, l’abattage et la diminution des gisements ; les habitants endormis restent invisibles.

Le cycle reste de 8 minutes de jour et 2 minutes de nuit, avec 30 minutes par saison. Aucun réglage ni ressource supplémentaire : les ombres sont actives au lancement. Le rendu utilise les silhouettes 2D sur le sol, avec une portée limitée et les objets visibles à l’écran. Les sols agricoles et la neige des enclos sont dessinés avant les ombres. Les feux et flambeaux ajoutent une ombre locale par objet, orientée à l’opposé de la source la plus forte ; le rendu ne calcule pas une occlusion 3D sur les façades.

Vérifications : 725 tests réussis, 2 tests PNG déjà ignorés ; scénarios natifs aux quatre saisons, jour/nuit, pluie, sommeil et abattage ; validation de l’architecture et d’une carte immense au dézoom maximal. Résultats dans `analysis/medieval/shadows/verification.json`.

### Feux défensifs et chaudrons mobiles

- **Défense → Feu de camp** : 25 bois et 10 pierre, construit par les bâtisseurs. Un foyer terminé éclaire la nuit et, à 4 cases, augmente la vision des miliciens alliés de 25 % et leur détection des saboteurs de 4 à 6 cases. Le bonus disparaît lorsque le milicien s’éloigne ou le foyer est détruit. La sélection affiche le rayon d’influence.
- **Atelier de siège → Chaudron mobile** : 60 bois et 40 pierre, livrés et assemblés par les bâtisseurs. Sélectionner le chaudron puis appeler deux soldats, ou affecter deux soldats sélectionnés par leur commande d’équipage. Ils viennent à pied et le poussent ; l’engin s’arrête s’il manque un équipier. Leur logement et leurs repas restent comptés normalement. Les soldats ennemis peuvent capturer le chaudron comme les autres engins.
- Les archers alliés situés à **3 cases** d’un chaudron allumé tirent automatiquement des flèches enflammées. Les impacts directs peuvent incendier les structures ennemies, qui utilisent ensuite le système habituel de dégâts, de perte de réserves et d’extinction au puits. Un chaudron ennemi ne fournit aucune flamme à vos archers.
- Les foyers, chaudrons et incendies éclairent leur voisinage, projettent des ombres vacillantes et émettent de la fumée portée par le vent. Les panaches projettent aussi une ombre au sol. La neige fond progressivement autour des sources chaudes, y compris au sol des enclos, puis revient lentement lorsque la source disparaît ou s’éloigne. Les particules et la fonte suivent la pause ; leurs caches restent plafonnés sur les grandes cartes.
