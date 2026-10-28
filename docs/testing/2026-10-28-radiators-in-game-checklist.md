# KSP1PartMod 0.11.0 : liste de tests en jeu des radiateurs (2026-10-28)

Redux laissait tomber `ModuleActiveRadiator`. 0.11.0 l'importe comme le refroidisseur **natif** de KSP2 : un radiateur
fixe devient `Module_Cooler`, un radiateur dépliable (avec `ModuleDeployableRadiator`) devient `Module_ActiveRadiator`.
Ils retirent de la chaleur à **toutes** les pièces chaudes du vaisseau par le modèle thermique de KSP2 (un seul réservoir
de refroidissement par vaisseau, pas de conduction entre pièces). Puissance = `maxEnergyTransfer` / 50 kW (chiffres
publics de KSP1 : 50000 donne 1000 kW). Le courant suit le rythme de KSP2 (100 kW par EC/s), donc un grand radiateur
pliant consomme environ **10 EC/s** tant qu'il est allumé ; `radiator_ksp1_rates` garde le rythme de KSP1 (0,5 EC/s).
Les radiateurs passifs (`emissiveConstant` élevé) marchent déjà par l'import de Redux. Non reproduits : `parentCoolingOnly`,
`overcoolFactor`, `isCoreRadiator`, la consommation proportionnelle à la charge, le suivi du soleil, la rupture au vent.
Tout est lu dans la décompilation de Redux 26w42c et testé dans l'éditeur (2147 tests EditMode, 2 explicites ignorés) ;
**rien n'a été lancé dans le jeu**.

Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`, filtrer sur `[KSP1PartMod] native:`,
`[KSP1PartMod] thermal:` et `[KSP1PartMod] audit:`.

## Avant de commencer

- [ ] Installé dans `<jeu>/mods/KSP1PartMod/` : `swinfo.json` en version **0.11.0**. `animations.json` : si le fichier
  installé n'est pas celui de 0.10.0, il n'a pas été remplacé ; y ajouter les cinq clés `ksp1_radiators` (`true`),
  `radiator_deploy_bridge` (`true`), `radiator_auto_on` (`true`), `radiator_flux_scale` (`1`), `radiator_ksp1_rates`
  (`false`). Sans elles, ce sont les valeurs par défaut, c'est équivalent.
- [ ] Une sauvegarde rapide avant chaque test de vol. Campagne où le mod est activé (Edit Pack).
- [ ] Pièces de test (de série KSP1) : `radPanelLg` (fixe), `foldingRadMed` et `foldingRadLarge` (dépliables), un ISRU
  KSP1 (0.10.0) ou tout moteur pour chauffer ; un radiateur passif (un panneau structurel).

## 1. Au chargement

- [ ] `native: cooler reference: … KSP2 cooler(s), median 100 kW per EC/s (…)`. Aucune ligne `no KSP2 cooler found,
  fallback` (sinon la recopier : les pièces KSP2 n'étaient pas lues à l'import).
- [ ] `native: radiators: … part(s) got Module_Cooler, … Module_ActiveRadiator (… kW in total); merged: …; resources
  dropped: …; not reproduced: …`. **Attendu avec l'installation actuelle** : 17 pièces `Module_Cooler`, 15
  `Module_ActiveRadiator`, 0 ressource abandonnée.
- [ ] `native: radiators: passive: … KSP1 part(s) with emissiveConstant >= 0.8 …` (attendu : environ 486).
- [ ] `thermal: armed: deploy bridge on, auto on on`. Aucune ligne `thermal: patch failed` ni `thermal: <membre> not found`.
- [ ] Une ligne `native: <pièce>: radiator -> Module_Cooler: 200 kW (maxEnergyTransfer 10000 / 50 x 1), ElectricCharge 2
  (KSP1 …)` pour `radPanelLg`, et `Module_ActiveRadiator: 1000 kW` pour `foldingRadLarge` (EC 10).

## 2. VAB

- [ ] L'infobulle d'un radiateur KSP1 (`radPanelLg`) montre « chaleur retirée » **200 kW** et sa consommation d'EC (2/s).
  Un `foldingRadLarge` montre **1000 kW**.
- [ ] Une pièce KSP1 sans radiateur (réservoir) n'a aucun refroidisseur ; un radiateur passif n'en a pas non plus.

## 3. Radiateur fixe

- [ ] En vol, le PAM du `radPanelLg` montre le refroidisseur KSP2 **déjà allumé** (« inactif », puis « retire … W » quand
  une pièce chauffe), et il consomme son EC.
- [ ] Le groupe d'action Radiateurs l'éteint et le rallume.
- [ ] Un radiateur fixe que le joueur a éteint reste éteint après sauvegarde et rechargement (ligne `thermal: <pièce>:
  cooler state repaired: off, IDLE -> OFF` possible au rechargement, c'est la réparation de l'état de départ).

## 4. Radiateur dépliable

- [ ] Plié, le `foldingRadMed` ne montre pas l'interrupteur du refroidisseur et ne consomme rien (« rentré »).
- [ ] Lignes `thermal: <pièce>: Module_ActiveRadiator deploy follows deploy:Deploy:0` (ou la clé d'entrée réelle). Sinon
  `always extended (…)` avec sa raison : la noter.
- [ ] Déployé avec le bouton KSP1PartMod, il s'allume tout seul : `thermal: <pièce>: radiator turned on when extended`,
  puis « retire … W » quand une pièce chauffe. Replié, il s'éteint et cache l'interrupteur.
- [ ] **Allumage au lancement.** Un vaisseau lancé avec un radiateur déjà déployé (VAB) : ligne `thermal: <pièce>: auto
  on: new part, an extended start turns it on`, et il refroidit. Si la ligne manque, l'allumage automatique a été raté
  (`justLaunched` déjà effacé) : le radiateur reste éteint jusqu'à ce que le joueur l'allume, rien d'autre ne casse.
- [ ] **Éteint par le joueur, reste éteint.** Déployer, éteindre l'interrupteur à la main, sauvegarder, recharger : ligne
  `auto on: saved cooler off, only a later extension turns it on`, et le radiateur reste éteint (replier puis déployer
  le rallume).
- [ ] Un radiateur dépliable sauvegardé allumé et déployé revient allumé et refroidit (pas bloqué sur « inactif » sans
  rien retirer) ; ligne `cooler state repaired: on, OFF -> IDLE` possible.

## 5. Chaleur

- [ ] Un ISRU KSP1 en marche (0.10.0) chauffe ; avec un radiateur allumé sur le même vaisseau, sa température monte moins
  vite ou se stabilise (le refroidissement KSP2 est commun à tout le vaisseau, la pièce du radiateur n'a pas besoin
  d'être voisine). **Noter les températures** avec et sans radiateur.
- [ ] Ligne `thermal: <pièce>: cooling <kW> kW, ElectricCharge <taux> (vessel pool)` au premier refroidissement de
  chaque pièce (200 kW pour `radPanelLg`).
- [ ] Si les radiateurs sont trop forts ou trop faibles, régler `radiator_flux_scale` (0,01 à 100) et le noter.
- [ ] Un radiateur passif refroidit comme avant (aucun changement attendu).

## 6. Électricité

- [ ] Noter la consommation d'un `foldingRadLarge` allumé : **10 EC/s attendus**, constants, même sans chaleur à retirer
  (les radiateurs KSP1 ne consommaient que 0,5 EC/s au plus).
- [ ] Si c'est trop pour les vaisseaux KSP1 (petites capsules vidées par l'allumage automatique), essayer
  `radiator_ksp1_rates: true` (0,5 EC/s) ou `radiator_auto_on: false`.

## 7. Interrupteurs à couper en cas de régression

À éditer dans `animations.json`, puis relancer le jeu (l'import se refait au chargement) :

- [ ] `ksp1_radiators: false` redonne 0.10.0 : aucun refroidisseur sur les pièces KSP1.
- [ ] `radiator_deploy_bridge: false` : un radiateur plié refroidit quand même, et ne s'allume jamais tout seul.
- [ ] `radiator_auto_on: false` : tous les radiateurs démarrent éteints.
- [ ] `radiator_flux_scale` pour la puissance, `radiator_ksp1_rates` pour le courant.

## 8. Audit

- [ ] La ligne `audit: native modules: … N radiator(s), 0 part(s) where one failed to initialize` compte les radiateurs
  (`Module_Cooler` ou `Module_ActiveRadiator` par pièce), avec **0 échec** d'initialisation. La colonne `native_modules`
  du CSV porte `Module_Cooler` / `Module_ActiveRadiator`.
