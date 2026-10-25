# KSP1PartMod 0.2.0 : liste de tests en jeu des animations KSP1 (2026-10-25)

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

- [ ] Reprendre `docs/testing/2026-10-25-in-game-checklist.md` (le log annonce maintenant `[KSP1PartMod] loaded 0.2.0`).

## En cas de régression

- Un module qui se comporte mal : `"enabled": false` dans `animations.json` coupe toutes les animations du mod (le placement reste actif).
- Le correctif `.mu` en cause : `"fix_mu_reader": false`. Les 50 modèles de version 0 à 2 repassent alors par le modèle de remplacement de Redux.
- Pièce Redux qui ne bouge plus ou bouge deux fois : essayer `own_first_deployable` dans l'autre sens.
- **Retour à l'ancienne disposition des dossiers KSP1** : `node tools/ksp1-layout.mjs --undo`, depuis la racine du dépôt de travail. Le retour se fait par le manifeste. N'**exécutez pas** `ksp1-layout.mjs` sans `--undo` maintenant : KRE, BDB et CRP ont été copiés à la racine de `mods/KSP1` (ils n'ont pas de destination « de série »), et un nouveau rangement s'arrêterait dessus.
- Les réglages de `animations.json` sont lus au démarrage : relancer le jeu après chaque changement. `own_first_deployable` ne ramène pas un module que Redux a déjà perdu avant le prochain changement de fonctionnalités ou de session.
- Un rapport de bogue pour Redux est rédigé, **non envoyé** : `docs/redux-reports/2026-10-25-mu-reader-old-versions.md`.

## À rapporter

Les lignes `mu:`, la ligne `step start`, toutes les lignes `mod <nom>`, toutes les lignes `not bound`, les lignes `data #`, `throttle from`, `heat from`, `shader '…'`, `action labels`, `aero gate`, `gear bridge`, `restricted Redux runtime`, `Redux runtime left untouched`, et toute ligne d'erreur ou d'avertissement du mod. Dire aussi, par pièce testée : bouge / ne bouge pas.
