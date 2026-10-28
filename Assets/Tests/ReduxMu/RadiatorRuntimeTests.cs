using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP.Sim.impl;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c), radiators plan Task 3: the runtime glue of KSP1 radiators (deploy
    /// bridge, auto-on, start-state repair, cooling line), Redux's real <c>Module_Cooler</c> and <c>Module_ActiveRadiator</c>
    /// components on test GameObjects with a <c>Data_Cooler</c> built by our importer's code, our patch methods called as Harmony
    /// would. <c>Data_Cooler</c> is read through <see cref="RadiatorAccess"/> (its <c>IComponentData</c> is not referenced here).</summary>
    public class RadiatorRuntimeTests
    {
        private readonly List<string> _lines = new List<string>();
        private readonly List<GameObject> _objects = new List<GameObject>();

        [SetUp] public void SetUp()
        {
            Disarm();
            _lines.Clear();
            NativeModules.Sink = _lines.Add;
            LogAssert.ignoreFailingMessages = true; // OnDestroy of the modules reaches for the game, absent in EditMode
        }

        [TearDown] public void TearDown()
        {
            foreach (var go in _objects)
            {
                try { UnityEngine.Object.DestroyImmediate(go); }
                catch (Exception) { /* the modules' OnDestroy reaches for the game */ }
            }
            _objects.Clear();
            Disarm();
            NativeModules.Sink = null;
        }

        private static void Disarm()
        {
            new Harmony(RadiatorRuntime.HarmonyId).UnpatchAll(RadiatorRuntime.HarmonyId);
            RadiatorRuntime.Reset();
            IsruRuntime.Reset();
            NativeModules.Reset();
        }

        private bool Arm(string json = "{}")
        {
            var armed = RadiatorRuntime.TryApply(new Harmony(RadiatorRuntime.HarmonyId), AnimationSettings.Parse(json, out _));
            IsruRuntime.PartNameForTests = m => m.gameObject.name;
            RadiatorRuntime.NewPartForTests = _ => true;
            return armed;
        }

        private static readonly System.Reflection.FieldInfo BackingMode = AccessTools.Field(typeof(PartBehaviourModule), "_cachedBackingMode");

        /// <summary>A part GameObject named <paramref name="part"/> with <typeparamref name="T"/> and a <c>Data_Cooler</c> of
        /// <paramref name="kw"/> kW (EC kW/100), on or off as the importer builds it, in flight unless <paramref name="oab"/>.</summary>
        private T Part<T>(string part, out ModuleData cooler, bool on, double kw = 200, bool oab = false) where T : PartBehaviourModule
        {
            // The PAM visibility the native handlers set needs the data registered by the game (absent here): it only logs.
            LogAssert.ignoreFailingMessages = true;
            var go = new GameObject(part);
            _objects.Add(go);
            T module = null;
            try { module = go.AddComponent<T>(); }
            catch (Exception e) { Assert.Ignore($"{typeof(T).Name} cannot be added in EditMode: " + e.Message); }
            cooler = RadiatorAccess.NewCooler(kw, new[] { ("ElectricCharge", kw / 100) }, on);
            // The game names each property when it binds the part's data (the PAM visibility is keyed by it).
            foreach (var name in new[] { "coolerEnabled", "coolerStatusText" })
                if (AccessTools.Field(RadiatorAccess.Get().DataCooler, name).GetValue(cooler) is IModuleDataContext p && p.GetContextKey() == null)
                    AccessTools.Property(p.GetType(), "ContextKey").SetValue(p, name);
            AccessTools.Field(typeof(Module_Cooler), "dataCooler").SetValue(module, cooler);
            BackingMode.SetValue(module, oab ? PartBehaviourModule.PartBackingModes.OAB : PartBehaviourModule.PartBackingModes.Flight);
            return module;
        }

        private static Data_Deployable Deployable(Module_ActiveRadiator r) =>
            (Data_Deployable)AccessTools.Field(typeof(Module_ActiveRadiator), "_dataDeployable").GetValue(r);

        private static void SetState(ModuleData cooler, CoolerStates s) => RadiatorAccess.Get().CurrentCoolerState.SetValue(cooler, s);

        private static ModuleProperty<bool> Enabled(ModuleData cooler) => (ModuleProperty<bool>)RadiatorAccess.Get().CoolerEnabled.GetValue(cooler);

        /// <summary>Both postfixes in the order the game runs them (the base call first).</summary>
        private static void Init(Module_ActiveRadiator r)
        {
            RadiatorRuntime.AfterCoolerInit(r);
            RadiatorRuntime.AfterActiveRadiatorInit(r);
        }

        // ------------------------------------------------------------------ arming

        [Test] public void Arm_AllMembersFound_ArmedLine_ThreePatches()
        {
            Assert.IsTrue(Arm());
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] thermal: armed: deploy bridge on, auto on on" }, _lines);
            Assert.IsTrue(RadiatorRuntime.DeployBridgeOn);
            var patched = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains(RadiatorRuntime.HarmonyId))
                .Select(m => m.DeclaringType.Name + "." + m.Name).OrderBy(n => n).ToList();
            CollectionAssert.AreEqual(new[] { "Module_ActiveRadiator.OnInitialize", "Module_Cooler.OnInitialize", "Module_Cooler.OnModuleFixedUpdate" }, patched);
        }

        [Test] public void Arm_RadiatorsOff_NothingPatched()
        {
            Assert.IsFalse(Arm("{\"ksp1_radiators\": false}"));
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] thermal: armed: deploy bridge off, auto on off" }, _lines);
            Assert.IsFalse(Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains(RadiatorRuntime.HarmonyId)));
        }

        [Test] public void Arm_BridgeOff_AutoOff_OnlyTheCoolerPatches()
        {
            Assert.IsTrue(Arm("{\"radiator_deploy_bridge\": false, \"radiator_auto_on\": false}"));
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] thermal: armed: deploy bridge off, auto on off" }, _lines);
            Assert.IsFalse(Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains(RadiatorRuntime.HarmonyId) &&
                                                                    m.DeclaringType == typeof(Module_ActiveRadiator)));
        }

        // ------------------------------------------------------------------ the deploy bridge, real types

        /// <summary>Review focus 3: the native retract path runs through the bridge. Extended (auto-on: on, IDLE), then retracted:
        /// the native <c>OnDeployStateChanged</c> switches the cooler off; extended again: on again; switched off by the player while
        /// extended: kept off.</summary>
        [Test] public void Bridge_ExtendedThenRetracted_NativeHandlerSwitchesTheCoolerOff_AutoOnAtEachExtension()
        {
            Assert.IsTrue(Arm());
            NativeModules.Record.AddConversion("foldingRadLarge", RadiatorMapping.NativeActiveRadiator);
            var r = Part<Module_ActiveRadiator>("foldingRadLarge", out var cooler, on: false, kw: 1000);
            var enabled = Enabled(cooler);
            _lines.Clear();
            try { Init(r); }
            catch (Exception e) { Assert.Ignore("the native radiator cannot run its handlers in EditMode: " + e.Message); }
            Assert.IsFalse(RadiatorRuntime.Broken, string.Join("\n", _lines));

            var bridge = Deployable(r);
            Assert.IsNotNull(bridge, "the bridge");
            Assert.IsTrue(RadiatorRuntime.TryGet(r, out var c));
            Assert.AreSame(bridge, c.Bridge);
            Assert.IsTrue(bridge.IsExtended, "no Module_Ksp1Animations on the part: always extended");
            Assert.IsTrue(enabled.GetValue(), "a new part extended at start: turned on");
            Assert.AreEqual("IDLE", RadiatorAccess.StateOf(cooler), "on and OFF repaired at the extension (no change event in EditMode)");
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] thermal: foldingRadLarge: auto on: new part, an extended start turns it on",
                "[KSP1PartMod] thermal: foldingRadLarge: radiator turned on when extended",
                "[KSP1PartMod] thermal: foldingRadLarge: cooler state repaired: on, OFF -> IDLE",
                "[KSP1PartMod] thermal: foldingRadLarge: Module_ActiveRadiator always extended (no deploy clip)",
            }, _lines);

            Assert.IsTrue(RadiatorRuntime.Write(c, BridgeDeploy.Retracting));
            Assert.IsFalse(bridge.IsExtended);
            Assert.IsFalse(enabled.GetValue(), "the native OnDeployStateChanged switched it off");
            Assert.IsFalse(RadiatorRuntime.Write(c, BridgeDeploy.Retracting), "written only on change");
            RadiatorRuntime.Write(c, BridgeDeploy.Retracted);
            RadiatorRuntime.Write(c, BridgeDeploy.Extending);
            Assert.IsFalse(enabled.GetValue(), "not while extending");
            RadiatorRuntime.Write(c, BridgeDeploy.Extended);
            Assert.IsTrue(enabled.GetValue(), "on again at the next extension");

            enabled.SetValue(false); // the player switches it off while extended
            Assert.IsFalse(RadiatorRuntime.Write(c, BridgeDeploy.Extended));
            Assert.IsFalse(enabled.GetValue(), "the player's choice is kept");
            Assert.AreEqual(1, _lines.Count(l => l.EndsWith("turned on when extended")), "the line once per part");
        }

        /// <summary>The carry of Task 1: a reloaded part (not just launched) that the player switched off while extended stays off.</summary>
        [Test] public void Bridge_Reload_SavedOff_Extended_StaysOff()
        {
            Assert.IsTrue(Arm());
            RadiatorRuntime.NewPartForTests = _ => false;
            NativeModules.Record.AddConversion("foldingRadMed", RadiatorMapping.NativeActiveRadiator);
            var r = Part<Module_ActiveRadiator>("foldingRadMed", out var cooler, on: false, kw: 250);
            _lines.Clear();
            try { Init(r); }
            catch (Exception e) { Assert.Ignore("the native radiator cannot run its handlers in EditMode: " + e.Message); }
            Assert.IsTrue(Deployable(r).IsExtended);
            Assert.IsFalse(Enabled(cooler).GetValue(), "an extended start of a saved part is no transition");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] thermal: foldingRadMed: auto on: saved cooler off, only a later extension turns it on");
            Assert.IsTrue(RadiatorRuntime.TryGet(r, out var c));
            RadiatorRuntime.Write(c, BridgeDeploy.Retracting);
            RadiatorRuntime.Write(c, BridgeDeploy.Extended);
            Assert.IsTrue(Enabled(cooler).GetValue(), "a later extension does");
        }

        [Test] public void Bridge_AutoOff_StaysOff_Oab_NoAutoOn()
        {
            Assert.IsTrue(Arm("{\"radiator_auto_on\": false}"));
            NativeModules.Record.AddConversion("foldingRadSmall", RadiatorMapping.NativeActiveRadiator);
            var r = Part<Module_ActiveRadiator>("foldingRadSmall", out var cooler, on: false, kw: 50);
            try { Init(r); }
            catch (Exception e) { Assert.Ignore("the native radiator cannot run its handlers in EditMode: " + e.Message); }
            Assert.IsTrue(Deployable(r).IsExtended);
            Assert.IsFalse(Enabled(cooler).GetValue(), "radiator_auto_on false");

            Disarm();
            Assert.IsTrue(Arm());
            NativeModules.Record.AddConversion("foldingRadLarge", RadiatorMapping.NativeActiveRadiator);
            var vab = Part<Module_ActiveRadiator>("foldingRadLarge", out var vabCooler, on: false, kw: 1000, oab: true);
            try { Init(vab); }
            catch (Exception e) { Assert.Ignore("the native radiator cannot run its handlers in EditMode: " + e.Message); }
            Assert.IsNotNull(Deployable(vab), "the bridge in the VAB too");
            Assert.IsFalse(Enabled(vabCooler).GetValue(), "auto-on is flight only");
        }

        [Test] public void Bridge_ReduxDeployableKept_NativePartUntouched_BridgeOff()
        {
            Assert.IsTrue(Arm());
            NativeModules.Record.AddConversion("foldingRadLarge", RadiatorMapping.NativeActiveRadiator);
            var r = Part<Module_ActiveRadiator>("foldingRadLarge", out _, on: false);
            var redux = new Data_Deployable();
            AccessTools.Field(typeof(Module_ActiveRadiator), "_dataDeployable").SetValue(r, redux);
            Init(r);
            Assert.AreSame(redux, Deployable(r), "Redux's own deployable kept");

            var native = Part<Module_ActiveRadiator>("radiator_2v_radial_1x8", out var nativeCooler, on: false);
            SetState(nativeCooler, CoolerStates.IDLE);
            Init(native);
            Assert.IsNull(Deployable(native), "not ours");
            Assert.IsFalse(RadiatorRuntime.TryGet(native, out _));
            Assert.AreEqual("IDLE", RadiatorAccess.StateOf(nativeCooler), "not repaired");

            Disarm();
            Assert.IsTrue(Arm("{\"radiator_deploy_bridge\": false}"));
            NativeModules.Record.AddConversion("foldingRadMed", RadiatorMapping.NativeActiveRadiator);
            var off = Part<Module_ActiveRadiator>("foldingRadMed", out _, on: false);
            Init(off);
            Assert.IsNull(Deployable(off), "radiator_deploy_bridge false: it cools folded too");
        }

        /// <summary>With our animation module on the part, the bridge waits for it; its init with no Deploy entry extends it.</summary>
        [Test] public void Bridge_HandedToTheAnimationModule()
        {
            Assert.IsTrue(Arm());
            NativeModules.Record.AddConversion("foldingRadLarge", RadiatorMapping.NativeActiveRadiator);
            var r = Part<Module_ActiveRadiator>("foldingRadLarge", out var cooler, on: false);
            Module_Ksp1Animations anim;
            try { anim = r.gameObject.AddComponent<Module_Ksp1Animations>(); }
            catch (Exception e) { Assert.Ignore("the animation module cannot be added in EditMode: " + e.Message); return; }
            _lines.Clear();
            Init(r);
            var bridge = Deployable(r);
            Assert.IsNotNull(bridge);
            Assert.IsFalse(bridge.IsExtended, "waits for the animation module");
            Assert.IsFalse(Enabled(cooler).GetValue());
            anim.FinishIsruInit();
            Assert.IsTrue(bridge.IsExtended);
            Assert.IsTrue(Enabled(cooler).GetValue(), "through RadiatorRuntime.Write: auto-on");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] thermal: foldingRadLarge: Module_ActiveRadiator always extended (no deploy clip)");
        }

        // ------------------------------------------------------------------ start-state repair

        [Test] public void Repair_OnWithOff_Idle_OffWithIdle_Off_OncePerPart_FlightOnly()
        {
            Assert.IsTrue(Arm());
            NativeModules.Record.AddConversion("radPanelLg", RadiatorMapping.NativeCooler);
            var on = Part<Module_Cooler>("radPanelLg", out var onCooler, on: true);
            SetState(onCooler, CoolerStates.OFF); // the state does not survive: only coolerEnabled is saved
            _lines.Clear();
            RadiatorRuntime.AfterCoolerInit(on);
            Assert.AreEqual("IDLE", RadiatorAccess.StateOf(onCooler));
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] thermal: radPanelLg: cooler state repaired: on, OFF -> IDLE" }, _lines);

            var off = Part<Module_Cooler>("radPanelLg", out var offCooler, on: true);
            Enabled(offCooler).SetValue(false); // saved switched off; the definition's IDLE came back
            RadiatorRuntime.AfterCoolerInit(off);
            Assert.AreEqual("OFF", RadiatorAccess.StateOf(offCooler), "else it cools switched off");
            CollectionAssert.Contains(_lines, "[KSP1PartMod] thermal: radPanelLg: cooler state repaired: off, IDLE -> OFF");

            // Once per part instance: a later OFF is the player's or the game's.
            SetState(onCooler, CoolerStates.OFF);
            RadiatorRuntime.AfterCoolerInit(on);
            Assert.AreEqual("OFF", RadiatorAccess.StateOf(onCooler));

            var vab = Part<Module_Cooler>("radPanelLg", out var vabCooler, on: true, oab: true);
            SetState(vabCooler, CoolerStates.OFF);
            RadiatorRuntime.AfterCoolerInit(vab);
            Assert.AreEqual("OFF", RadiatorAccess.StateOf(vabCooler), "flight only");

            var native = Part<Module_Cooler>("radiator_2v_radial_fixed", out var nativeCooler, on: true);
            SetState(nativeCooler, CoolerStates.OFF);
            RadiatorRuntime.AfterCoolerInit(native);
            Assert.AreEqual("OFF", RadiatorAccess.StateOf(nativeCooler), "Redux's own cooler untouched");
            Assert.AreEqual(2, _lines.Count);
            Assert.IsFalse(RadiatorRuntime.Broken);
        }

        // ------------------------------------------------------------------ cooling line

        [Test] public void Cooling_LoggedOnceAtTheFirstRemoving()
        {
            Assert.IsTrue(Arm());
            NativeModules.Record.AddConversion("radPanelLg", RadiatorMapping.NativeCooler);
            var m = Part<Module_Cooler>("radPanelLg", out var cooler, on: true);
            AccessTools.Property(RadiatorAccess.Get().DataCooler, "fluxRemoved").SetValue(cooler, 200.0); // the bind does it in game
            RadiatorRuntime.AfterCoolerInit(m);
            _lines.Clear();
            RadiatorRuntime.AfterCoolerFixedUpdate(m);
            CollectionAssert.IsEmpty(_lines, "IDLE");
            SetState(cooler, CoolerStates.REMOVING);
            for (var i = 0; i < 3; i++) RadiatorRuntime.AfterCoolerFixedUpdate(m);
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] thermal: radPanelLg: cooling 200 kW, ElectricCharge 2 (vessel pool)" }, _lines);
        }

        // ------------------------------------------------------------------ allocation

        /// <summary>10 000 bridge steps (a change every third step, through the four states, auto-on and the extension check): no
        /// managed allocation once warm.</summary>
        [Test] public void BridgeSteps_DoNotAllocate()
        {
            Assert.IsTrue(Arm());
            var state = CoolerStates.IDLE;
            var c = new RadiatorRuntime.Cooler
            {
                Part = "p",
                Flight = true,
                Enabled = new ModuleProperty<bool>(false),
                GetState = () => state,
                SetState = s => state = s,
                Bridge = new Data_Deployable(),
                NewPart = true,
            };
            var states = new[] { BridgeDeploy.Retracted, BridgeDeploy.Extending, BridgeDeploy.Extended, BridgeDeploy.Retracting };
            var i = 0;
            var writes = 0;
            void Steps()
            {
                for (var k = 0; k < 10000; k++, i++)
                    if (RadiatorRuntime.Write(c, states[(i / 3) & 3])) writes++;
            }

            Steps(); // warm up (JIT, the once-only lines)
            Assert.IsTrue(c.Enabled.GetValue(), "auto-on ran");
            Assert.That(Steps, Is.Not.AllocatingGCMemory());
            Assert.Greater(writes, 6000, "a write on each change only");
            Assert.Less(writes, 7000);
            Assert.IsFalse(RadiatorRuntime.Broken);
        }
    }
}
