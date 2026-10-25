# Changelog

## 0.2.2 - 2026-10-25

Fixes from the second in-game test.

- **Redux deployables that did not move now move** (KRE landing legs, grid fins and booster wing, and every part with a single `ModuleAnimateGeneric`, `ModuleDeployableSolarPanel`, `ModuleWheelDeployment` or `ModuleLandingLeg`, plus every `ModuleParachute`). Redux binds those clips to a Mecanim copy that moves nothing; the new "Redux follower" entries draw the original clip from Redux's own state (`Data_Deployable` / `Data_Parachute`) at KSP1 speed. Redux keeps its PAM toggle, state, action groups and shielding. Parts with several deployable sources go through Redux's legacy runtime, which works, and are left alone. Switch: `"follow_redux_deployables"` (default true). Log: `anim: <part> follows Redux deployable '<clip>' (<module>)` and one `anim: mod <name>: <n> Redux deployable(s) followed, <m> Redux parachute(s) followed` line per mod.
- **Renderers without any material** on KSP1 parts (drawn with Unity's error colour) are disabled on every part instance, in the VAB and in flight, whether or not the part carries the animation module. Redux's generated reentry hull is never disabled (it gets its material when the reentry effect turns it on); an active hull without a material is switched off instead. Switch: `"hide_material_less_renderers"` (default true). Log: `render: <part> hid <n> renderer(s) without material (<paths>)`.
- **Black KSP1 part icons**: icons are rendered with the same shader swap as Redux's KSP2 parts (`KSP2/Parts/Paintable` → `KSP2/Scenery/Standard (Opaque)`). Switch: `"fix_part_icons"` (default true). Log: `icons: rendered <n> KSP1 icon(s) with hotswapPartShaders=true …`. Icons already rendered in a session stay as they were until the game restarts.
- **KSP1 parts and their VAB categories missing when the game starts in the VAB**: the tech-tree placement now runs right after the game's "Loading Science Data" step, before the VAB builds its part list; the campaign-loaded action only places when that early step could not. Log: `placed after 'Loading Science Data', before the VAB builds its part list`.
- The tech-tree placement no longer reports "completed" twice nor "could not start the placement coroutine" when it was ready at once, and its steps are registered once per session.
- 518 EditMode tests.

## 0.2.1 - 2026-10-25

Fixes from the first in-game test.

- Parts carrying the KSP1 animation module no longer fail to load in the VAB ("Unknown Type: Module_Ksp1Animations"; they could not be placed again after deletion and their animations did not run). The module's behaviour and component types are now registered with Unity's ECS `TypeManager` at startup or campaign load; if that fails, the module is not added at all for the session (`anim: registered ECS type …` / `ECS registration failed` log lines).
- The tech-tree placement runs again on later campaign loads: its coroutine now runs on the mod's own hidden, persistent object instead of the plugin's, which can be inactive ("could not start the placement coroutine").
- New tech nodes stay inside the vertical range of their tier, at the free slot nearest to their anchor, instead of above the visible tree (`new node … tree y range [..]` log line). The configured offset now only chooses the preferred side.
- One-shot material diagnostic of a few KSP1 parts in the VAB (`diag:` lines), switched off with `"diagnostics": false` in `animations.json`.
- 430 EditMode tests.

## 0.2.0 - 2026-10-25

- KSP1 part animations, phase 1: a `Module_Ksp1Animations` module is added to imported parts at campaign load. It drives `ModuleAnimateGeneric` (several per part), heat, throttle, colour, light, gear and aero-gate animations, with a Part Action Menu button and an action-group action per entry, state kept in saves, and symmetry.
- Redux's own deployable runtime is kept but restricted to the clips it owns; `own_first_deployable` hands the first deployable over to this mod.
- The `.mu` reader fix is now guarded by a behavioural self-check and is taken back if it fails (`mu:` log lines).
- New `animations.json` keys: `enabled`, `own_first_deployable`, `shader_property_map` (with the existing `fix_mu_reader`).
- Redux's runtime is never stopped on missing data (no data, no entry of ours, no clip list: left untouched and logged), is restricted after our entries are bound so a failed entry's clip goes back to Redux, and a clip Redux keeps is never taken over; the tech-tree step is registered first and a failing animation step registration is logged without costing it.
- 418 EditMode tests. Nothing here has run in game yet; checklist in `docs/testing/2026-10-25-animations-in-game-checklist.md`.

## 0.1.0 - 2026-10-25

- Places the KSP1 parts imported by Redux in the tech tree of campaigns that enable the mod.
- Mapping table from the stock 1.12 and Community Tech Tree nodes to KSP2 nodes, with a category fallback and a catch-all.
- New robotics and miscellaneous nodes, created only when they receive parts.
- `overrides.json` to move a part or a whole KSP1 node.
- English and French names for the new nodes.
- Parts without a category (EVA kerbals, flag) and parts needing the placeholder node Unresearcheable are no longer placed; they no longer create a miscellaneous node.
- The 50 stock models of `.mu` versions 0 to 2 (parachutes, batteries, LV-T30/T45, ladders, and others) now load instead of the placeholder: a guarded patch on Redux's `.mu` reader, switched off with `"fix_mu_reader": false` in the new `animations.json`.
