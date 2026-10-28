# KSP1PartMod 0.9.0 : liste de tests en jeu des surcharges B9PartSwitch (2026-10-28)

Jusqu'ici Redux ne gardait d'un sous-type B9 que le visuel. 0.9.0 ajoute à chaque variante de Redux, par ses propres
transformers V-SwiFT : la **masse** (`addedMass` + `tankMass` × volume), les **réservoirs** (ressources et capacités du
type de réservoir), les **données de module** (poussée, Isp, cardan, RCS, panneaux solaires, commande), les **modules
éteints** (cardan, RCS, découpleur), les **valeurs de pièce** (`maxTemp`, `skinMaxTemp`, `crashTolerance`, `CoMOffset`)
et des **sélecteurs nouveaux** pour les pièces qui n'en avaient pas. Le coût est calculé et journalisé, **pas appliqué**.
Tout est lu dans l'IL de Redux 26w42c et testé dans l'éditeur (1907 tests EditMode, 2 explicites ignorés) ; **rien n'a
été lancé dans le jeu**.

Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`, filtrer sur `[KSP1PartMod] b9:` et `audit:`.

## Avant de commencer

- [ ] Installé dans `<jeu>/mods/KSP1PartMod/` : `swinfo.json` en version **0.9.0**. `animations.json` : si le fichier
  installé n'est pas celui de 0.8.0 il n'a pas été remplacé, y ajouter les six clés `b9_mass`, `b9_resources`,
  `b9_module_data`, `b9_module_active`, `b9_part_scalars`, `b9_new_part_switches` (toutes à `true` ; sans elles, ce sont les
  valeurs par défaut, c'est équivalent).
- [ ] Campagne où le mod est activé (Edit Pack). Garder une sauvegarde rapide avant chaque test de vol, et **garder une
  sauvegarde et un vaisseau faits en 0.8.0** pour le test des sauvegardes.
- [ ] Pièces de test (BDB, installées) : un étage à plusieurs sous-types de réservoir (Agena, Centaur…), un moteur à
  sous-types (`bluedog_Titan_Transtage`, sous-type `Transtage-34D`), `bluedog_UA120` (SRB Titan, moteur multi-modes),
  `bluedog_Saturn_Engine_H1` (cardan éteint par un sous-type), `bluedog_Ranger_Bus` (RCS éteint par un sous-type),
  `bluedog_Gemini_EquipmentModule` (deux sélecteurs de réservoir).

## 1. Démarrage

- [ ] Au démarrage : `b9: armed: mass on, resources on, module data on, module off on, part scalars on, new part switches
  on`. Aucune ligne `b9: <élément> not found, B9 overrides off`, ni `b9: tank types not read`.
- [ ] À la fin de l'import : `b9: <p> part(s), <s> set(s) (<n> new, <m> on parts that had no part switch): mass on …,
  resources on …, scalars on …; cost computed, not applied, on … variant(s)`. **Noter les nombres** (attendu d'après les
  cfg bruts : environ 60 pièces avec un set ajouté, dont 28 qui gagnent un sélecteur ; ModuleManager peut changer ces
  chiffres).
- [ ] Juste après : `b9: module data: <t> transformer(s) on <p> part(s) (Module_Engine.engineModes …, …); off forms …, no
  off form … (…); no native effect … (…)`. **Noter les nombres** (attendu d'après les cfg bruts : 191 transformers sur 79
  pièces, 33 modifications sautées sur 7 pièces, 3 formes « éteint »).
- [ ] **Aucune** ligne `b9: <pièce>: failed, Redux's variants kept`, ni `transformer … dropped`, ni `b9: stopped for this
  session after 3 failures`. Sinon recopier la ligne. Une ligne `Redux's sets differ from the plan` est notée, sans arrêt.
- [ ] `b9: module types: Module_Engine resolves true` (une ligne par type de comportement édité).

## 2. Masse

- [ ] Dans le VAB, un réservoir BDB à plusieurs sous-types (par exemple un Agena) : changer de sous-type change la masse
  affichée de l'assemblage du montant attendu. Comparer avec la ligne `b9: <pièce>: <set> -> <n> variant(s): mass a..b t,
  resources …, scalars …` (a et b sont les extrêmes du delta, en tonnes). Noter masse avant / après.
- [ ] Lancer le vaisseau, puis vérifier la même masse en vol (fenêtre d'ingénieur ou Δv).
- [ ] Un sous-type à masse négative (le delta est parfois négatif, ex. `bluedog_SRMU` : −10,67 t au plus bas) ne donne pas
  de masse totale négative ni de Δv absurde.

## 3. Réservoirs

- [ ] Le même réservoir : chaque type (LFOX, LH2, monoergol…) affiche ses ressources et capacités dans le PAM (et la
  fenêtre de ressources) ; repasser au premier type redonne **exactement** les mêmes quantités qu'au départ. En vol : mêmes
  ressources.
- [ ] Une pièce dont le réservoir n'avait aucune ressource de base montre bien le panneau de ressources
  (`no resource in the base definition, Module_ResourceCapacities added …` dans le log).
- [ ] `bluedog_Gemini_EquipmentModule` (deux sélecteurs de réservoir) : le log donne `tank resources of '…' not applied: the
  part has a second tank switch …` ; **les ressources de Redux sont gardées** à tous les sous-types, mais la masse et les
  valeurs de pièce suivent encore le sous-type.

## 4. Données de module

- [ ] Un moteur BDB dont un sous-type change `maxThrust` (`bluedog_Titan_Transtage` : `Transtage-34D` = 36,5 kN, Isp vide
  309,1 s) : la poussée max du moteur (info-bulle et Δv du VAB) change avec le sous-type, et en vol la poussée suit.
  Ligne `b9: <pièce>: <set>/<variant>: <module> -> Module_Engine.engineModes (1 field(s))`.
- [ ] **SRB du Titan, moteur multi-modes** (`bluedog_UA120`, sous-type S1SRB) : le mode principal donne **486,3 kN** et non
  1311,4 kN ; tous les modes sont édités par un seul transformer. Idem `XLR129` (6 modes).
- [ ] Un cardan désactivé par un sous-type (`bluedog_Saturn_Engine_H1`) ne bouge plus : ligne `… ModuleGimbal off in <variant>
  -> Module_Gimbal.gimbalRange …`.
- [ ] Un découpleur désactivé par un sous-type (**seulement** quand c'est le seul module stageable de la pièce) ne figure plus
  dans l'étagement ou n'est plus déclenchable. Sur les pièces qui ont un autre module stageable : ligne `off form skipped,
  the part also stages through …`, et le découpleur reste actif (attendu).
- [ ] Un RCS désactivé par un sous-type (`bluedog_Ranger_Bus`) ne pousse pas.
- [ ] Un moteur « éteint » par un sous-type **reste allumé** (`engine off form (maxThrust 0) dropped, left on`) : le Δv de
  cette pièce n'est pas NaN.
- [ ] Les 33 modifications sautées (LR91 moteur principal et verniers, AJ260 Inline, APS du S4B) : lignes `… appears 2 times,
  edits skipped (V-SwiFT edits the first only)`. Pas d'erreur, la pièce se charge comme en 0.8.0.

## 5. Valeurs de pièce

- [ ] Un sous-type qui change `maxTemp` ou `CoMOffset` : la valeur suit le sous-type (ligne du set : `scalars maxTemp, …`),
  **en vol seulement** : V-SwiFT n'applique pas ces valeurs dans le VAB (`PartScalarTransformer.ApplyInOab` est vide), qui
  montre les valeurs de base. En vol, le centre de masse est décalé (vérifier avec l'affichage du centre de masse ou la
  température maximale de la pièce), sans sauter hors de la pièce.

## 6. Nouveaux sélecteurs

- [ ] Une pièce BDB qui n'avait pas de menu de variante en 0.8.0 (sélecteur de masse ou de réservoir sans visuel) en a un
  maintenant, avec des noms lisibles (pas de clé `KSP1/Parts/…`). Ligne `b9: <pièce>: new set '<id>' for … (n subtypes,
  default '…')`. Le sous-type par défaut est celui attendu et le choix est conservé après lancement et rechargement.
- [ ] Une pièce de la liste gagnant un sélecteur (28 installées) se charge sans erreur dans le VAB et en vol.

## 7. Sauvegardes

- [ ] Charger la sauvegarde et le vaisseau faits en **0.8.0** contenant ces pièces : tout se charge, les animations gardent
  leur état. Aucune ligne `Unknown Type` ni `No serialized data of type`.
- [ ] **Un vaisseau déjà en vol** garde ses valeurs de 0.8.0 (ressources, masse). **Noter ce qu'on voit.**
- [ ] **Sauvegarder en vol, recharger, vérifier ressources et masse.** Attendu : après ce cycle les ressources et valeurs de
  pièce suivent le sous-type par défaut de 0.9.0, mais la masse reste celle de 0.8.0 (pas de delta de masse) ; après
  reconstruction de la fusée dans le VAB, tout s'applique.
- [ ] **Vaisseau 0.8.0 en arrière-plan au moment de la sauvegarde** (pas le vaisseau actif). Le prendre avec une pièce qui
  avait déjà un sélecteur visuel de Redux en 0.8.0 **et** un sélecteur B9 ajouté en 0.9.0 dont le sous-type par défaut
  n'est pas le premier (un réservoir BDB dont le premier sous-type est Structural ou un autre carburant). Charger la
  sauvegarde 0.8.0, se placer sur **un autre** vaisseau, sauvegarder, recharger, passer sur le vaisseau testé. Attendu : le
  réservoir suit le sous-type **par défaut** (pas le premier de la liste), et le log contient une fois
  `[KSP1PartMod] b9: old saves: set '<id>' was missing from a saved part state, filled with its default '<défaut>'
  (Redux's first is '<premier>')`. Sans cette ligne, ou avec `b9: … not found, old saves:`, le réservoir prend le premier
  sous-type : **le noter**.
- [ ] Au démarrage, la ligne `[KSP1PartMod] b9: old saves: a set missing from a saved part state gets its default variant`
  suit la ligne `b9: armed:`.
- [ ] Un vaisseau 0.8.0 **construit dans le VAB** (pas encore lancé) : à l'ouverture, les pièces concernées montrent les
  valeurs de 0.9.0.
- [ ] Recharger une sauvegarde faite en 0.9.0 : sous-types, masses et ressources identiques à ceux d'avant la sauvegarde.
- [ ] Charger une campagne **sans** le mod dans la même session : aucune ligne `b9:` après le chargement.

## 8. Portes 2b

- [ ] Une tuyère extensible (`ModuleDeployableEngine`) d'un sous-type « éteint » : si son sélecteur est **nouveau**
  (0.9.0), l'animation suit maintenant le choix du sous-type. Si la ligne `appended set '…' not found when planning the
  animation gates again` apparaît, la porte reste au sous-type par défaut (bénin) : la noter.

## 9. Interrupteurs

- [ ] Chaque clé `b9_*` à `false` dans `animations.json` redonne le comportement de 0.8.0 pour ce point, avec la ligne
  `b9: armed: …` correspondante (par ex. `mass off`). `b9_module_active` sans `b9_module_data` n'agit pas. Tout à `false` :
  `b9: mass, resources, module data and part scalars off, nothing armed`.
- [ ] `b9_new_part_switches: false` : les pièces du point 6 n'ont plus de menu, comme en 0.8.0.

## 10. Audit

- [ ] Lancer l'audit (`audit_on_vab_entry`) : `audit: B9 overrides: <p> part(s) with overrides, 0 part(s) where the default
  variant failed to apply`. Si `f` > 0, recopier les lignes `failed:` de la colonne `b9_overrides` du CSV.
- [ ] Le rapport Markdown a la section `B9PartSwitch (this session)` et la section `B9 overrides`.

## Non appliqué (attendu, ne pas le signaler)

Le coût (le coût du VAB reste celui de 0.8.0), les nœuds d'attache par sous-type (`node`), les décalages `TRANSFORM`,
`upgradeRequired`, le choix du sous-type d'un enfant pour le volume d'un parent (c'est son sous-type par défaut qui compte),
les ressources d'un second sélecteur de réservoir.
