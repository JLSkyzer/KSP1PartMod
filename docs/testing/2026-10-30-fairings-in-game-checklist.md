# KSP1PartMod 0.15.0 : liste de tests en jeu des coiffes (2026-10-30)

Redux n'importait ni `ModuleProceduralFairing` (bases de coiffe de série et BDB) ni `ModuleSimpleAdjustableFairing` (SAF, les
pièces `_SAF` de BDB) : une base de coiffe KSP1 n'était qu'un anneau. 0.15.0 les importe toutes deux comme la coiffe
procédurale **native** de KSP2 (`Module_Fairing`). Éditeur de coiffe, largage en coquilles, protection aérodynamique et
protection de la charge (`Module_CargoBay` de Redux) : tout est fait par KSP2.

Deux gardes rendent `Module_Fairing` utilisable sur une pièce KSP1. Elles n'agissent que sur les bases converties :
- **Parent des panneaux** : faute de transform `model`, les panneaux sont accrochés au transform de la pièce.
- **Matériau** : faute de `Module_Color`, le matériau est marqué chargé.

Les maillages KSP1 des SAF sont masqués. Tout a été lu dans la décompilation de Redux 26w42c et testé dans l'éditeur
(EditMode). **Rien n'a été lancé dans le jeu.**

Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`. Filtrer sur `[KSP1PartMod] native:`,
`[KSP1PartMod] fairing:`, `[KSP1PartMod] b9:` et `[KSP1PartMod] audit:`.

## Avant de commencer

- [ ] Le `swinfo.json` installé est en version **0.15.0**.
- [ ] L'`animations.json` installé contient `ksp1_procedural_fairings` et `ksp1_saf_fairings` à `true`. Il doit aussi avoir
  `fix_fairing_init` et `ksp1_engine_shrouds` à `true`, sinon les coiffes restent coupées.
- [ ] Faire une sauvegarde rapide avant chaque vol, dans une campagne où le mod est activé.
- [ ] Pièces de test :
  - la base de série `fairingSize1` ;
  - une base BDB à coquilles (par exemple `bluedog_AgenaB_FairingBase_Ranger`) ;
  - une SAF BDB (`bluedog_FairingBase_0p625m_SAF`).

## 1. Au chargement

- [ ] `fairing: guards armed (_modelTransform, _materialLoaded)` apparaît, sans ligne `not found, guards off`.
- [ ] `native: armed: …` cite `ModuleProceduralFairing -> Module_Fairing` et `ModuleSimpleAdjustableFairing -> Module_Fairing`.
- [ ] Les résumés `native: ModuleProceduralFairing -> Module_Fairing: <n> part(s)` et
  `native: ModuleSimpleAdjustableFairing -> Module_Fairing: <n> part(s)` apparaissent. **Recopier les deux lignes.** Attendu
  d'après les cfg bruts : 49 et 46 (ModuleManager peut changer ces nombres). Les refus éventuels (`second`,
  `ModuleJettison`, `mapping`, `failed`) sont listés.
- [ ] Pour chaque SAF : `fairing: <pièce>: SAF mesh hidden <k> transform(s), <m> not found`. **Noter les pièces où m > 0** et
  les noms cités.
- [ ] `b9: module data: …; <d> fairing definition diff(s)` : d est supérieur à 0 (rayon maximal des sous-types SAF).

## 2. VAB : base de série et base BDB

- [ ] Poser `fairingSize1` : aucun cylindre rouge, aucune erreur.
- [ ] Éditer la coiffe avec l'éditeur de KSP2 : la coiffe se construit sur l'anneau, à la bonne hauteur et au bon diamètre.
- [ ] Ligne `fairing: fairingSize1: model fallback to …` (une fois par pièce). Noter le transform cité.
- [ ] Ligne `fairing: fairingSize1: material without Module_Color, panels allowed`.
- [ ] Fermer la coiffe sur une charge. La couleur des panneaux est celle du matériau partagé (non vérifiée) : **la noter**.
- [ ] Masse : comparer la masse de la coiffe à celle de KSP1 pour une taille voisine (`UnitAreaMass` est lu en t/m², supposé).
- [ ] Symétrie et copie (Alt-clic) : chaque copie a sa coiffe.
- [ ] La base BDB s'ouvre en **deux** coquilles (`useClamshell = true`), la base de série en **quatre**.

## 3. VAB : SAF

- [ ] Poser `bluedog_FairingBase_0p625m_SAF` : **plus aucun maillage BDB** de paroi, de base ou de cône, quel que soit le
  sous-type B9 « Fairing Type ».
- [ ] La coiffe de KSP2 se construit dessus. Changer de sous-type ne change que le rayon maximal.
- [ ] La traînée de la pièce reste celle des maillages KSP1, car le cube de traînée est calculé avant le masquage
  (constat attendu, sans conséquence grave).

## 4. Vol

- [ ] Au lancement, la ligne `fairing: <pièce>: model fallback to …` apparaît **en vol aussi** pour chaque coiffe convertie
  (pas seulement au VAB). **La coiffe est visible en vol.** Si elle manque, recopier les lignes `fairing:` de la pièce.
- [ ] Les panneaux sont à la même place qu'au VAB : ni flottants, ni décalés.
- [ ] L'étagement largue la coiffe en coquilles, avec la force d'éjection du cfg. L'action Jettison marche aussi.
- [ ] Protection : une charge sous la coiffe est protégée (`Module_CargoBay`). Pas de traînée ni de chauffe sur elle avant
  le largage.

## 5. Sauvegardes

- [ ] Sauvegarder puis recharger en vol : la coiffe reste fermée, ou reste larguée si elle l'était.
- [ ] Sauvegarder puis recharger au VAB : la coiffe éditée est conservée.
- [ ] Un vaisseau de 0.14.0 avec une base de coiffe KSP1 se charge, et la base reçoit la coiffe.

## 6. Interrupteurs coupés

- [ ] Mettre `ksp1_procedural_fairings` et `ksp1_saf_fairings` à `false`. Les lignes `… off (… false)` apparaissent, aucune
  coiffe n'est créée, et les bases redeviennent comme en 0.14.0.
- [ ] Un vaisseau sauvegardé en 0.15.0 perd sa coiffe à ce chargement (attendu).

## 7. Audit (facultatif)

- [ ] `audit: fairings: <p> procedural, <s> SAF, <i> initialised, <m> with a model fallback` : i = p + s.
- [ ] `audit: shrouds: … 0 still carrying Redux's Module_Fairing`.

## Non vérifié hors du jeu (à confirmer ci-dessus)

- La position des panneaux sur le transform de la pièce, au VAB et en vol.
- Le moment où arrive le matériau (synchrone ou non).
- La couleur du matériau partagé.
- L'unité de masse.
- Le largage et l'étagement.
- La protection de la charge.
- Les noms des transforms SAF dans les vrais modèles.
- La traînée des SAF.
- Les vaisseaux de 0.14.0.
