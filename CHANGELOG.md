# Changelog

## 0.4.1 - 2026-10-28

Fixes from the 2026-10-28 in-game test of 0.4.0. Not run in game yet; checklist (French) in `docs/testing/2026-10-28-fixes-041-in-game-checklist.md`.

- **KSP1 engines with a shroud in the VAB** (LV-T45, RE-I2, RK-7, Mainsail…). Redux turns each KSP1 `ModuleJettison` shroud into a KSP2 `Module_Fairing` whose base model transform is hard-coded as `model`; KSP1 imports have none, so `Module_Fairing.OnInitialize` threw on a null collider array. The part's finalization stopped halfway: Redux's placeholder cylinder (the red cylinder) stayed, and, because the second spawn of a part runs `FinalizeLoad` synchronously without a try/catch, the part could not be placed again in the same VAB visit. The mod now gives those KSP1 parts an empty collider array before `OnInitialize` (`fairing: <part> base colliders were null …`), and skips Redux's `OnMaterialLoaded` on KSP1 parts without `Module_Color`, where it would throw. The KSP2 procedural shroud stays unbuilt, as before. Switch: `fix_fairing_init`.
- **Engine stats in the VAB tooltip.** Redux gives KSP1 parts a per-mod category, so the tooltip never took its engine branch. The mod adds the native max thrust and Isp lines, plus the propellant, to KSP1 engines (`tooltip: <n> engine line(s) added …`). Switch: `engine_tooltip_stats`.
- **Third column of the part grid.** While the sub-category bar is shown, the parts panel widens by the bar's width instead of narrowing the grid; the ship toolbar (and the trash can when outside the panel) moves with its edge, and everything is restored on hide and before Redux's collapse/expand (`ui: grid kept …`). Switch: `subcategory_bar_keeps_grid`. The grid cut seen during a search is not explained yet: a one-time `ui: grid during search: …` line logs the grid then.
- **KSP1 textures.** After the import, Redux rebuilds each KSP1 part on demand with an emptied texture cache, so every rebuilt part loaded its own copy of every texture. The mod keeps the cache across rebuilds so each file is loaded once and shared (`share_ksp1_textures`); the gain in video memory is estimated, not measured: the `textures: memory at …` and `textures: cache at …` lines give the real figures. Readable KSP1 textures follow the game's texture quality (`ksp1_textures_follow_quality`). Opt-in: `raise_streaming_budget` raises the texture streaming budget by the KSP1 texture memory. A temporary icon prefab now keeps its textures whenever sharing is armed and the import is complete, even after the sharing stopped on an error (textures handed out before stay shared).
- **Stock `#autoLOC` titles** come from the missing KSP1 dictionaries, not from the mod: the workspace tool `tools/ksp1-layout.mjs --localization` copies `Squad/Localization` and `SquadExpansion/Serenity/Localization` from a KSP1 install into `mods/KSP1` (undone by `--undo`).
- New `animations.json` keys: `fix_fairing_init`, `engine_tooltip_stats`, `subcategory_bar_keeps_grid`, `share_ksp1_textures`, `ksp1_textures_follow_quality` (default true), `raise_streaming_budget` (default false).
- 975 EditMode tests (974 passing, 1 explicit and skipped).

## 0.4.0 - 2026-10-27

Animations phase 2a: material, light and glow curves of KSP1 clips. Not run in game yet; checklist in `docs/testing/2026-10-27-animations-phase2-in-game-checklist.md` (section 2a).

- **Tracks.** A worker thread reads the `.mu` clips at campaign load (`anim: tracks: read <n> model(s) … ; <f> failed`) and the mod plays their material and light curves with the Transform pose, on the same clock. Switches: `material_curves`, `light_curves`.
- **One writer per renderer.** Every value written to a renderer goes through one property block, once per frame, after the game's writers. `mpb check` reads the block back before the render (once per part type, and once more at the first reentry of a part type, `mpb check '<prop>' at reentry`) and compares it, within 1e-4, with what the block held right after our write; if another writer replaced it, the mod flushes again at render time (every renderer after a part-type check, the renderers under an active reentry hull after a reentry check). While Redux's reentry hull is active its renderers are rewritten every frame (a `rebound` line per watched renderer at its first activation is expected).
- **Several clips on one renderer property** blend as Unity's legacy layers do (higher layer first, weights normalised on a layer); a clip at rest or at weight 0 never hides another. On the installed KSP1 files, 1 slot in 143 is shared (two throttle clips on one light).
- **Glow from the throttle.** `ModuleColorChanger` clips follow `FXModuleThrottleEffects` (`color '<id>' follows FXModuleThrottleEffects`): Bluedog_DB engine glow with the throttle. Heat entries play KSP1's heat glow. Switches: `throttle_effects`, `heat_glow` (off: Heat entries are not built at all, `Heat '<clip>' not built: heat_glow off`). Two FX setters naming one `moduleID` are noted in the plan; the later one wins, as in KSP1.
- **Emission.** A glow written to a material whose emission is off gets an own material copy (through `renderer.materials`) with `_EMISSION` on, and a white `_EmissionMap` when the shader has none, once our emission colour is written. Switch: `emission_setup`.
- **Lights.** Light curves drive lights Redux does not drive; Redux's `Module_Light` keeps its own.
- New `animations.json` keys: `material_curves`, `light_curves`, `emission_setup`, `throttle_effects`, `heat_glow`, `skip_modules`, `perf_counter` (default false, logs `anim: perf:` every 60 s). Default `shader_property_map`: `_EmissionColor` before `_EmissiveColor`, plus `_TintColor` and `_Color`. An existing `animations.json` keeps its old map.
- Entry keys of 0.3.2 are unchanged (saves keep their values; a test compares the keys of every real part with a snapshot).
- **Not verified in game:** `normalizedOutput` as `FinalThrustValue / maxThrust`; `[DefaultExecutionOrder]` on a mod type (the `mpb check` and its reentry check detect a writer running after ours, and the render-time flush is the fallback; read both lines); the visibility of the emission; a doubled glow with KSP2's own heat (`heat_glow`).
- If the channel table of a part fails to set up, that part keeps its phase 1 behaviour (no colours, no tracks).
- 907 EditMode tests (906 passing, 1 explicit and skipped).

## 0.3.2 - 2026-10-27

Fixes from the third in-game test.

- **Smooth animations in the VAB.** Landing legs and other Redux-followed parts snapped to their end in the VAB, because KSP1 snaps gear there. Every animation of the mod now plays in the VAB at its real flight speed (gear: times its KSP1 speed factor), and the Redux follower keeps its own clock: Redux's state reaching Extended in one frame no longer shortens the clip. A saved state still shows at once on load. New setting `"editor_animation_speed"` (default 1; 1000 = instant; 0 = KSP1's editor values). Log: `anim: VAB animation speed: …` at startup.
- **Black KSP1 icons, real cause.** Redux renders on URP and maps legacy light culling masks to rendering layers (`Redux.Rendering.LegacyLightCullingMask`). The icon studio (`PartIconRenderer.RenderTexture2D`) moves its subject to layer 31 and its lights cull to that layer (rendering layer `0x1`), but leaves the subject's `renderingLayerMask` as the prefab had it; KSP1 prefabs are scene objects that Redux maps to the VAB's layer (`0x800000`), so no studio light reached them. The mod now gives every KSP1 subject renderer the studio lights' rendering layers (`fix_part_icons`). The 0.2.2 shader swap did nothing: the prefabs are already in `KSP2/Scenery/Standard (Opaque)`. One-shot diagnostic for the first KSP1 and the first KSP2 icon: `icons: diag …` lines (renderer layer and rendering layers before and after, shader, textures, colours, keywords, then the studio lights' masks and the ambient light); the VAB `diag:` lines now show each renderer's layer and rendering layers.
- 598 EditMode tests.

## 0.3.1 - 2026-10-25

Fixes from the review of 0.3.0 (not run in game yet).

- The ship toolbar (`manipulateShipToolbarRT`) no longer drifts by one bar width each time the parts panel is collapsed and expanded while the sub-category bar is shown: a Harmony prefix on Redux's `AssemblyPartsPicker.ExpandCollapsePartsWindow` gives the grid its own width back before Redux measures it (`ui: collapse guard armed on …`; if the method is missing the bar still works, `collapse guard off`). The grid offsets are also restored when the bar's picker is disabled.
- A picker whose bar could not be built (three attempts on successive frames, `ui: no bar on … after 3 attempts`) is no longer filtered by a type chosen in another picker.
- The bar is added only to a picker showing a KSP1 category, its old buttons are hidden before being destroyed, and it no longer allocates a string every frame.

## 0.3.0 - 2026-10-25

- **KSP1 sub-category bar in the VAB.** When one of Redux's KSP1 mod categories (Squad, Bluedog_DB, KerbalReusabilityExpansion…) is selected, a second vertical bar appears right of the category bar with the KSP1 part types of that mod, in KSP1 editor order (Pods, Fuel Tanks, Engines, Control, Structural, Coupling, Payload, Aerodynamics, Ground, Thermal, Electrical, Communication, Science, Utility, Robotics, Cargo, Other), after an "All" button. Clicking a type filters the grid; "Group by" and the search keep working (the search ignores the bar). Types come from each part's raw KSP1 `category`; unknown or custom categories go to "Other".
  - Only types with at least one part the picker would show (tech level, vessel type and hidden parts filters applied) are listed, so no button opens an empty grid. The bar stays hidden when a mod has a single type, while searching, and when the panel is collapsed.
  - The selected type is remembered per mod (by name) for the session.
  - Icons: the stock category sprite for the 14 types the game has; Cargo takes Storage (else Payload); Robotics and Other take the icon of their first part. Labels use the game's translation of the stock category, else the KSP1 name (French for Robotique, Autres, Tout).
  - One Harmony postfix on Redux 26w42c's `AssemblyPartsPicker.ApplyPartsFilter`; the bar is a component added to the picker. If a member it needs is missing, the feature stays off (`ui: sub-category bar off, not found in this Redux: …`). Switch: `"subcategory_bar"` in `animations.json` (default true). Log: `[KSP1PartMod] ui:` lines, including a one-time dump of the picker's RectTransforms.
  - Not run in game yet; checklist in `docs/testing/2026-10-25-subcategories-in-game-checklist.md`.
- 574 EditMode tests.

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
