# KSP1PartMod 0.12.0 : liste de tests en jeu de la science (2026-10-28)

Redux laissait tomber toutes les expériences KSP1. 0.12.0 importe `ModuleScienceExperiment`, `DMModuleScienceAnimate` et
`DMModuleScienceAnimateGeneric` comme le module de science **natif** de KSP2 (`Module_ScienceExperiment`, celui des
capsules KSP2) : une expérience KSP2 par expérience KSP1, lancée depuis le PAM, un groupe d'action ou le bouton Science.
Chaque expérience KSP1 prend :
- une expérience KSP2 **identique** quand il y en a une (`crewReport` → `CrewObservation`) ;
- sinon une **définition générée** `KSP1_<id>` à partir de l'`EXPERIMENT_DEFINITION` KSP1 (titre, situations, valeur,
  textes), ajoutée par le Patch Manager de Redux (donc absente d'une campagne où le mod est désactivé) ;
- sans définition générée, l'expérience KSP2 la **plus proche** de ses situations ; sans définition KSP1 sur le
  disque, l'expérience KSP2 de la table `science_fallback_ids` (comptées toutes deux « closest »).

Les biomes KSP1 deviennent les régions KSP2 du vaisseau. Une expérience qui ne transmet qu'une part de sa valeur
(`xmitDataScalar` < 1) donne un rapport de données (cette part, à transmettre) et un échantillon (le reste, à
récupérer). Les pièces KSP1 à conteneur ou à expérience **stockent** les rapports (Redux ne le fait pour aucune pièce
KSP1). Le clip KSP1 s'ouvre quand l'expérience tourne, et une expérience DMagic qui attend son clip ne se termine qu'une
fois ouverte. Non reproduits : laboratoire, réinitialisation, rendements décroissants, tests de pièces, science au sol
(Breaking Ground), expériences de kerbal KSP1, modules DMagic spécialisés.
Tout est lu dans la décompilation de Redux 26w42c et testé dans l'éditeur (2311 tests EditMode, 2 explicites ignorés) ;
**rien n'a été lancé dans le jeu**.

Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`, filtrer sur `[KSP1PartMod] science:`,
`[KSP1PartMod] native:` et `[KSP1PartMod] audit:`.

## Avant de commencer

- [ ] Installé dans `<jeu>/mods/KSP1PartMod/` : `swinfo.json` en version **0.12.0**. `animations.json` : le fichier installé
  a été gardé ; y ajouter, si on veut les voir, les onze clés `ksp1_science` (`true`), `science_generate_definitions`
  (`true`), `science_definitions_via_patch_manager` (`true`), `science_report_storage` (`true`), `science_value_scale`
  (`1`), `science_transmit_split` (`true`), `science_native_ids`, `science_fallback_ids`, `science_skip_ids`,
  `science_deploy_animations` (`true`), `science_wait_for_animation` (`true`) (copier les tables depuis le
  `animations.json` du zip). Sans elles, ce sont les valeurs par défaut, c'est équivalent. Le fichier installé le
  2026-10-28 n'a pas non plus les clés de 0.10.0 et 0.11.0 (`ksp1_converters` … `radiator_ksp1_rates`) : valeurs par
  défaut aussi.
- [ ] **Préparation : le `ScienceDefs.cfg` de série est déjà en place.** `node tools/ksp1-layout.mjs --science` (dépôt
  parent) l'a copié le 2026-10-28 depuis l'installation KSP1 dans `mods/KSP1/Squad/Resources/ScienceDefs.cfg`, inscrit
  dans le manifeste. L'outil sert à refaire la copie sur une autre machine. Grâce à ce fichier, les instruments de série
  ont leur propre expérience générée ; sans lui, ils prennent l'expérience KSP2 la plus proche, et plusieurs instruments
  partagent alors une expérience (ils ne marquent qu'une fois par lieu, ensemble).
  - [ ] Faire le **premier essai avec le fichier en place** (toute la liste ci-dessous).
  - [ ] Facultatif, un second essai sans : `node tools/ksp1-layout.mjs --undo`, puis relancer le jeu et relire les lignes
    du § 1 (chiffres « sans le fichier »). **Attention : `--undo` défait tout le manifeste**, pas seulement la science :
    le rangement de `mods/KSP1` sous `Squad/` et `SquadExpansion/`, les dictionnaires `Localization` (titres
    `#autoLOC`) et `ScienceDefs.cfg`. Pour revenir : relancer le rangement, puis `--localization`, puis `--science`.
- [ ] Une sauvegarde rapide avant chaque test de vol. Campagne où le mod est activé (Edit Pack).
- [ ] Pièces de test : un cockpit ou une capsule KSP1 (`mk1pod_v2`), le pot de goo (`GooExperiment`), la baie de
  matériaux (`science_module`), le magnétomètre de série (`Magnetometer`), un instrument DMagic de BDB (magnétomètre ou
  spectromètre), un conteneur de science KSP1.

## 1. Au chargement

- [ ] `science: definitions: read <d> EXPERIMENT_DEFINITION(s) from <f> of <n> cfg file(s) under mods/KSP1 in <ms> ms; 0
  MM edit(s) of EXPERIMENT_DEFINITION not applied`. **Attendu : d = 73** avec le fichier de série (55 de BDB + 18 de
  série) ; **55** sans. Noter le temps `<ms>`.
- [ ] `science: localization: <t> term(s) added (…)`. Aucune ligne `localization … failed`.
- [ ] `science: definitions: <g> queued through Patch Manager; skipped: …`. g = définitions construites : au plus 76 avec
  le fichier (73 + 3 variantes DMagic `KSP1_bd_massSpec_s48_b1`, `KSP1_bd_cosmicRay_s48_b1`, `KSP1_bd_gammaRay_s48_b1`) ;
  noter la valeur réelle. Aucune ligne `Patch Manager's C# science patching not found` ni `Patch Manager queued … of …`.
- [ ] Une ligne `science: definition KSP1_<id> (<titre>): …` par définition, avec un titre lisible (pas `#autoLOC_…`).
- [ ] `native: science: <p> part(s) got Module_ScienceExperiment (resolutions: <gen> generated, <id> identical, <c>
  closest); report storage on <s> part(s) (<k> container(s)); …`. **Attendu avec le fichier de série** : 286 pièces,
  310 générées, 48 identiques, 0 proche ; stockage sur 312 pièces (74 conteneurs). **Sans le fichier** : 286 pièces,
  269 générées, 48 identiques, 41 proches. Ces chiffres viennent des cfg bruts ; les patchs ModuleManager appliqués en jeu
  peuvent les déplacer un peu.
- [ ] `not imported: ModuleScienceLab 12, ModuleScienceConverter 12, ModuleTestSubject 247, ground science 19, specialised
  DMagic 0` (cfg bruts).
- [ ] `science: armed: deploy animations on, wait for animation on`. Aucune ligne `science: … not found, … off` ni
  `science: patch failed`.
- [ ] À l'ouverture de **chaque** campagne (la ligne sort que le mod soit activé ou non : le contrôle tourne dans le rappel
  du magasin d'expériences, qu'aucune fonctionnalité de campagne ne filtre), ici une campagne où le mod est activé : `science: store: <p> of <g> KSP1 definition(s) present; KSP2
  experiments: …`, **p = g attendu**. Si p = 0 : les doublons du Patch Manager n'arrivent pas au magasin ; noter la
  ligne, et refaire l'essai avec `science_definitions_via_patch_manager: false` (§ 10). Une ligne `store: KSP2
  experiments differ from 26w42c` est possible après une mise à jour de Redux : la recopier.

## 2. VAB

- [ ] L'infobulle d'un instrument BDB montre le nom de l'expérience générée, le type (données, échantillon ou les deux),
  la taille et les situations.
- [ ] Le pot de goo et la baie de matériaux montrent **leur propre** expérience (avec le fichier de série), pas une
  expérience KSP2 de relevé.
- [ ] Un cockpit KSP1 montre « Observation de l'équipage » et 1 kerbal requis.
- [ ] Aucun avertissement Unity `Parameter '' does not exist` en posant ou en saisissant une pièce de science KSP1.

## 3. Stockage

- [ ] Un vaisseau fait seulement de pièces KSP1 (cockpit KSP1 et un instrument) stocke les rapports : plus aucun
  « stockage insuffisant ».
- [ ] Un instrument BDB sur un vaisseau KSP2 lui donne aussi le stockage (sans effet visible : le stockage KSP2 n'a pas de
  capacité).
- [ ] Avec `science_report_storage: false`, le vaisseau tout KSP1 retrouve l'état de la 0.11.0 (« stockage
  insuffisant »).

## 4. Expérience

- [ ] En orbite basse de Kerbin, lancer une expérience BDB : le rapport apparaît dans l'inventaire de recherche avec une
  valeur ; ligne `science: <pièce>: report <id> stored at Kerbin_LowOrbit…, value <v>` (une fois par pièce et par vol).
  **Noter la valeur.**
- [ ] La relancer au même endroit : « déjà stocké ».
- [ ] Au sol, changer de région : nouveau rapport (les biomes KSP1 suivent les régions KSP2).
- [ ] Une expérience hors de ses situations ne se lance pas (message natif de KSP2).

## 5. Transmission et récupération

- [ ] Transmettre un rapport de données : les points de science arrivent.
- [ ] Un échantillon (pot de goo, avec le fichier de série) ne se transmet pas et rapporte à la récupération.
- [ ] Une expérience qui donne données **et** échantillon : la somme des deux vaut la valeur KSP1 (`data` + `sample` de la
  ligne `definition`).
- [ ] **Noter les valeurs** face aux expériences KSP2 du même lieu. Si elles sont trop fortes ou trop faibles, régler
  `science_value_scale` (0,01 à 100) et le noter.

## 6. Animations

- [ ] Le pot de goo et la baie de matériaux s'ouvrent quand l'expérience tourne (ligne `science: <pièce>: <id> opens
  <clé>`). Le clip reste ouvert ; il se ferme avec son bouton.
- [ ] Un instrument DMagic de BDB (magnétomètre) s'ouvre, et l'expérience ne se termine qu'une fois ouvert (ligne
  `science: <pièce>: <id> waits for <clé> (KSP2 deploy gate, <t> s)`).
- [ ] Une pièce sans clip : ligne `has no clip (<raison>)`, et l'expérience se termine quand même.
- [ ] Aucun avertissement Unity `Parameter '' does not exist` aux lancements et aux arrêts d'expérience.
  **Exception attendue** : il peut encore apparaître **une fois**, au chargement d'un vaisseau dont une expérience
  tourne (ligne `science: <pièce>: loaded with <id> running, the native trigger already fired`). Toute autre occurrence
  est un défaut : recopier le log autour.
- [ ] **Vaisseau déchargé pendant l'attente.** Lancer une expérience DMagic, puis quitter le vaisseau (centre de suivi,
  autre vaisseau) **avant** que le bras soit ouvert : l'expérience reste en pause tant que le vaisseau n'est pas
  rechargé. En y revenant, le bras s'ouvre et l'expérience se termine.

## 7. EVA

- [ ] Un kerbal en EVA prend les données du vaisseau (« Prendre les données ») et les rapporte dans la capsule.

## 8. Campagne sans le mod

- [ ] Dans la même session, une campagne où KSP1PartMod est désactivé n'a aucune expérience `KSP1_*` : ligne `science:
  store: 0 of <g> KSP1 definition(s) present (KSP1PartMod off in this campaign: expected)`.
- [ ] Les pièces KSP1 y gardent seulement les expériences KSP2 identiques ou proches.

## 9. Campagne où le mod a été désactivé après usage

- [ ] Dans une campagne où des rapports KSP1 ont été faits, désactiver KSP1PartMod (Edit Pack), recharger : **aucun
  plantage**. Lignes `science: <pièce>: <id> not in the experiment store, dropped (definitions missing in this campaign)`,
  une par pièce et par expérience retirée. La ligne `store` dit `(KSP1PartMod off in this campaign: expected)`.
- [ ] Ouvrir l'archive de R&D (inventaire de recherche) : **noter** ce qu'elle montre des anciens rapports KSP1 (rien,
  une ligne vide, un nom manquant, une erreur dans le log).
- [ ] **Avertissement.** Un vaisseau **sauvegardé de nouveau** dans une campagne sans les définitions KSP1 perd ces
  expériences pour de bon : réactiver le mod ne les rend pas. Faire cet essai sur une copie de la sauvegarde.

## 10. Interrupteurs

À éditer dans `animations.json`, puis relancer le jeu (l'import se refait au chargement) :

- [ ] `ksp1_science: false` redonne la 0.11.0 : aucune expérience sur les pièces KSP1, aucune définition générée.
- [ ] `science_generate_definitions: false` : tout passe par les expériences KSP2 (identiques ou proches) ; les
  définitions sont quand même lues (il faut leurs situations pour choisir la plus proche).
- [ ] `science_definitions_via_patch_manager: false` : mêmes définitions, ajoutées au magasin (ligne `store: <n> KSP1
  definition(s) added to the experiment store, …`).
- [ ] `science_transmit_split: false` : un seul rapport de données, à pleine valeur.
- [ ] `science_deploy_animations: false` ou `science_wait_for_animation: false` : l'expérience se termine sans attendre le
  clip.

## 11. Audit

- [ ] La ligne `audit: native modules: … <x> science part(s), 0 part(s) where one failed to initialize` compte les pièces
  de science (module d'expérience ou stockage des rapports ; attendu : environ 312 avec le fichier de série), avec **0
  échec** d'initialisation. La colonne `native_modules` du CSV porte `Module_ScienceExperiment` et `report storage` ;
  `report storage:missing` voudrait dire que l'import a perdu le drapeau de stockage.
