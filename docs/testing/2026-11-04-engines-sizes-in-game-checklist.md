# KSP1PartMod 0.16.0 : liste de tests en jeu des moteurs et des tailles (2026-11-04)

Le test en jeu du 2026-11-04 a montré quatre problèmes, tous corrigés à l'import de Redux, en mémoire (aucun fichier cfg ni
modèle n'est modifié) :
- **Grappes SEP** (`SEP_25_BOOSTER_CLUSTER`, `SEP_23_BOOSTER_CLUSTER`) : 1000 kN, 0 t, TWR 0,111. Elles deviennent un seul
  moteur à trois modes (All Engines, Middle Inner, Center Three), avec la poussée et la masse calculées depuis les
  `ENGINESET` du cfg et le nombre de moteurs du modèle.
- **Pièces à plusieurs moteurs sans sélecteur** (LR91 ×6, AJ260 Inline, Merlin 1Ci et 1CV, tours de sauvetage Apollo, Big G
  et Mercury) : Redux ne gardait que le **dernier** moteur. Elles deviennent un seul moteur multi-modes, mode 0 = moteur
  principal.
- **Étiquettes de taille** : elles suivent désormais le plus grand nœud d'empilement mesuré sur le modèle.
- **Règles d'échelle de KSP1** : `rescaleFactor` absent = 1,25 (19 pièces de série), `scale` des anciens nœuds, position
  des `MODEL`, rayon et suspension des roues.

Tout a été lu dans la décompilation de Redux 26w42c ou dans les cfg, et testé dans l'éditeur (EditMode). **Rien n'a été
lancé dans le jeu.**

Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`. Filtrer sur `[KSP1PartMod] engines:`,
`[KSP1PartMod] size:`, `[KSP1PartMod] rescale:`, `[KSP1PartMod] b9:` et `[KSP1PartMod] audit:`.

## Avant de commencer

- [ ] Le `swinfo.json` installé est en version **0.16.0**.
- [ ] L'`animations.json` installé contient les sept nouvelles clés à `true` : `sep_procedural_engines`,
  `ksp1_engine_modes_fallback`, `ksp1_size_from_nodes`, `ksp1_default_rescale`, `ksp1_legacy_node_scale`,
  `ksp1_model_position_rescale`, `wheel_radius_rescale`.
- [ ] Faire une sauvegarde rapide avant chaque vol, dans une campagne où le mod est activé.
- [ ] Garder sous la main un vaisseau **sauvegardé en 0.15.1** avec une grappe SEP ou un LR91 (point 7).

## 1. Au chargement

- [ ] `engines: modes armed (sep_procedural_engines on, ksp1_engine_modes_fallback on)`, sans ligne `not found` ni
  `stopped`.
- [ ] Pour `SEP_25_BOOSTER_CLUSTER` : `engines: SEP_25_BOOSTER_CLUSTER: ModuleSEPEngineSwitch order AllEngines, MiddleInner,
  CenterThree`, puis la ligne `SEP type '…': RBoosts 20 x 580, RMiddles13 10 x 580, RCenters13 3 x 580 kN; modes 19140 /
  7540 / 1740 kN; engine mass +44.5 t (covers excluded); thrust transforms …`. **Recopier la ligne.** Une ligne
  `SEP values not set (…)` veut dire que les noms de transforms du modèle ne sont pas ceux attendus : la recopier aussi.
- [ ] Même chose pour `SEP_23_BOOSTER_CLUSTER` (mêmes chiffres attendus).
- [ ] Pour chacune des 12 pièces de repli : `engines: <pièce>: <n> engine modules, no switch: modes … (mode 0 <F> kN, KSP1
  sum <S> kN)`. Pour l'AJ260 et `bluedog_LR91_5_FourVernier` : `duplicate engineID 'Engine' -> 'Engine_2'`.
- [ ] `engines: summary: 2 SEP part(s), 12 fallback part(s), 0 refused`. **Recopier la ligne.**
- [ ] `size: <mod>: …` pour chaque mod, `size: largest moves: …` et `size: docking parts relabelled: …`. **Recopier ces
  lignes**, en particulier celles de Launchers Pack et de Bluedog_DB.
- [ ] `rescale: 19 part(s) without rescaleFactor set to 1.25, 7 excluded (KerbalEVA, FlagSite)`,
  `rescale: node scale on 13 part(s)`, `rescale: MODEL position x rescaleFactor on 3 model(s)` (attendus d'après les cfg
  bruts ; ModuleManager peut changer ces nombres).
- [ ] La ligne `b9: …` se termine par `<d> engine edit(s) now on a multi-mode engine, <e> dropped on SEP clusters`.
- [ ] La ligne de 0.13.1 `engines: emissive curve filled on <n> engine module(s) …` est toujours là. n est **plus petit**
  qu'en 0.15.1 (moins de modules moteur) : c'est attendu.

## 2. VAB : grappe SEP_25

- [ ] Poser `SEP_25_BOOSTER_CLUSTER` avec des réservoirs SEP : aucune erreur.
- [ ] Poussée affichée : **19 140 kN** dans le mode All Engines.
- [ ] Masse de la pièce : environ **44,5 t** (moteurs seuls, sans les capots), réservoirs en plus. Le TWR n'est plus 0,111.
- [ ] Le menu de la pièce (PAM) propose **trois modes** : All Engines, Middle Inner, Center Three.
- [ ] En changeant de mode : 7 540 kN (Middle Inner) puis 1 740 kN (Center Three). Le Δv suit.
- [ ] **Le sens des modes est supposé cumulatif** (le mode *k* allume son anneau et tous les suivants). Si la poussée du
  jeu KSP1 était 11 600 / 5 800 / 1 740 kN, le dire : c'est une seule règle à changer.

## 3. Vol : grappe SEP

- [ ] Au décollage en All Engines : poussée et débit de carburant cohérents avec 19 140 kN (Isp du cfg).
- [ ] La poussée est dans l'axe (anneau symétrique de 20 tuyères) : pas de rotation parasite.
- [ ] Changer de mode en vol : la poussée suit. Noter si les flammes des autres modes s'affichent (chaque mode a maintenant
  ses panaches, supposé : seulement celles du mode choisi).
- [ ] Refaire les points 2 et 3 en bref avec `SEP_23_BOOSTER_CLUSTER` (mêmes chiffres attendus, `rescaleFactor` 0,625).

## 4. Moteurs à verniers et tours de sauvetage

- [ ] Un **LR91** (par exemple `bluedog_LR91_5`) : mode 0 = moteur principal (`basicEngine`), poussée du principal seul ;
  le mode vernier se choisit dans le PAM. Le principal et les verniers **ne tournent plus ensemble** : perte de 3 à 11 %
  de poussée face à la somme KSP1, attendue (point de feuille de route).
- [ ] Un **Merlin 1Ci** (`KK_SPX_Merlin1Ci`) : mode 0 = tuyère principale (`M1Ci_nozzle`), le mode vernier se choisit.
- [ ] L'**AJ260 Inline** : deux modes distincts `Engine` et `Engine_2` (mêmes noms affichés), mode 0 = principal.
- [ ] Une **tour de sauvetage** (`bluedog_Apollo_LES`) : mode 0 = moteur d'éjection (`LES_Escape`). Noter si l'étagement
  l'allume (le plugin BDB qui les déclenchait ne tourne pas) et si le moteur de largage est choisissable.
- [ ] Info-bulle du VAB et Δv de ces pièces : ceux du mode 0.

## 5. Tailles

- [ ] Les anneaux Raptor de Launchers Pack (`KK_SpaceX_Raptor_outerCircle`, étiqueté MD en 0.15.1 pour 11,3 m) ont une
  étiquette de taille et un filtre de taille cohérents avec leur diamètre.
- [ ] Quelques réservoirs BDB : étiquette et filtre de taille corrects (comparer à la ligne `size: largest moves:`).
- [ ] Les icônes de ces pièces sont cadrées (la caméra d'icône suit la taille) : ni minuscules ni coupées.
- [ ] Un **port d'amarrage** relabellisé (ligne `docking parts relabelled`) : il s'amarre toujours ; au-dessus de 2 m de
  diamètre, l'amarrage est à plusieurs liaisons. Noter la portée de capture.
- [ ] La taille change aussi le rayon de dégâts du jet des moteurs et le bouclier aérodynamique : noter toute pièce
  détruite par un moteur voisin qui ne l'était pas en 0.15.1.

## 6. Échelle KSP1

- [ ] Le **LV-T30 de KSP1** (`liquidEngine`) a sa taille KSP1 (1,25 × la 0.15.1) et ses nœuds collés au modèle : le nœud
  du haut à **0,90 m** (7,21 m avant).
- [ ] Le **RAPIER** KSP1 : taille KSP1, nœuds en place, ses modes inchangés (Redux le gérait déjà).
- [ ] Les **roues de rover** de série à la taille KSP1 : roues posées au sol, ni enfoncées ni flottantes, nœuds en place.
  Rayon et suspension suivent la pièce comme dans KSP1 ; la vitesse maximale est `wheelMaxSpeed` du cfg (KSP1 la
  multipliait par le facteur) : la noter.
- [ ] Une jambe KRE (facteur jusqu'à ×2,9) et la jambe Starship (×1,6) : contact au sol correct.
- [ ] Les 3 pièces Gemini (`bluedog_Gemini_Decoupler_1p875m`, `bluedog_Gemini_Heatshield_1p875m`,
  `bluedog_Gemini_Heatshield_2p5m`) : modèle à sa place par rapport aux nœuds.

## 7. Vaisseaux sauvegardés

- [ ] Un vaisseau 0.15.1 avec une grappe SEP ou un LR91 se charge sans erreur. Le moteur tourne en mode 0 et le menu
  déroulant affiche le même mode (ligne `engines: <pièce>: saved mode '…' (a 0.15.x craft) shown as '…'`).
- [ ] Un vaisseau 0.15.1 avec une des 19 pièces de série agrandies : il se charge. Les pièces gardent leur position et
  peuvent se chevaucher ou laisser un jour **jusqu'à la reconstruction au VAB** : attendu, à noter.

## 8. Chaque clé à `false`

Une clé à la fois, redémarrer le jeu à chaque fois. Chaque clé à `false` redonne la 0.15.1 pour son correctif.
- [ ] `sep_procedural_engines` : la grappe SEP revient à 1000 kN et 0 t.
- [ ] `ksp1_engine_modes_fallback` : le LR91 revient à un `Module_Engine` par module.
- [ ] `ksp1_size_from_nodes` : les étiquettes de Redux reviennent (ligne `size: labels from stack nodes off`).
- [ ] `ksp1_default_rescale` : le LV-T30 revient à 0,8 × sa taille KSP1.
- [ ] `ksp1_legacy_node_scale` : les nœuds du LV-T30 repartent loin du modèle.
- [ ] `ksp1_model_position_rescale` : les pièces Gemini reviennent à leur position de 0.15.1.
- [ ] `wheel_radius_rescale` : les roues reviennent aux valeurs du cfg.

## 9. Audit (facultatif)

- [ ] Avec `audit_on_vab_entry` à `true` : `audit: engines: 2 SEP, 12 fallback, 0 parts with more than one Module_Engine`
  (le dernier nombre doit être 0) et `audit: sizes: <n> from nodes, <t> from tokens, <r> Redux's`. **Recopier les deux
  lignes.**
- [ ] Le CSV a les colonnes `size_key`, `size_source`, `rescale` et `engine_modes` (par exemple `3:AllEngines:19140`).

## Que m'envoyer

- Le `Player.log` complet, ou toutes les lignes `[KSP1PartMod] engines:`, `size:`, `rescale:` et `audit:`.
- Pour chaque case non cochée : la pièce, la scène et ce qui a été vu.
