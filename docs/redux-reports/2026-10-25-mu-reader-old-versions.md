**Draft — not sent**

# KSP1 import: every `.mu` model of format version 0, 1 or 2 fails to load

## Build

- Redux 0.2.9 (26w42c), `KSP2_x64_Data/Managed/Ksp2Redux.dll`: 15 570 432 bytes, dated 2026-10-18,
  assembly version 0.0.9786.34508.
- KSP1 stock parts (Squad and SquadExpansion folders) copied under `mods/KSP1/Squad/...` and
  `mods/KSP1/SquadExpansion/...`.

## Symptom

Each affected part logs, at campaign load:

```
Failed to load KSP1 model ... InvalidDataException: Unsupported .mu entry type 1065353216
```

The part then gets placeholder geometry and none of its animations. 1065353216 is `0x3F800000`,
the bytes of the float `1.0` read as an integer: the reader is out of step with the file.

## Scope

Of the 494 stock `.mu` files, **all 50 files of version 0, 1 or 2 fail** and **all 444 files of
version 3, 4 or 5 load**. The version is the second int of the file header.

Affected stock models (version in brackets):

- Aero: wingletAV-R8 [2], wingletAV-T1 [2], wingletDeltaDeluxe [1]
- Command: advancedSasModuleLarge [0], externalCommandSeat [2], inlineAdvancedStabilizer [1],
  inlineReactionWheel [1], probeRoverBody [2], probeStackLarge [2], probeStackSmall [2]
- Electrical: RTG [1], radialFlatSolarPanel [1], z-100Battery [1], z-1kBattery [1], z-400Battery [2]
- Engine: ionEngine [1], liquidEngineLV-N [1], liquidEngineLV-T30 [1], liquidEngineLV-T45 [1]
- FuelTank: RCSTankRadial [1], xenonTank [1]
- Misc: PotatoComet/Cube [2], PotatoRoid/Cube [2]
- Science: sensorAccelerometer [1], sensorBarometer [1], sensorGravimeter [1], sensorThermometer [1]
- Structural: stationHub [1], structuralIBeam200 [2], structuralIBeam200Pocket [2],
  structuralIBeam650 [2], structuralMicronode [2], structuralPanel1x1 [2], structuralPanel2x2 [2],
  strutOcto [1], trussGirderL [1], trussGirderXL [1]
- Utility: commsDish16 [1], decouplerRadialHDM [0], decouplerRadialTT-38K [1],
  decouplerRadialTT-70 [1], ladderRadial [0], ladderTelescopic [0], launchClamp1 [0],
  parachuteMk1 [1], parachuteMk12-R [1], parachuteMk16-XL [2], parachuteMk2-R [1],
  parachuteMk25 [2], radialAttachmentPoint [2]

## Cause

The difference is in the **object header**, not in any component entry.

- In versions 0 to 2, each object (the root and every child) has its name followed by **11 floats**:
  position (3), rotation (4), scale (3), and one more float.
- From version 3 there are 10 floats.
- `Ksp1MuReader.ReadObject` always reads 10 floats. In an old file, it then reads the 11th float as
  the next entry tag and throws.

The extra float is not always `1.0`. In the stock files it ranges from -1.03 to 2.5, and it
equals the x scale in all 219 object headers of the 50 files, as if the old exporter wrote the
scale as four values. KSP1's own
reader gets past it by accident: it reads the float as an entry tag, matches no known tag, and
moves on.

**How we checked:**

- We took each of the 50 failing files and removed the 4 bytes after the 10th header float of
  every object. `Ksp1MuReader.Read` then loads all 50 files.
- It also gives the same object tree and the same animation clips (names and curve counts) as an
  independent reader that keeps the 11th float.
- The version checks inside the entries (renderer from version 1, light spot angle from version 2,
  material format from version 4) are correct.

## Reproduction

1. Take any stock model listed above, for example `Squad/Parts/Utility/parachuteMk1/model.mu`
   (version 1).
2. Call `Redux.Ksp1Import.Model.Ksp1MuReader.Read(path)`. It throws
   `InvalidDataException: Unsupported .mu entry type 1065353216`.
3. In game, the part shows the placeholder model and does not animate.

## Suggested fix

In `ReadObject`, after the scale is read, read one more float when the file version is below 3.
The version is already passed to `ReadObject`. Version 3 to 5 files are not affected.

## Our workaround

The KSP1PartMod mod (`JLSkyzer/KSP1PartMod`) does exactly this with a Harmony transpiler on
`Ksp1MuReader.ReadObject` (Harmony id `KSP1PartMod.MuReaderFix`).

- **The patch:** it adds one call after the scale is stored. The call reads one float when the
  version is below 3 and does nothing otherwise.
- **Effect on newer files:** version 3 to 5 files give the same trees as without the patch. This
  was checked on all 444 stock files.
- **Safety:** if the method, the `_reader` field or the scale store is not found as expected, the
  patch is skipped. The mod then logs `[KSP1PartMod] mu: fix skipped: …` and patches nothing.
- **Turning it off:** set `"fix_mu_reader": false` in `mods/KSP1PartMod/animations.json`.

The patch will be removed once Redux reads these files itself.
