# KSP1PartMod 0.13.0 : liste de tests en jeu des mesures de performance (2026-10-28)

0.13.0 n'optimise **rien** : elle mesure. Une seule session de jeu doit donner les chiffres qui décideront des
optimisations suivantes (feuille de route, point 8b). Le mod ajoute :
- une **chronologie du chargement** : les phases de l'import KSP1 de Redux qu'on peut observer, chaque étape du mod, le
  total et les pièces les plus lentes (lignes `[KSP1PartMod] perf: import:` et `perf: ours:`). Ce ne sont que des patchs
  de mesure : aucun argument, aucun résultat, aucun ordre d'exécution n'est changé ;
- des **instantanés de mémoire** et un **recensement des textures KSP1** vivantes (format, taille, mipmaps, puissance de
  deux, lisibles, les plus grosses) à la fin de l'import, au menu principal, au VAB et en vol ;
- le **coût par image de chaque fonction** du mod (avec `perf_counter`) ;
- un **fichier de rapport** `<jeu>/mods/KSP1PartMod/perf/perf-<date>.md`, réécrit à chaque moment de mémoire : c'est le
  fichier à renvoyer avec `Player.log`.

Tout est testé dans l'éditeur (2425 tests EditMode, 2 explicites ignorés) ; **rien n'a été lancé dans le jeu**.

Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`, filtrer sur `[KSP1PartMod] perf:` et
`[KSP1PartMod] textures:`.

## Ordre du grand test

Cette liste ne demande pas de session à part : elle se fait **pendant** le grand test, dans cet ordre :
1. `2026-10-28-fixes-041-in-game-checklist.md` (0.4.1) ;
2. `2026-10-27-animations-phase2-in-game-checklist.md`, section 2a, puis section 2b ;
3. `2026-10-28-audit-in-game-checklist.md` ;
4. `2026-10-28-engine-shrouds-in-game-checklist.md` ;
5. `2026-10-28-native-modules-1-in-game-checklist.md` ;
6. `2026-10-28-b9-overrides-in-game-checklist.md` ;
7. `2026-10-28-converters-drills-in-game-checklist.md` ;
8. `2026-10-28-radiators-in-game-checklist.md` ;
9. `2026-10-28-science-in-game-checklist.md` ;

et les points de cette liste au fil de l'eau : le premier chargement au début, la mémoire à chaque scène, les images par
seconde quand une fusée de test est posée.

## Avant de lancer

- [ ] Installé dans `<jeu>/mods/KSP1PartMod/` : `swinfo.json` en version **0.13.0**. `animations.json` : le fichier
  installé a été gardé. Les clés `perf_load_timeline`, `perf_memory_lines` et `perf_report_file` valent `true` par défaut,
  même absentes du fichier : les laisser ainsi.
- [ ] Pour la mesure des images : ajouter `"perf_counter": true` dans `animations.json` (absent = `false`).
  Facultatif : `"perf_window_seconds": 30` pour une ligne toutes les 30 s au lieu de 10.
- [ ] Ouvrir le **Gestionnaire des tâches**, onglet **Performances**, **GPU** : on y lira la « Mémoire GPU dédiée ».
  C'est le seul vrai chiffre de mémoire vidéo : le mod ne sait pas le lire (il faudrait du code natif). Le noter à la
  main, avec son moment, à chaque point « Mémoire » ci-dessous.
- [ ] Au démarrage, une ligne `perf: armed: load timeline on (24 of 24 Redux phase(s) timed), memory lines on, report
  file on, frame cost on (window 10 s); timer overhead ~<n> ns per timing`. **Attendu** : 24 sur 24, aucune liste
  `not found`, et `<n>` de quelques dizaines de ns (25 à 33 ns mesurés dans l'éditeur ; 0 voudrait dire une mesure ratée).
  Aucune ligne `perf: load: stopped`.
- [ ] Aucune ligne `perf: load: <membre> not found`. Si une apparaît, la noter : la phase correspondante n'est pas
  chronométrée, le reste marche.

## 1. Premier chargement de campagne

- [ ] Ne rien toucher pendant l'import. À la fin, relever dans `Player.log` :
  - `perf: import: worker: cache key … ms over … call(s), cfg read … ms (… file(s)), MM finalize … ms, …` ;
  - `perf: import: <p> part(s) in <s> s wall over <f> frame(s): translate … s (Redux importers … s, of which ours … s …;
    prefab … s, of which .mu read … s over … model(s) and textures … s over … load(s); other … s), localization … ms,
    icons … s, release … ms; outside our timings (frames, worker wait, loading screen) … s` ;
  - `perf: import: report lines: <n> in <ms> ms (<w> warning(s))` ;
  - `perf: import: slowest parts: …` ;
  - `perf: ours: mu fix …, shroud import …, B9 …, native importers …, science scan …, tracks read … (worker), animation
    step …, placement …` ;
  - `perf: campaign load: <s> s from Loading to <scène>`.
- [ ] **Attendu** : le total `<s> s wall` est proche du `[System] Redux: Importing KSP1 parts completed in …` du jeu
  (315 s avec la 0.4.0). La ligne dit où vont ces secondes : prefab, textures, `.mu`, icônes, ou attente des images
  (`outside our timings`). Noter les quatre plus grosses parts.
- [ ] `textures: measure armed on Ksp1TextureLoader.LoadTexture and Ksp1ImportedPartRegistry.MarkImportComplete` puis
  `textures: sharing armed …` au démarrage. Une ligne `no memory line at the end of the import (sharing and quality
  unaffected)` voudrait dire que seule la mémoire de fin d'import manque : le partage doit être armé quand même.
- [ ] Les moments `end of import (prefabs still resident)` puis `after the import released its prefabs` donnent chacun
  les lignes de la section 3.
- [ ] Une ligne `perf: report written to <jeu>/mods/KSP1PartMod/perf/perf-<date>.md (…)` (la première fois seulement ;
  les suivantes sont au niveau debug).

## 2. Rechargement

- [ ] Quitter vers le menu, recharger la campagne : `perf: import: reused <n> cached part(s) in <ms> ms; no import phase
  to time` et `perf: ours: …`. Noter `animation step` (environ 3 s avec la 0.4.0). Au rechargement, les étapes faites une
  seule fois par session (mu fix, science scan) affichent `—` : c'est normal.
- [ ] `perf: game start: main menu after <s> s` une fois, au premier menu principal.

## 3. Mémoire

- [ ] À chaque moment (menu principal, VAB, vol), noter la « Mémoire GPU dédiée » du Gestionnaire des tâches à côté des
  lignes du même moment :
  - `perf: memory at <moment>: allocated … MB, reserved … MB, mono …/… MB, process working set … MB, private … MB; system
    RAM … MB` ;
  - `textures: memory at <moment>: …` et `textures: cache at <moment>: …` (lignes de la 0.4.1, inchangées) ;
  - `textures: census at <moment>: <n> live KSP1 texture(s) ~<m> MB; by format …; by size …; without mipmaps …;
    uncompressed …; not a power of two …; readable … (… MB of RAM); census <ms> ms` ;
  - `textures: largest at <moment>: …`.
- [ ] Les moments : `main menu entry` et `main menu entry + 5 s` (aussi en quittant une campagne), `VAB entry` et
  `VAB entry + 5 s`, `flight entry` et `flight entry + 5 s` (pas en revenant de la carte).
  **Le moment « + 5 s » est sauté si on quitte la scène avant 5 s** (ligne debug `… skipped: the scene was left
  before`) : rester au moins 5 s dans chaque scène.
- [ ] **Profiler** : les chiffres `allocated`, `reserved` et `mono` viennent du profileur d'Unity, qui peut lire **0** dans
  le joueur de Redux (version de production). S'ils sont à 0 ou `n/a`, le noter dans le compte rendu : seuls les chiffres
  du processus (`working set`, `private`) et le Gestionnaire des tâches compteront alors.
- [ ] Noter le `census <ms> ms` au menu principal et à l'entrée en vol : un à-coup visible à ce moment viendrait du
  recensement (`perf_memory_lines: false` le retire).
- [ ] Au VAB, poser une vingtaine de pièces BDB (Saturn, Apollo), puis revenir au VAB une deuxième et une troisième fois :
  la ligne `perf: rebuilds since <moment>: <n> prefab rebuild(s) in <ms> ms (textures … ms over … load(s)); slowest: …`
  et le chiffre KSP1 de `census` doivent se stabiliser. **S'ils montent à chaque visite, le noter.**

## 4. Images par seconde (avec `perf_counter: true`)

- [ ] Au VAB, la fusée de vingt pièces : attendre 30 s sans bouger.
- [ ] En vol, la même fusée sur le pas de tir, puis au décollage, puis en orbite, 30 s à chaque fois.
- [ ] Relever les lignes de chaque scène :
  - `perf: frame: <scène> over <s> s, <f> frame(s), <fps> fps (frame p95 <ms> ms); ours <avg> ms avg, <p95> ms p95, <max>
    ms max per frame; timing overhead ~<ms> ms per frame` ;
  - une ligne par fonction qui a coûté quelque chose : `perf: frame: <scène>: <fonction> <avg> ms avg, <p95> ms p95, <max>
    ms max per frame; <p> part(s), <e> entr(ies)`. Les fonctions : `clips`, `2a material`, `2b plugins`, `shrouds`,
    `B9 gates`, `ISRU/radiator bridge`, `science`, `save`, `flush` (pièces = tables écrites, entrées = valeurs écrites),
    `look-at`, et sans compte de pièces (elles comptent des appels) `radiator patch`, `drill patch`, `subcategory bar`.
- [ ] Un changement de scène (VAB → vol, vol → carte) ferme la fenêtre en cours : une série de lignes sort à ce moment.
- [ ] Si un vaisseau BDB de plus de 300 pièces est disponible, le charger aussi et relever ses lignes.
- [ ] La ligne de la 0.4.0 `anim: perf: …` sort toujours toutes les 60 s ; son temps de `Drive` inclut désormais les
  horodatages (environ 25 à 30 ns chacun).

## 5. Audit (si on le lance)

- [ ] À la fin, une ligne `audit: perf: prefab_ms p50 <a>, p95 <b>, max <c>; finalize_ms p50 …, p95 …, max … over <n>
  part(s)` (pièces de contrôle exclues), et le même tableau, section « Timings », dans `audit-<date>.md`.
- [ ] Le rapport `perf-<date>.md` gagne, dans « Session », une ligne `- Audit: [audit-<date>.md](../audit/audit-<date>.md)`
  avec la ligne `audit: perf:`. Il est réécrit juste après l'audit.

## 6. Interrupteurs

À éditer dans `animations.json`, puis relancer le jeu :

- [ ] `perf_load_timeline: false` : aucune ligne `perf: import` ni `perf: ours`, et aucun patch de mesure (ligne
  `perf: armed: load timeline off (0 of 24 …)`).
- [ ] `perf_memory_lines: false` : seules les lignes `textures: memory/cache` de la 0.12.0 restent, et il n'y a plus de
  moment au menu principal.
- [ ] `perf_memory_lines: false` avec `perf_report_file: true` : **le fichier est quand même écrit**, avec seulement les
  sections du chargement (et du coût par image avec `perf_counter`) ; les sections Mémoire et Textures disent
  `not measured (perf_memory_lines is false)`. C'est voulu.
- [ ] `perf_report_file: false` : aucun fichier dans `perf/`.
- [ ] `perf_counter: false` (défaut) : aucune ligne `perf: frame:`.

## 7. Fichiers à envoyer

- [ ] `Player.log`, complet (pas un extrait : les lignes `[System] … completed in …` du jeu servent aussi).
- [ ] Le fichier le plus récent de `<jeu>/mods/KSP1PartMod/perf/` (`perf-<date>.md` ; les 10 plus récents sont gardés).
- [ ] Si l'audit a tourné : `<jeu>/mods/KSP1PartMod/audit/audit-<date>.md`.
- [ ] Les chiffres « Mémoire GPU dédiée » notés à la main, avec leur moment (menu principal, VAB après chaque visite,
  pas de tir, orbite).
- [ ] Les lignes à recopier si le log est trop gros : `[KSP1PartMod] perf:`, `[KSP1PartMod] textures:`,
  `[KSP1PartMod] anim: perf:`, `[KSP1PartMod] audit: perf:` et les `[System] … completed in …`.
