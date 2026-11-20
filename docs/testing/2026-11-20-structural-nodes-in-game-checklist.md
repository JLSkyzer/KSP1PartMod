# Noeuds structurels (0.17.0) : liste de tests en jeu

Interrupteur : `ksp1_structural_nodes` dans `animations.json` (vrai par défaut ; faux = tout reste visible, comme 0.16.4).
Lignes du log à lire : `[KSP1PartMod] anim: <pièce> structure '<racine>': found, n of m attach node(s) found`, puis `shown` / `hidden`.

## Au VAB

1. Poser une base de coiffe BDB à ferme (par exemple `bluedog_AgenaB_FairingBase_Ranger`) seule : les capuchons `Cap1` à `Cap6` et les
   fermes `Truss1` et suivantes doivent être **cachés** (avant 0.17.0, tout était visible).
2. Poser une pièce sous le nœud `interstage01a` ou `interstage01b` : `Cap1` et les fermes qui listent ce nœud apparaissent. Ligne
   `structure 'Cap1' shown`.
3. Retirer la pièce : ils disparaissent (`hidden`).
4. Bouton « Hide Truss Structure » dans le PAM de cette pièce : toutes les racines se cachent, même avec des pièces attachées. Le bouton
   devient « Show Truss Structure ». Sauvegarder le vaisseau, le recharger : l'état est gardé.
5. Une pièce en symétrie : le bouton agit sur les deux.
6. Carénages Squad (`fairingSize1`, `fairingSize1p5`, `fairingSize2`, `fairingSize3`, `fairingSize4`) : la coiffe de la 0.15.0 reste
   comme avant ; les capuchons d'interétage suivent leurs nœuds. **À regarder** : que la coiffe elle-même ne disparaisse pas.
7. Une pièce sans nœud correspondant dans le modèle : ligne `attach node(s) found` avec moins que prévu ; la racine reste cachée.

## En vol

8. Un vaisseau avec une base de coiffe BDB et un étage dessus : les capuchons et fermes sont visibles à l'état de départ.
9. Découpler l'étage : les racines liées à ce nœud disparaissent (`hidden`, ligne en vol).
10. Aucune exception avec un cadre `KSP1PartMod` dans `Player.log`.

## Hors périmètre (volontairement)

- « Interstage Nodes » : le type des nœuds ne change pas.
- La coque de rentrée peut encore contenir la géométrie d'un capuchon caché (le prefab n'est pas modifié).
