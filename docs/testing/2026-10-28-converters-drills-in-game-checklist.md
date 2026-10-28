# KSP1PartMod 0.10.0 : liste de tests en jeu des convertisseurs, foreuses et scanners (2026-10-28)

Redux laissait tomber les modules ISRU de KSP1. 0.10.0 les importe comme les modules **natifs** de KSP2 : les
convertisseurs KSP1 (`ModuleResourceConverter` : piles à combustible, ISRU) deviennent `Module_ResourceConverter`, une
pièce ayant un mode par convertisseur KSP1 ; les foreuses de sol (`ModuleResourceHarvester`, type 0) deviennent
`Module_Mine` ; le scanner orbital (`ModuleOrbitalSurveyor`) et le scanner de surface (`ModuleResourceScanner` sans
`RequiresUnlock`) deviennent `Module_ResourceScanner`. L'Ore de KSP1 est importé comme **CarbonRegolith** (la ressource
que l'ISRU de KSP2 transforme), parce que `TestRocks`, le choix de Redux, n'a de carte sur aucun corps. Aucun système
nouveau : pas d'astéroïde, d'océan ni d'atmosphère, pas de chaleur de cœur, pas de nouvelle ressource ni de nouvelle carte.
Tout est lu dans la décompilation de Redux 26w42c et testé dans l'éditeur (2057 tests EditMode, 2 explicites ignorés) ;
**rien n'a été lancé dans le jeu**.

Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`, filtrer sur `[KSP1PartMod] native:`,
`[KSP1PartMod] isru:` et `anim:`.

## Avant de commencer

- [ ] Installé dans `<jeu>/mods/KSP1PartMod/` : `swinfo.json` en version **0.10.0**. `animations.json` : si le fichier
  installé n'est pas celui de 0.9.0, il n'a pas été remplacé ; y ajouter les huit clés `ksp1_converters`, `ksp1_drills`,
  `ksp1_survey_scanners`, `ksp1_resource_scanners` (`true`), `ksp1_ore_resource` (`"CarbonRegolith"`),
  `harvest_multiplier` (`1`), `isru_heat_scale` (`1`), `isru_active_animation` (`true`). Sans elles, ce sont les valeurs
  par défaut, c'est équivalent.
- [ ] **Garder une sauvegarde de 0.9.0** contenant un vaisseau avec de l'Ore dans un réservoir KSP1 (test des anciennes
  sauvegardes, section 8), et une sauvegarde rapide avant chaque test de vol.
- [ ] Campagne où le mod est activé (Edit Pack). Pièces de test (de série KSP1) : `ISRU`, `MiniISRU`, `RadialDrill`
  (Drill-O-Matic), `MiniDrill`, `FuelCell`, `FuelCellArray`, `SurveyScanner`, `SurfaceScanner`, `OrbitalScanner` ; une
  pile BDB (« Fuel Cell ») ; un magnétomètre BDB.

## 1. Au chargement

- [ ] `native: ore: KSP1 Ore imported as CarbonRegolith (Redux's mapping: TestRocks, which has no resource map)`. Aucune
  ligne `ore: … is not a game resource` ni `ore: Redux's resource table not found`.
- [ ] La ligne `native: armed: …` se termine par `…, ModuleResourceConverter -> Module_ResourceConverter,
  ModuleResourceHarvester -> Module_Mine, ModuleOrbitalSurveyor -> Module_ResourceScanner, ModuleResourceScanner ->
  Module_ResourceScanner`.
- [ ] `native: isru reference: drill <pièce KSP2> … EC/s, … kW per EC/s, …; converters <n> mode(s), median … kW per EC/s;
  scanner …`. Aucune ligne `isru reference: no KSP2 … found, fallback …` (sinon la recopier : les pièces KSP2 n'étaient pas
  lues au moment de l'import).
- [ ] `native: minable resources: … (from the KSP2 drill)`. **Attendu** : la source est la foreuse KSP2, pas les cartes ;
  Redux ne remplit les cartes de ressources qu'après l'import des pièces KSP1 (lu dans la décompilation), donc **aucune**
  ligne `… map on n body/bodies: …` n'est attendue. `(from fallback)` serait un défaut à noter.
- [ ] `native: drills: a KSP1 drill yields what a KSP2 drill mines per EC/s x its EC/s x Efficiency / (0.1 x EC/s) x 1
  (harvest_multiplier, default)`.
- [ ] Les résumés, **noter les nombres**. Attendu avec l'installation actuelle (d'après les cfg bruts ; ModuleManager peut
  changer ces chiffres) :
  - `native: converters: 18 part(s) got Module_ResourceConverter with 24 mode(s) (… part(s) with several KSP1 converters,
    one runs at a time); modes dropped: 0 (unknown resource); not reproduced: …` ;
  - `native: converter names: … term(s) added to Redux's part localization` (pas de `converter names not registered`) ;
  - `native: drills: 2 part(s) got Module_Mine (CarbonRegolith); skipped: 0 ocean, 0 atmospheric, 0 exospheric, 0 resource
    without a map; ModuleAsteroidDrill/ModuleCometDrill ignored on 2 part(s) (no asteroids or comets in KSP2); …` ;
  - `native: scanners: 1 survey scanner(s) and 1 resource scanner(s) got Module_ResourceScanner; 19 ModuleResourceScanner
    left out (RequiresUnlock: …), 0 with no scannable resource`.
- [ ] Une ligne par mode, par exemple `native: ISRU: converter 'Lf+Ox' -> mode 0: in CarbonRegolith … ElectricCharge 30
  out Methane … Oxidizer … (mass in … t/s, out … t/s), heat … kW; Ore weighs 0.05 t per KSP1 unit in Redux, 0.01 t in KSP1:
  Ore mass x5`. Le « x5 » est attendu (voir section 3).
- [ ] Une ligne par foreuse : `native: RadialDrill: drill: Ore -> CarbonRegolith at … units/s at density 1 (KSP2 yield …
  per EC/s from <foreuse KSP2> x 15 EC/s x Efficiency factor 1 (1.5 / (0.1 x 15)) x harvest_multiplier 1); per EC/s: KSP1
  drill …, KSP2 drill …; EC 15/s, heat … kW, impact 'ImpactTransform' 5.42 m`. **Les deux valeurs « per EC/s » doivent être
  égales** pour les foreuses de série.
- [ ] Une ligne par scanner : `native: SurveyScanner: scanner: surveyor -> Module_ResourceScanner: …, … EC/s, 5 s, altitude
  0 m to 5000 km, inclination 80° to 100°` et `native: SurfaceScanner: scanner: resource scanner -> Module_ResourceScanner:
  CarbonRegolith, 0 EC/s, …, altitude … to 1 km, …`.
- [ ] `isru: armed: impact transforms on, deploy bridge on, active clips on`. Aucune ligne `isru: <membre> not found, …
  off`, ni `isru: patch failed, stopped for this session`.
- [ ] Aucune ligne `isru: <pièce>: Module_Mine not in our record, runtime left as is` (ou `Module_ResourceScanner`) : sinon
  la recopier, la clé de nom de pièce ne correspond pas.
- [ ] Aucune ligne `native: <pièce>: … not converted`.

## 2. Pile à combustible

- [ ] Une pile KSP1 (`FuelCell`, `FuelCellArray` ou une pile BDB) : le PAM montre le convertisseur KSP2, son mode (nom
  lisible, pas une clé `Parts/Ksp1Converter/…`), la jauge de débit à 100 %.
- [ ] En vol, activée : elle consomme son carburant et produit de l'électricité.
- [ ] Noter si l'entrée LF+Ox (Methane + Oxidizer chez Redux) se prend bien dans des réservoirs KSP1 (Methalox) ; sinon,
  la ligne d'état dit « ressource insuffisante ». Une pile BDB à MonoPropellant : noter qu'elle tire bien la monergol.

## 3. ISRU

- [ ] Le menu Mode de l'ISRU KSP1 liste quatre modes aux noms lisibles (dont Lf+Ox et Monoprop), pas de clé brute.
  Idem pour le `MiniISRU`.
- [ ] Avec du CarbonRegolith à bord (réservoir d'Ore KSP1 rempli par une foreuse, ou au VAB), le mode Lf+Ox remplit un
  réservoir KSP1 de Methalox. Noter le débit.
- [ ] Noter le cas où un réservoir de sortie est plein : KSP2 arrête la conversion, KSP1 jetait l'excédent
  (`DumpExcess` non reproduit).
- [ ] **Masse** : l'Ore pèse 0,05 t par unité chez Redux contre 0,01 t dans KSP1. Le nombre d'unités suit KSP1, mais la
  masse de carburant produite par tonne d'Ore est environ **5 fois plus faible** que dans KSP1 (ligne « Ore mass x5 »).
  C'est attendu, pas un défaut.
- [ ] Le clip actif `ProcessorLarge_running` tourne pendant la conversion et s'arrête avec elle (ligne
  `anim: ISRU group '…' active clip follows Module_ResourceConverter running`).

## 4. Foreuse

- [ ] Poser sur **Kerbin ou Duna** (pas la Mun, Ike, Drast ni Minmus : pas de carte de CarbonRegolith) une foreuse
  Drill-O-Matic (`RadialDrill`) avec une batterie et un réservoir d'Ore KSP1.
- [ ] Au chargement du vol : `isru: RadialDrill: drill probe 'ImpactTransform' found, 5.42 m along +Z`. Si la ligne dit
  `… not found on the part, the drill stays not deployed (no deploy bridge)`, la foreuse reste « non déployé » : la
  recopier.
- [ ] `isru: RadialDrill: Module_Mine deploy follows <clé>` (la foreuse suit son animation de déploiement KSP1).
- [ ] Déployer avec le bouton KSP1PartMod : le statut KSP2 passe de « non déployé » à « inactif », puis « en marche » une
  fois activée. Le clip `Drill_Deploy` joue, puis `Drill_Running` tourne tant qu'elle fore (ligne
  `anim: RadialDrill group '…' active clip follows Module_Mine running`).
- [ ] Après quelques secondes : `isru: RadialDrill: drilling on <corps>: CarbonRegolith <x> units/s (density <d>), ground
  at <m> m, EC <e>/s`. **Noter** le débit, la densité et la distance au sol.
- [ ] **Sens de la sonde (+Z).** Si le statut reste « trop haut » alors que la foreuse touche le sol, le sens +Z est faux :
  noter la ligne `drill probe` et la position de la foreuse. Faire le test aussi avec une foreuse posée en symétrie
  miroir.
- [ ] **Débit par EC.** Comparer avec la foreuse native de KSP2 au même endroit : à `harvest_multiplier` 1, le débit par
  EC/s de la foreuse KSP1 doit être celui de la foreuse KSP2 (ligne `per EC/s: KSP1 drill …, KSP2 drill …`). Si le débit
  paraît absurde, régler `harvest_multiplier` (0,01 à 100) et noter la valeur retenue.
- [ ] Le `MiniDrill` : mêmes lignes, débit 5 fois plus faible (3 EC/s au lieu de 15).
- [ ] Rétracter : le statut repasse à « non déployé », le clip actif s'arrête.

## 5. Chaleur

- [ ] Pendant la foreuse et l'ISRU, la température de la pièce monte, puis la coupure thermique de KSP2 (90 % de la
  température maximale) l'arrête si besoin. Si la pièce surchauffe trop vite, baisser `isru_heat_scale` (0 = aucune
  chaleur). Noter la température atteinte.

## 6. Scanners

- [ ] Le `SurveyScanner` déployé en orbite polaire (inclinaison 80° à 100°) entre 0 et 5 000 km finit un balayage en 5 s,
  et la fenêtre des ressources montre les cartes du corps. Ligne `isru: SurveyScanner: Module_ResourceScanner deploy
  follows …` (ou `always extended (…)`).
- [ ] Le `SurfaceScanner` posé (sous 1 km) balaie le CarbonRegolith du corps.
- [ ] Un magnétomètre BDB n'a pas de module de scanner : il fait partie des 19 `left out` du résumé.
- [ ] L'`OrbitalScanner` (RequiresUnlock) n'a pas de module de scanner ; son clip actif reste immobile, comme en 0.9.0.

## 7. Interrupteurs (en cas de régression)

Chaque clé ci-dessous, mise à `false` dans `animations.json` (redémarrer le jeu), redonne le comportement de 0.9.0 pour son
point, avec la ligne `native: <module KSP1> -> <module KSP2> off (<clé> false)` ou l'absence de la ligne correspondante :

- [ ] `ksp1_converters` : plus de convertisseur sur les pièces KSP1 (`ModuleResourceConverter -> Module_ResourceConverter
  off (ksp1_converters false)`).
- [ ] `ksp1_drills` : plus de foreuse (`ModuleResourceHarvester -> Module_Mine off (ksp1_drills false)`), et
  `isru: armed: impact transforms off, …`.
- [ ] `ksp1_survey_scanners`, `ksp1_resource_scanners` : plus de scanner (lignes `… off (…)`).
- [ ] `ksp1_ore_resource` à `"TestRocks"` (ou `""`) : plus de ligne `ore: KSP1 Ore imported as …` ; les réservoirs d'Ore
  KSP1 retiennent du TestRocks, les foreuses sont sautées (`resource without a map`).
- [ ] `isru_active_animation` : les clips actifs (ISRU, foreuses) ne jouent plus que pour les pièces que KSP1 marque
  « toujours actives » ; plus de ligne `active clip follows`.
- [ ] `harvest_multiplier`, `isru_heat_scale` : réglages numériques, pas des interrupteurs. L'ancienne clé
  `harvest_abundance_at_full_density` est signalée comme inconnue.

## 8. Anciennes sauvegardes

- [ ] Charger la sauvegarde de 0.9.0 avec de l'Ore (TestRocks) dans un réservoir KSP1 : `native: ore: old save: <pièce>:
  saved TestRocks <a> moved to CarbonRegolith <b> (mass kept[, capped at the capacity <c>])`, puis `native: ore: old saves:
  <n> part(s) migrated this session, TestRocks <a> -> CarbonRegolith <b>`. Le réservoir affiche bien la quantité `<b>`.
  **Attendu** : aucune perte (la masse est gardée).
- [ ] Aucune ligne `ore: old saves: … not found, not migrated` ni `no resource database while …`.
- [ ] Un vaisseau enregistré dans le VAB avant 0.10.0 (espace de travail) **n'est pas migré** : son Ore est perdu. Le noter
  si c'est le cas.
- [ ] Le remplacement de l'Ore est **global** : une campagne où le mod est désactivé, dans la même session, importe aussi
  ses réservoirs d'Ore KSP1 en CarbonRegolith. C'est attendu.

## 9. Audit (optionnel)

- [ ] Avec `audit_on_vab_entry` à `true`, la ligne `audit: native modules: … heat shield(s), … crossfeed toggle(s), 18
  converter(s), 2 drill(s), 2 scanner(s), 0 part(s) where one failed to initialize` compte les convertisseurs, foreuses et
  scanners, avec **0** échec d'initialisation. La colonne `native_modules` du CSV nomme `Module_ResourceConverter`,
  `Module_Mine` et `Module_ResourceScanner`.

## Non repris (normal)

- Foreuses d'astéroïdes et de comètes (`ModuleAsteroidDrill`, `ModuleCometDrill`), récolteurs d'océan, d'atmosphère et
  d'exosphère : KSP2 n'a pas ces systèmes.
- `ModuleCoreHeat` (température de cœur, `ThermalEfficiency`, `TemperatureModifier`) : KSP2 n'en a pas ; débit plein.
- Les bonus d'équipage (`UseSpecialistBonus`…), `FillAmount`, `DumpExcess`, `HarvestThreshold`.
- Les scanners `RequiresUnlock` (magnétomètres BDB, `OrbitalScanner`) : KSP2 n'a pas de lecture locale de l'abondance.
- Plusieurs convertisseurs KSP1 tournaient en même temps ; dans KSP2, un seul mode à la fois.
