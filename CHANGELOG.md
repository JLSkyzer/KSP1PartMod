# Changelog

## 0.7.0 - 2026-10-28

KSP1 engine shrouds (`ModuleJettison`). Not run in game yet; checklist (French) in `docs/testing/2026-10-28-engine-shrouds-in-game-checklist.md`.

- **Switch** (`animations.json`): `ksp1_engine_shrouds` (default true). `false` gives the behaviour of 0.6.0, Redux's `Module_Fairing` included.
- **Reproduced from KSP1.** The KSP1 shroud mesh is kept and shown while a part is attached to the bottom node (always for `ignoreNodes`), in the VAB and in flight. It follows the B9 variant (first enabled `jettisonName`), has a VAB "Hide shroud" button, and its colliders are disabled in flight. It goes away on decoupling (`isFairing` or `decoupleEnabled`), or on staging for an engine that is not a fairing (`!isFairing`, not `manualJettison`). The jettison is saved.
- **Not reproduced.** No mass, drag or debris; `ignoreNodes` shrouds go on staging only, as in KSP1; an `isFairing` shroud with `decoupleEnabled = false` is hidden after a decouple, where KSP1 keeps it (a plan choice); `ignoreNodes` shrouds keep their reentry geometry after a jettison.
- **Import patch (session-wide).** A Harmony patch on Redux's import stops `Module_Fairing` being added to KSP1 parts with a `ModuleJettison` shroud, hides the shroud transforms and disables their renderers on the prefab, so Redux's reentry hull never includes a hidden shroud. A shroud renderer with only null materials is also disabled on the prefab, but not recorded, so it is never shown. The patches are armed together or not at all.
- **Legacy saves.** A vessel saved with 0.6.0 loads: at most one shroud, no `Module_Fairing`.
- **Debounce.** In flight the shroud's visibility follows the same 3-step attached state as the jettison latch: a clear shorter than 3 frames neither flickers it nor logs a line.
- **Polish after the final review.** The shown / hidden log lines follow the debounced visibility (a short clear that never hid the shroud logs nothing); an `ignoreNodes` shroud hidden with "Hide shroud" is re-hidden each frame if Redux's variants switch it back on; an engine staged before a 0.6.0 save (`Data_Engine.staged`) no longer shows its shroud again; the limits are added to the checklist.
- **Audit.** New `redux_fairing` CSV column and `audit: shrouds:` line.
- No material or texture is written; the shared KSP1 textures are untouched.
- 1507 EditMode tests (1505 passing, 2 explicit and skipped).

## 0.6.0 - 2026-10-28

Audit mode: load every KSP1 part once, off screen, and report the ones that fail. Off by default. Not run in game yet; checklist (French) in `docs/testing/2026-10-28-audit-in-game-checklist.md`.

- **Switches** (`animations.json`): `audit_on_vab_entry` (default false), `audit_frame_budget_ms` (default 8, 1 to 50), `audit_unload_every` (default 100, 10 to 1000).
- **How it runs.** At the first VAB entry of a game session (campaign feature on, VAB only, not the BAE), after a 5 s settle, one part per step on a detached copy: the part is built, finalized, inspected and destroyed in the same call, paced to the frame budget. Five KSP2 control parts (engine, pod, tank, then the first by name) are spawned first; if one fails, the audit says so. Leaving the VAB cancels at once, and the next entry resumes.
- **Never touched:** the master part list, the assemblies, the selection, the undo history, staging, the module type cache, the real part objects, and the shared KSP1 textures (the audit evicts only the cache entries its own loads added, then calls `Resources.UnloadUnusedAssets` every `audit_unload_every` parts).
- **Report:** `<mod>/audit/audit-<date>.csv` (one row per part, written as it goes) and `.md` (summary: by kind, by mod, by module, slowest parts, and a `pods:` line for KSP1 pods with engines). A part that crashes the game is named by the breadcrumb file and added to `skip.txt` at the next start.
- **Leak checks:** the OAB event delegate counts and the message subscriptions of the real builder are compared around each spawn (lines `OAB event ... grew`, `message ... kept ...`). A failed snapshot is logged and skips the check for that part only.
- **Risks:** the detached spawn is read from the 26w42c decompilation, not tried in game; a module may behave differently when detached; the cost per part and the `UnloadUnusedAssets` hitches are unmeasured.
- **Stop paths hardened:** a failed address lookup for the control parts logs one line and the audit runs without them; an error that stops the audit now stops the session clock and writes the partial `.md`; the `.md` and the `starting` / `done` lines give the path of `skip.txt` and say to delete it to retry the skipped parts.
- 1355 EditMode tests (1353 passing, 2 explicit and skipped).

## 0.5.0 - 2026-10-28

Animations phase 2b: KSP1 plugin modules and B9PartSwitch gates. Not run in game yet; checklist (French) in `docs/testing/2026-10-27-animations-phase2-in-game-checklist.md` (section 2b, after 2a; the single in-game test starts with the 0.4.1 checklist, then 2a, then 2b).

- **Visual only.** The mod reads `Data_Engine`, `Data_Decouple`, `Data_RCS`, `Data_PartSwitch` and `Data_Deployable` and never writes them; it calls no engine, decoupler or staging method. Ignition and separation are not delayed (the KSP1 delay is only shown in the log); science is not run.
- **B9 gates** (`b9_module_gates`). A B9PartSwitch subtype that switches a module off (`moduleActive = false`) also switches off the animation entries of that module, following the variant picked on the part (polled each frame, by reference). The gates apply to all our entries, phase 1 kinds included. Set ids and variant ids follow Redux's (`Ksp1B9PartSwitchVariantBuilder`). A variant change rebinds the renderers once (`rebind Renderers after variant …`) without a `rebound` line per renderer. Lines: `gated by B9 …`, `variants at bind: …`, `B9 '<set>' -> '<id>': <entry> active|inactive`, `transforms of '<id>' active <n>/<m>`.
- **Plugin modules** (`plugin_modules`): engine nozzle extensions (`ModuleDeployableEngine`, `ModuleBdbAnimatedEngine`) following `EngineIgnited`, the multi-mode nozzle (`ModuleMultiStateEngine`) following the engine mode, look-at pistons (`ModuleAdvancedLookAtConstraint`, left to Redux for rotators it already reads), animated decouplers (`ModuleBdbDecouplerAnimation`, first `Module_Decouple` only), RCS glow and emissive (`FXModuleAnimateRCS`, `ModuleBdbRCSEmissive`), mesh hiders (`ModuleBDBDeployableMeshHider`), DMagic science booms (visual only, one owner per clip), the acceleration spring (`ModuleBdbAccelAnimation`, our own spring model) and the Mercury landing bag.
- **Clip ownership.** One owner per clip, unless the B9 gates keep two entries apart. `skip_modules` is applied before that rule.
- New `animations.json` keys: `plugin_modules`, `b9_module_gates` (default true). `skip_modules` also names plugin modules.
- **Not verified in game:** see section 2b of the checklist. The main ones: the B9 set and variant ids against Redux's, `ActiveVariants` changing on a VAB pick, whether Redux's `TransformActivator` really toggles the variant transforms (it looks objects up by direct child), `EngineIgnited` and `isDecoupled` timing, the unit and scale of the RCS force (kN), the host-to-thruster pairing, and the cost on a 1000-part craft.
- **Final-review fixes:**
  - The RCS emissive colour is premultiplied, so an idle thruster stays dark (the KSP2 `_EmissionColor` ignores alpha).
  - An entry inactive at load holds its rest pose until the next frame.
  - A B9 switch with no variant set at bind is looked up again for 10 s.
  - A nozzle previewed out in the VAB no longer starts flight extended, because only the flight state is saved.
- 1292 EditMode tests (1290 passing, 2 explicit and skipped).

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
