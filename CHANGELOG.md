# Changelog

## 0.1.0 - 2026-10-25

- Places the KSP1 parts imported by Redux in the tech tree of campaigns that enable the mod.
- Mapping table from the stock 1.12 and Community Tech Tree nodes to KSP2 nodes, with a category fallback and a catch-all.
- New robotics and miscellaneous nodes, created only when they receive parts.
- `overrides.json` to move a part or a whole KSP1 node.
- English and French names for the new nodes.
- Parts without a category (EVA kerbals, flag) and parts needing the placeholder node Unresearcheable are no longer placed; they no longer create a miscellaneous node.
- The 50 stock models of `.mu` versions 0 to 2 (parachutes, batteries, LV-T30/T45, ladders, and others) now load instead of the placeholder: a guarded patch on Redux's `.mu` reader, switched off with `"fix_mu_reader": false` in the new `animations.json`.
