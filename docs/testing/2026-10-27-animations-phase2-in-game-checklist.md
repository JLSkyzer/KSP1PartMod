# KSP1PartMod 0.4.0 : liste de tests en jeu des animations KSP1, phase 2 (2026-10-27)

Cette liste couvre la **section 2a** : courbes de matériau et de lumière des clips KSP1, lueur des
moteurs et de la chaleur, activation de l'émission. La **section 2b** (RCS, modules BDB, etc.) sera
ajoutée en bas par le plan 2b. Rien de cela n'a encore été lancé dans le jeu : 888 tests EditMode
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
- [ ] **Contrôle du bloc de propriétés** : `anim: <pièce> mpb check '<prop>': wrote <v>, read <v'>` avec `read` = `wrote`.
  - Si les valeurs diffèrent : un autre écrivain remplace le bloc après notre `LateUpdate`. Recopier la ligne et la
    pièce ; le repli se déclenche seul, avec l'avertissement `anim: <pièce> mpb check read another value: every
    written renderer is flushed again at render time`. La lueur doit alors s'afficher quand même.
  - `mpb check '<prop>': wrote <v>, renderer gone before the render` : le renderer a disparu avant le rendu, sans conséquence.
- [ ] **Lignes `rebound` attendues, pas des erreurs** : une ligne `anim: <pièce> renderer '<chemin>' rebound: material
  changed (<ancien shader> -> <nouveau>)` par renderer surveillé à **chaque activation de la coque de rentrée**
  (l'écrivain `ReentryMesh` change le matériau). Une ligne `anim: <pièce> <n> renderer(s) rewritten every frame while
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
  laisser `false` et le noter. Avec `false`, le log dit `anim: heat glow off by animations.json` (une fois).
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
- `[DefaultExecutionOrder]` sur un type du mod : non prouvé. Le `mpb check` et son repli au rendu le couvrent.
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
