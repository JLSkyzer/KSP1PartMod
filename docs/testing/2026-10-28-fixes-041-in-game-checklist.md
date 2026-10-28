# KSP1PartMod 0.4.1 : liste de tests en jeu des correctifs (2026-10-28)

Cette liste couvre les correctifs du test en jeu du 2026-10-28 (0.4.0) : moteurs KSP1 à carénage, infobulle des
moteurs, troisième colonne de la grille, textures KSP1, titres `#autoLOC`. Rien n'a encore été lancé dans le jeu :
975 tests EditMode passent (1 explicite ignoré), mais aucune ligne ci-dessous n'est vérifiée en jeu.

Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`, filtrer sur `[KSP1PartMod]`.

## Avant de commencer

- [ ] Installé dans `<jeu>/mods/KSP1PartMod/` : `swinfo.json` en version **0.4.1**, `KSP1PartMod.dll`, `animations.json`
  avec les clés `fix_fairing_init`, `engine_tooltip_stats`, `subcategory_bar_keeps_grid`, `share_ksp1_textures`,
  `ksp1_textures_follow_quality`, `raise_streaming_budget`.
- [ ] Dictionnaires KSP1 copiés : `mods/KSP1/Squad/Localization/dictionary.cfg` et
  `mods/KSP1/SquadExpansion/Serenity/Localization/dictionary.cfg` présents.
- [ ] Mod activé **par campagne** (Edit Pack).
- [ ] Au démarrage, ces lignes sont présentes :
  - [ ] `[KSP1PartMod] fairing: KSP1 shroud fix armed on Module_Fairing.OnInitialize (empty base colliders), OnMaterialLoaded (skipped without Module_Color)`
  - [ ] `[KSP1PartMod] tooltip: KSP1 engine stats armed on PartInfoOverlay.PopulatePriorityInfoFromPart`
  - [ ] `[KSP1PartMod] ui: grid width kept: the parts panel widens by the sub-category bar's width while it is shown`
  - [ ] `[KSP1PartMod] textures: measure armed on Ksp1TextureLoader.LoadTexture and Ksp1ImportedPartRegistry.MarkImportComplete`
  - [ ] `[KSP1PartMod] textures: sharing armed on Ksp1TextureLoader.LoadTexture/ClearCache and Ksp1ImportedPartRegistry.DestroyPrefabGraph/ReleaseResidentPrefabs: …`
  - [ ] `[KSP1PartMod] textures: quality armed: readable KSP1 textures follow the game's texture quality`
  - [ ] `[KSP1PartMod] textures: streaming budget left as the game sets it (raise_streaming_budget false)`
  - [ ] Aucune ligne `… not found, fix off` ni `… not found, sharing off`. Sinon, recopier la ligne.

## 1. Moteurs KSP1 à carénage (VAB)

À faire avec un moteur de série à carénage : LV-T45, RE-I2 « Skiff », RK-7, Mainsail…

- [ ] Poser le moteur : **pas de cylindre rouge** sous ou dans le moteur.
- [ ] Ligne `[KSP1PartMod] fairing: <pièce> base colliders were null (Redux looks for transform 'model'), set empty` (une fois par pièce).
- [ ] Ligne `[KSP1PartMod] fairing: <pièce> has no Module_Color, Redux's OnMaterialLoaded skipped (it would throw); the KSP2 shroud stays unbuilt, as before`.
- [ ] Supprimer le moteur, puis le **reprendre dans la liste** : il apparaît dans la main et se pose à nouveau.
- [ ] Le reprendre une troisième fois : il se pose encore.
- [ ] Aucune ligne `[ASYNC] Caught Error during loading` ni `One or more OAB parts could not be finalized` pour ces pièces.
- [ ] Aucune ligne `[KSP1PartMod] fairing: fix failed, stopped for this session`. Sinon, la recopier avec sa pile.
- [ ] Attacher une pièce **sous** le moteur : pas d'erreur `NullReferenceException` avec `Module_Fairing` / `OnPartPlaced` dans le log.
- [ ] Le groupe d'action de largage du carénage : noter s'il existe et ce qu'il fait (non prévu par le correctif, à observer).

## 2. Infobulle des moteurs KSP1 (VAB)

- [ ] Survoler un moteur KSP1 : l'infobulle montre la **poussée max** (« Max Thrust »).
- [ ] Elle montre l'**Isp**.
- [ ] Elle montre l'**ergol** (« Propellants » : par exemple carburant liquide et oxydant).
- [ ] Ces lignes sont **avant** les lignes de ressources, comme pour un moteur KSP2.
- [ ] Ligne `[KSP1PartMod] tooltip: <n> engine line(s) added for '<pièce>' (first: '<clé>' = '<valeur>'); logged once` : la recopier.
- [ ] Un moteur KSP2 de série : son infobulle est inchangée (pas de ligne en double).
- [ ] La comparaison de pièces (survol avec une pièce en main) montre aussi ces lignes : à noter.

## 3. Grille des pièces (VAB)

- [ ] Choisir une catégorie KSP1 (Squad, Bluedog_DB…) : la barre des sous-catégories s'affiche et la **troisième colonne** de pièces est entière.
- [ ] Ligne `[KSP1PartMod] ui: grid kept <w> wide: picker widened by <d> for the bar (…); grid '<nom>' … , <k> column(s) fit; ship toolbar …; trash can …` : la recopier en entier.
- [ ] La **barre d'outils du vaisseau** (à droite du panneau) n'est pas recouverte par le panneau élargi.
- [ ] La corbeille est visible et utilisable.
- [ ] **Replier puis déplier** le panneau des pièces, barre affichée : la barre d'outils ne dérive pas, le panneau revient à la même largeur. Refaire deux fois.
- [ ] Passer à une catégorie KSP2 : le panneau revient à sa largeur d'origine, la barre d'outils à sa place.
- [ ] Faire une **recherche** : noter si la troisième colonne est coupée.
- [ ] Ligne `[KSP1PartMod] ui: grid during search: viewport <w>, cell <cw>x<ch>, spacing <s>, padding <l>/<r>, constraint <c> <n>, columns that fit <k>, content width <cw> (grid '<nom>' <w> wide)` : **la recopier en entier** (elle peut aussi apparaître dès l'ouverture du VAB, sans catégorie choisie : la recopier quand même et dire à quel moment elle est apparue).

## 4. Textures KSP1 (mémoire)

Même vaisseau aux deux passages (par exemple une fusée de 15 à 30 pièces KSP1, BDB de préférence), sauvegardé.

- [ ] **Passage A, `share_ksp1_textures` à true** (défaut). Charger la campagne, ouvrir le VAB, charger le vaisseau, attendre 10 s, puis passer en vol.
- [ ] Recopier toutes les lignes `[KSP1PartMod] textures: memory at <moment>: …` et `[KSP1PartMod] textures: cache at <moment>: …` :
  - [ ] `end of import (prefabs still resident)` ;
  - [ ] `after the import released its prefabs` ;
  - [ ] `VAB entry` et `VAB entry + 5 s` ;
  - [ ] `flight entry` et `flight entry + 5 s`.
- [ ] Dans les lignes `cache at`, `sharing active` après l'import, et `S kept across rebuilds by sharing` supérieur à 0.
- [ ] Aucune ligne `[KSP1PartMod] textures: sharing failed, stopped for this session …`. Sinon, la recopier avec sa pile.
- [ ] Les pièces KSP1 ont leurs textures (pas de pièce blanche, rose ou noire), dans le VAB **et** en vol.
- [ ] Une pièce à variantes de texture (changement de texture KSP1) : changer de variante, la texture suit.
- [ ] **Passage B** : quitter le jeu, mettre `"share_ksp1_textures": false` dans `animations.json`, refaire exactement le même parcours.
- [ ] Ligne `[KSP1PartMod] textures: sharing disabled by animations.json: one copy per rebuilt part, as in 0.4.0`.
- [ ] Recopier les mêmes lignes `memory at` et `cache at` que pour le passage A.
- [ ] Comparer surtout : `live KSP1 textures <n>, ~<m> MB`, `fresh load(s)`, `current` et `non-streaming`.
- [ ] Remettre `share_ksp1_textures` à true.

## 5. Titres traduits

- [ ] Dans le VAB, les pièces KSP1 de série (Squad et SquadExpansion) ont un vrai nom, **pas** `#autoLOC_…` : le Skiff (RE-I2), le LV-T45, une pièce Serenity (robotique ou science déployée).
- [ ] Le fabricant et la description dans l'infobulle sont traduits aussi.
- [ ] Les textes sont en **français**, même avec le jeu en anglais (attendu : l'installation KSP1 n'a que le dictionnaire `fr-fr`).
- [ ] La recherche du VAB trouve une pièce par son nom traduit (par exemple « Skiff »).

## 6. Interrupteurs en cas de régression

À mettre à `false` dans `mods/KSP1PartMod/animations.json` (jeu fermé), un seul à la fois, puis refaire le test concerné :

- [ ] Moteur à carénage cassé autrement, ou erreur `fairing:` : `"fix_fairing_init": false` (retour au comportement de Redux).
- [ ] Infobulle fausse ou en double : `"engine_tooltip_stats": false`.
- [ ] Panneau des pièces mal placé, barre d'outils recouverte : `"subcategory_bar_keeps_grid": false` (grille rétrécie comme en 0.4.0).
- [ ] Textures manquantes ou fausses sur des pièces KSP1 : `"share_ksp1_textures": false` (une copie par pièce comme en 0.4.0).
- [ ] Textures floues : `"ksp1_textures_follow_quality": false`.
- [ ] `raise_streaming_budget` reste à `false` (défaut) ; à n'essayer que si les textures KSP2 deviennent floues avec beaucoup de pièces KSP1, et noter les lignes `textures: budget at …`.
- [ ] Titres `#autoLOC` revenus ou faux : `node tools/ksp1-layout.mjs --undo` dans le dépôt de travail retire les dictionnaires copiés (et défait le rangement de `mods/KSP1`).
