# KSP1PartMod 0.7.0 : liste de tests en jeu des carénages de moteurs KSP1 (2026-10-28)

Redux transforme le carénage (`ModuleJettison`) d'un moteur KSP1 en `Module_Fairing` KSP2, qu'il ne sait ni dimensionner
ni colorer. Depuis 0.7.0, le mod garde le maillage KSP1 du carénage et le montre comme KSP1 : visible quand une pièce
est attachée sous le moteur, selon la variante B9, et il disparaît au découplage (ou à l'étagement pour un moteur sans
`isFairing`). Pas de masse, de traînée ni de débris. Rien n'a encore été lancé dans le jeu : les tests EditMode passent,
mais aucune ligne ci-dessous n'est vérifiée en jeu.

Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`, filtrer sur `[KSP1PartMod] shroud:` (import) et
`[KSP1PartMod] anim: … shroud` (exécution).

## Avant de commencer

- [ ] Installé dans `<jeu>/mods/KSP1PartMod/` : `swinfo.json` en version **0.7.0**, `animations.json` avec
  `"ksp1_engine_shrouds": true`.
- [ ] Campagne où le mod est activé (Edit Pack). Dans une campagne où il est désactivé : aucun carénage, aucune erreur
  `Module_Fairing`.

## 1. Démarrage et chargement

- [ ] Au démarrage : `shroud: armed: …` puis `shroud: staging signal armed on PartBehavior.ActivateAllModules`.
  Aucune ligne `shroud: … not found, shrouds off`, ni `shroud: patch failed`, ni `import patches not applied`.
- [ ] Au chargement de la campagne : `shroud: import: <p> part(s) kept their KSP1 shroud, <m> with a transform missing,
  <r> prefab rebuild(s)`, et **aucune** ligne `converted KSP1 jettison shroud` ni `no renderer geometry … to size the
  KSP2 shroud`.
- [ ] Lignes `shroud: <pièce>: shroud transform(s) '…' not found in the model` : noter les pièces concernées
  (carénage manquant ou mal placé).
- [ ] Aucune erreur `Module_Fairing` ni `OnInitialize` dans le log.

## 2. VAB

- [ ] RE-I2 seul : pas de carénage, pas de cylindre rouge. Ligne `anim: <pièce> shroud '…': n/m transform(s) found, node
  'bottom' found, isFairing …`.
- [ ] Attacher un réservoir sous lui : le carénage apparaît (`shroud 'Shroud2x3' shown (part attached below)`).
- [ ] Variante « Bare » du RE-I2 : le petit carénage remplace le grand (`variant '…' -> '…': shroud 'Shroud1p5x3'`).
  Revenir à la variante d'origine : le grand revient.
- [ ] Retirer le réservoir : le carénage disparaît (`shroud hidden (nothing attached below)`).
- [ ] Bouton « Hide shroud » du PAM : masque le carénage même avec une pièce dessous (`shroud hide override on`), puis
  « Show shroud » le rend. Le choix survit à un enregistrement et un rechargement de la fusée.
- [ ] Symétrie : le bouton agit sur les moteurs en symétrie.
- [ ] Un moteur `ignoreNodes` : carénage visible même sans pièce dessous.
- [ ] Aucun cylindre rouge, et aucun carénage fantôme à la reprise d'une fusée enregistrée en 0.6.0 (la clé
  `fix_fairing_init` reste active).
- [ ] Pas de **coque lumineuse de rentrée** ni de cylindre rouge autour d'un carénage masqué.

## 3. Vol

- [ ] LV-T45 sur un découpleur, lancer : pas de tremblement au lancement (`shroud: n collider(s) disabled in flight`).
- [ ] Découpler : le carénage disparaît avec l'étage (`shroud '…' jettisoned (bottom node detached)`), et **aucune** ligne
  `jettisoned` avant le découplage. Une perte de contact d'une ou deux images ne fait ni clignoter le carénage ni
  écrire deux lignes.
- [ ] Moteur `isFairing = False` (BDB) : le carénage disparaît à l'allumage de son étage (`jettisoned (staged)`).
- [ ] Sauvegarder après le largage et recharger : le carénage reste absent, sans ligne `jettisoned` (état restauré).
- [ ] Charger un vaisseau enregistré en 0.6.0 avec un moteur KSP1 : il se charge, un seul carénage au plus.
- [ ] Rentrée atmosphérique avec un carénage encore en place : pas de coque de rentrée incohérente autour d'un carénage
  masqué.

## 4. Audit (0.6.0)

- [ ] Avec `"audit_on_vab_entry": true` : la ligne `audit: shrouds: <k> part(s) with a KSP1 shroud kept, <m> with a shroud
  transform missing, <n> without the node, 0 still carrying Redux's Module_Fairing`. Le compte `still carrying` doit être **0**.
- [ ] La colonne `redux_fairing` du CSV est vide ; la section « Shrouds » du `.md` est présente.

## 5. Limites connues (à ne pas signaler comme des bogues)

- Les carénages `ignoreNodes` (11 cfg) gardent leur géométrie dans la coque de rentrée après un largage : seul le rendu
  disparaît.
- Un carénage `isFairing = True` avec `decoupleEnabled = False` disparaît après un découplage, alors que KSP1 le garde :
  c'est un choix du plan.
- Un carénage n'a ni masse, ni traînée, ni débris.
- Non vérifiés en jeu : les noms de transformations (`jettisonName`), le signal d'étagement
  (`PartBehavior.ActivateAllModules`), le repli au premier allumage, le coût sur une fusée de 1000 pièces.

## 6. En cas de régression

Dans `animations.json`, jeu fermé :

- [ ] `"ksp1_engine_shrouds": false` : au démarrage `shroud: off (ksp1_engine_shrouds)`, comportement de 0.6.0
  (`Module_Fairing` de Redux, avec `fix_fairing_init`).
- [ ] `"skip_modules": ["ModuleJettison"]` : ne prend plus les carénages dans les animations.
