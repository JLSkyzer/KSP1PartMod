# KSP1PartMod 0.14.0 : liste de tests en jeu des roues (2026-10-30)

Redux laissait tomber les neuf modules de roue de KSP1 (`ModuleWheelBase`, `…Suspension`, `…Steering`, `…Motor`,
`…MotorSteering`, `…Brakes`, `…Damage`, `…Lock`, `…Bogey`) : une roue, un train ou une jambe KSP1 était une pièce rigide,
qui ne roulait pas et ne s'enfonçait pas. 0.14.0 les importe comme les modules de roue **natifs** de KSP2
(`Module_WheelBase`, `Module_WheelSuspension`, etc.), champ par champ. Toute la physique est celle de KSP2 : aucun solveur,
aucune friction ni aucun ressort à nous. `ModuleWheelDeployment` reste celui de Redux (`Module_Deployable` natif), et la
roue de KSP2 s'allume et s'éteint d'elle-même avec lui. Le bouton Gear du pont de 0.2.x se retire sur ces pièces, pour que
G ne bascule le train qu'une fois. Couples supposés en kN·m (non documenté dans les cfg). Tout est lu dans la décompilation
de Redux 26w42c et testé dans l'éditeur (2592 tests EditMode, 2 explicites ignorés) ; **rien n'a été lancé dans le jeu**.

Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`, filtrer sur `[KSP1PartMod] native:`,
`[KSP1PartMod] wheel:`, `[KSP1PartMod] anim:` et `[KSP1PartMod] audit:`.

## Avant de commencer

- [ ] Cette liste suit le test de 0.13.1 en cours. **Installer 0.14.0 seulement après ce test** : le zip attend dans
  `KSP1PartMod/Deploy/KSP1PartMod.zip`. `swinfo.json` installé en version **0.14.0**.
- [ ] Garder l'`animations.json` installé : les neuf nouvelles clés (`ksp1_wheels`, `wheel_steering`, `wheel_motors`,
  `wheel_brakes`, `wheel_leg_locks`, `wheel_damage`, `wheel_bogeys`, `wheel_ksp1_friction`, `wheel_ksp1_deploy_timing`)
  prennent leur valeur par défaut, toutes `true`. Les ajouter seulement pour en couper une.
- [ ] Une sauvegarde rapide avant chaque test de vol. Campagne où le mod est activé (Edit Pack).
- [ ] Pièces de test : roues de rover KSP1 `roverWheel1` (motorisée, direction) et `roverWheel3` (moteur-direction),
  trains `GearSmall` et `GearMedium`, jambes `landingLeg1-2` (LT-2), jambes KRE Falcon (friction 4) et KRE HexLegs (pied
  qui pivote, bogie).

## 1. Au chargement

- [ ] `native: wheel reference: <n> KSP2 wheel part(s) (LEG …, FREE …, MOTORIZED …), median radius … m, brake … kN·m,
  lock … kN·m, motor peak … kN·m at … EC/s, rated load … t`. **Recopier la ligne** : ce sont les roues KSP2 de référence,
  pour comparer freins et moteurs. Si elle dit `none read (…)`, recopier la raison.
- [ ] `native: wheels: 36 part(s) got Module_WheelBase (23 leg(s), 7 free, 6 motorized); suspension 36, steering 9,
  motor 5, motor-steering 1, brakes 12, damage 35, lock 23, bogey 12; refused: none; not reproduced: …; unused by Redux's
  solver: …`. **Attendu avec l'installation actuelle** : ces chiffres exacts et `refused: none`. Un autre chiffre : recopier
  la ligne (les patchs ModuleManager du jeu peuvent changer les cfg bruts comptés dans l'éditeur).
- [ ] `native: wheels: B9: 1 subtype edit(s) of wheel modules, 1 on state fields not carried (bluedog_LRV_Wheel:
  frictionMultiplier)` (la friction « Tire » de ce sous-type B9 n'est pas portée, c'est prévu).
- [ ] Une ligne `native: <pièce>: wheel <type> r <rayon> m on '<hôte>': <modules> (deploy on at …, off at …; friction
  …/…, auto|manual)` par pièce. Exemples attendus : une jambe LT-2 en `LEG`, friction `1/1, manual` ; une jambe KRE Falcon
  `4/4, manual` ; une roue de rover `1.5/1.3, auto`.
- [ ] `wheel: armed: …` avec les modules allumés. Aucune ligne `wheels: … not found`, `wheel: … not found` ni
  `wheel: patch failed`.
- [ ] Aucune ligne `[Module_WheelBase]: No transform called …`, aucun `Unable to find Module_WheelBase`.
- [ ] **Collider de suspension.** Aucune `NullReferenceException` venant de `Module_WheelSuspension.OnInitialize`. Aucune
  ligne `ModuleWheelSuspension: suspensionColliderName '…' … cleared` n'est attendue sur l'installation actuelle (aucune
  pièce installée n'est concernée, d'après l'éditeur) ; si une apparaît, la recopier.

## 2. VAB

- [ ] L'infobulle d'une roue de rover KSP1 (`roverWheel1`) montre la consommation d'EC du moteur.
- [ ] Les trains et les jambes se posent et se replient comme avant (lignes `anim:` inchangées, animation du VAB).
- [ ] **Groupe d'action Train d'atterrissage.** Pour un train KSP1 à `Module_Deployable` natif, le VAB ne liste **aucune**
  action Gear : c'est le même comportement que les trains KSP2, qui reçoivent leur action en vol seulement. Noter ce que le
  VAB montre. Ligne `anim: … gear bridge: Module_WheelBase already on Gear` pour ces pièces.

## 3. Rover

- [ ] Un petit rover de roues KSP1 roule : la touche avant fait avancer, la direction tourne (les roues avant pivotent),
  les freins (B) arrêtent, l'EC baisse en roulant et pas à l'arrêt. Les roues tournent visuellement.
- [ ] Ligne `wheel: <pièce>: MOTORIZED, collider host 'WheelCollider' found, wheel transform '…' found, deploy gate none
  (fixed wheel)`.
- [ ] **Unités.** Comparer à un rover de roues KSP2 : freins et moteur du même ordre de grandeur. Des freins ou un moteur
  1000 fois trop forts ou trop faibles veulent dire que l'unité supposée (kN·m) est fausse : le noter.
- [ ] `roverWheel3` (moteur-direction) : il avance et tourne aussi.
- [ ] Le PAM d'une roue montre la friction, la direction et le moteur avec les valeurs KSP1 (friction auto, moteur allumé).
  Si une jambe montre la friction **auto** au lieu de manuelle, les valeurs de départ ne survivent pas à la sérialisation
  de Redux : le noter.

## 4. Avion

- [ ] Un avion avec un `GearSmall` à l'avant et deux `GearMedium` : il roule droit sur la piste et freine.
- [ ] Le train se replie et se déploie avec G **une seule fois par appui** (avant cette version, deux bascules pouvaient
  s'annuler). Train replié, l'avion ne roule pas dessus.
- [ ] Ligne `wheel: <pièce>: FREE, collider host '…' found, …, deploy gate native Module_Deployable`.
- [ ] **Vaisseau de 0.13.1.** Charger un avion sauvegardé en 0.13.1 avec un train KSP1 : il gardait une association Gear
  vers l'action de notre pont, qui n'existe plus sur ces pièces. Vérifier qu'il se charge sans erreur dans le log et que G
  replie et déploie le train une fois par appui.

## 5. Jambes

- [ ] Un atterrisseur sur la Mun avec quatre LT-2 (`landingLeg1-2`) : déployer avec G, se poser. Les jambes s'enfoncent
  un peu à l'impact, le vaisseau ne rebondit pas et ne glisse pas. Le vaisseau est « Posé ».
- [ ] La même chose avec des jambes KRE Falcon (friction 4) et des KRE HexLegs (pied qui pivote, bogie).
- [ ] Noter si l'animation du piston et la compression de la suspension se voient ensemble (si les jambes paraissent
  rigides alors que le vaisseau s'enfonce, c'est la pose tenue du clip qui l'emporte : visuel seulement).
- [ ] `landingLeg1`, `landingLeg1-2`, `bluedog_LRV_Wheel` et `bluedog_X15_NoseGear` n'ont pas de WheelCollider dans leur
  `.mu` ; KSP2 crée le sien depuis les données de la roue (supposé). Vérifier qu'ils roulent ou portent normalement.
- [ ] **Jambes que Redux a gardées en `Ksp1Module_Deployable`.** Lignes `wheel: <pièce>: …, deploy gate none: always
  active (Redux kept its legacy deployable)`. **Recopier les noms de ces pièces** : elles gardent l'action Gear de notre
  pont, mais n'ont pas de gating natif, donc **elles roulent et portent même repliées**. C'est connu, pas une régression.
- [ ] **Seuil de déploiement.** Une jambe ou un train à `TsubSys` < 1 (`deploy on at 0.7, off at 0.3` dans sa ligne
  `native:`) : la roue s'allume vers la fin du déploiement et s'éteint au début du repli. Si c'est l'inverse (éteinte trop
  tôt ou trop tard), essayer `wheel_ksp1_deploy_timing: false`.

## 6. Dégâts

- [ ] Poser un rover trop vite : une roue casse (« Cassée » au PAM, la pièce reste), et un kerbal en EVA la répare.

## 7. Sauvegarde

- [ ] Sauvegarder un rover et un atterrisseur posés, recharger : ils restent posés, les réglages de friction et de freins
  du PAM sont gardés.
- [ ] Un vaisseau sauvegardé en 0.13.1 avec des roues KSP1 reçoit les modules de roue au chargement : il se charge sans
  erreur et ses roues marchent.
- [ ] **Posé en 0.13.1, chargé en 0.14.0.** Un atterrisseur ou un rover sauvegardé posé en 0.13.1 ne doit ni sauter ni
  rebondir au chargement en 0.14.0. Ses jambes et ses roues reçoivent maintenant des colliders de roue, qui peuvent
  descendre plus bas que les colliders de pièce sur lesquels il reposait. Faire une sauvegarde de secours avant, et noter
  s'il saute, de combien, et s'il casse quelque chose.
- [ ] Ligne `native: <pièce>: … ModuleWheelBogey: wheelTransformRefName '…' and wheelTransformBaseName '…' cleared (…)` :
  aucune attendue sur l'installation actuelle (les deux trains `GearLarge` et `GearExtraLarge` ont leurs roues `wheels2`
  dans le modèle) ; si une apparaît, la recopier.

## 8. Points supposés, à lire dans le log

- [ ] **Ordre d'initialisation en vol** (supposé) : notre module s'initialise après `Module_WheelBase`. Le retrait du pont
  ne dépend plus de cet ordre (il refait les deux recherches de transformations de la roue) ; si une ligne `wheel:` dit
  `MISSING`, la recopier avec la ligne `anim:` de la même pièce.
- [ ] **La ligne `wheel:` montre les valeurs de l'import** (noms, type de roue, déployable) : une modification B9 de la
  base de roue n'y apparaîtrait pas.
- [ ] **Courbes `torqueCurve` à trois valeurs** : traitées comme des clés lissées (tangentes de `Add(t, v)`), supposé.
  Aucune pièce installée n'en a ; à vérifier seulement avec une collection plus large.

## 9. Interrupteurs à couper en cas de régression

À éditer dans `animations.json`, puis relancer le jeu (l'import se refait au chargement) :

- [ ] `ksp1_wheels: false` redonne 0.13.1 : aucune roue, les clés ci-dessous ne font plus rien. Un vaisseau sauvegardé en
  0.14.0 perd alors ses modules de roue.
- [ ] Puis, un par un : `wheel_damage`, `wheel_bogeys`, `wheel_leg_locks`, `wheel_ksp1_friction` (frictions de KSP2,
  1.45/1.1 et auto), `wheel_ksp1_deploy_timing` (0.98/0.02 de KSP2, sans collider de remplacement), `wheel_motors`,
  `wheel_steering`, `wheel_brakes`.

## 10. Audit

- [ ] La ligne `audit: native modules: … N wheel part(s), 0 part(s) where one failed to initialize` compte les roues
  (une pièce par roue, quel que soit le nombre de modules), avec **0 échec** d'initialisation. La colonne
  `native_modules` du CSV porte les `Module_Wheel*` de chaque pièce. La copie de l'audit est une pièce du VAB, où KSP2 ne
  monte aucune physique de roue : un échec y est un vrai défaut.
