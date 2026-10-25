# KSP1PartMod 0.3.0 : liste de tests en jeu de la barre des sous-catégories (2026-10-25)

Deuxième barre verticale du VAB, à droite de la barre des catégories, quand une catégorie KSP1 de
Redux (un mod KSP1 : `Squad`, `Bluedog_DB`, `KerbalReusabilityExpansion`…) est choisie. Rien de cela
n'a encore tourné dans le jeu. Cette liste dit quoi regarder et quelle ligne du log le prouve. Les
listes du placement et des animations restent valables.

## Avant de commencer

- Le log annonce `[KSP1PartMod] loaded 0.3.0`.
- `animations.json` contient `"subcategory_bar": true` (une clé absente vaut aussi `true`).
- Mod activé dans la campagne (Edit Pack). Log : `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`,
  filtrer sur `[KSP1PartMod] ui:`.

## 1. Démarrage

- [ ] Au démarrage du jeu : `[KSP1PartMod] ui: sub-category bar armed on AssemblyPartsPicker.ApplyPartsFilter`.
- [ ] Si à la place `[KSP1PartMod] ui: sub-category bar off, not found in this Redux: …` : Redux a changé, la
  fonctionnalité est coupée. Noter la liste et s'arrêter là.

## 2. Premier passage dans le VAB

- [ ] À la première catégorie KSP1 choisie : `[KSP1PartMod] ui: indexed <n> KSP1 part(s) in <k> type(s): Pods, FuelTank, …`.
  Si la ligne finit par `(Redux import not complete yet)`, noter s'il en vient une seconde sans cette mention.
- [ ] Une ligne par mod : `[KSP1PartMod] ui: <mod>: <n> visible part(s) in <k> type(s): Pods 3, Engine 12, …; bar shown`
  (ou `bar hidden (fewer than 2 types)`). Le nombre de pièces doit être celui de la grille avec « Tout ».
- [ ] **Questions ouvertes à trancher**, lignes écrites une seule fois, à la création de la barre. Les copier en entier
  dans le compte rendu :
  - `[KSP1PartMod] ui: sub-category bar created on <picker> under '<parent>', next to '<toolbar>' (button template '<nom>', stock toggle group: none|'<nom>', stock layout: VerticalLayoutGroup|none)`.
    `stock toggle group` répond à la question 1 (groupe partagé ou non ; la copie le quitte de toute façon) ;
  - sept lignes `[KSP1PartMod] ui: layout <nom> '<objet>' parent '<parent>' [<LayoutGroup>] anchors (…) pivot (…) offsetMin (…) offsetMax (…) rect (…) active=…`
    pour `partsPickerBGPanelRT`, `toolbar parent`, `partsCategoriesToolbarRT`, `partContentRT`, `SearchFilterSortRT`,
    `partsPickerScrollRect` et `manipulateShipToolbarRT` (question 2 : ancrage de la grille) ;
  - `[KSP1PartMod] ui: stock category sprites: Pods='…' FuelTank='…' … Storage=<no button>|'…' Payload='…'` (question 3 :
    un bouton de série, donc un sprite, pour chaque type).
- [ ] `[KSP1PartMod] ui: bar placed at x … y … size …x… in '<parent>' (toolbar scrollbar …); partContentRT offsetMin.x a -> b; SearchFilterSortRT …` :
  `b - a` doit valoir la largeur de la barre.

## 3. Affichage

- [ ] Catégorie `Squad` : la barre apparaît collée à droite de la barre des catégories, même hauteur, même
  largeur qu'une colonne de boutons. La grille commence après elle, sans chevauchement ni pièce coupée à droite.
- [ ] La ligne de recherche et « Grouper par » : soit au-dessus (inchangés, log `above or below the bar, left as it is`),
  soit à côté et décalés comme la grille (log `beside the bar, narrowed too`). Rien ne doit se chevaucher.
- [ ] `[KSP1PartMod] ui: <mod>: bar built with <n> button(s): All, Pods (stock Pods sprite '…'), …, Robotics (icon of part '…')`.
  Vérifier : « Tout » en premier, avec l'icône du bouton du mod ; l'ordre KSP1 ; Robotique et Autres avec l'icône
  d'une de leurs pièces ; Cargo avec le sprite Storage (sinon Payload).
- [ ] Infobulles : le nom du type traduit (français : « Moteurs », « Réservoirs »… si le jeu a la traduction ;
  « Robotique », « Autres », « Tout » sinon) et le nombre de pièces.
- [ ] Un mod qui n'a qu'un type (par exemple `CommunityResourcePack` s'il a des pièces) : pas de barre.
- [ ] Catégorie de série (Pods, Engines…) ou Favoris : pas de barre, et la grille reprend sa place d'origine.
- [ ] Panneau replié (bouton de repli) : pas de barre ; déplié : elle revient.
- [ ] Beaucoup de types (BDB) : la molette fait défiler la barre si elle dépasse la hauteur.

## 4. Filtre

- [ ] Clic sur Moteurs dans `Squad` : la grille ne montre que les moteurs ; le bouton reste enfoncé, « Tout » ne
  l'est plus ; le bouton `Squad` de la barre principale reste sélectionné.
  Log : `[KSP1PartMod] ui: Squad: filter Engine keeps <n> of <m> part(s)`.
- [ ] Clic sur « Tout » : `… filter All keeps <m> of <m> part(s)`, toute la grille revient.
- [ ] « Grouper par » (Type, Taille, Palier…) sur un type choisi : les groupes ne contiennent que ce type.
- [ ] Recherche (taper « tank ») avec un type choisi : la barre disparaît, la recherche porte sur toutes les pièces.
  Effacer la recherche : la catégorie revient avec le même type choisi.
- [ ] Mémoire par mod : Moteurs dans `Squad`, puis `Bluedog_DB` (s'ouvre sur « Tout » ou sur son propre choix),
  puis retour à `Squad` : Moteurs est toujours choisi. Elle dure jusqu'à la fermeture du jeu.
- [ ] **Aucun type vide** : en carrière avec peu de nœuds débloqués, chaque bouton de type ouvre une grille non vide.
  Si un type choisi n'a plus de pièce visible (filtre de palier), la grille repasse sur « Tout ».
- [ ] Bouton Favoris, sous-assemblages et glisser-déposer d'une pièce filtrée : inchangés.

## 5. Interrupteurs et cas limites

- [ ] `"subcategory_bar": false` (redémarrer) : `[KSP1PartMod] ui: sub-category bar disabled by animations.json`, plus de barre.
- [ ] Campagne sans le mod (même session) : pas de barre, aucune ligne `filter`.
- [ ] Hangar BAE (si présent) : même comportement, barre propre à ce sélecteur.
- [ ] Aucune ligne `[KSP1PartMod] ui: sub-category bar failed in …, stopped for this session` ; si elle apparaît,
  copier l'exception : la barre s'est coupée sans gêner le jeu.
