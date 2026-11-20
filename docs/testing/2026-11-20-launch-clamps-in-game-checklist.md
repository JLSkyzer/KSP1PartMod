# Pinces de lancement (0.19.0) : liste de tests en jeu

Interrupteur : `ksp1_launch_clamps` dans `animations.json` (vrai par défaut ; faux = la pince n'a aucun module, comme 0.18.0).
Ligne du log : `[KSP1PartMod] native: launch clamps: n part(s) got Module_Decouple ...` (attendu : 7 pièces).

1. Au VAB, poser une fusée avec une ou deux pinces TT-18 (`launchClamp1`) fixées sur le côté, à la base. La pince apparaît dans
   l'étagement (icône de structure) : une ligne d'étage de plus, placée avec les découpleurs.
2. Sur la rampe, lancer l'étage de la pince (touche d'étagement) : la pince se détache de la fusée et reste au sol (pas de poussée).
   Avant 0.19.0 elle partait avec la fusée.
3. Un vaisseau avec une tour BDB (`FASA_Launch_Tower`, `FASA_Umbilical_Tower`) : même chose. Surveiller : tour soudée à la fusée ?
   chute qui traverse le sol ?
4. Le PAM de la pince : les actions de découpleur de série (« Découpler ») ; pas d'animation de libération (non reproduite).
5. Une pièce qui avait déjà un découpleur : aucune ligne `LaunchClamp: the part already has a decoupler` en dehors des cas prévus.
6. Aucune exception avec un cadre `KSP1PartMod` dans `Player.log`.

À lire en cas de doute : `native: <pièce>: LaunchClamp: <clé> not reproduced` (par pièce), et `attach node 'srfAttach' not found` si le
nœud de surface a un autre nom.

Hors périmètre : la tour qui s'étire, l'animation de libération, l'ancrage au sol de KSP1.
