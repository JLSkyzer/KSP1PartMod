# KSP1PartMod : liste de tests en jeu (2026-10-25)

Rien de ce mod n'a encore été lancé dans le jeu. Cette liste dit quoi vérifier et quelle ligne du log le prouve.

## Avant de commencer

- Installation : `mods/KSP1PartMod/` contient `KSP1PartMod.dll`, `swinfo.json`, `tech_mapping.json`, `overrides.json` et `localizations/`. Les pièces KSP1 sont dans `mods/KSP1/`.
- Le mod doit être **activé par campagne** : dans l'écran de création, Edit Pack ; ou, avant, Default Features dans le menu Mods. Sans cela, il ne fait rien (c'est voulu).
- Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log` (pas `Ksp2-2.log`). Filtrer sur `[KSP1PartMod]`.
- Les lignes `placed` n'apparaissent qu'après le chargement de la campagne, pas au menu principal.

## 1. Chargement et placement

Créer une **nouvelle campagne d'exploration** avec KSP1PartMod activé (Edit Pack), puis la charger.

- [ ] Le log contient `[KSP1PartMod] loaded <version>` (0.2.0 avec les animations) au démarrage du jeu.
- [ ] Au chargement de la campagne : `[KSP1PartMod] placed N part(s) in M node(s), created K node(s) in X ms`, avec N > 0.
- [ ] Une ligne `[KSP1PartMod] mod <nom>: A placed (B table, C override, D fallback, E catch-all), F hidden, G kept` par dossier de `mods/KSP1`. Pour les dossiers KSP1 de série, `E` doit valoir `0 catch-all`. Noter les `E` non nuls : ce sont les pièces à ajouter à la table ou à `overrides.json`.
- [ ] Pas de ligne `placement failed`, `gave up` ni `tech_mapping.json: …` (avertissement de validation). Si `gave up: import complete=…, tree ready=…` apparaît, l'import KSP1 ou l'arbre n'était pas prêt en 30 s : noter lequel est `false`.
- [ ] Les lignes `[KSP1PartMod] fallback: <pièce> (<mod>, TechRequired=…, <catégorie>/<taille>) -> <nœud>` listent les pièces sans entrée dans la table : à relire pour juger si le nœud choisi est sensé.

## 2. Centre de R&D

- [ ] Des pièces KSP1 apparaissent dans les nœuds de la table.
- [ ] Les nœuds ajoutés par le mod (robotique, divers) n'existent que s'ils reçoivent des pièces. Ils affichent un **vrai nom** (pas une clé du type `KSP1PartMod/TechNodes/Names/…`), en anglais et en français.
- [ ] Ces nœuds ne chevauchent aucun autre nœud, et leurs liens vers les nœuds requis sont corrects. Sinon, noter leur position : l'ancre et le décalage sont dans `new_nodes` de `tech_mapping.json`, et le sens du décalage vertical est supposé, pas vérifié.
- [ ] Ouvrir le R&D, fermer, rouvrir : pas de doublon de nœud.

## 3. Atelier d'assemblage (VAB)

- [ ] Une pièce KSP1 d'un nœud **débloqué** est utilisable dans le VAB.
- [ ] Une pièce KSP1 d'un nœud **verrouillé** ne l'est pas.
- [ ] Débloquer un nouveau nœud portant des pièces KSP1 : elles deviennent utilisables sans redémarrer.

## 4. Sauvegarde et rechargement

- [ ] Débloquer un nœud (y compris un nœud créé par le mod), sauvegarder, quitter vers le menu, recharger : le nœud est toujours débloqué et ses pièces utilisables.
- [ ] Le log du rechargement contient de nouveau une ligne `[KSP1PartMod] placed …` avec les mêmes nombres.
- [ ] Au rechargement, les lignes `mod <nom>: …` des dossiers KSP1 de série doivent afficher `0 kept`. Un `kept` non nul est le signe d'un défaut : des objets de nœud partagés entre deux arbres.
- [ ] **Sauvegarde sans le mod** : débloquer un nœud `tNode_ksp1_*`, sauvegarder, puis désinstaller KSP1PartMod (ou déplacer toutes ses pièces ailleurs par une surcharge) et recharger cette sauvegarde. Ouvrir le R&D et le VAB : la sauvegarde se charge et `Player.log` ne contient aucune exception. Cela vérifie la promesse de la spec : une sauvegarde se recharge encore une fois le mod retiré.

## 5. Campagne sans le mod, dans la même session

Le jeu reconstruit l'arbre à chaque chargement et l'action est conditionnée à la fonctionnalité du mod : on ne cherche donc **pas** de ligne de retrait. La doc officielle de `SaveLoad.OnCampaignUnload` dit que le retrait s'exécute en quittant vers le menu, avant qu'un autre chargement remplace la campagne, et quand un chargement échoue ; une lecture de l'IL (2026-10-25) n'a trouvé que l'appelant du chargement échoué. À trancher en jeu : le code est correct dans les deux cas (`Place` retire d'abord ses ajouts, et le magasin est reconstruit).

- [ ] Depuis le menu, charger (ou créer) une campagne **sans** KSP1PartMod activé.
- [ ] Aucun nœud `tNode_ksp1_…` dans le R&D, et aucune pièce KSP1 placée par le mod.
- [ ] Aucune nouvelle ligne `[KSP1PartMod] placed` pour ce chargement.
- [ ] Revenir sur la première campagne : le placement refait son travail (`placed …` à nouveau).

## 6. Autre mod de pièces KSP1

- [ ] Ajouter un mod de pièces KSP1 (CurseForge) dans `mods/KSP1`, redémarrer le jeu, charger la campagne.
- [ ] Une ligne `[KSP1PartMod] mod <son nom>: …` apparaît et ses pièces sont dans le R&D, sans avoir touché à KSP1PartMod.
- [ ] Noter la part de `fallback` et de `catch-all` pour ce mod : c'est la mesure de la qualité du repli par catégorie.

## 7. Surcharges (`overrides.json`)

Dans `mods/KSP1PartMod/overrides.json`, par exemple :

```json
{ "parts": { "<nom de pièce KSP1>": "tNode_2v_aero_01" }, "ksp1_nodes": {} }
```

- [ ] Après redémarrage, la pièce est dans le nœud indiqué, et le résumé du mod compte une pièce en `override`.
- [ ] Un identifiant de nœud inexistant est ignoré, la pièce suit la règle suivante (table, repli), et le log contient `[KSP1PartMod] note: <pièce> (<mod>): … <id> is not in the tree`.
- [ ] Un fichier JSON invalide donne `[KSP1PartMod] overrides.json: …` et le placement continue sans surcharge.

## Limites connues

- Les pièces de robotique KSP1 sont réparties sur plusieurs nœuds KSP1 : seules celles du nœud `actuators` vont dans le nouveau nœud de robotique.
- Les animations et les modules non pris en charge par Redux sont hors du périmètre du mod.
- Tout ce qui précède est non vérifié en jeu à cette date.
