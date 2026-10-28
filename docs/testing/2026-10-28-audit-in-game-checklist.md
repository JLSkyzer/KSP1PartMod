# KSP1PartMod 0.6.0 : liste de tests en jeu du mode audit (2026-10-28)

L'audit charge chaque pièce KSP1 une fois, hors écran et une par une, et écrit un rapport des pièces qui échouent. Il ne
touche ni à la fusée en cours, ni à la liste des pièces, ni à la sélection, ni à l'historique d'annulation. Rien n'a
encore été lancé dans le jeu : les tests EditMode passent, mais aucune ligne ci-dessous n'est vérifiée en jeu.

Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`, filtrer sur `[KSP1PartMod] audit:`.

## Avant de commencer

- [ ] Installé dans `<jeu>/mods/KSP1PartMod/` : `swinfo.json` en version **0.6.0**, `animations.json` avec les clés
  `audit_on_vab_entry` (false), `audit_frame_budget_ms` (8) et `audit_unload_every` (100).
- [ ] Mod activé **dans la campagne** (Edit Pack) : l'audit ne démarre pas si la fonctionnalité du mod est coupée
  (`audit: not started at this VAB entry: KSP1PartMod feature off in this campaign`). Il faut le **VAB**, pas le BAE
  (`not the VAB (BAE)`).
- [ ] Sans rien changer (clé à false) : au démarrage, `audit: off (audit_on_vab_entry)`.

## 1. Audit des pièces KSP1

- [ ] Dans `<mod>/animations.json`, mettre `"audit_on_vab_entry": true`, lancer le jeu, charger une campagne où le mod
  est activé et entrer au VAB. Ne rien construire pendant l'audit.
- [ ] Au démarrage : `audit: armed, runs at the next VAB entry (budget 8 ms per frame, unload every 100 part(s))`. Aucune
  ligne `audit: … not found, audit off` (sinon, recopier la ligne).
- [ ] Dans `Player.log` : `audit: starting: …`, une ligne de progression toutes les 100 pièces
  (`audit: <i>/<n> part(s), …`) suivie de la ligne `textures: memory at audit …`, aucune ligne `OAB event … grew`, aucune
  ligne `message … kept … subscription(s)`, aucune ligne `control part … failed`, puis `audit: done in …`.
- [ ] Le VAB reste utilisable (fps, caméra), avec des à-coups possibles à chaque déchargement
  (`audit: unloaded unused assets after … part(s)`). Noter la durée de ces à-coups.
- [ ] Le rapport est dans `<mod>/audit/` : ouvrir `audit-<date>.csv` et `audit-<date>.md`.
- [ ] Prendre trois pièces signalées `threw` et les poser à la main : même erreur dans le log ? Poser une pièce signalée
  `ok` : rien dans le log ?
- [ ] La ligne `audit: pods: …` répond à la question du Crew Dragon : les capsules KSP1 qui ont des moteurs sont-elles
  finalisées avec un `Module_Command` initialisé (un pod avec moteurs fonctionne-t-il comme pod ?). La comparer au
  comportement réel d'une de ces capsules posée à la main.
- [ ] Noter : le nombre de pièces par type d'échec (`audit: by kind:`), la mémoire au début et à la fin
  (`textures: memory at audit start` / `end`), la durée totale, et la ligne `audit: slowest:` (temps par pièce).
- [ ] Les textures des pièces KSP1 déjà posées dans la fusée sont intactes pendant et après l'audit.
- [ ] Un second audit, annulé : dans une nouvelle session (ou après avoir effacé les lignes de la session dans le CSV),
  relancer l'audit et sortir du VAB pendant qu'il tourne : une ligne `audit: cancelled at …`, puis la reprise au retour
  au VAB (`already reported` dans la ligne `starting`).
- [ ] Si le jeu plante pendant l'audit : relancer, ligne `audit: the previous run stopped during <pièce> (game closed or
  crashed): added to skip.txt`. Cette pièce n'est plus auditée (`<mod>/audit/skip.txt`).
- [ ] Remettre `"audit_on_vab_entry": false` à la fin.

## 2. À envoyer

- [ ] Les fichiers `<mod>/audit/audit-<date>.csv` et `audit-<date>.md` (et `skip.txt` s'il existe).
- [ ] Les lignes `[KSP1PartMod] audit:` et `textures: memory at audit` du `Player.log`.

## 3. Points à connaître

- Une pièce remappée par Patch Manager vers un asset non KSP1 n'a ni `rebuilt` ni avertissements d'import capturés
  (chargement asynchrone hors capture). Une pièce remappée vers le prefab d'une autre pièce KSP1 est capturée.
- Les 5 pièces de contrôle (KSP2 de série : un moteur, une capsule, un réservoir, puis les premières par ordre de nom)
  doivent sortir propres. Si l'une échoue, la ligne `control part … failed` dit que le harnais est suspect pour ce type
  d'échec : ne pas se fier aux pièces KSP1 de ce type.
- Quitter le VAB annule l'audit tout de suite ; un retour rapide le reprend.
