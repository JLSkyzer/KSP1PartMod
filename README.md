# KSP1PartMod

A KSP2 Redux mod that puts the KSP1 parts imported by Redux (from `mods/KSP1`) into the tech tree of exploration campaigns, so they can be researched and used there.

Redux imports KSP1 part configs but does not give them a place in the KSP2 tech tree. This mod does it at each campaign load, without editing the parts.

Status: 0.1.0. Unit-tested outside the game; not yet tested in game (see `docs/testing/2026-10-25-in-game-checklist.md`).

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

## Known limits

- Robotics parts of KSP1 are spread over several KSP1 nodes, so only those of `actuators` land in the new robotics node.
- Part animations and modules Redux does not support are out of scope.
- The table is a judgement of which KSP2 node plays the same role; use `overrides.json` where you disagree.

## KSP1 content

No KSP1 part, model or config is shipped or copied. The mod only reads what Redux already imported from your own `mods/KSP1`.

## Build

Unity 6000.6.0f1 with the Redux SDK template 26w42c. Run `Deploy to Zip File` (ThunderKit) to produce `Deploy/KSP1PartMod.zip`. EditMode tests: Unity Test Runner, or `Unity.exe -batchmode -runTests -testPlatform EditMode`.

## Licence

MIT, see `LICENSE`.
