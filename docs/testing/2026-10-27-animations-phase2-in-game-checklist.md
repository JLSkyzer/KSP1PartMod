# KSP1PartMod 0.4.0 et 0.5.0 : liste de tests en jeu des animations KSP1, phase 2 (2026-10-27)

Cette liste couvre la **section 2a** : courbes de matériau et de lumière des clips KSP1, lueur des
moteurs et de la chaleur, activation de l'émission. La **section 2b** (0.5.0, en bas : modules de plugin et portes B9) est
ajoutée à la fin. Rien de cela n'a encore été lancé dans le jeu : 888 tests EditMode
passent (1 explicite ignoré), mais aucune ligne ci-dessous n'est vérifiée en jeu.

Phase 1 (0.2.x à 0.3.2) : `docs/testing/2026-10-25-animations-in-game-checklist.md`.

## Avant de commencer

- Installé dans `<jeu>/mods/KSP1PartMod/` : `KSP1PartMod.dll` (0.4.0), `animations.json` (nouveau, avec les
  clés de la phase 2a et la nouvelle table `shader_property_map`), `overrides.json`, `tech_mapping.json`,
  `swinfo.json`, `localizations/`. Si tu gardes un ancien `animations.json`, il manque les clés
  `material_curves`, `light_curves`, `emission_setup`, `throttle_effects`, `heat_glow`, `skip_modules`,
  `perf_counter` (les valeurs par défaut s'appliquent, c'est équivalent) mais pas la nouvelle table :
  `_EmissionColor` passe avant `_EmissiveColor` pour `_EmissiveColor`, et `_TintColor` / `_Color` sont ajoutées.
- Activer le mod **par campagne** (Edit Pack). Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`,
  filtrer sur `[KSP1PartMod]`.
- Les lignes `anim: tracks: …` (sans nom de pièce) viennent d'un fil de travail ; les autres, par pièce, sont
  écrites quand le module de la pièce se lie (VAB ou vol).

## 2a. Courbes de matériau, de lumière et lueurs

### Chargement

- [ ] **Résumé du fil de travail** : `anim: tracks: read <n> model(s) for <p> part(s) in <ms> ms on a worker; <c> clip(s) with <t> track(s); <f> failed`
  avec `f` = **0**. Sinon recopier les lignes `anim: tracks: <pièce> failed: …` et `anim: tracks: <pièce> … not found`
  (ou toute ligne `anim: tracks: <pièce> …` qui précède le résumé). La ligne arrive **après**
  `anim: step start, registry complete=True, <n> part(s)` ; le chargement de la campagne ne doit pas être plus long
  qu'en 0.3.2 (le temps `<ms>` est celui du fil, pas du chargement).
- [ ] Lignes `anim: tracks: <pièce> dropped <a> m_DrawHalo, <b> audio, <c> particle curve(s)` : informatives
  (courbes que le mod ne joue pas), pas des erreurs.
- [ ] `anim: added 0, refreshed <n>` en rechargeant la **même** partie ; une sauvegarde de 0.3.2 se charge ; les
  portes, jambes et trains gardent leur état.

### Matériaux et émission

- [ ] **Shader d'une pièce KSP1** dans le VAB **et** en vol : `anim: <pièce> shaders: [...]` (règle : Paintable ou
  Scenery/Standard). Recopier la liste si elle contient autre chose.
- [ ] **Décision d'émission**, une ligne par (shader, matériau) :
  `anim: emission on '<shader>' material '<nom>': keyword _EMISSION <declared|absent> <on|off>, _EmissionMap <set|none|absent> -> <action>`
  où `<action>` est `own material copy with keyword on`, `keyword left alone (absent)`, `white _EmissionMap`,
  `no action` ou `no action (…)`. Les trois états de `_EmissionMap` (`set`, `none`, `absent`) sont possibles.
  Recopier les lignes des pièces qui ne s'allument pas.
- [ ] **LV-T45 en vol** : la tuyère rougit avec la manette. Lignes attendues : `anim: <pièce> tracks '<clip>': host
  '<chemin>' x1, <r> renderer(s) via [_EmissionColor], <l> light(s), 0 unbound (...)` puis l'émission ci-dessus.
  Refaire avec `"emission_setup": false` : la lueur doit disparaître (ou rester invisible), ce qui prouve que
  l'émission de KSP2 était éteinte.
- [ ] **Contrôle du bloc de propriétés** : `anim: <pièce> mpb check '<prop>': wrote <v>, read <v'>` avec `read` = `wrote`
  (à 1e-4 près ; une précision `(the block held <v''> after our write)` signale seulement l'arrondi de l'espace Linear).
  - Si les valeurs diffèrent : un autre écrivain remplace le bloc après notre `LateUpdate`. Recopier la ligne et la
    pièce ; le repli se déclenche seul, avec l'avertissement `anim: <pièce> mpb check read another value: every
    written renderer is flushed again at render time`. La lueur doit alors s'afficher quand même.
  - `mpb check '<prop>': wrote <v>, renderer gone before the render` : le renderer a disparu avant le rendu, sans conséquence.
- [ ] **Contrôle du bloc en rentrée** (cône ou pièce KSP1 sous une coque de rentrée) : à la première rentrée de chaque
  type de pièce, `anim: <pièce> reentry hull active on '<chemin>': mpb check armed`, puis
  `anim: <pièce> mpb check '<prop>' at reentry: wrote <v>, read <v'>`. Recopier les deux lignes.
  - `read` = `wrote` : notre écriture passe après `ReentryMesh`, `[DefaultExecutionOrder]` est appliqué.
  - `read` différent : `ReentryMesh` écrit après nous ; l'avertissement `anim: <pièce> mpb check at reentry read another
    value: the renderers under an active reentry hull are flushed again at render time` suit, et la lueur doit
    s'afficher quand même. Le noter : la comparaison `heat_glow` ci-dessous n'est lisible qu'avec cette ligne.
- [ ] **Lignes `rebound` attendues, pas des erreurs** : une ligne `anim: <pièce> renderer '<chemin>' rebound: material
  changed (<ancien shader> -> <nouveau>)` par renderer surveillé à **la première activation de la coque de rentrée**
  (l'écrivain `ReentryMesh` change le matériau ; Unity rend ensuite les mêmes instances, une absence de la ligne aux
  activations suivantes est normale). Une ligne `anim: <pièce> <n> renderer(s) rewritten every frame while
  their reentry hull is active` accompagne la pièce concernée. Un déluge de `rebound` en dehors d'une rentrée,
  lui, est un défaut : recopier.
- [ ] **VAB, variante** : changer la variante d'une pièce KSP1 à variantes ; la lueur continue (un `rebound` est
  attendu si le matériau change).

### Moteurs et chaleur

- [ ] **Moteur BDB** : `anim: <pièce> color '<moduleID>' follows FXModuleThrottleEffects (<entrée>, response <r>)`
  avec `<entrée>` du type `throttle from <champ>` ou `main throttle from <champ>` ; en plus une fois par champ
  `anim: throttle from <champ>` (et éventuellement `anim: output read as throttle`). La lueur **suit la manette**
  en vol, s'éteint à la coupure.
- [ ] **Cône d'avionique en rentrée** (ou autre pièce chauffée) : lueur KSP1 de chaleur. Comparer avec
  `"heat_glow": false` : si KSP2 fait déjà rougir la pièce, la lueur est **doublée** avec `true`. Dans ce cas,
  laisser `false` et le noter. Avec `false`, le log dit `anim: heat glow off by animations.json` (une fois) et, par pièce,
  `anim: <pièce> Heat '<clip>' not built: heat_glow off`.
- [ ] Moteur ionique : la couleur change (courbe de matériau du clip). Bloc RCS : rien de nouveau avant 2b.

### Lumières

- [ ] **GATV** : les lumières d'amarrage s'allument au bouton de la PAM. Lignes : `anim: <pièce> <Kind> '<clip>'
  bound tracks-only (no state), length <s> s` et, pour les lumières que Redux pilote déjà (`Module_Light`),
  `anim: <pièce> light '<chemin>' left to Module_Light` (pas de double commande).
- [ ] **Cockpit Mk1** : lumières du groupe Lights ; couleur, intensité, portée suivent le clip.
- [ ] Clip sans propriétaire : `anim: tracks: <pièce> clip '<clip>' has no owner, tracks skipped` : informatif.

## Non vérifié, à observer et à signaler

- `normalizedOutput` du moteur est lu comme `FinalThrustValue / maxThrust` : la lueur suit-elle bien la poussée
  réelle et pas seulement la manette ?
- `[DefaultExecutionOrder]` sur un type du mod : non prouvé. Le `mpb check` (par type de pièce) et sa variante en
  rentrée le détectent, le repli au rendu corrige ; il faut lire les deux lignes, la rentrée étant le seul cas où un
  écrivain du jeu remplace notre bloc à chaque image.
- Visibilité de l'émission : la table des propriétés (`_EmissionColor` en premier) est une hypothèse issue des
  shaders vus ; `anim: shader '<nom>': …` (une ligne par shader, diagnostic) liste ce qui existe vraiment.
- Double lueur avec la chaleur de KSP2 (`heat_glow`).

## Coût

- `"perf_counter": true` (désactivé par défaut) ajoute toutes les 60 s `anim: perf: <n> module(s), <e> entr(ies),
  <w> write(s), <ms> ms over 60 s`. À activer seulement pour un rapport de performance.

## En cas de régression

Dans `animations.json`, un à un :

- `"material_curves": false` et `"light_curves": false` : retour au comportement de 0.3.2 pour les courbes
  (le log dit `anim: tracks: off (material_curves and light_curves false)` quand les deux le sont) ;
- `"emission_setup": false` : plus de copie de matériau ni de `_EmissionMap` blanche ;
- `"throttle_effects": false` : plus de lueur BDB liée à la manette ;
- `"heat_glow": false` : plus de lueur de chaleur ;
- `"skip_modules": ["FXModuleThrottleEffects"]` : écarte un module KSP1 qui se comporte mal ;
- `"editor_animation_speed"` à 1, 1000, 0 : comportement de 0.3.2 inchangé (à vérifier aussi).

---

# Section 2b (0.5.0) : modules de plugin et portes B9

**Un seul test en jeu pour tout** : on commence par la liste de 0.4.1
(`docs/testing/2026-10-28-fixes-041-in-game-checklist.md`), puis la section 2a ci-dessus, puis cette section 2b.
Rien de la 2b n'a encore été lancé dans le jeu : les tests EditMode passent, mais chaque ligne ci-dessous reste
à vérifier. Toutes les lignes de log commencent par `[KSP1PartMod] anim:`.

Règle de la 2b : **purement visuelle**. Le mod lit `Data_Engine`, `Data_Decouple`, `Data_RCS`, `Data_PartSwitch`
et `Data_Deployable`, il ne les écrit jamais, et n'appelle aucune méthode de moteur, de découpleur ni d'étagement.
L'allumage et la séparation ne sont pas retardés (le délai KSP1 est seulement affiché dans la ligne de log) ; la
science n'est pas lancée. Si un moteur s'allume ou un étage se sépare différemment d'avant, c'est un défaut.

## 2b.1 Portes B9 (VAB, RL10 de Bluedog_DB)

- [ ] Au bind : `<pièce> <entrée> gated by B9 '<jeu>' (Data_PartSwitch set <k>), inactive in [<ids>]`. Le
  `(Data_PartSwitch set <k>)` doit être présent, **pas** `(default '<id>')` : sinon l'identifiant du jeu de
  variantes ne correspond pas à celui de Redux. Recopier aussi `<pièce> variants at bind: [<jeu>=<id>, …],
  Module_PartSwitch initialized <true|false>` (règle l'ordre d'initialisation des modules).
- [ ] Changer le **sous-type** du RL10 dans le VAB : à chaque choix `<pièce> B9 '<jeu>' -> '<id>': <entrée>
  active|inactive` (une ligne par entrée gardée, aussi écrite une fois au bind avec la sélection de départ), et
  **une seule tuyère active** (certains sous-types n'en ont aucune : tuyère fixe, attendu). Ligne jumelle :
  `B9 '<jeu>' -> '<id>': transforms of '<id>' active <n>/<m>` (aussi au bind) : si `n` < `m` alors que le
  sous-type est choisi, le `TransformActivator` de Redux ne trouve pas les objets (recherche directe sous la
  racine, constatée dans le log du 2026-10-25 avec `[VSwift] Could not find child of …`) et la tuyère visible ne
  suit pas la variante : à signaler.
- [ ] `<pièce> rebind Renderers after variant '<jeu>' -> '<id>'` quand la variante change. **Changement en 0.5.0** :
  un rebind de variante n'écrit plus de ligne `rebound` par renderer (la lueur de 2a continue après le choix) ;
  s'il y en a un déluge, recopier.
- [ ] Le **bouton d'aperçu** de la tuyère (PAM du VAB) joue à la vitesse `editor_animation_speed` (1 : réelle).
- [ ] Une pièce **symétrique** : chaque exemplaire garde sa propre porte (données copiées).
- [ ] Lignes d'erreur, aucune attendue : `variant watch failed, B9 gates on their default subtype`,
  `variant watch stopped for this session, B9 gates frozen`, `rebind <portée> after <raison> failed`.

## 2b.2 Tuyères qui sortent (vol, RL10 et XLR129)

- [ ] RL10 : `<pièce> engine deploy '<clip>' follows Module_Engine.EngineIgnited; ignition not delayed (KSP1 waits
  until <s>)`. À l'allumage la tuyère **sort** ; à l'extinction elle rentre si elle est rétractable (sinon elle
  reste dehors). Le moteur s'allume sans délai : c'est voulu.
- [ ] XLR129 (multi-mode) : `<pièce> engine mode '<clip>' follows Data_Engine.currentEngineModeData.engineID,
  time-zero mode '<id>'`. Changer de mode : la tuyère suit, aussi dans le VAB (pose immédiate au bind).
- [ ] Une partie **chargée avec le moteur déjà allumé** montre la tuyère sortie, sans rejouer l'animation.
- [ ] Si une entrée est écartée : `<pièce> EngineDeploy '<clip>' disabled: Module_Engine with Data_Engine not found`.

## 2b.3 Vérins qui suivent la tuyère

- [ ] Un moteur à vérins (J-2 ou autre moteur à cardan avec `ModuleAdvancedLookAtConstraint`) :
  `<pièce> advanced look-at: <n> constraint(s), 0 missing transform(s)` (`missing` = **0**). Les vérins
  **suivent la tuyère** quand elle bascule au cardan, sans dériver image après image (la rotation en cours sert
  de base à chaque image, comme KSP1).
- [ ] `<pièce> look-at '<rotateur>' left to Redux` : le rotateur appartient aussi à un `FXModuleLookAtConstraint`
  que Redux lit déjà ; il est laissé à Redux, pas de double commande.

## 2b.4 Découpleurs animés

- [ ] Découpleur Apollo (BDB) : `<pièce> decoupler '<clip>' plays on Data_Decouple.isDecoupled; separation not
  delayed (KSP1 waits until <s>)`. À l'étagement l'ombilical **tourne** à la séparation ; après rechargement de
  la sauvegarde, il est dans sa **pose finale** (pas rejoué).
- [ ] **Premier `Module_Decouple` seulement** (`decouplerNodeID` n'est pas lu) : vérifier sur une pièce BDB à
  **deux découpleurs** que l'animation suit le bon ; sinon noter la pièce.
- [ ] Étiquette par défaut du bouton du VAB (seul 1 jeu BDB sur 13 a un `editorGUIName`) : lisible ?

## 2b.5 Modules petits et DMagic

- [ ] Bras DMagic : `<pièce> science toggle '<clip>' (visual only)`. Boutons de la PAM et aperçu du VAB ;
  **aucune science** n'est lancée ni enregistrée. Un bras à plusieurs modules : seul le **premier** module
  anime le clip (98 entrées planifiées sur 111), les autres n'ont pas de bouton propre.
- [ ] Ressort d'accélération : `<pièce> accel spring from VesselComponent.AccelerationOrbital-graviticAcceleration`
  en vol, `… from editor 1 g` dans le VAB, `… from none, held` sinon. En poussée la pièce **bouge** puis se
  recentre ; son bouton bascule l'ancre. Le modèle du ressort est de nous (formule dans le plan), à comparer à KSP1.
- [ ] Sac Mercury : bouton de PAM ; le sac se déploie, la rentrée n'est pas perturbée.

## 2b.6 Lueur et émission des RCS

- [ ] ERCS et bloc RCS de série : `<pièce> rcs glow: <n> thruster(s), <h> host(s) matched` (pas `count
  mismatch`). À l'impulsion les tuyères s'allument (clip de matériau seul : `bound tracks-only`).
- [ ] `<pièce> rcs glow first values: force <f>, maxThrust <m>, scalar <s>` : `RCSForce.RelativeForce` est lue en
  **kN** ; `scalar` doit monter jusqu'à ~1 à la poussée pleine, pas saturer ni rester à 0. Recopier la ligne.
- [ ] `<pièce> rcs emissive -> <n> renderer(s) via [<propriétés>]` (bloc `ModuleBdbRCSEmissive`), ou `rcs emissive
  no renderer of <n> found has any of [...]` : alors vérifier `shader_property_map`. Le premier `MeshRenderer` sous
  la tuyère est supposé être le maillage émissif (les maillages frères ne sont pas cherchés) ; la plume de poussée
  de Redux reste **hors** de la lueur : si elle brille, le noter. `<n>` peut surestimer.
- [ ] Sous warp sur rails la poussée RCS est zéro : la lueur s'éteint (attendu).
- [ ] RCS au repos (aucune commande) : le maillage de la tuyère ne brille **pas** (couleur émissive prémultipliée,
  noire à la poussée nulle). S'il brille quand même, ajouter `"ModuleBdbRCSEmissive"` à `skip_modules` dans
  `animations.json` et le signaler.

## 2b.7 Cache-maillages

- [ ] Pièce à `ModuleBDBDeployableMeshHider` : `<pièce> mesh hider: <n> object(s) follow our entry '<clé>'` (ou
  `follow Redux Data_Deployable`). Objets **masqués rentrée**, **visibles sortie**.
- [ ] Le cache **laisse en paix les objets masqués par B9** : on ne rend visible que ce qu'il a lui-même masqué.
  Changer le sous-type d'une pièce à cache-maillage : aucun objet de l'autre variante ne réapparaît.
- [ ] Objets introuvables : `<pièce> MeshHider '<clé>' disabled: transform(s) '<noms>' not found`.

## 2b.8 Propriété des clips et coût

- [ ] `anim: <Kind> entries planned but not driven yet in this version, left at rest (first: …)` ne doit **plus**
  apparaître pour aucun des dix genres de la 2b (sinon un genre n'a pas son moteur).
- [ ] Un clip n'est jamais joué par deux entrées : sur un bras DMagic à plusieurs modules, un seul propriétaire.
- [ ] **Grande fusée BDB (≥ 1000 pièces)** : activer `"perf_counter": true` et lire `anim: perf: <n> module(s),
  <e> entr(ies), <w> write(s), <ms> ms over 60 s` (cible : ≤ 0,3 ms par image à moteur constant ; non mesuré).
  Les chargements ne doivent pas être plus longs qu'en 0.4.1.
- [ ] `editor_animation_speed` à 1, 1000 puis 0 sur les **nouveaux aperçus** (tuyères, découpleur, bras DMagic,
  sac Mercury) : 1000 = instantané, 0 = valeurs de KSP1 (tuyère et découpleur 1, mode moteur 2).

## 2b. Non vérifié, à observer et à signaler

- Identifiants de jeu et de variantes B9 = ceux de Redux (`(Data_PartSwitch set <k>)`, `B9 '<jeu>' -> '<id>'`).
- `Data_PartSwitch.ActiveVariants` change à chaque choix du VAB, y compris sur les exemplaires symétriques (relu
  par sondage à chaque image, par référence).
- Redux bascule-t-il vraiment les transformations des variantes B9 (`transforms of '<id>' active n/m`) ?
- `EngineIgnited` suit l'étagement et la coupure ; `Data_Decouple.isDecoupled` passe à vrai à la séparation.
- Unité de `RCSForce.RelativeForce` (kN), appariement des hôtes aux tuyères par ordre de hiérarchie.
- Champ d'accélération du vaisseau (`accel spring from …`) et son coût.
- Le modèle du ressort d'accélération (formule à nous, non issue de KSP1).

## 2b. En cas de régression

Dans `animations.json`, jeu fermé, un à un :

- [ ] `"plugin_modules": false` : plus aucune entrée de plugin (retour à 0.4.1 pour ces modules) ;
- [ ] `"b9_module_gates": false` : les entrées s'animent quelle que soit la variante B9 (les portes valent pour
  toutes nos entrées, phase 1 comprise) ;
- [ ] `"skip_modules": ["<module>"]` : écarte un module précis, plugin compris (par exemple `ModuleDeployableEngine`) ;
- [ ] `"editor_animation_speed"`, `"material_curves"`, `"emission_setup"` comme dans la section 2a ;
- [ ] puis les points reportés de la spec : icônes noires et rectangle gris du VAB (hors animations).
