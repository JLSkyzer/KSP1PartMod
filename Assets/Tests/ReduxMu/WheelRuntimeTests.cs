using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Wheels;
using NUnit.Framework;
using Redux.Ksp1Import.Modules;
using UnityEngine;
using UnityEngine.TestTools;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c), wheels plan Task 4: the gear bridge's stand-down
    /// (<see cref="WheelRuntime.NativeWheelGear"/>) and the wheel setup line, on Redux's real <c>Module_WheelBase</c>,
    /// <c>Module_Deployable</c> and <c>Ksp1Module_Deployable</c> components added to test GameObjects, our postfix called as
    /// Harmony would.</summary>
    public class WheelRuntimeTests
    {
        private readonly List<string> _lines = new List<string>();
        private readonly List<GameObject> _objects = new List<GameObject>();

        private static readonly System.Reflection.FieldInfo BackingMode = AccessTools.Field(typeof(PartBehaviourModule), "_cachedBackingMode");

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
            new Harmony(WheelRuntime.HarmonyId).UnpatchAll(WheelRuntime.HarmonyId);
            WheelRuntime.Reset();
            IsruRuntime.Reset();
            NativeModules.Reset();
        }

        private bool Arm(string json = "{}")
        {
            var armed = WheelRuntime.TryApply(new Harmony(WheelRuntime.HarmonyId), AnimationSettings.Parse(json, out _));
            IsruRuntime.PartNameForTests = m => m.gameObject.name;
            return armed;
        }

        /// <summary>The wheel record an import leaves: with a converted base unless <paramref name="baseConverted"/> is false
        /// (a record whose first wheel call was refused, review F4).</summary>
        private static WheelImporter.WheelPart Record(string part, bool hasDeployment, bool baseConverted = true,
            WheelTypeName type = WheelTypeName.LEG, string wheelTransform = "")
        {
            var r = new WheelImporter.WheelPart(part) { HasDeployment = hasDeployment };
            if (baseConverted)
            {
                r.Base = new WheelBaseValues { WheelType = type, WheelColliderTransformName = "wheelCollider", WheelTransformName = wheelTransform };
                r.Summary.Convert(WheelKind.Base);
            }
            else r.Summary.Refuse(WheelKind.Base, WheelRefusal.Mapping);
            WheelImporter.RecordForTests(r);
            return r;
        }

        private enum Deploy { None, Native, NativeWithoutData, Legacy }

        /// <summary>A part GameObject named <paramref name="part"/> with Redux's <c>Module_WheelBase</c> (unless
        /// <paramref name="wheel"/> is false) and the deployable asked, in flight unless <paramref name="oab"/>.</summary>
        private Module_WheelBase Part(string part, Deploy deploy, bool oab = false, bool wheel = true)
        {
            var go = new GameObject(part);
            _objects.Add(go);
            Module_WheelBase w = null;
            try
            {
                switch (deploy)
                {
                    case Deploy.Native:
                    case Deploy.NativeWithoutData:
                    {
                        var d = go.AddComponent<Module_Deployable>();
                        if (deploy == Deploy.Native)
                        {
                            AccessTools.Field(typeof(Module_Deployable), "dataDeployable").SetValue(d, new Data_Deployable { animationName = "Deploy" });
                            d.InitForDataModules();
                        }
                        break;
                    }
                    case Deploy.Legacy:
                        go.AddComponent<Ksp1Module_Deployable>();
                        break;
                }
                w = wheel ? go.AddComponent<Module_WheelBase>() : null;
            }
            catch (Exception e) { Assert.Ignore("the modules cannot be added in EditMode: " + e.Message); }
            foreach (var m in go.GetComponents<PartBehaviourModule>())
                BackingMode.SetValue(m, oab ? PartBehaviourModule.PartBackingModes.OAB : PartBehaviourModule.PartBackingModes.Flight);
            return w;
        }

        /// <summary>The part's model as <c>FindModelTransform</c> sees it: only <paramref name="names"/> are found.</summary>
        private static void Model(params string[] names) =>
            WheelRuntime.FindForTests = (m, n) => names.Contains(n) ? m.gameObject.transform : null;

        /// <summary>What a wheel base that did not return early leaves: its collider host set.</summary>
        private static void HostSet(Module_WheelBase w)
        {
            var host = new GameObject("wheelCollider");
            host.transform.SetParent(w.gameObject.transform, false);
            w.WheelColliderHost = host.transform;
        }

        private static PartBehaviourModule Any(GameObject go) => go.GetComponent<PartBehaviourModule>();

        // ------------------------------------------------------------------ resolution and arming

        [Test] public void Members_ResolveAgainst26w42c()
        {
            var m = WheelRuntime.Resolve();
            Assert.IsNull(m.Missing);
            Assert.AreEqual(typeof(Module_WheelBase), m.WheelBase);
            Assert.AreEqual(typeof(Module_WheelBase), m.OnInitialize.DeclaringType);
            Assert.AreEqual("OnInitialize", m.OnInitialize.Name);
            Assert.AreEqual(typeof(Transform), m.Host.FieldType);
            Assert.AreEqual(typeof(Transform), m.Transform.FieldType);
        }

        [Test] public void Arm_AllKinds_OnePostfixOnOnInitialize()
        {
            Assert.IsTrue(Arm());
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] wheel: armed: base, suspension, steering, motor, motor-steering, brakes, damage, lock, bogey" }, _lines);
            var patched = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains(WheelRuntime.HarmonyId))
                .Select(m => m.DeclaringType.Name + "." + m.Name).ToList();
            CollectionAssert.AreEqual(new[] { "Module_WheelBase.OnInitialize" }, patched);
        }

        [Test] public void Arm_SomeKindsOff_ListedKindsOnly()
        {
            Assert.IsTrue(Arm("{\"wheel_steering\": false, \"wheel_motors\": false}"));
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] wheel: armed: base, suspension, brakes, damage, lock, bogey" }, _lines);
        }

        [Test] public void Arm_WheelsOff_NothingPatched()
        {
            Assert.IsFalse(Arm("{\"ksp1_wheels\": false}"));
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] wheel: armed: none" }, _lines);
            Assert.IsFalse(Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains(WheelRuntime.HarmonyId)));
        }

        // ------------------------------------------------------------------ the gear bridge stand-down (review F4, F5)

        [Test] public void GearBridge_NativeDeployable_VAB_StandsDown()
        {
            Record("GearSmall", true);
            var w = Part("GearSmall", Deploy.Native, oab: true);
            Assert.IsTrue(WheelRuntime.NativeWheelGear(w, "GearSmall"));
        }

        /// <summary>Decided by the wheel base's own early-return lookups, so before and after its <c>OnInitialize</c> alike
        /// (review: independent of the init order).</summary>
        [Test] public void GearBridge_NativeDeployable_Flight_TransformsFound_StandsDown_WhateverTheInitOrder()
        {
            Record("GearSmall", true);
            var w = Part("GearSmall", Deploy.Native);
            Model("wheelCollider");
            Assert.IsTrue(WheelRuntime.NativeWheelGear(w, "GearSmall"), "before the wheel base's init");
            HostSet(w);
            Assert.IsTrue(WheelRuntime.NativeWheelGear(w, "GearSmall"), "after it");
        }

        /// <summary>The wheel base will return (or returned) before adding its Gear toggle: the bridge keeps its own, before
        /// and after the wheel base's init, so never zero Gear actions in flight.</summary>
        [Test] public void GearBridge_NativeDeployable_Flight_HostMissing_KeepsItsAction_WhateverTheInitOrder()
        {
            Record("GearSmall", true);
            var w = Part("GearSmall", Deploy.Native);
            Model();
            Assert.IsFalse(WheelRuntime.NativeWheelGear(w, "GearSmall"), "before the wheel base's init");
            HostSet(w); // even a stale field does not decide
            Assert.IsFalse(WheelRuntime.NativeWheelGear(w, "GearSmall"), "after it");
        }

        [Test] public void GearBridge_NativeDeployable_Flight_NamedWheelTransformMissing_KeepsItsAction()
        {
            Record("roverWheel2", true, wheelTransform: "foot");
            var w = Part("roverWheel2", Deploy.Native);
            Model("wheelCollider");
            Assert.IsFalse(WheelRuntime.NativeWheelGear(w, "roverWheel2"), "the wheel base returns on the wheel transform first");
            Model("foot", "wheelCollider");
            Assert.IsTrue(WheelRuntime.NativeWheelGear(w, "roverWheel2"));
        }

        [Test] public void GearBridge_NativeDeployable_Flight_NoPartBehavior_KeepsItsAction()
        {
            Record("GearSmall", true);
            var w = Part("GearSmall", Deploy.Native);
            Assert.IsFalse(WheelRuntime.NativeWheelGear(w, "GearSmall"), "no part to look in: nothing found");
        }

        /// <summary>Review F5: Redux's legacy deployable does not derive from <c>Module_Deployable</c>; the wheel base finds none
        /// and adds no Gear toggle, so the bridge keeps its own, in flight and in the VAB.</summary>
        [Test] public void GearBridge_LegacyDeployable_KeepsItsAction()
        {
            Assert.IsFalse(typeof(Module_Deployable).IsAssignableFrom(typeof(Ksp1Module_Deployable)));
            Record("landingLeg1", true);
            var w = Part("landingLeg1", Deploy.Legacy);
            Assert.IsNull(w.GetComponent<Module_Deployable>());
            Model("wheelCollider");
            Assert.IsFalse(WheelRuntime.NativeWheelGear(w, "landingLeg1"), "flight");
            var vab = Part("landingLeg1", Deploy.Legacy, oab: true);
            Assert.IsFalse(WheelRuntime.NativeWheelGear(vab, "landingLeg1"), "VAB");
        }

        [Test] public void GearBridge_DeployableWithoutData_KeepsItsAction()
        {
            Record("GearSmall", true);
            var w = Part("GearSmall", Deploy.NativeWithoutData);
            Assert.IsFalse(WheelRuntime.NativeWheelGear(w, "GearSmall"));
        }

        /// <summary>Review F4: a record whose base was refused (the first wheel call writes the record anyway), or no record at
        /// all: the wheel base is not ours.</summary>
        [Test] public void GearBridge_BaseNotConverted_KeepsItsAction()
        {
            Record("refusedLeg", true, baseConverted: false);
            Assert.IsNotNull(WheelImporter.RecordOf("refusedLeg"));
            var w = Part("refusedLeg", Deploy.Native, oab: true);
            Assert.IsFalse(WheelRuntime.NativeWheelGear(w, "refusedLeg"), "refused base");
            var other = Part("noRecord", Deploy.Native, oab: true);
            Assert.IsFalse(WheelRuntime.NativeWheelGear(other, "noRecord"), "no record");
        }

        [Test] public void GearBridge_NoWheelBase_KeepsItsAction()
        {
            Record("GearSmall", true);
            var go = Part("GearSmall", Deploy.Native, oab: true, wheel: false);
            Assert.IsNull(go);
            Assert.IsFalse(WheelRuntime.NativeWheelGear(Any(_objects.Last()), "GearSmall"));
        }

        // ------------------------------------------------------------------ the setup line

        [Test] public void Setup_NativeGate_OncePerPartName_FlightOnly()
        {
            Assert.IsTrue(Arm());
            Record("GearSmall", true, type: WheelTypeName.FREE);
            _lines.Clear();
            var vab = Part("GearSmall", Deploy.Native, oab: true);
            WheelRuntime.AfterInitialize(vab);
            CollectionAssert.IsEmpty(_lines, "VAB");

            var w = Part("GearSmall", Deploy.Native);
            HostSet(w);
            WheelRuntime.AfterInitialize(w);
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] wheel: GearSmall: FREE, collider host 'wheelCollider' found, wheel transform '' none, deploy gate native Module_Deployable" }, _lines);
            var again = Part("GearSmall", Deploy.Native);
            WheelRuntime.AfterInitialize(again);
            Assert.AreEqual(1, _lines.Count, "once per part name");
            Assert.IsFalse(WheelRuntime.Broken);
        }

        /// <summary>The wheel base returned on its missing wheel transform and left the host unset: the host is looked up as it
        /// would have been (review item 2), found here, missing in the second part.</summary>
        [Test] public void Setup_LegacyKept_AlwaysActive_TransformMissing_HostLookedUp()
        {
            Assert.IsTrue(Arm());
            Record("landingLeg1", true, wheelTransform: "foot");
            Record("landingLeg2", true, wheelTransform: "foot");
            _lines.Clear();
            Model("wheelCollider");
            WheelRuntime.AfterInitialize(Part("landingLeg1", Deploy.Legacy));
            Model();
            WheelRuntime.AfterInitialize(Part("landingLeg2", Deploy.Legacy));
            CollectionAssert.AreEqual(new[]
            {
                "[KSP1PartMod] wheel: landingLeg1: LEG, collider host 'wheelCollider' found, wheel transform 'foot' MISSING, deploy gate none: always active (Redux kept its legacy deployable)",
                "[KSP1PartMod] wheel: landingLeg2: LEG, collider host 'wheelCollider' MISSING, wheel transform 'foot' MISSING, deploy gate none: always active (Redux kept its legacy deployable)",
            }, _lines);
        }

        [Test] public void Setup_OwnFirst_AlwaysActive()
        {
            Assert.IsTrue(Arm("{\"own_first_deployable\": true}"));
            Record("landingLeg3", true);
            _lines.Clear();
            WheelRuntime.AfterInitialize(Part("landingLeg3", Deploy.None));
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] wheel: landingLeg3: LEG, collider host 'wheelCollider' MISSING, wheel transform '' none, deploy gate none: always active (own_first_deployable)" }, _lines);
        }

        [Test] public void Setup_FixedWheel()
        {
            Assert.IsTrue(Arm());
            Record("roverWheel1", false, type: WheelTypeName.MOTORIZED);
            _lines.Clear();
            WheelRuntime.AfterInitialize(Part("roverWheel1", Deploy.None));
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] wheel: roverWheel1: MOTORIZED, collider host 'wheelCollider' MISSING, wheel transform '' none, deploy gate none (fixed wheel)" }, _lines);
        }

        [Test] public void Setup_NotOurs_NoLine()
        {
            Assert.IsTrue(Arm());
            Record("refusedLeg", true, baseConverted: false);
            _lines.Clear();
            WheelRuntime.AfterInitialize(Part("refusedLeg", Deploy.Native));
            WheelRuntime.AfterInitialize(Part("wheel_kerbal_rover", Deploy.None));
            CollectionAssert.IsEmpty(_lines);
        }
    }
}
