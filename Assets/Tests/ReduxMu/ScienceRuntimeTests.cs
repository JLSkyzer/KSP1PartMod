using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Game.Science;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP.Sim.impl;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Game;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Science;
using KSP1PartMod.Tests.Animation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KSP1PartMod.Tests.ReduxMu
{
    /// <summary>Against the referenced Ksp2Redux.dll (26w42c), science plan Task 5: the runtime of converted KSP1 experiments. Redux's
    /// real <c>Module_ScienceExperiment</c> on test GameObjects, its real <c>PartComponentModule_ScienceExperiment</c> counting a run
    /// down, real <c>Data_ScienceExperiment</c> and <c>ExperimentStanding</c> objects, our patch methods called as Harmony would.
    /// Cfg text in the shape of the stock and DMagic files, ids invented.</summary>
    public class ScienceRuntimeTests
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
            new Harmony(ScienceRuntime.HarmonyId).UnpatchAll(ScienceRuntime.HarmonyId);
            ScienceRuntime.Reset();
            ScienceImporter.ResetSession();
            IsruRuntime.Reset();
            NativeModules.Reset();
        }

        private bool Arm(string json = "{}")
        {
            var armed = ScienceRuntime.TryApply(new Harmony(ScienceRuntime.HarmonyId), AnimationSettings.Parse(json, out _));
            IsruRuntime.PartNameForTests = m => m.gameObject.name;
            return armed;
        }

        private const string GooCfg = @"
PART
{
    name = gooTest
    MODULE
    {
        name = ModuleAnimateGeneric
        animationName = Deploy
    }
    MODULE
    {
        name = ModuleScienceExperiment
        experimentID = mysteryGoo
        xmitDataScalar = 0.3
        FxModules = 0
    }
}";

        private const string DMagicCfg = @"
PART
{
    name = dmagicTest
    MODULE
    {
        name = DMModuleScienceAnimateGeneric
        experimentID = magnetometer
        animationName = Boom
        experimentAnimation = true
        experimentWaitForAnimation = true
        waitForAnimationTime = 4
    }
}";

        /// <summary>The part's resolution, recorded as the importer records it.</summary>
        private static SciencePart Record(string cfg)
        {
            var node = CfgText.Parse(cfg).GetNodes("PART").Single();
            var name = node.GetValue("name");
            var part = ScienceMapping.Resolve(node.GetNodes("MODULE").ToList(), new Dictionary<string, ScienceDefinitionModel>(),
                NativeExperiments.Redux26w42c, AnimationSettings.Defaults);
            Assert.IsTrue(part.HasModule, name);
            ScienceImporter.RecordForTests(name, part);
            return part;
        }

        private static readonly System.Reflection.FieldInfo BackingMode = AccessTools.Field(typeof(PartBehaviourModule), "_cachedBackingMode");
        private static readonly System.Reflection.FieldInfo ModuleData = AccessTools.Field(typeof(Module_ScienceExperiment), "dataScienceExperiment");

        /// <summary>Redux's data as the importer builds it, with the native module's own <c>OnInitialize</c> outcome on a part with an
        /// <c>Animator</c>: <c>IsPartAnimated</c> true.</summary>
        private static Data_ScienceExperiment Data(SciencePart part, ExperimentState state = ExperimentState.READY)
        {
            var data = (Data_ScienceExperiment)ScienceAccess.NewExperiment(part);
            foreach (var c in part.Configurations)
                data.ExperimentStandings.Add(new ExperimentStanding { ExperimentID = c.ExperimentDefinitionId, CurrentExperimentState = state });
            data.IsPartAnimated = true;
            return data;
        }

        /// <summary>A part GameObject with Redux's real module, a child holding an <c>Animator</c> (Redux's Mecanim copy), the data set,
        /// the module's <c>Animator</c> pointing at the child (what its <c>OnInitialize</c> found), in flight unless <paramref name="oab"/>.</summary>
        private Module_ScienceExperiment Part(string name, Data_ScienceExperiment data, out Animator animator, bool oab = false)
        {
            var go = new GameObject(name);
            _objects.Add(go);
            var child = new GameObject("model");
            child.transform.SetParent(go.transform, false);
            animator = child.AddComponent<Animator>();
            Module_ScienceExperiment module = null;
            try { module = go.AddComponent<Module_ScienceExperiment>(); }
            catch (Exception e) { Assert.Ignore("Module_ScienceExperiment cannot be added in EditMode: " + e.Message); }
            ModuleData.SetValue(module, data);
            module.Animator = animator;
            BackingMode.SetValue(module, oab ? PartBehaviourModule.PartBackingModes.OAB : PartBehaviourModule.PartBackingModes.Flight);
            return module;
        }

        // ------------------------------------------------------------------ arming

        [Test] public void Arm_AllMembersFound_ArmedLine_ThreePatches()
        {
            Assert.IsTrue(Arm());
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] science: armed: deploy animations on, wait for animation on" }, _lines);
            Assert.IsTrue(ScienceRuntime.On);
            Assert.IsTrue(ScienceRuntime.ReportOn);
            var patched = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains(ScienceRuntime.HarmonyId))
                .Select(m => m.DeclaringType.Name + "." + m.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(new[]
            {
                "Module_ScienceExperiment.OnInitialize",
                "PartComponentModule_ScienceExperiment.CreateScienceReports",
                "ScienceStorageComponent.StoreResearchReport",
            }, patched);
        }

        [Test] public void Arm_ScienceOff_NothingPatched_NoLine()
        {
            Assert.IsFalse(Arm("{\"ksp1_science\": false}"));
            CollectionAssert.IsEmpty(_lines);
            Assert.IsFalse(Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains(ScienceRuntime.HarmonyId)));
        }

        [Test] public void Arm_TheSwitchesShowInTheLine()
        {
            Assert.IsTrue(Arm("{\"science_wait_for_animation\": false}"));
            Assert.AreEqual("[KSP1PartMod] science: armed: deploy animations on, wait for animation off", _lines.Single());
            Disarm();
            _lines.Clear();
            NativeModules.Sink = _lines.Add;
            Assert.IsTrue(Arm("{\"enabled\": false}"));
            Assert.AreEqual("[KSP1PartMod] science: armed: deploy animations off, wait for animation off", _lines.Single(),
                "the animation switches need enabled; the gate is still armed");
        }

        // ------------------------------------------------------------------ the animator gate (Task 4 review, I-2)

        /// <summary>I-2: KSP2 marked the part animated because of Redux's <c>Animator</c>. Ours lose the <c>Animator</c> (no empty
        /// trigger at any later start or stop) and the flag; with no animation module, no clip, logged per configuration.</summary>
        [Test] public void Init_Flight_OursLoseTheAnimatorAndTheFlag_NoClipWithoutAnimationModule()
        {
            Assert.IsTrue(Arm());
            var record = Record(GooCfg);
            var data = Data(record);
            var m = Part("gooTest", data, out _);
            _lines.Clear();
            ScienceRuntime.AfterInit(m);
            Assert.IsFalse(ScienceRuntime.Broken, string.Join("\n", _lines));
            Assert.IsNull(m.Animator, "no trigger reaches Redux's Mecanim copy");
            Assert.IsFalse(data.IsPartAnimated, "KSP2's countdown never waits on a native deploy state");
            Assert.IsFalse(data.PartIsDeployed);
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] science: gooTest: EnvironmentSurvey has no clip (no entry)" }, _lines);

            ScienceRuntime.AfterInit(m); // a second init: the line once per session
            Assert.AreEqual(1, _lines.Count);
        }

        [Test] public void Init_NotInTheRecord_Untouched()
        {
            Assert.IsTrue(Arm());
            var record = Record(GooCfg);
            var data = Data(record);
            var m = Part("pod_1.5v_conical_crew", data, out var animator);
            _lines.Clear();
            ScienceRuntime.AfterInit(m);
            Assert.AreSame(animator, m.Animator, "KSP2's own parts keep their Animator");
            Assert.IsTrue(data.IsPartAnimated);
            CollectionAssert.IsEmpty(_lines);
        }

        [Test] public void Init_Oab_AnimatorAndFlagCleared_NoLine()
        {
            Assert.IsTrue(Arm());
            var data = Data(Record(GooCfg));
            var m = Part("gooTest", data, out _, oab: true);
            _lines.Clear();
            ScienceRuntime.AfterInit(m);
            Assert.IsNull(m.Animator);
            Assert.IsFalse(data.IsPartAnimated);
            CollectionAssert.IsEmpty(_lines, "the clip lines are for flight");
        }

        /// <summary>KSP2 sent the empty trigger inside its own <c>OnInitialize</c>, before any postfix: logged once.</summary>
        [Test] public void Init_LoadedRunning_NativeTriggerAlreadyFired_LoggedOnce()
        {
            Assert.IsTrue(Arm());
            var data = Data(Record(GooCfg), ExperimentState.RUNNING);
            var m = Part("gooTest", data, out _);
            _lines.Clear();
            ScienceRuntime.AfterInit(m);
            ScienceRuntime.AfterInit(m);
            Assert.AreEqual(1, _lines.Count(l => l == "[KSP1PartMod] science: gooTest: loaded with EnvironmentSurvey running, the native trigger already fired"),
                string.Join("\n", _lines));
            Assert.IsNull(m.Animator);
        }

        [Test] public void Init_AnimationsOff_NoClipReason()
        {
            Assert.IsTrue(Arm("{\"science_deploy_animations\": false}"));
            var m = Part("gooTest", Data(Record(GooCfg)), out _);
            _lines.Clear();
            ScienceRuntime.AfterInit(m);
            CollectionAssert.AreEqual(new[] { "[KSP1PartMod] science: gooTest: EnvironmentSurvey has no clip (animations off)" }, _lines);
        }

        /// <summary>With our animation module, the part waits for it; a module that bound no entry gives no clip, and a waiting
        /// configuration does not make the part animated.</summary>
        [Test] public void Init_HandedToTheAnimationModule_NoBoundEntry_NotAnimated()
        {
            Assert.IsTrue(Arm());
            var record = Record(DMagicCfg);
            Assert.IsTrue(record.Configurations.Single().Waits, "the mapping's wait");
            var data = Data(record);
            var m = Part("dmagicTest", data, out _);
            Module_Ksp1Animations anim;
            try { anim = m.gameObject.AddComponent<Module_Ksp1Animations>(); }
            catch (Exception e) { Assert.Ignore("the animation module cannot be added in EditMode: " + e.Message); return; }
            _lines.Clear();
            ScienceRuntime.AfterInit(m);
            CollectionAssert.IsEmpty(_lines, "waits for the animation module");
            anim.FinishIsruInit();
            anim.FinishScienceInit();
            Assert.IsFalse(data.IsPartAnimated);
            var id = record.Configurations.Single().ExperimentDefinitionId;
            CollectionAssert.AreEqual(new[] { $"[KSP1PartMod] science: dmagicTest: {id} has no clip (no entry)" }, _lines);
        }

        // ------------------------------------------------------------------ KSP2's countdown, real type

        private static PartComponentModule_ScienceExperiment Component(Data_ScienceExperiment data, ResearchLocation location)
        {
            var c = new PartComponentModule_ScienceExperiment();
            AccessTools.Field(typeof(PartComponentModule_ScienceExperiment), "dataScienceExperiment").SetValue(c, data);
            AccessTools.Field(typeof(PartComponentModule_ScienceExperiment), "_currentLocation").SetValue(c, location);
            return c;
        }

        /// <summary>The deploy gate of KSP2's own <c>PartComponentModule_ScienceExperiment.OnUpdate</c>: animated and not deployed, the
        /// run does not count down; deployed, it does; not animated (what the postfix leaves), it does whatever the gate. A first
        /// frame at the full time (no resource check, which needs the game).</summary>
        [Test] public void Countdown_RealComponent_WaitsOnlyWhileAnimatedAndNotDeployed() => WithCalendar(CountdownCase);

        /// <summary>OnUpdate formats the remaining time with the game's calendar (PhysicsSettings, loaded by the game): a default
        /// instance for the test, the static put back after.</summary>
        private static void WithCalendar(Action body)
        {
            var settingsField = AccessTools.Field(typeof(PhysicsSettings), "_physicsSettings");
            var saved = settingsField.GetValue(null);
            try
            {
                if (saved == null) settingsField.SetValue(null, new PhysicsSettings());
                body();
            }
            finally
            {
                settingsField.SetValue(null, saved);
            }
        }

        private void CountdownCase()
        {
            var record = Record(DMagicCfg);
            var data = Data(record, ExperimentState.RUNNING);
            var time = data.Experiments[0].TimeToComplete;
            Assert.AreEqual(4f, time, "waitForAnimationTime");
            var location = new ResearchLocation(false, "Kerbin", ScienceSitutation.LowOrbit, null);
            var standing = data.ExperimentStandings[0];
            standing.CurrentRunningTime = time;
            standing.ExperimentLocation = location;
            var component = Component(data, location);

            data.IsPartAnimated = true;
            data.PartIsDeployed = false;
            try
            {
                for (var i = 0; i < 5; i++) component.OnUpdate(0, 1.0);
            }
            catch (Exception e) { Assert.Ignore("PartComponentModule_ScienceExperiment.OnUpdate cannot run in EditMode: " + e); }
            Assert.AreEqual(time, standing.CurrentRunningTime, 1e-9, "the gate keeps the countdown");
            Assert.AreEqual(ExperimentState.RUNNING, standing.CurrentExperimentState);

            data.PartIsDeployed = true;
            component.OnUpdate(0, 5.0);
            Assert.Less(standing.CurrentRunningTime, 0.0, "deployed: it counts down, the next frame stores the report");

            standing.CurrentRunningTime = time;
            data.IsPartAnimated = false;
            data.PartIsDeployed = false;
            component.OnUpdate(0, 1.0);
            Assert.AreEqual(time - 1, standing.CurrentRunningTime, 1e-9, "not animated: no gate (the postfix's I-2 fix)");
        }

        // ------------------------------------------------------------------ the bridge

        private static ToggleMachine Toggle(bool oneWay = false) =>
            new ToggleMachine(new AnimEntryDef { Kind = EntryKind.ScienceToggle, Key = "science:Boom:0" }, 1f, EditorSpeed.Ksp1, 1f, oneWay);

        private static ScienceBridge.Target Target(ToggleMachine m, Func<bool> live = null) =>
            new ScienceBridge.Target("science:Boom:0", live ?? (() => true), () => m.Closed && m.Toggle(false), () => m.Closed, () => m.IsMoving);

        private static ScienceBridge Bridge(Data_ScienceExperiment data, string id, ScienceBridge.Target target, bool waits) =>
            new ScienceBridge("dmagicTest", data, new[] { new ScienceBridge.Config(id, new[] { target }, waits ? target : null) });

        [Test] public void Bridge_OpensAtRunning_GateFollowsTheClip_LatchedUntilTheRunEnds()
        {
            var record = Record(DMagicCfg);
            var id = record.Configurations.Single().ExperimentDefinitionId;
            var data = Data(record);
            var m = Toggle();
            var bridge = Bridge(data, id, Target(m), waits: true);
            Assert.IsTrue(bridge.Waits);
            var standing = data.ExperimentStandings[0];

            bridge.Step();
            Assert.IsTrue(m.Closed, "READY: nothing opens");
            Assert.IsFalse(data.PartIsDeployed);

            standing.CurrentExperimentState = ExperimentState.RUNNING;
            bridge.Step();
            Assert.IsFalse(m.Closed, "RUNNING: the clip opens");
            Assert.IsTrue(m.IsMoving);
            Assert.IsFalse(data.PartIsDeployed, "still moving");
            m.Tick(0.5f);
            bridge.Step();
            Assert.IsFalse(data.PartIsDeployed);
            m.Tick(0.6f);
            bridge.Step();
            Assert.IsTrue(data.PartIsDeployed, "open and still");

            m.Toggle(false); // the player closes it during the run
            m.Tick(1f);
            bridge.Step();
            Assert.IsTrue(data.PartIsDeployed, "latched until the run ends");

            standing.CurrentExperimentState = ExperimentState.ALREADYSTORED;
            bridge.Step();
            Assert.IsFalse(data.PartIsDeployed, "no run");
            Assert.IsTrue(m.Closed, "the clip is not closed by us (here the player closed it)");
            Assert.AreEqual(1, bridge.Opens);
        }

        /// <summary>DMagic's <c>oneWayAnimation</c>: the toggle plays back as soon as it is open, never seen open and still.</summary>
        [Test] public void Bridge_OneWayClip_TheReturnOpensTheGate_AndARefusedOpenIsAskedAgain()
        {
            var record = Record(DMagicCfg);
            var id = record.Configurations.Single().ExperimentDefinitionId;
            var data = Data(record);
            var m = Toggle(oneWay: true);
            var bridge = Bridge(data, id, Target(m), waits: true);
            var standing = data.ExperimentStandings[0];

            standing.CurrentExperimentState = ExperimentState.RUNNING;
            bridge.Step();
            m.Tick(1.1f); // reaches open, plays back at once
            Assert.IsTrue(m.Closed && m.IsMoving);
            bridge.Step();
            Assert.IsTrue(data.PartIsDeployed, "the return means it was open");

            // A second run starts while the clip still plays back: the open is refused, then asked again once it is closed.
            standing.CurrentExperimentState = ExperimentState.ALREADYSTORED;
            bridge.Step();
            standing.CurrentExperimentState = ExperimentState.RUNNING;
            bridge.Step();
            Assert.IsFalse(data.PartIsDeployed);
            Assert.IsTrue(m.Closed && m.IsMoving, "refused while moving");
            m.Tick(2f);
            Assert.IsTrue(m.Closed && !m.IsMoving);
            bridge.Step();
            Assert.IsFalse(m.Closed, "asked again");
            m.Tick(1.1f);
            bridge.Step();
            Assert.IsTrue(data.PartIsDeployed);
        }

        [Test] public void Bridge_GateEntryGone_NeverBlocks_NoWait_NoWrite()
        {
            var record = Record(DMagicCfg);
            var id = record.Configurations.Single().ExperimentDefinitionId;
            var data = Data(record, ExperimentState.RUNNING);
            var live = true;
            var m = Toggle();
            var bridge = Bridge(data, id, Target(m, () => live), waits: true);
            live = false; // switched off by B9 before the run's first frame
            bridge.Step();
            Assert.IsTrue(m.Closed, "a dead entry is not opened");
            Assert.IsTrue(data.PartIsDeployed, "the countdown is not held by a clip that cannot open");

            var plain = Data(record, ExperimentState.READY);
            var noWait = Bridge(plain, id, Target(Toggle()), waits: false);
            Assert.IsFalse(noWait.Waits);
            plain.ExperimentStandings[0].CurrentExperimentState = ExperimentState.RUNNING;
            noWait.Step();
            Assert.IsFalse(plain.PartIsDeployed, "a configuration that does not wait never writes the gate");
            Assert.AreEqual(1, noWait.Opens, "but its clip opens");
        }

        /// <summary>A zero-time run goes from READY to a stored report between two frames: the clip opens anyway. A part loaded with a
        /// stored report opens nothing; one loaded running opens.</summary>
        [Test] public void Bridge_ZeroTimeRun_And_LoadedStates()
        {
            var record = Record(GooCfg);
            var id = record.Configurations.Single().ExperimentDefinitionId;
            var data = Data(record);
            var m = Toggle();
            var bridge = Bridge(data, id, Target(m), waits: false);
            bridge.Step();
            data.ExperimentStandings[0].CurrentExperimentState = ExperimentState.ALREADYSTORED;
            bridge.Step();
            Assert.IsFalse(m.Closed);

            var stored = Data(record, ExperimentState.ALREADYSTORED);
            var m2 = Toggle();
            Bridge(stored, id, Target(m2), false).Step();
            Assert.IsTrue(m2.Closed);

            var running = Data(record, ExperimentState.RUNNING);
            var m3 = Toggle();
            Bridge(running, id, Target(m3), false).Step();
            Assert.IsFalse(m3.Closed);
        }

        /// <summary>10 000 science steps with a waiting configuration, the run starting and stopping every 50 steps and the clip moving:
        /// no managed allocation once warm.</summary>
        [Test] public void BridgeSteps_DoNotAllocate()
        {
            var record = Record(DMagicCfg);
            var id = record.Configurations.Single().ExperimentDefinitionId;
            var data = Data(record);
            var m = Toggle(oneWay: true);
            var bridge = Bridge(data, id, Target(m), waits: true);
            var standing = data.ExperimentStandings[0];
            void Run(int n)
            {
                for (var i = 0; i < n; i++)
                {
                    if (i % 50 == 0)
                        standing.CurrentExperimentState = standing.CurrentExperimentState == ExperimentState.RUNNING
                            ? ExperimentState.ALREADYSTORED : ExperimentState.RUNNING;
                    m.Tick(0.05f);
                    bridge.Step();
                }
            }
            Run(200); // warm
            Assert.That(() => Run(10000), Is.Not.AllocatingGCMemory());
            Assert.Greater(bridge.Opens, 10);
        }

        // ------------------------------------------------------------------ a break (Task 5 review, item 1)

        /// <summary>One bridge throws: every bridge stops, and each part drops its own at its next step and is no longer animated, so
        /// a gated part that had not latched still counts down (KSP2's real countdown).</summary>
        [Test] public void Break_OneBridgeThrows_TheOtherPartStillCountsDown() => WithCalendar(() =>
        {
            var record = Record(DMagicCfg);
            var id = record.Configurations.Single().ExperimentDefinitionId;

            var dataA = Data(record, ExperimentState.RUNNING);
            var throwing = new ScienceBridge.Target("science:Boom:0", () => throw new InvalidOperationException("test"), () => false,
                () => true, () => false);
            var a = new ScienceRuntime.Handle { Part = "partA", Data = dataA, Record = record, Bridge = Bridge(dataA, id, throwing, waits: true) };

            var dataB = Data(record, ExperimentState.RUNNING);
            var m = Toggle();
            var b = new ScienceRuntime.Handle { Part = "partB", Data = dataB, Record = record, Bridge = Bridge(dataB, id, Target(m), waits: true) };
            Assert.IsTrue(dataA.IsPartAnimated && dataB.IsPartAnimated, "as the bind leaves a waiting part");

            ScienceRuntime.Step(b);
            Assert.IsFalse(dataB.PartIsDeployed, "B opens its clip and waits");
            Assert.IsTrue(m.IsMoving);

            _lines.Clear();
            ScienceRuntime.Step(a);
            Assert.IsTrue(ScienceRuntime.Broken);
            Assert.IsNull(a.Bridge);
            Assert.IsFalse(dataA.IsPartAnimated);
            Assert.IsTrue(_lines.Single().StartsWith("[KSP1PartMod] science: patch failed, stopped for this session: "), _lines.Single());

            ScienceRuntime.Step(b);
            Assert.IsNull(b.Bridge, "every bridge stops");
            Assert.IsFalse(dataB.IsPartAnimated, "B no longer waits for a gate nobody writes");
            Assert.IsFalse(dataB.PartIsDeployed);

            var location = new ResearchLocation(false, "Kerbin", ScienceSitutation.LowOrbit, null);
            var standing = dataB.ExperimentStandings[0];
            var time = dataB.Experiments[0].TimeToComplete;
            standing.CurrentRunningTime = time;
            standing.ExperimentLocation = location;
            Component(dataB, location).OnUpdate(0, 1.0);
            Assert.AreEqual(time - 1, standing.CurrentRunningTime, 1e-9, "B's countdown advances");
        });

        /// <summary>After a break, a part initialized later still loses the Animator and the flag; only the bridge and its lines are
        /// skipped.</summary>
        [Test] public void Break_LaterInit_StillClearsTheGate_NoBridgeNoLines()
        {
            Assert.IsTrue(Arm());
            var data = Data(Record(DMagicCfg), ExperimentState.RUNNING);
            var m = Part("dmagicTest", data, out _);
            Module_Ksp1Animations anim;
            try { anim = m.gameObject.AddComponent<Module_Ksp1Animations>(); }
            catch (Exception e) { Assert.Ignore("the animation module cannot be added in EditMode: " + e.Message); return; }
            ScienceRuntime.Break(new InvalidOperationException("test"));
            _lines.Clear();
            ScienceRuntime.AfterInit(m);
            Assert.IsNull(m.Animator);
            Assert.IsFalse(data.IsPartAnimated);
            anim.FinishIsruInit();
            anim.FinishScienceInit();
            Assert.IsFalse(data.IsPartAnimated);
            Assert.IsNull(AccessTools.Field(typeof(Module_Ksp1Animations), "_science").GetValue(anim), "no bridge handed over");
            CollectionAssert.IsEmpty(_lines, "no loaded-running nor clip line");
        }

        // ------------------------------------------------------------------ the real bind (Task 5 review, item 3)

        /// <summary>The goo canister's path in full: the planner's entry for its <c>ModuleAnimateGeneric</c> (a Toggle) bound by our
        /// module's own setup on a legacy <c>Animation</c>, the science bind (<c>BindScience</c>, <c>ScienceTarget</c>) and a step to
        /// RUNNING: the toggle opens.</summary>
        [Test] public void Bind_RealSetup_GooToggleOpensAtRunning()
        {
            Assert.IsTrue(Arm());
            var node = CfgText.Parse(GooCfg).GetNodes("PART").Single();
            var modules = node.GetNodes("MODULE").ToList();
            var plan = OwnershipPlanner.Plan(modules, false, new HashSet<string>(), true, false, new HashSet<string>(), true, true); // own_first_deployable: the clip is ours
            var toggleDef = plan.Entries.Single(e => e.ModuleIndex == 0);
            Assert.AreEqual(EntryKind.Toggle, toggleDef.Kind, "the planner's entry for the goo's ModuleAnimateGeneric");
            var record = Record(GooCfg);
            var data = Data(record);
            var m = Part("gooTest", data, out _);

            // The clip on a legacy Animation of the model, as the KSP1 import leaves an owned clip.
            var model = m.gameObject.transform.Find("model").gameObject;
            var animation = model.AddComponent<UnityEngine.Animation>();
            var clip = new AnimationClip { legacy = true };
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            animation.AddClip(clip, toggleDef.Clip);

            Module_Ksp1Animations anim;
            try { anim = m.gameObject.AddComponent<Module_Ksp1Animations>(); }
            catch (Exception e) { Assert.Ignore("the animation module cannot be added in EditMode: " + e.Message); return; }
            var animData = new Data_Ksp1Animations { Entries = plan.Entries.ToList(), ReduxClips = plan.ReduxClips.ToList() };
            AccessTools.Field(typeof(Module_Ksp1Animations), "data").SetValue(anim, animData);
            BackingMode.SetValue(anim, PartBehaviourModule.PartBackingModes.Flight);
            AccessTools.Field(typeof(Module_Ksp1Animations), "_name").SetValue(anim, "gooTest");
            anim.InitForDataModules();
            try { AccessTools.Method(typeof(Module_Ksp1Animations), "Setup").Invoke(anim, null); }
            catch (Exception e) { Assert.Ignore("our module's setup cannot run in EditMode: " + e.GetBaseException()); }
            Assert.IsTrue((bool)AccessTools.Field(typeof(Module_Ksp1Animations), "_ready").GetValue(anim), "bound");

            _lines.Clear();
            ScienceRuntime.AfterInit(m);
            anim.FinishIsruInit();
            anim.FinishScienceInit();
            CollectionAssert.AreEqual(new[] { $"[KSP1PartMod] science: gooTest: EnvironmentSurvey opens {toggleDef.Key}" }, _lines);
            Assert.IsFalse(data.IsPartAnimated, "the stock experiment does not wait");

            var step = AccessTools.Method(typeof(Module_Ksp1Animations), "StepScience");
            step.Invoke(anim, null);
            var handle = (ScienceRuntime.Handle)AccessTools.Field(typeof(Module_Ksp1Animations), "_science").GetValue(anim);
            Assert.IsNotNull(handle?.Bridge);
            Assert.AreEqual(0, handle.Bridge.Opens, "READY: nothing opens");
            data.ExperimentStandings[0].CurrentExperimentState = ExperimentState.RUNNING;
            step.Invoke(anim, null);
            Assert.AreEqual(1, handle.Bridge.Opens);
            var entries = (System.Collections.IList)AccessTools.Field(typeof(Module_Ksp1Animations), "_entries").GetValue(anim);
            var entry = entries.Cast<object>().Single();
            var toggle = (ToggleMachine)AccessTools.Field(entry.GetType(), "Toggle").GetValue(entry);
            Assert.IsFalse(toggle.Closed, "the goo's toggle opened");
            Assert.IsTrue(toggle.IsMoving);
        }

        /// <summary>The Redux follower's open path: the part's <c>Data_Deployable</c> of the clip, whose <c>toggleExtend</c> the bridge
        /// sets.</summary>
        [Test] public void DeployableOf_ReduxFollower_TheClipsDeployable()
        {
            var go = new GameObject("followerTest");
            _objects.Add(go);
            Module_Deployable deployable;
            Module_Ksp1Animations anim;
            try
            {
                deployable = go.AddComponent<Module_Deployable>();
                anim = go.AddComponent<Module_Ksp1Animations>();
            }
            catch (Exception e) { Assert.Ignore("the modules cannot be added in EditMode: " + e.Message); return; }
            var data = new Data_Deployable { animationName = "Deploy" };
            AccessTools.Field(typeof(Module_Deployable), "dataDeployable").SetValue(deployable, data);
            deployable.InitForDataModules();
            Assert.AreSame(data, ReduxFollowerBinder.DeployableOf(anim, "Deploy"));
            Assert.IsFalse(data.toggleExtend.GetValue());
            data.toggleExtend.SetValue(true);
            Assert.IsTrue(data.toggleExtend.GetValue(), "the value the bridge writes");
        }

        // ------------------------------------------------------------------ the report line

        [Test] public void Report_FirstStoredReportOfOurs_OncePerPartInstance()
        {
            Assert.IsTrue(Arm());
            var record = Record(GooCfg);
            var id = record.Configurations.Single().ExperimentDefinitionId;
            ScienceRuntime.ComponentPartForTests = _ => "gooTest";
            var location = new ResearchLocation(false, "Kerbin", ScienceSitutation.LowOrbit, null);
            var component = new PartComponentModule_ScienceExperiment();
            _lines.Clear();

            ScienceRuntime.AfterStoreReport(new ResearchReport(id, "x", location, ScienceReportType.DataType, 3f, ""), true);
            CollectionAssert.IsEmpty(_lines, "outside CreateScienceReports");

            ScienceRuntime.BeforeCreateReports(component);
            ScienceRuntime.AfterStoreReport(new ResearchReport(id, "x", location, ScienceReportType.DataType, 12.5f, ""), false);
            CollectionAssert.IsEmpty(_lines, "not stored");
            ScienceRuntime.AfterStoreReport(new ResearchReport(id, "x", location, ScienceReportType.DataType, 12.5f, ""), true);
            ScienceRuntime.AfterStoreReport(new ResearchReport(id, "x", location, ScienceReportType.SampleType, 4f, ""), true);
            ScienceRuntime.AfterCreateReports();
            CollectionAssert.AreEqual(new[] { $"[KSP1PartMod] science: gooTest: report {id} stored at {location.ResearchLocationId}, value 12.5" }, _lines);

            ScienceRuntime.BeforeCreateReports(component);
            ScienceRuntime.AfterStoreReport(new ResearchReport(id, "x", location, ScienceReportType.DataType, 1f, ""), true);
            ScienceRuntime.AfterCreateReports();
            Assert.AreEqual(1, _lines.Count, "once per part instance");

            ScienceRuntime.ComponentPartForTests = _ => "pod_1.5v_conical_crew";
            ScienceRuntime.BeforeCreateReports(new PartComponentModule_ScienceExperiment());
            ScienceRuntime.AfterStoreReport(new ResearchReport("CrewObservation", "x", location, ScienceReportType.DataType, 1f, ""), true);
            ScienceRuntime.AfterCreateReports();
            Assert.AreEqual(1, _lines.Count, "KSP2's own parts");
            Assert.IsFalse(ScienceRuntime.Broken);
        }
    }
}
