# Gating des antennes (0.18.0) : liste de tests en jeu

Interrupteur : `ksp1_antenna_gating` dans `animations.json` (vrai par défaut ; faux = l'antenne transmet repliée, comme 0.17.0).
Ligne du log : `[KSP1PartMod] anim: <pièce> antenna: transmitter gated by deploy:<clip>:0, starts <état>`.

1. Un vaisseau avec une antenne parabolique BDB ou Squad (par exemple `HG-5` ou `commDish88-88`), **repliée** au lancement : la ligne
   `antenna: transmitter gated` dit `starts Retracted`. L'antenne ne doit plus compter comme émettrice : pas de liaison via elle, et
   l'envoi de données par elle est refusé.
2. Déployer l'antenne (bouton du PAM) : pendant le déploiement elle ne transmet pas ; une fois le clip terminé (`Extended`), la liaison
   revient et l'envoi de données marche.
3. La replier : la liaison via cette antenne se coupe ; une transmission en cours s'arrête.
4. Une antenne qui démarre déployée : liaison dès le départ.
5. Sauvegarder et recharger en vol avec l'antenne repliée, puis déployée : même état à la reprise.
6. Une pièce avec deux antennes (ou un émetteur natif de KSP2 sur le même vaisseau) : celles de KSP2 ne sont pas touchées.
7. Aucune exception avec un cadre `KSP1PartMod` dans `Player.log`.

À lire en cas de doute : si la ligne `transmitter gated` manque, c'est que le module de simulation de l'émetteur n'existait pas encore
(il réessaie 120 images) ou que `animations.json` a la clé à faux.

Hors périmètre : le bouclier des ports d'amarrage (`deployAnimationController`), 9 fichiers, à étudier à part.
