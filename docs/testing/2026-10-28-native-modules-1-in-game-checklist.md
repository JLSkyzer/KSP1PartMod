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
- [ ] Au chargement de la campagne, **noter** les lignes de résumé (copier les chiffres dans le rapport) :
  - `mm: arithmetic: …` : environ **475** lignes appliquées avec l'installation actuelle (le plan en attendait 483 ; les
    8 lignes à composante de vecteur sont comptées à part) ;
  - `mm: index: …` : environ **350** indices (40 sur des valeurs, 310 sur des nœuds) ;
  - `mm: <n> vector component edit(s) … skipped` : les lignes `key[i] *= x` sont **ignorées** (Redux écrirait l'opérande
    sur toutes les valeurs) ; 8 avec opérateur dans l'installation actuelle ;
  - `mm: BEFORE/AFTER of an absent mod: …` : attendu RemoteTech, FerramAerospaceResearch, zzRealPlume, EngineLight…
    pour **277** patchs écartés au total, dont 5 patchs BDB qui nomment `zzz_Bluedog_DB` / `zzzBluedog_DB` (fautes de
    frappe amont que ModuleManager n'appliquait pas non plus). **Jamais** `zzBluedog_DB`. Ces 277 patchs étaient
    appliqués par Redux en 0.7.0 : c'est le changement visible de la version ;
  - `mm: unindexed edits that matched more than one …` : **noter ce nombre**, il décide de `mm_edit_first_match`
    (laissé à `false` : Redux modifie toutes les correspondances là où ModuleManager ne modifie que la première) ;
  - `mm: <n> value(s) with #$ variables left as text (not supported yet)`.
- [ ] Dans le VAB, la masse d'une pièce BDB rééquilibrée par `@mass *=` : comparer avec la valeur du cfg (calcul à la
  main).
- [ ] Une pièce qui avait une ligne `@volume,1 = …` : seul le deuxième volume change.

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
- [ ] `mm_before_after_need_mod` : les patchs `:BEFORE` / `:AFTER` d'un mod absent s'appliquent de nouveau (les 277).
- [ ] `mm_arithmetic` et `mm_index_selectors` : Redux ignore les opérateurs, modifie tous les indices.
- [ ] `mm_edit_first_match` : à passer à `true` seulement après lecture du compte du point 1.
- [ ] `ksp1_crossfeed_toggle` : Redux abandonne le module, plus d'interrupteur.
- [ ] `ksp1_heatshields` : Redux abandonne le module, plus de bouclier. `ablator_char` : la couleur reste comme en 0.7.0.
