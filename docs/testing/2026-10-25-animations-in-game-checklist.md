# KSP1PartMod 0.2.1 : liste de tests en jeu des animations KSP1 (2026-10-25)

Phase 1 des animations : chargement des modèles `.mu` anciens, modules d'animation KSP1 recréés
sous Redux. Rien de cela n'a encore été lancé dans le jeu. Cette liste dit quoi regarder et quelle
ligne du log le prouve. La liste du placement dans l'arbre technologique reste valable :
`docs/testing/2026-10-25-in-game-checklist.md`.

## Avant de commencer

- Installé dans `<jeu>/mods/KSP1PartMod/` : `KSP1PartMod.dll`, `animations.json`, `overrides.json`,
  `tech_mapping.json`, `swinfo.json`, `localizations/`.
- Dossiers KSP1 dans `<jeu>/mods/KSP1/` : `Squad`, `SquadExpansion`, `KerbalReusabilityExpansion` (KRE),
  `Bluedog_DB` (BDB), `CommunityResourcePack` (CRP). Les `.dll` ne sont pas copiés.
- Activer le mod **par campagne** (Edit Pack), comme pour le placement. Une campagne sans le mod ne change rien.
- Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`. Filtrer sur `[KSP1PartMod]`.
  Les lignes `mu:` apparaissent au démarrage du jeu, les lignes `anim:` au chargement d'une campagne,
  celles par pièce à la création du module (VAB ou vol).
- Relever aussi, à part, tout `Failed to load KSP1 model` et `Missing KSP1 model` (lignes de Redux).

## 0. Correctifs de la 0.2.1 (premier test en jeu)

- [ ] **Types ECS** : au démarrage ou au premier chargement de campagne, deux lignes
  `[KSP1PartMod] anim: registered ECS type Module_Ksp1Animations as index <n> (…, UnityEngine.Object)` et
  `… PartComponentModule_Ksp1Animations as index <n> (…, managed component, direct, shared statics refreshed)`
  (ou `already registered`). Une ligne `ECS TypeManager not initialized yet` avant elles est normale.
- [ ] **Plus aucune** `Unknown Type: KSP1PartMod.Game.Animation.Module_Ksp1Animations` ni `[VAB] [ASYNC] Caught Error
  during loading for part …` : le Mk2Pod et la mk1-3pod se posent, se suppriment et se reposent ; les jambes
  d'atterrissage s'animent.
- [ ] Si `ECS registration failed, KSP1 animation modules disabled for this session` apparaît, suivie de
  `ECS types not registered (Failed), no animation module added` : aucune pièce ne doit être cassée (pas
  d'animation KSP1, le reste marche). Noter la raison donnée.
- [ ] **Placement aux chargements suivants** : charger une campagne, revenir au menu, en charger une autre (ou la
  même). Chaque chargement donne `placed n part(s) in m node(s)` ; plus aucune `could not start the placement coroutine`.
- [ ] **Nouveaux nœuds visibles** : ligne `[KSP1PartMod] new node <id> at (<x>,<y>) next to <ancre> (<ax>,<ay>), tree y
  range [<min>,<max>]`. Dans le R&D, le nœud est dans la zone visible, à côté de son ancre, sans chevauchement.
- [ ] **Diagnostic des matériaux** (pièces rouges, icônes noires) : dans le VAB, lignes `[KSP1PartMod] diag: …` une
  fois par pièce pour `mk1pod_v2`, `Mk2Pod`, `liquidEngine` et les 5 premières autres pièces KSP1 chargées :
  `diag: <pièce>: <n> renderer(s)`, puis par matériau `renderer '<chemin>' shader '<nom>' _Color=… _PaintA=… _PaintB=…
  _EmissionColor=… _EmissiveColor=… mask=… keywords=[…]` (seules les propriétés présentes). Ligne d'armement au
  démarrage : `diag: material dump armed for …`. Coupure : `"diagnostics": false` dans `animations.json`. Ces lignes
  décrivent le modèle du VAB après le remplacement des shaders par Redux ; les icônes viennent d'une autre copie du
  modèle, reconstruite par Redux.

## 1. Chargement

- [ ] Démarrage : une des lignes `mu:` ci-dessous. Toutes sont acceptables, mais notez laquelle :
  - `[KSP1PartMod] mu: fix enabled on Ksp1MuReader.…` : le correctif est posé (cas attendu) ;
  - `[KSP1PartMod] mu: fix skipped: Redux already reads v0-2` : Redux a corrigé lui-même, tant mieux ;
  - `[KSP1PartMod] mu: fix skipped: patched reader failed the self-check (…)` : le correctif a été retiré, à signaler avec le détail ;
  - `[KSP1PartMod] mu: fix disabled by animations.json` : `fix_mu_reader` est à `false` ;
  - `[KSP1PartMod] mu: fix skipped: <member> not found in Ksp2Redux <version>` : le lecteur de Redux a changé, le correctif n'est pas posé. À signaler avec le membre et la version ;
  - `[KSP1PartMod] mu: fix failed, Redux's reader left as it was: …` : exception pendant la pose. À signaler avec la pile.
- [ ] Aucun `Failed to load KSP1 model` ni `Missing KSP1 model` (avant le correctif : 50 modèles de version 0 à 2 échouaient). Il reste les 9 pièces `Prebuilt` sans modèle (drapeau, kerbals EVA) : elles n'en ont pas, même en KSP1.
- [ ] Si `[KSP1PartMod] anim: disabled by animations.json` apparaît au démarrage, `enabled` est à `false` : aucun module d'animation ne sera ajouté. Le remettre à `true` pour ce test.
- [ ] Au chargement de la campagne : `[KSP1PartMod] anim: step start, registry complete=True, <n> part(s)`. `registry complete=False` ou `anim: Redux's KSP1 import is not complete, no module added` : noter, aucun module ajouté.
- [ ] Une ligne `[KSP1PartMod] anim: mod <nom>: <n> part(s), <e> entries (…), <r> left to Redux, <z> none` par dossier : `Squad`, `SquadExpansion`, `KerbalReusabilityExpansion`, `Bluedog_DB`. (Comptes hors jeu : BDB 279 pièces / 1436 entrées, KRE 27 / 41, Squad 94 / 433, SquadExpansion 14 / 50.)
- [ ] Ligne finale `[KSP1PartMod] anim: added <n>, refreshed 0, untouched <u> in <x> ms` à la première charge.
- [ ] **Ne doivent pas apparaître** :
  - l'avertissement de Patch Manager `No serialized data of type …` ;
  - `[KSP1PartMod] anim: <pièce> module has no data (type not resolved?)` ;
  - `anim: step failed`, `anim: <pièce> skipped: …`, `PartJson not rewritten`, `module data setup failed`, `module init failed`.
  Leur présence signifie que le type de données du module n'est pas résolu au rechargement du JSON : c'est le risque principal de cette phase.
- [ ] Recharger la même partie (retour au menu, charger de nouveau) : `anim: added 0, refreshed <n>`, et les animations marchent toujours.
- [ ] Pas d'exception dans `Player.log` autour de ces lignes.

## 2. VAB : deux sources d'animation sur une pièce

Le VAB crée le module pour chaque pièce posée : une ligne `<pièce> module present (OAB) with <n> entries`.

- [ ] Pièce Redux à un seul déployable (baie de service, `mk3CargoBayL`) : bouge-t-elle ? Si non, mettre `own_first_deployable` à `true` dans `animations.json`, relancer, revérifier. Avec ce réglage, la ligne `<pièce> removed Redux's … (own_first_deployable)` doit apparaître au chargement.
  **Attention** : avec `own_first_deployable = true`, les soutes perdent la protection de KSP2 (`Module_CargoBay` lit `PartComponentModule_Deployable`, retiré avec le déployable de Redux). Les pièces rangées dedans ne sont plus abritées.
- [ ] Pièce à **deux** `ModuleAnimateGeneric` : deux boutons dans le PAM, deux mouvements indépendants.
- [ ] Deux exemplaires de la même pièce bougent indépendamment. Lire les lignes `<pièce> data #<n> entries … bound …` : mêmes numéros = données partagées (`shared with the definition`), numéros différents = une copie par pièce. Noter ce que vous voyez.
- [ ] `<pièce> action labels static|dynamic: <b> PAM button(s), <a> action-group action(s)` : si `dynamic`, le texte du bouton suit l'état ; si `static`, il reste fixe (acceptable).
- [ ] Les boutons d'une pièce en symétrie suivent les copies (`symmetry: '<clé>' on|off on <n> counterpart(s)`).
- [ ] Lignes `<pièce> <Kind> '<clip>' not bound: <raison>` : les relever toutes. `no Animation holds clip` pour un clip de matière est attendu (phase 2).

## 3. Vol

Lancer un vaisseau portant les pièces ci-dessous. À chaque fois, la ligne de log prouve la source.

- [ ] Réacteur Ion ou turboréacteur : la tuyère suit la manette. Ligne `<pièce> throttle from Module_Engine.throttleSetting/isOperational` (avec `while mode 'X' is active` ou `any mode`, ou `none (no Module_Engine …)`).
- [ ] Cockpit : les lumières s'allument par le groupe Lights ; ligne `<pièce> color '<propriété>' -> <n> renderer(s) via [..]` et `shader '<nom>': …` (la propriété d'émission réelle des shaders de Redux). Si `no renderer … has any of [..]`, corriger `shader_property_map`.
- [ ] Cône d'avionique chauffé : le clip suit la température. Ligne `<pièce> heat from PartComponent.Temperature/MaxTemp ('<clip>', <n> state(s))`.
- [ ] Antenne Communotron 88-88 et radiateur pliant : sortie et rentrée.
- [ ] Foreuse : déploiement.
- [ ] Parachutes : ils s'ouvrent. Leur cause (modèles de version 0 à 2) est réglée par le correctif `.mu`.
- [ ] Redux garde bien son déployable quand le nôtre est ajouté : `<pièce> restricted Redux runtime to [<clips>] (had [..], layer <L>, start extended <b>)` ou `Redux runtime disabled, it keeps no clip`. Aucun mouvement double ou contradictoire. Un clip d'une de nos entrées `not bound` revient à Redux : il figure alors dans la liste `restricted Redux runtime to [..]`.
- [ ] `<pièce> Redux runtime left untouched (<raison>)` : le module n'a pas de données utilisables (`no data, an empty one was made`, `no entry of ours` ou `no Redux clip list`), donc Redux continue de jouer toutes ses animations. `no entry of ours` est normal ; les deux autres vont avec `module has no data` et sont à signaler.

## 4. KRE (KerbalReusabilityExpansion)

- [ ] Jambes d'atterrissage : groupe Gear, touche **G**. Lignes `<pièce> gear bridge drives Module_Deployable.…` ou `gear bridge: Redux already on Gear` (attendu sur les pièces dont le cfg met déjà le déployable sur Gear : une seule bascule, pas deux qui s'annulent). Les jambes démarrent **rentrées** (choix de la spec, KSP1 les démarre sorties).
- [ ] Grilles et aile de booster : lignes `<pièce> aero gate open at <position> (<n> control surface(s), follows <contrôleur>)` à la bonne position : **grilles à 1, aile à 0**. Ligne `removed Redux's aero marker, the gate follows …` ou `aero gate follows … (no Redux aero marker on the part)`.
- [ ] Sauvegarder en position ouverte, recharger : la pièce reste ouverte (état enregistré dans `State`).

## 5. BDB (Bluedog_DB)

- [ ] Panneau solaire Agena, radiateur du LM, découpleur Apollo : rien de nouveau attendu pour les modules que Redux gère déjà.
- [ ] Une pièce à deux animations : deux boutons indépendants.
- [ ] Relever toutes les lignes `not bound` : les clips de matière sont la phase 2.

## 6. Sauvegarde et retrait du mod

- [ ] Sauvegarder en vol avec des pièces ouvertes, recharger : état conservé. Pas de ligne `saved animation state: ignored …` (si elle apparaît, noter l'entrée).
- [ ] Retirer KSP1PartMod d'une partie (Edit Pack) : la sauvegarde se charge, les pièces restent, aucune exception.

## 7. Placement dans l'arbre technologique

- [ ] Reprendre `docs/testing/2026-10-25-in-game-checklist.md` (le log annonce maintenant `[KSP1PartMod] loaded 0.2.2`).

## 8. Correctifs 0.2.2 (second test en jeu)

Au démarrage, le log doit contenir `[KSP1PartMod] loaded 0.2.2`, `[KSP1PartMod] render: material-less renderer cleanup armed on PartModuleLoadPatcher.ApplyOnGameObject` et `[KSP1PartMod] icons: KSP1 icon shader swap armed on PartIconRenderer.RenderTexture2D`.

### A. Déployables Redux qui ne bougeaient pas (jambes KRE, grilles, aile de booster, portes, panneaux, parachutes)

- [ ] Au chargement de la campagne, une ligne par mod : `[KSP1PartMod] anim: mod KerbalReusabilityExpansion: 29 Redux deployable(s) followed, 0 Redux parachute(s) followed` (attendu, d'après les cfg : Bluedog_DB 198 et 14 parachutes, Squad 36 et 5, SquadExpansion 1).
- [ ] Poser une jambe KRE (`KRE-FalconLeg-L`) dans le VAB. Lignes `anim: KRE-FalconLeg-L(Clone) follows Redux deployable 'XDeploy' (Module_Deployable)` puis `… ReduxFollower 'XDeploy' bound: on '<objet>', pose keeper disabled, Redux Animator kept; Mecanim copy '…_XDeploy_deploy' empty=<True|False> legacy=False length=… s`. **Noter la valeur de `empty`** : elle confirme (True) ou non la cause.
- [ ] Bouton Redux « Déployer la pièce » : la jambe **bouge** jusqu'à la position sortie, l'état passe à « Étendu ». Ligne `follower 'XDeploy' -> extend (Redux Extending)` (ou `(Redux Extended)`). Re-cliquer : elle rentre, ligne `-> retract`.
- [ ] Même chose avec le groupe d'action Gear (touche G en vol).
- [ ] Grille KRE et aile de booster (`BoosterWingKRE`) : elles bougent ; la ligne `aero gate … follows our reduxfollower:…` remplace maintenant `follows Module_Deployable …`.
- [ ] Une pièce de série à une seule animation (porte de soute de série, panneau solaire de série) : elle bouge.
- [ ] Une pièce **à plusieurs animations** (BDB `bluedog_LM_Shelter`, `bluedog_GATV_DockingPort`) : elle bougeait déjà, elle bouge toujours, **pas** de ligne `follows Redux deployable` pour elle.
- [ ] Parachute de série en vol : ligne `follows Redux parachute 'semiDeploy'/'fullyDeploy' (Module_Parachute)`, puis à l'ouverture `parachute follower -> SemiDeployed` et `-> Deployed` ; la voile se déploie en deux temps.
- [ ] Toute ligne `ReduxFollower '…' not bound: …` ou `ChuteFollower '…' not bound: …` : la recopier.
- [ ] En cas de problème : `"follow_redux_deployables": false` dans `animations.json` revient au comportement de 0.2.1.

### B. Zones rouges sur les pièces

- [ ] Lignes `[KSP1PartMod] render: <pièce> hid <n> renderer(s) without material (<chemins>)` : les recopier (une par nom de pièce).
- [ ] Lignes `[KSP1PartMod] render: <pièce> reentry hull(s): <n> renderer(s) without material, <m> active (left to ReentryMesh)` (les 5 premières pièces) : si `m` vaut 0 partout et que les zones rouges restent, la coque de rentrée n'est pas en cause ; le dire.
- [ ] Ligne `… deactivated <n> reentry hull(s) without material (…)` : la recopier.
- [ ] Le cylindre et les bandes rouges ont-ils disparu (VAB, vol) ? Rentrée atmosphérique : l'effet de rentrée apparaît toujours.
- [ ] En cas de problème : `"hide_material_less_renderers": false`.

### C. Catégories KSP1 absentes quand le jeu démarre dans le VAB

- [ ] Quitter le jeu depuis le VAB, relancer, charger la partie (elle s'ouvre dans le VAB) : les boutons de catégorie KSP1 (Squad, KRE, BDB…) et leurs pièces sont là **sans** ressortir du VAB.
- [ ] Lignes, dans cet ordre : `placed 1776 part(s) in …`, `[KSP1PartMod] placed after 'Loading Science Data', before the VAB builds its part list`, puis plus tard `[KSP1PartMod] already placed before the VAB, nothing to do at campaign loaded`. Si à la place on lit `not ready after 'Loading Science Data' (…)`, recopier la ligne.

### D. Icônes noires

- [ ] Lignes `[KSP1PartMod] icons: rendered 1 KSP1 icon(s) with hotswapPartShaders=true (KSP2/Parts/Paintable -> KSP2/Scenery/Standard (Opaque)); last '<pièce>' had [<shaders>]`, puis `rendered 10 …`, `100 …`. Recopier la liste `[<shaders>]` : si elle ne contient pas `KSP2/Parts/Paintable`, l'hypothèse est fausse.
- [ ] Les icônes des pièces KSP1 et des boutons de catégorie KSP1 ne sont plus noires.
- [ ] En cas de problème : `"fix_part_icons": false`.

### E. Placement enregistré deux fois

- [ ] Une seule ligne `[System] Place KSP1 parts in the tech tree completed` par chargement, aucune ligne `could not start the placement coroutine`.

## En cas de régression

- Un module qui se comporte mal : `"enabled": false` dans `animations.json` coupe toutes les animations du mod (le placement reste actif).
- Le correctif `.mu` en cause : `"fix_mu_reader": false`. Les 50 modèles de version 0 à 2 repassent alors par le modèle de remplacement de Redux.
- Pièce Redux qui ne bouge plus ou bouge deux fois : essayer `own_first_deployable` dans l'autre sens.
- **Retour à l'ancienne disposition des dossiers KSP1** : `node tools/ksp1-layout.mjs --undo`, depuis la racine du dépôt de travail. Le retour se fait par le manifeste. N'**exécutez pas** `ksp1-layout.mjs` sans `--undo` maintenant : KRE, BDB et CRP ont été copiés à la racine de `mods/KSP1` (ils n'ont pas de destination « de série »), et un nouveau rangement s'arrêterait dessus.
- Les réglages de `animations.json` sont lus au démarrage : relancer le jeu après chaque changement. `own_first_deployable` ne ramène pas un module que Redux a déjà perdu avant le prochain changement de fonctionnalités ou de session.
- Un rapport de bogue pour Redux est rédigé, **non envoyé** : `docs/redux-reports/2026-10-25-mu-reader-old-versions.md`.

## À rapporter

Les lignes `registered ECS type`, `new node`, `diag:`, `mu:`, la ligne `step start`, toutes les lignes `mod <nom>`, toutes les lignes `not bound`, les lignes `data #`, `throttle from`, `heat from`, `shader '…'`, `action labels`, `aero gate`, `gear bridge`, `restricted Redux runtime`, `Redux runtime left untouched`, `follows Redux`, `follower`, `render:`, `icons:`, `placed after`, et toute ligne d'erreur ou d'avertissement du mod. Dire aussi, par pièce testée : bouge / ne bouge pas.
