**Draft — not sent**

# KSP1 import: single deployables and parachutes do not move, KSP1 icons are black

Redux 0.2.9 (26w42c), `Ksp2Redux.dll` of 2026-10-18. Read in a decompile of that assembly; the in-game
observations come from a 2026-10-25 session with stock KSP1 parts, KerbalReusabilityExpansion (KRE) and
Bluedog_DB copied under `mods/KSP1`.

## 1. The Mecanim copy of a KSP1 clip moves nothing

**Observed.** A KRE landing leg (`KRE-FalconLeg-L`, one `ModuleWheelDeployment`, clip `XDeploy`): the Part
Action Menu shows "Deploy part", the state goes to "Extended", the mesh does not move. Some other KSP1 parts
do deploy (the player saw wings); which ones was not recorded. The split below predicts which.

**Two paths in `Ksp1DeployableModuleBuilder.ConfigurePrefab`:**

- one source: `Ksp1GenericDeployableAnimatorBuilder.TryConfigure` instantiates the legacy clip, sets
  `legacy = false`, overrides `KSP1_GenericDeployable_Deploy` of `KSP1_GenericDeployable.controller` with it, adds an
  `Animator`, disables the `Animation` and adds `Ksp1DeployableAnimationPoseKeeper`; the part gets the stock
  `Module_Deployable`. **Does not move.**
- two or more sources (or a failed bind): `Ksp1DeployableAnimationRuntime` samples the legacy `Animation`.
  **Moves.**

`Ksp1ParachuteAnimatorBuilder.CloneAsMecanimClip` does the same for every `ModuleParachute`
(`KSP1_GenericParachute.controller`).

**Probable cause.** The clip is built at runtime: `Ksp1ModelImporter.ToUnityAnimationClip` creates
`new AnimationClip { legacy = true }` and fills it with `SetCurve`. Unity's documentation of
`AnimationClip.SetCurve` says it works at runtime for legacy clips only (for other clips it is an editor
function). The `Instantiate` + `legacy = false` copy therefore has no curve Mecanim can bind, and the Animator
writes nothing. The state machine still runs: the `OPENED` state is reached and
`Module_Deployable.OnAnimStateEnter` → `UpdateDeployStateToFullStop` sets Extended, which matches the PAM.
Not proven by an experiment: KSP1PartMod 0.2.2 logs `empty=` and `legacy=` of the override clip on each followed
part (`Mecanim copy '…' empty=… legacy=… length=…`) to settle it.

**Scope** (cfgs of the 2026-10-25 install, before ModuleManager patches): 264 parts with a single source
(Bluedog_DB 198, KRE 29, Squad 36, SquadExpansion 1) and 19 parts with `ModuleParachute`.

**Suggested fix.** Keep the legacy `Animation` and drive it from `Data_Deployable` (as the fallback runtime
does), or build the Mecanim clip from the curves with an API available at runtime (for example an
`AnimationClip` produced in the editor, or a Playables graph over the legacy clip).

**Workaround in KSP1PartMod 0.2.2.** It re-enables the legacy `Animation`, disables the pose keeper, keeps
Redux's Animator (its state machine ends the moves) and draws the clip from `Data_Deployable.CurrentDeployState`
/ `Data_Parachute.deployState`.

## 2. KSP1 part icons are black

`Ksp1ImportedPartRegistry.EnsureIconGenerated` calls `RuntimePartIconRenderer.RenderIcon(…,
IconRenderProfile.Ksp1Import)`, whose only difference from `Ksp2PartFallback` is
`hotswapPartShaders = false`. `Ksp1ModelImporter.FindBestShader` gives KSP1 materials `KSP2/Parts/Paintable`
when it exists. The OAB swaps that shader for `KSP2/Scenery/Standard (Opaque)` (the VAB shows the parts
correctly: material dump of the OAB template), the icon studio does not, and the subject comes out black under
the studio's lights. KSP1PartMod 0.2.2 turns the swap on for KSP1 parts in a prefix of
`PartIconRenderer.RenderTexture2D` and logs the shaders of the rendered prefab (`icons: … had [...]`) to confirm.

**Suggested fix.** `hotswapPartShaders = true` for `IconRenderProfile.Ksp1Import`, or the reason it is off.

## 3. Generated reentry hull without a material (to check)

`RuntimeReentryMeshFallback.EnsureGenerated` (from `PartModuleLoadPatcher.ApplyOnGameObject`) adds
`lod_reentry/lod_reentry_lod_0..2` to every KSP1 part, with a `MeshRenderer` and no material: by design,
`ReentryMesh.OnEnable` assigns `ContextualFxSystem.ReentryMaterial`, and the root is created inactive. If
`ReentryMaterial` is null when a hull is enabled (in the VAB, for example), the hull draws Unity's error
colour. Red regions were seen on KSP1 parts in the VAB; whether they are this hull is not established.
KSP1PartMod 0.2.2 logs, for the first five parts, how many hull renderers have no material and how many are
active, and switches off a hull that is active without one.

## Not Redux's: KSP1 categories missing when the game starts in the VAB

Our own bug: our tech-tree placement ran at `CampaignPhase.Loaded`, after `ObjectAssemblyPartTracker` had
filtered the part list through `TechNodeDataStore.PartIDLookup` (once, at construction), so the KSP1 parts and
their `ShowWhenEmpty = false` category buttons were absent until the VAB was entered again. Fixed in
KSP1PartMod 0.2.2 by placing right after "Loading Science Data".
