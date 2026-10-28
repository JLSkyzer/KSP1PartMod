# KSP1PartMod 0.8.0 : liste de tests en jeu des modules natifs, lot 1 (2026-10-28)

Quatre changements, tous lus dans l'IL de Redux 26w42c et testés dans l'éditeur, **aucun lancé dans le jeu** :

1. **ModuleManager, filet de sécurité** : les patchs des plugins KSP1 absents (RealFuels, FAR, AJE…) ne s'appliquent
   plus ; `:NEEDS[X]` est faux et `:NEEDS[!X]` vrai pour eux ; un patch `:BEFORE[x]` / `:AFTER[x]` d'un mod absent est
   écarté, comme ModuleManager.
2. **ModuleManager, arithmétique et indices** : `*=`, `+=`, `-=`, `/=` et `,n` / `,-n` / `,*` (Redux les ignore).
3. **`ModuleToggleCrossfeed`** devient le crossfeed natif de KSP2 (`Module_ToggleCrossfeed`).
4. **`ModuleAblator`** devient le bouclier thermique natif (`Module_Heatshield`), et la couleur de brûlure
   (`shieldChar`) suit l'ablator.

Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`, filtrer sur `[KSP1PartMod] mm:` et
`[KSP1PartMod] native:`.

## Avant de commencer

- [ ] Installé dans `<jeu>/mods/KSP1PartMod/` : `swinfo.json` en version **0.8.0**. `animations.json` : si le fichier
  installé n'est pas celui de 0.7.0 il n'a pas été remplacé, y ajouter les clés `mm_*`, `ksp1_crossfeed_toggle`,
  `ksp1_heatshields`, `ablator_rate_scale`, `ablator_char` (voir le README).
- [ ] Campagne où le mod est activé (Edit Pack). Garder une sauvegarde rapide avant chaque test de vol.

## 1. ModuleManager : démarrage et chargement

- [ ] Au démarrage : `mm: armed: plugin safety net on, arithmetic on, index selectors on, first-match edits off, BEFORE/AFTER
  need their mod on`. Aucune ligne `mm: … not found, … off` ni `mm: patch failed`.
- [ ] Au chargement de la campagne, **noter** les lignes de résumé (copier les chiffres dans le rapport).
  **Deux sortes de chiffres, à ne pas confondre** :
  - les chiffres **bruts** du recensement des cfg (`MmCensusTests`) comptent des **lignes de cfg**, sans appliquer NEEDS :
    475 lignes à opérateur, 350 indices (40 sur des valeurs, 310 sur des nœuds), 8 lignes à composante de vecteur avec
    opérateur, 277 patchs racine `:BEFORE` / `:AFTER` d'un mod absent. **Le log ne donnera pas ces chiffres** ;
  - le log ne compte que les patchs qui ont **passé NEEDS**, et chaque valeur ou nœud modifié **une fois par pièce
    touchée** (une ligne `@PART[bluedog_*]` compte autant de fois qu'elle touche de pièces). Les chiffres attendus
    ci-dessous viennent d'un passage de `Ksp1ConfigFinalizer.Finalize` de Redux, dans l'éditeur, sur les fichiers
    installés (`MmEntryKeyTests`, 2026-10-28). Le jeu devrait donner les mêmes avec les mêmes fichiers, **non vérifié** :
  - `mm: arithmetic: 1 applied (… /= 1), skipped: 17 non-numeric, 6 missing key, 0 divide by zero; … 0 !=, 68 ^=` :
    presque toutes les lignes à opérateur sont dans des patchs écartés (NEEDS non satisfait, ou règle BEFORE/AFTER) ;
  - `mm: index: 0 value edit(s), 485 node edit(s), 0 out of range` ;
  - `mm: 0 vector component edit(s) … skipped` : les 8 lignes `key[i] *= x` sont toutes dans des patchs écartés ;
  - `mm: BEFORE/AFTER of an absent mod: 122 patch(es) dropped: RemoteTech 73, FerramAerospaceResearch 26, zzRealplume 9,
    ProceduralFairings 3, zzz_Bluedog_DB 3, DefaultActionGroups 2, zRealPlume 2, TweakableEverything 1, USILifeSupport 1,
    zzRocketSoundEnhancementDefault 1, zzzBluedog_DB 1`. Les 4 patchs BDB qui nomment `zzz_Bluedog_DB` / `zzzBluedog_DB`
    sont des fautes de frappe amont que ModuleManager n'appliquait pas non plus. **Jamais** `zzBluedog_DB`. Ces 122 patchs
    étaient appliqués par Redux en 0.7.0 : c'est le changement visible de la version. Un écart de quelques unités n'est
    pas un défaut ; un écart important (par exemple 277) en serait un ;
  - `mm: unindexed edits that matched more than one …` : 0 attendu. **Noter ce nombre**, il décide de
    `mm_edit_first_match` (laissé à `false` : Redux modifie toutes les correspondances là où ModuleManager ne modifie
    que la première). Il ne compte que les patchs que KSP1PartMod applique (ceux qui ont un opérateur ou un indice) ;
  - `mm: 254 value(s) with #$ variables left as text (not supported yet)` ;
  - `mm: 0 delete line(s) with a wildcard (!key*) left to Redux, …` (1 ligne brute, dans un patch écarté).
- [ ] La seule ligne arithmétique appliquée sur cette installation est `@rescaleFactor /= 0.9375` de
  `Bluedog_DB/Patches/Star-37-48-Rescale.cfg` sur `bluedog_UpperSolids_Star48BV` : `rescaleFactor` passe de 0,762 (0.7.0)
  à 0,8128 (passage de `Finalize` dans l'éditeur). Dans le VAB, ce moteur Star 48 est donc **environ 7 % plus grand**
  qu'en 0.7.0 : le comparer à une capture de 0.7.0 ou au Star 37 voisin. Les trois pièces du patch voisin
  (`@PART[…Star37BV,…Star37FMV,…Burner2]`) gardent `rescaleFactor = 1.0` dans les deux versions : Redux n'applique pas ce
  patch à liste de noms séparés par des virgules (constat de l'éditeur, cause non lue).
- [ ] Aucun indice sur une valeur (`@volume,1`) ne passe NEEDS sur cette installation : pas de test en jeu possible, les
  tests de l'éditeur couvrent ce cas.

## 2. Filet de sécurité

- [ ] Copier (dans un dossier de test, puis le retirer) le dossier `RealFuels` de `I:/ksp1` dans `mods/KSP1` : les lignes
  `mm: blocked plugin names: … RealFuels …` et `mm: RealFuels: … NEEDS lookup(s) answered false …` apparaissent, et les
  moteurs gardent leur `Module_Engine` (pas de `ModuleEnginesRF` dans l'audit). Retirer le dossier ensuite.
- [ ] Ligne `blocked plugin names` : `curated <a>` (liste `mm_plugin_folders` moins `mm_needs_keep`) et `automatic <b>`
  (dossiers avec un `.dll` et sans pièce) ; sur une copie complète de `I:/ksp1` le test a trouvé 14 noms de la liste et 102
  dossiers automatiques ; aucun patch du dossier `GameData` lui-même n'est écarté.

## 3. Crossfeed

- [ ] Au chargement : `native: armed: ModuleToggleCrossfeed -> Module_ToggleCrossfeed, ModuleAblator -> Module_Heatshield`,
  puis `native: crossfeed: <n> part(s) got Module_ToggleCrossfeed (<s> start with crossfeed on), techRequired ignored on
  <t>`. Avec l'installation actuelle : 155 pièces, **4** démarrent avec le crossfeed allumé (pas 3), techRequired ignoré
  sur 13.
- [ ] Sur un découpleur KSP1 : le PAM du VAB propose l'interrupteur de crossfeed, éteint par défaut (allumé sur les 4
  pièces concernées).
- [ ] En vol : l'interrupteur marche, et les **trois actions de groupe** (allumer, éteindre, basculer) aussi. Le carburant
  passe ou non à travers le découpleur, selon l'état.
- [ ] Lignes `native: <pièce>: toggleEditor / toggleFlight missing, KSP2's default true used` : noter les pièces (aucune
  attendue).

## 4. Bouclier thermique

- [ ] Ligne `native: heat shield reference: <n> KSP2 shield(s), median Rate <r>, capacity <c>` : **noter les valeurs**. Si
  elle dit `no KSP2 shield found, fallback`, les boucliers KSP2 n'étaient pas lisibles à ce moment.
- [ ] Ligne de détail `native: heat shield reference: (Rate x PLF, capacity) of each: …` : noter chaque couple Rate × PLF
  et capacité des boucliers KSP2. La règle du mod est un Rate **constant par bouclier** (le Rate natif, mis à l'échelle de
  `pyrolysisLossFactor`), sans rapport avec la capacité : si le Rate natif × PLF croît avec la capacité, chaque taille est
  fausse du rapport de ses surfaces.
- [ ] Ligne `native: heat shields: <n> part(s) got Module_Heatshield (<k> without the resource, skipped); not reproduced: …`.
- [ ] Une capsule KSP1 avec le bouclier 2,5 m, rentrée depuis une orbite basse de Kerbin : l'ablator **diminue**, la
  capsule survit, le PAM affiche l'état d'ablation. Comparer avec un bouclier KSP2 de même taille dans la même rentrée.
- [ ] La direction de protection est **supposée** vers -Y local de la pièce : le bouclier protège quand son dessous est
  face au flux, pas autrement.
- [ ] La couleur du bouclier passe du clair au noir à mesure que l'ablator baisse (ligne `char 'shieldChar' follows the
  ablator`). Clair quand le bouclier est plein : **supposé**, à confirmer.
- [ ] **Les 6 boucliers `_BurnColor`** (Squad HeatShield0, 1, 1p5, 2, 3 et KRE HeatShield-M) : le shader
  `KSP2/Scenery/Standard (Opaque)` de leurs matériaux n'a probablement **pas** de `_BurnColor` (absent de la liste du wiki,
  `Modding-Docs/Game reference/Shaders and visuals/Scenery - Standard (Opaque) shader.md` ; non vérifié sur le shader
  lui-même). On attend donc une ligne `anim: … no renderer … has any of [_BurnColor]` pour chacun, et **aucune couleur
  de brûlure** sur ces boucliers. Noter la ligne réelle ; si la couleur s'affiche quand même, le noter aussi.
- [ ] **Capsules KV1, KV2 et KV3** (Making History) : leur `shieldChar` et leur lumière écrivent la même propriété
  (`_EmissiveColor`). Le mod laisse la propriété à la lumière : ligne `anim: <KV> MODULE[…] ModuleColorChanger: moduleID
  'shieldChar' not planned, MODULE[…] ModuleColorChanger writes the same shader property (_EmissiveColor): it keeps it`.
  Vérifier que les lumières s'allument et s'éteignent (PAM et groupe d'action Light), et que la capsule **ne brille pas**
  sur le pas de tir. Pas de couleur de brûlure sur ces capsules.
- [ ] Au-dessus de 50 MW le flux est comprimé en racine carrée (règle du jeu) : à flux extrême un grand bouclier tient
  jusqu'à 4 fois plus longtemps qu'un petit (surface 16 fois plus grande). Noter si c'est gênant.
- [ ] `Module_Heatshield` puise l'ablator dans **tout le vaisseau** (et non dans sa pièce) : mettre deux boucliers sur un
  même vaisseau et noter si l'un vide l'autre. La couleur, elle, suit l'ablator de sa propre pièce.
- [ ] Si le bouclier s'use trop vite ou trop lentement : régler `ablator_rate_scale` (0,01 à 100) dans `animations.json`.

## 5. Audit

- [ ] Lancer l'audit (`audit_on_vab_entry`). La ligne `audit: native modules: <h> heat shield(s), <c> crossfeed toggle(s),
  <f> part(s) where one failed to initialize` indique **0 échec**. Un échec apparaît dans la colonne CSV
  `native_modules` (suffixe `:uninitialized` ou `:missing`).
- [ ] Le rapport `.md` commence par la section « ModuleManager (this session) » avec les lignes de résumé de la session,
  et contient la section « Native modules ».

## 6. Interrupteurs (retour à 0.7.0 en cas de régression)

Chaque clé à `false` dans `animations.json` redonne le comportement de 0.7.0, avec la ligne correspondante au démarrage
(`mm: armed: … off`, `native: … off`) :

- [ ] `mm_plugin_safety_net` : plus de filet pour les plugins absents.
- [ ] `mm_before_after_need_mod` : les patchs `:BEFORE` / `:AFTER` d'un mod absent s'appliquent de nouveau (122 attendus
  sur cette installation, d'après le passage de `Finalize` dans l'éditeur ; 277 est le compte brut des cfg).
- [ ] `mm_arithmetic` et `mm_index_selectors` : Redux ignore les opérateurs, modifie tous les indices.
- [ ] `mm_edit_first_match` : à passer à `true` seulement après lecture du compte du point 1.
- [ ] `ksp1_crossfeed_toggle` : Redux abandonne le module, plus d'interrupteur.
- [ ] `ksp1_heatshields` : Redux abandonne le module, plus de bouclier. `ablator_char` : la couleur reste comme en 0.7.0.
