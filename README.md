# KSP1PartMod

A KSP2 Redux mod that puts the KSP1 parts imported by Redux (from `mods/KSP1`) into the tech tree of exploration campaigns, so they can be researched and used there, and brings back the part animations of KSP1 (phase 1).

Redux imports KSP1 part configs but does not give them a place in the KSP2 tech tree. This mod does it at each campaign load, without editing the parts.

Status: 0.2.1. Unit-tested outside the game (430 EditMode tests); a first in-game test led to 0.2.1, which is not yet tested in game. Checklists: `docs/testing/2026-10-25-in-game-checklist.md` (tech tree) and `docs/testing/2026-10-25-animations-in-game-checklist.md` (animations, in French).

## Requirements

- KSP2 Redux 0.2.9 (26w42c) or later, which includes Space Warp 2.
- KSP1 parts in the game's `mods/KSP1` folder, which Redux imports. This mod ships none of them.

## Install

Unzip `KSP1PartMod.zip` into the game's `mods` folder, so that `mods/KSP1PartMod/KSP1PartMod.dll` exists. Then turn the mod on **per campaign**: in Edit Pack when creating the campaign, or through Default Features in the Mods menu. A campaign without the mod enabled is left untouched.

The log is `%LOCALAPPDATA%Low/Intercept Games/Kerbal Space Program 2/Player.log`; the mod's lines start with `[KSP1PartMod]`.

## How parts are placed

For each imported KSP1 part, the first rule that gives an existing node wins:

1. **Override** from `overrides.json` (by part, then by KSP1 node).
2. **Table**: `tech_mapping.json` maps each KSP1 tech node (stock 1.12 and Community Tech Tree) to a KSP2 node.
3. **Category fallback**: by part category and size, for parts whose KSP1 node is not in the table.
4. **Catch-all**: a miscellaneous node, so no part is lost.

Parts that KSP1 hides are not placed. A part already unlocked by a KSP2 node stays where it is. The mod creates its own nodes (`tNode_ksp1_*`: three robotics nodes and a miscellaneous one) only when they receive at least one part.

The log gives one summary line per mod folder, and one line per part that fell back.

## overrides.json

Located in `mods/KSP1PartMod/`. Both maps are optional and win over `tech_mapping.json`:

```json
{
  "parts": {
    "mk1pod_v2": "tNode_1v_payload"
  },
  "ksp1_nodes": {
    "advExploration": "tNode_2v_utility"
  }
}
```

- `parts`: KSP1 part name (case-sensitive, as in its `.cfg`) to a KSP2 node id.
- `ksp1_nodes`: KSP1 tech node id (any case) to the KSP2 node id that takes all its parts.

An id missing from the game's tree is ignored, the part falls through to the next rule, and the log gets a line `[KSP1PartMod] note: <part> (<mod>): …` naming the missing id. Edit the file and restart the game.

## Animations (phases 1, 2a and 2b)

Redux imports KSP1 parts but loses most of their animations. This mod does two things, at each campaign load, without editing your KSP1 files.

**1. Old models load.** Redux's `.mu` reader fails on model format versions 0 to 2 (the 50 oldest stock models: parachutes, batteries, LV-T30/T45, ladders and others), because it reads one float too few in an object header. The mod patches the reader with a guarded transpiler: it checks that the reader really fails on a generated version 2 file before patching, and takes the patch back if the patched reader then fails a self-check. Switch it off with `"fix_mu_reader": false`. A draft bug report for Redux is in `docs/redux-reports/2026-10-25-mu-reader-old-versions.md` (not sent).

**2. KSP1 animation modules come back** on Redux parts, as one extra module (`Module_Ksp1Animations`) per part with a Part Action Menu button and an action-group action per controllable entry:

- `ModuleAnimateGeneric` (several per part, each independent), `ModuleAnimateHeat`, `FXModuleAnimateThrottle`, `ModuleColorChanger`, `ModuleAnimationGroup`, the gear animations (`ModuleWheelDeployment`, `ModuleLandingLeg`, driven from the Gear action group) and the aero gates of KerbalReusabilityExpansion (`ModuleDeployableAero`), plus the deploy animation of parts Redux leaves alone.
- State survives saving and loading, symmetry counterparts follow.
- Clips of the Transform kind only. Redux's own deployable, solar and parachute modules are kept and restricted to the clips they own; the mod never plays a clip twice.
- **Redux followers.** When a part has a single deployable source (`ModuleAnimateGeneric`, `ModuleDeployableSolarPanel`, `ModuleWheelDeployment`, `ModuleLandingLeg`), and for every `ModuleParachute`, Redux 26w42c plays the clip through a Mecanim copy that does not move. Redux keeps the state, its PAM toggle and its action groups; the mod only draws the original clip from that state, at KSP1 speed. The mod keeps its own clock: Redux's copy reaches Extended in one frame, the clip still takes its full length. Parts with several sources use Redux's legacy runtime, which moves, and are left alone.

**3. Visual fixes on every KSP1 part.** Renderers whose materials are all missing (drawn with Unity's error colour) are hidden; Redux's reentry hull is left to the reentry effect. KSP1 part icons are lit: Redux's icon studio lights only renderers that share their rendering layers, and KSP1 prefabs carry the VAB's layer; the mod gives the icon subject the studio's layers.

Written against: stock `Squad` and `SquadExpansion`, `KerbalReusabilityExpansion`, `Bluedog_DB` (the counts per folder are in the log, one `anim: mod <name>` line each). `CommunityResourcePack` is present but has no animation.

### animations.json

Located in `mods/KSP1PartMod/`, read at startup (restart the game after a change). A missing or broken file logs one line and the defaults apply.

```json
{
  "enabled": true,
  "fix_mu_reader": true,
  "own_first_deployable": false,
  "follow_redux_deployables": true,
  "hide_material_less_renderers": true,
  "fix_part_icons": true,
  "subcategory_bar": true,
  "editor_animation_speed": 1,
  "diagnostics": true,
  "material_curves": true,
  "light_curves": true,
  "emission_setup": true,
  "throttle_effects": true,
  "heat_glow": true,
  "skip_modules": [],
  "perf_counter": false,
  "fix_fairing_init": true,
  "engine_tooltip_stats": true,
  "subcategory_bar_keeps_grid": true,
  "share_ksp1_textures": true,
  "ksp1_textures_follow_quality": true,
  "raise_streaming_budget": false,
  "shader_property_map": {
    "_EmissiveColor": ["_EmissionColor", "_EmissiveColor"],
    "_TintColor": ["_TintColor"],
    "_Color": ["_Color"],
    "_BurnColor": ["_BurnColor"]
  }
}
```

- `enabled`: master switch of the animation modules (tech tree placement is unaffected).
- `fix_mu_reader`: patch Redux's `.mu` reader for format versions 0 to 2. `false` leaves Redux untouched.
- `own_first_deployable`: remove Redux's deployable module from parts that have one and let this mod play that clip too. Since 0.2.2 such parts move through `follow_redux_deployables`; keep this for a part that still does not.
- `follow_redux_deployables`: draw the clip of Redux's single deployables and parachutes from Redux's state (see above). `false` leaves those clips still. Ignored for a part where `own_first_deployable` already gives the clip to this mod.
- `hide_material_less_renderers`: hide the renderers of KSP1 parts whose materials are all missing (`[KSP1PartMod] render:` lines).
- `fix_part_icons`: light KSP1 part icons by giving the icon subject the rendering layers of the studio lights (`[KSP1PartMod] icons:` lines). `false` keeps Redux's black icons.
- `editor_animation_speed`: speed of the KSP1 animations in the VAB, as a multiple of the flight speed. `1` (default) plays every clip at its real speed, so legs, doors and panels move smoothly; `1000` makes them instant; `0` restores KSP1's own editor values (KSP1 played doors and panels 10 times faster in the editor, `editorAnimationSpeedMult = 10`, and snapped gear and legs).
- `subcategory_bar`: the KSP1 sub-category bar of the VAB (see VAB sub-categories). `false` removes it.
- `diagnostics`: log once, in the VAB, the materials of a few KSP1 parts (`[KSP1PartMod] diag:` lines) for bug reports. `false` disables it.
- `material_curves`: play the material colour and value curves of KSP1 clips (engine glow, windows, tints). `false` keeps them still, as in 0.3.2.
- `light_curves`: play the light curves of KSP1 clips (intensity, colour, range, spot angle) on lights Redux does not drive. Lights owned by Redux's `Module_Light` are left to it (log: `light '<path>' left to Module_Light`).
- `emission_setup`: when a glow is written to a KSP2 material whose emission is off, give that renderer its own material copy with `_EMISSION` on, and a white `_EmissionMap` when it has none. `false` leaves the materials untouched.
- `throttle_effects`: `FXModuleThrottleEffects` drives the colour changers of its engine with the throttle (Bluedog_DB engine glow).
- `heat_glow`: play KSP1's heat glow (Heat entries and their curves). Set `false` if KSP2 already makes hot parts glow and you see it doubled.
- `skip_modules`: list of KSP1 module names the mod never takes, for example `["FXModuleThrottleEffects"]`, to switch one module off if it misbehaves.
- `perf_counter`: default `false`. `true` logs every 60 s the time spent by the animations (`[KSP1PartMod] anim: perf:` lines), for bug reports.
- `fix_fairing_init` (0.4.1): Redux turns the jettison shroud of KSP1 engines into a KSP2 `Module_Fairing` that looks for a transform named `model`, which KSP1 imports lack; it then breaks the part in the VAB (red cylinder left, part that cannot be placed again in the same visit). The mod gives those parts an empty collider array and skips Redux's `OnMaterialLoaded` on KSP1 parts without `Module_Color` (`[KSP1PartMod] fairing:` lines). `false` leaves Redux as it is.
- `engine_tooltip_stats` (0.4.1): add the max thrust, Isp and propellant lines of KSP1 engines to the VAB tooltip, as the game does for KSP2 engines (`[KSP1PartMod] tooltip:` line).
- `subcategory_bar_keeps_grid` (0.4.1): while the sub-category bar is shown, widen the parts panel by the bar's width so the third column stays whole; the ship toolbar follows. `false` narrows the grid instead, as in 0.4.0.
- `share_ksp1_textures` (0.4.1): load each KSP1 texture file once and share it between the KSP1 parts Redux rebuilds after the import, instead of one copy per rebuilt part. Less video memory: an estimate from reading Redux's code, not measured yet. `false` keeps one copy per part.
- `ksp1_textures_follow_quality` (0.4.1): the KSP1 textures Redux keeps readable follow the game's Texture Quality setting. Most KSP1 textures are not readable and stay at full size either way.
- `raise_streaming_budget` (0.4.1): default `false`. `true` raises the game's texture streaming budget by the KSP1 texture memory, capped at the video card's memory less a quarter (at least 1 GB); it can cause stutter on cards with little memory. The `[KSP1PartMod] textures:` lines give the texture memory at the end of the import and at each VAB or flight entry.
- `ksp1_engine_shrouds` (0.7.0, default `true`): keep the KSP1 mesh of engine shrouds (`ModuleJettison`) and show it the way KSP1 does instead of Redux's `Module_Fairing`: visible while a part is attached below the engine (VAB and flight), follows the B9 variant, "Hide shroud" in the VAB part menu, gone on decoupling (or on staging for an engine that is not a fairing). No mass, drag or debris. `false` gives the behaviour of 0.6.0. Not run in game yet: `docs/testing/2026-10-28-engine-shrouds-in-game-checklist.md`; log lines `[KSP1PartMod] shroud:` and `anim: ... shroud`.
- ModuleManager patches (0.8.0; Redux applies them when it imports KSP1 parts, nothing is written to the cfg files). Lines `[KSP1PartMod] mm:` count what each switch changes. Checklist: `docs/testing/2026-10-28-native-modules-1-in-game-checklist.md`.
  - `mm_plugin_safety_net` (default `true`): KSP1 plugins never run under Redux, so the names in `mm_plugin_folders` (and, with `mm_plugin_folders_auto`, every KSP1 folder that holds a `.dll` and no part) count as absent: `:NEEDS[name]` is false, `:NEEDS[!name]` true, and the patches written for them (`:FOR`, `:BEFORE`, `:AFTER`, and those inside the folder) are left out. Parts are never removed. `mm_needs_keep` lists names never treated as absent (B9PartSwitch, Waterfall and the libraries Redux or the mod reproduces). `false`: Redux's rule, a folder of that name counts as present.
  - `mm_before_after_need_mod` (default `true`): as ModuleManager does, leave out a patch whose `:BEFORE[x]` or `:AFTER[x]` names an absent mod (a `:FOR[x]` name counts as there). Some patches Redux applied in 0.7.0 are therefore no longer applied, as with ModuleManager: 122 on the author's set, counted by running Redux's patcher in the editor (the raw cfg files hold 277 such patches, but most already failed a NEEDS). The in-game line `mm: BEFORE/AFTER of an absent mod:` gives the count for your files. `false` restores them.
  - `mm_arithmetic` (default `true`): apply `*=`, `+=`, `-=`, `/=` on a single number (Redux skips them). `^=`, `!=` and `#$variables$` stay unsupported.
  - `mm_index_selectors` (default `true`): apply `,n`, `,-n` and `,*` after a value or node name (`@volume,1` edits the second volume only; Redux edits all of them).
  - `mm_edit_first_match` (default `false`): `true` makes an edit without an index change the first match only, as ModuleManager does (Redux changes every match). Left off until the log count of `unindexed edits that matched more than one` is read in game.
- `ksp1_crossfeed_toggle` (0.8.0, default `true`): KSP1's `ModuleToggleCrossfeed` (decouplers, adapters) becomes KSP2's crossfeed toggle: the switch in the VAB and in flight and the three action-group actions, starting in the cfg's `crossfeedStatus`. `false`: Redux drops the module, as in 0.7.0. Line `[KSP1PartMod] native: crossfeed:`.
- `ksp1_heatshields` (0.8.0, default `true`): KSP1's `ModuleAblator` becomes KSP2's `Module_Heatshield`. It burns the part's Ablator above `ablationTempThresh` and shields the part when its bottom faces the airflow, at KSP2's own shield rate (the same for every size). `lossExp`, `lossConst` and `reentryConductivity` are not reproduced; `Module_Heatshield` takes its Ablator from the whole vessel. A part without the resource gets no shield. `false`: Redux drops the module. Lines `[KSP1PartMod] native: heat shield`.
- `ablator_rate_scale` (0.8.0, default `1`, 0.01 to 100): multiplies the burn rate of those shields. `ablator_char` (default `true`): the shield's `shieldChar` colour follows the part's Ablator, white when full, black when empty. It is not used where another colour changer of the part writes the same shader property (the KV pods keep their lights); on the stock and KRE shields (`_BurnColor`) it probably shows nothing, since the KSP2 shader of KSP1 parts is not documented with that property.
- B9 overrides (0.9.0, all default `true`, see "B9PartSwitch overrides"): `b9_mass`, `b9_resources`, `b9_module_data`, `b9_module_active` (needs `b9_module_data`), `b9_part_scalars`, `b9_new_part_switches`.
- `shader_property_map`: KSP1 material property to the KSP2 property names to try, in order; the first one the material has is used. When present it replaces the built-in map. Since 0.4.0 the default tries `_EmissionColor` before `_EmissiveColor` for `_EmissiveColor`, and adds `_TintColor` and `_Color`. The values are starting guesses, not verified in game. A `animations.json` from 0.3.2 keeps its old map: copy the new one to get the new default.

### Phase 2a: material, light and glow curves (0.4.0)

Not run in game yet: checklist in `docs/testing/2026-10-27-animations-phase2-in-game-checklist.md`.

- **Tracks.** The mod reads the `.mu` clips itself (on a worker thread at campaign load, `anim: tracks: read …`) and plays their material and light curves next to the Transform animation, on the same clock as the visible pose. Colour and value curves go to the KSP2 property chosen by `shader_property_map`; light curves go to the lights Redux does not drive.
- **One writer per renderer.** All values of a renderer are written once per frame, after the game's own writers, through a property block. A check (`mpb check`) reads the block back before the render; if another writer replaced it, the mod rewrites at render time. Redux's reentry hull rewrites its material every frame, so the mod rewrites those renderers every frame while the hull is active (`rebound` log lines are expected then).
- **Colour changers.** `ModuleColorChanger` clips follow `FXModuleThrottleEffects` when the engine has one (`follows FXModuleThrottleEffects`), so Bluedog_DB engines glow with the throttle; heat entries drive the heat glow.
- **Emission.** A glow written to a material whose emission is off gets an own material copy with `_EMISSION` on, plus a white `_EmissionMap` where the shader has none (`emission_setup`).
- **Not verified in game:** `normalizedOutput` read as thrust over max thrust, `[DefaultExecutionOrder]` on a mod type, the visibility of the emission, a doubled glow with KSP2's own heat (`heat_glow`).

### Phase 2b: plugin modules and B9 gates (0.5.0)

Not run in game yet: checklist in `docs/testing/2026-10-27-animations-phase2-in-game-checklist.md` (section 2b).

- **Visual only.** The mod reads `Data_Engine`, `Data_Decouple`, `Data_RCS`, `Data_PartSwitch` and `Data_Deployable` and never writes them, and calls no engine, decoupler or staging method. Ignition and separation are not delayed; science is not run.
- **Plugin modules** (`plugin_modules`): engine nozzle extensions (`ModuleDeployableEngine`, `ModuleBdbAnimatedEngine`) that follow the ignition, the multi-mode nozzle (`ModuleMultiStateEngine`), look-at pistons (`ModuleAdvancedLookAtConstraint`), animated decouplers (`ModuleBdbDecouplerAnimation`), RCS glow and emissive (`FXModuleAnimateRCS`, `ModuleBdbRCSEmissive`), mesh hiders (`ModuleBDBDeployableMeshHider`), DMagic science booms (`DMModuleScienceAnimateGeneric`, animation only), the acceleration spring (`ModuleBdbAccelAnimation`) and the Mercury landing bag.
- **B9 gates** (`b9_module_gates`): a B9PartSwitch subtype that switches a module off also switches off that module's animation entries, following the variant picked in the VAB. The gates apply to all our entries, phase 1 kinds included.
- **Settings.** `plugin_modules` and `b9_module_gates` (default true); `skip_modules` also names plugin modules; `editor_animation_speed` governs the new previews.
- **Not done yet:** links by module index (cargo bay shielding, docking port gating, science experiments, animation groups with converters), deploy limit and `restrictedNode`, one-shot inflatables, crew capacity when open, radiator and antenna tracking, wheel physics, robotics, Waterfall effects.

## B9PartSwitch overrides (0.9.0)

Redux turns a B9PartSwitch subtype into a KSP2 part variant, but only for its visuals (textures and meshes). Since 0.9.0 the mod also appends to each variant the rest of what a subtype says, through Redux's own variant transformers (V-SwiFT), so the choice made in the VAB or in the Part Action Menu changes the part for real, in the VAB and in flight. Nothing is written to the cfg files; Redux's own transformers and sets are never replaced, only added to. Built, not verified in game. Checklist (French): `docs/testing/2026-10-28-b9-overrides-in-game-checklist.md`. Lines `[KSP1PartMod] b9:`; the first one, `b9: armed: …`, says which of the switches below are on.

- **Mass** (`b9_mass`): `addedMass` plus the tank's `tankMass` times its volume, applied as a part-switch mass modifier. The volume is `baseVolume × volumeMultiplier + volumeAdded` plus the default subtype of each child (`parentID`); the volume formula and the dry-mass product are inferred from B9's public wiki, not read from its code.
- **Tanks** (`b9_resources`): the subtype's tank type (from the `B9_TANK_TYPE` nodes of the KSP1 files, the first definition wins) and its own RESOURCE nodes replace the base resources and capacities (`unitsPerVolume × volume`, `percentFilled`, an explicit `amount` or `maxAmount` wins). Going back to the first subtype gives the same quantities. A part with a second tank switch keeps Redux's resources: its mass and part values still apply.
- **Module data** (`b9_module_data`): a subtype's `MODULE` `DATA` block (engine thrust and Isp, gimbal ranges, RCS, solar panel, command) is read as a KSP1 module, imported by Redux's own importer, and the fields that differ from the base are written to the matching KSP2 module. A multi-mode engine (XLR129, UA120) gets one transformer that edits every mode. A behaviour that appears twice on the part (a main engine and its verniers) is skipped: V-SwiFT edits the first instance only (33 edits on 7 installed BDB parts).
- **Modules turned off** (`b9_module_active`, needs `b9_module_data`): `moduleActive = false` uses the native off form of the module: a gimbal with ranges at 0, an RCS with no thrust, a decoupler no longer stageable (only when it is the part's only stageable module, since staging is per part). Engines are not turned off (a zero thrust would give a NaN Δv and burn time), and a module without an off form stays on; each case has a log line.
- **Part values** (`b9_part_scalars`): `maxTemp`, `skinMaxTemp`, `crashTolerance` and `CoMOffset`.
- **New selectors** (`b9_new_part_switches`): a part that had no part switch (a mass or tank switch with no visual) gets one, with readable names (28 installed parts). With `false`, such parts get none, as in 0.8.0.
- **Gates.** The 2b animation gates (`b9_module_gates`) follow these new selectors too.
- **Audit.** `b9_overrides` CSV column, `audit: B9 overrides:` line.
- **Switch off.** Each of the six `b9_*` keys, set to `false` in `animations.json`, restores the 0.8.0 behaviour for its point. They work whatever `enabled` is. An existing `animations.json` without them gets the defaults (all `true`).

## Audit mode (0.6.0)

Off by default. With `"audit_on_vab_entry": true` in `animations.json`, the first time you enter the VAB in a game session (mod enabled in the campaign), the mod loads every KSP1 part once, off screen and one at a time, and writes a report of the parts that fail, throw, stay unfinished or log errors. It changes nothing in your craft, the part list or the undo history, and the shared KSP1 textures are not destroyed. Do not build while it runs; leaving the VAB cancels it, and the next entry resumes. Not run in game yet: checklist in `docs/testing/2026-10-28-audit-in-game-checklist.md`.

| Key | Default | Effect |
|---|---|---|
| `audit_on_vab_entry` | false | Run the audit at the first VAB entry of the session. |
| `audit_frame_budget_ms` | 8 | Time the audit may use per frame (1 to 50). |
| `audit_unload_every` | 100 | Parts between two unloads of the audit's textures (10 to 1000). |

Output, in `<mod>/audit/`: `audit-<date>.csv` (one row per part), `audit-<date>.md` (summary, including a `pods:` line on KSP1 pods with engines), `skip.txt` (parts that crashed the game, skipped from then on) and a breadcrumb file. Log lines start with `[KSP1PartMod] audit:`.

## VAB sub-categories

Redux puts the KSP1 parts in one VAB category per KSP1 mod. When such a category is selected, the mod adds a second vertical bar right of the category bar, with the KSP1 part types of that mod in KSP1 editor order: "All" first, then Pods, Fuel Tanks, Engines, Control, Structural, Coupling, Payload, Aerodynamics, Ground, Thermal, Electrical, Communication, Science, Utility, Robotics, Cargo and Other (custom or unknown KSP1 categories). Clicking a type filters the grid.

- Only types with at least one part you can use in this campaign are listed; the bar is hidden when the mod has a single type, during a search and when the panel is collapsed.
- The chosen type is remembered per mod until the game is closed.
- "Group by" applies inside the chosen type; the search always looks at every part.
- Switch it off with `"subcategory_bar": false` in `animations.json`. Log lines start with `[KSP1PartMod] ui:`.

## Known limits

- Robotics parts of KSP1 are spread over several KSP1 nodes, so only those of `actuators` land in the new robotics node.
- Native modules, lot 1 (0.8.0): built, not verified in game. `^=`, `!=`, `#$variables$` and vector-component edits (`key[i] *= x`) are not applied; the heat shield ignores KSP1's temperature curve; the BEFORE/AFTER rule now leaves out patches that name an absent mod, which Redux applied in 0.7.0.
- B9 overrides (0.9.0): built, not verified in game.
  - **Not applied:** the part cost (computed and logged only; the VAB cost is unchanged), `node` (stack nodes per subtype), `TRANSFORM` offsets, `upgradeRequired`, the child's volume choice (a parent's volume uses the child's default subtype), a second tank switch's resources, engine off forms, and a decoupler off form on a part that stages through another module.
  - **Parts gaining a selector** (28 installed) have no `Module_PartSwitch` in a vessel saved in 0.8.0: such a vessel in flight keeps its 0.8.0 values until it is saved and reloaded; after that the tank and part values apply, but the mass only after the craft is rebuilt in the VAB.
  - **Gates:** a gate may name a set that was never appended (a switch whose only override has no native form); this is benign, the part keeps its default subtype.
  - The volume formula and the dry-mass product are inferred from B9's public wiki.
- Engine shrouds (0.7.0): built, not verified in game. `ignoreNodes` shrouds keep their reentry geometry after a jettison.
- Animations: phases 1, 2a and 2b (all built; none verified in game yet beyond what the checklists record). The items under "Not done yet" in phase 2b are not played.
- The table is a judgement of which KSP2 node plays the same role; use `overrides.json` where you disagree.
- Stock KSP1 parts titled `#autoLOC_…`: Redux reads every `Localization` node under `mods/KSP1`, but a copy of the stock part folders alone carries no dictionary. Copy `GameData/Squad/Localization` and `GameData/SquadExpansion/Serenity/Localization` from your KSP1 install to the same paths under `mods/KSP1` (Making History's keys are in the Squad dictionary). Redux keeps one text per key, English when present, else the first language it reads: a KSP1 installed in French gives French titles in every game language.

## KSP1 content

No KSP1 part, model or config is shipped or copied. The mod only reads what Redux already imported from your own `mods/KSP1`. The behaviour of the KSP1 animation modules is re-implemented from how they act in game and from their config fields; no code of KSP1 or of any KSP1 mod is included in this repository.

## Build

Unity 6000.6.0f1 with the Redux SDK template 26w42c. Run `Deploy to Zip File` (ThunderKit) to produce `Deploy/KSP1PartMod.zip`. EditMode tests: Unity Test Runner, or `Unity.exe -batchmode -runTests -testPlatform EditMode`.

## Licence

MIT, see `LICENSE`.
