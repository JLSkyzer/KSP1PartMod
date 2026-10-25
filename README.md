# KSP1PartMod

A KSP2 Redux mod that puts the KSP1 parts imported by Redux (from `mods/KSP1`) into the tech tree of exploration campaigns, so they can be researched and used there, and brings back the part animations of KSP1 (phase 1).

Redux imports KSP1 part configs but does not give them a place in the KSP2 tech tree. This mod does it at each campaign load, without editing the parts.

Status: 0.2.0. Unit-tested outside the game (408 EditMode tests); not yet tested in game. Checklists: `docs/testing/2026-10-25-in-game-checklist.md` (tech tree) and `docs/testing/2026-10-25-animations-in-game-checklist.md` (animations, in French).

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

## Animations (phase 1)

Redux imports KSP1 parts but loses most of their animations. This mod does two things, at each campaign load, without editing your KSP1 files.

**1. Old models load.** Redux's `.mu` reader fails on model format versions 0 to 2 (the 50 oldest stock models: parachutes, batteries, LV-T30/T45, ladders and others), because it reads one float too few in an object header. The mod patches the reader with a guarded transpiler: it checks that the reader really fails on a generated version 2 file before patching, and takes the patch back if the patched reader then fails a self-check. Switch it off with `"fix_mu_reader": false`. A draft bug report for Redux is in `docs/redux-reports/2026-10-25-mu-reader-old-versions.md` (not sent).

**2. KSP1 animation modules come back** on Redux parts, as one extra module (`Module_Ksp1Animations`) per part with a Part Action Menu button and an action-group action per controllable entry:

- `ModuleAnimateGeneric` (several per part, each independent), `ModuleAnimateHeat`, `FXModuleAnimateThrottle`, `ModuleColorChanger`, `ModuleAnimationGroup`, the gear animations (`ModuleWheelDeployment`, `ModuleLandingLeg`, driven from the Gear action group) and the aero gates of KerbalReusabilityExpansion (`ModuleDeployableAero`), plus the deploy animation of parts Redux leaves alone.
- State survives saving and loading, symmetry counterparts follow.
- Clips of the Transform kind only. Redux's own deployable, solar and parachute modules are kept and restricted to the clips they own; the mod never plays a clip twice.

Written against: stock `Squad` and `SquadExpansion`, `KerbalReusabilityExpansion`, `Bluedog_DB` (the counts per folder are in the log, one `anim: mod <name>` line each). `CommunityResourcePack` is present but has no animation.

### animations.json

Located in `mods/KSP1PartMod/`, read at startup (restart the game after a change). A missing or broken file logs one line and the defaults apply.

```json
{
  "enabled": true,
  "fix_mu_reader": true,
  "own_first_deployable": false,
  "shader_property_map": {
    "_EmissiveColor": ["_EmissiveColor", "_EmissionColor"],
    "_BurnColor": ["_BurnColor"]
  }
}
```

- `enabled`: master switch of the animation modules (tech tree placement is unaffected).
- `fix_mu_reader`: patch Redux's `.mu` reader for format versions 0 to 2. `false` leaves Redux untouched.
- `own_first_deployable`: remove Redux's deployable module from parts that have one and let this mod play that clip too. Use it when a part with a single Redux deployable (a service bay, for example) does not move.
- `shader_property_map`: KSP1 material property to the KSP2 property names to try, in order. When present it replaces the built-in map. The shipped values are starting guesses, not verified in game.

### Phase 2 (not planned yet)

1. Material and light curves of clips (emissive and tint colours, light intensity, engine heat glow).
2. `FXModuleAnimateRCS` (per-nozzle glow).
3. Bluedog_DB modules (`ModuleBdbAnimatedEngine`, `ModuleBdbDecouplerAnimation`, `ModuleBdbAccelAnimation`, `ModuleBdbMercuryLandingBag`, `ModuleBDBDeployableMeshHider`), `ModuleDeployableEngine`, `ModuleMultiStateEngine`, DMagic `DMModuleScienceAnimateGeneric`.
4. B9PartSwitch `SUBTYPE` overrides that pick which clip a module uses.
5. Links by module index: cargo bay shielding, docking port gating, science experiments, animation groups with converters.
6. Deploy limit (cargo bays), `restrictedNode`, one-shot inflatables, crew capacity when open.
7. Radiator and antenna tracking and breakage, wheel physics, robotics, Waterfall effects.

## Known limits

- Robotics parts of KSP1 are spread over several KSP1 nodes, so only those of `actuators` land in the new robotics node.
- Animations: phase 1 only, see below. Material and light curves of clips, and the modules listed under phase 2, are not played yet.
- The table is a judgement of which KSP2 node plays the same role; use `overrides.json` where you disagree.

## KSP1 content

No KSP1 part, model or config is shipped or copied. The mod only reads what Redux already imported from your own `mods/KSP1`. The behaviour of the KSP1 animation modules is re-implemented from how they act in game and from their config fields; no code of KSP1 or of any KSP1 mod is included in this repository.

## Build

Unity 6000.6.0f1 with the Redux SDK template 26w42c. Run `Deploy to Zip File` (ThunderKit) to produce `Deploy/KSP1PartMod.zip`. EditMode tests: Unity Test Runner, or `Unity.exe -batchmode -runTests -testPlatform EditMode`.

## Licence

MIT, see `LICENSE`.
