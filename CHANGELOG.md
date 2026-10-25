# Changelog

## 0.2.0 - 2026-10-25

- KSP1 part animations, phase 1: a `Module_Ksp1Animations` module is added to imported parts at campaign load. It drives `ModuleAnimateGeneric` (several per part), heat, throttle, colour, light, gear and aero-gate animations, with a Part Action Menu button and an action-group action per entry, state kept in saves, and symmetry.
- Redux's own deployable runtime is kept but restricted to the clips it owns; `own_first_deployable` hands the first deployable over to this mod.
- The `.mu` reader fix is now guarded by a behavioural self-check and is taken back if it fails (`mu:` log lines).
- New `animations.json` keys: `enabled`, `own_first_deployable`, `shader_property_map` (with the existing `fix_mu_reader`).
- 408 EditMode tests. Nothing here has run in game yet; checklist in `docs/testing/2026-10-25-animations-in-game-checklist.md`.

## 0.1.0 - 2026-10-25

- Places the KSP1 parts imported by Redux in the tech tree of campaigns that enable the mod.
- Mapping table from the stock 1.12 and Community Tech Tree nodes to KSP2 nodes, with a category fallback and a catch-all.
- New robotics and miscellaneous nodes, created only when they receive parts.
- `overrides.json` to move a part or a whole KSP1 node.
- English and French names for the new nodes.
- Parts without a category (EVA kerbals, flag) and parts needing the placeholder node Unresearcheable are no longer placed; they no longer create a miscellaneous node.
- The 50 stock models of `.mu` versions 0 to 2 (parachutes, batteries, LV-T30/T45, ladders, and others) now load instead of the placeholder: a guarded patch on Redux's `.mu` reader, switched off with `"fix_mu_reader": false` in the new `animations.json`.
