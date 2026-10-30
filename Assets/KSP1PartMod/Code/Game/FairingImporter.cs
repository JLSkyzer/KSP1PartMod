using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using KSP1PartMod.Native.Fairings;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>KSP1 fairing bases → KSP2's native procedural <c>Module_Fairing</c> (fairings plan, Task 2): one importer type for
    /// <c>ModuleProceduralFairing</c> (<c>ksp1_procedural_fairings</c>) and <c>ModuleSimpleAdjustableFairing</c>
    /// (<c>ksp1_saf_fairings</c>). It fills a <c>Data_Fairing</c> from <see cref="FairingMapping"/> and builds the module through
    /// Redux's own <c>CreateSerialized</c>, then records the part in <see cref="Record"/>, which the runtime guards
    /// (<see cref="FairingFixes"/>) and the SAF prefab pass (<see cref="ShroudImport"/>) read.
    /// <list type="bullet">
    /// <item>armed only when the runtime guards are (<see cref="FairingFixes.GuardsArmed"/>): without them a converted base
    /// throws or never spawns; SAF also needs the 0.7.0 prefab patch, which hides its KSP1 meshes;</item>
    /// <item>one fairing per part (KSP2's rule): a second fairing module, or a part already holding a <c>Data_Fairing</c>, is
    /// refused (<c>second</c>);</item>
    /// <item>B9's scratch re-imports (<see cref="B9ModuleDiff"/>) build the module but record and log nothing.</item>
    /// </list>
    /// Any failure leaves the part as Redux 0.14.0 imports it (the module dropped, nothing of ours left on the part).</summary>
    internal sealed class FairingImporter : IKsp1PartModuleImporter
    {
        public const string ProceduralSwitch = "ksp1_procedural_fairings";
        public const string SafSwitch = "ksp1_saf_fairings";

        /// <summary>The bases converted this session, by part name.</summary>
        internal static readonly FairingRecord Record = new FairingRecord();

        /// <summary>Tests: called after Redux's <c>CreateSerialized</c>, before the conversion is recorded (a throw = a failure).</summary>
        internal static Action<string> FaultForTests;
        /// <summary>Tests: the state of the 0.7.0 prefab patch instead of <see cref="ShroudImport.IsArmed"/>.</summary>
        internal static Func<bool> PrefabPatchArmedSource;

        private static readonly object Lock = new object();
        private static readonly ConditionalWeakTable<Ksp1PartImportContext, object> Seen = new ConditionalWeakTable<Ksp1PartImportContext, object>();
        private static readonly object SeenMark = new object();

        private readonly FairingKind _kind;

        public FairingImporter(FairingKind kind)
        {
            _kind = kind;
        }

        public static string SwitchKey(FairingKind kind) => kind == FairingKind.Saf ? SafSwitch : ProceduralSwitch;

        /// <summary>What this converter needs and lacks: the guards, and for SAF the 0.7.0 prefab patch.</summary>
        internal static List<string> MissingMembers(FairingKind kind)
        {
            var missing = new List<string>();
            if (!FairingFixes.GuardsArmed) missing.Add(FairingLines.GuardsNotArmed);
            if (kind == FairingKind.Saf && !(PrefabPatchArmedSource?.Invoke() ?? ShroudImport.IsArmed)) missing.Add(FairingLines.PrefabPatchNotArmed);
            return missing;
        }

        internal static Func<string, string> MissingLine(FairingKind kind) => m => FairingLines.ConverterOff(kind, m);

        internal static string Summary(FairingKind kind) =>
            FairingLines.Summary(kind, Record.Count(kind), Record.Refusals(kind), Record.NotReproduced(kind));

        public bool TryImport(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule)
        {
            serializedModule = default;
            var part = context?.PartName ?? "?";
            var scratch = B9ModuleDiff.IsScratch(context);
            var added = -1;
            try
            {
                if (context == null || moduleNode == null) return false;
                bool second;
                lock (Lock)
                {
                    second = Seen.TryGetValue(context, out _);
                    if (!second) Seen.Add(context, SeenMark);
                }
                if (second || context.ModuleData.Any(d => d is Data_Fairing))
                {
                    Refuse(part, scratch, FairingRefusal.Second, null);
                    return false;
                }

                var module = new Ksp1NodeAdapter(moduleNode);
                var partNode = context.PartNode != null ? new Ksp1NodeAdapter(context.PartNode) : null;
                var map = _kind == FairingKind.Saf ? FairingMapping.Saf(module, partNode) : FairingMapping.Procedural(module, partNode);
                if (!map.Converted)
                {
                    Refuse(part, scratch, FairingRefusal.Mapping, map.Reason);
                    return false;
                }

                var data = NewData(map.Values);
                added = context.ModuleData.Count;
                serializedModule = NativeModules.CreateSerialized(typeof(Module_Fairing), data, context);
                FaultForTests?.Invoke(part);
                added = -1;
                if (scratch) return true;
                NativeModules.Record.AddConversion(part, FairingMapping.NativeModule);
                if (!Record.Add(part, _kind, map.NotReproduced)) return true; // a second import of the same part: logged once
                try
                {
                    NativeModules.Info(FairingLines.Part(part, _kind, map.Values));
                }
                catch (Exception e)
                {
                    // Only the log failed: the module stays.
                    NativeModules.Error(NativeLines.Prefix + part + ": fairing line not logged: " + e.Message);
                }
                return true;
            }
            catch (Exception e)
            {
                // Redux's own result for this module: dropped, nothing of ours left in the part's module data.
                if (added >= 0 && context != null)
                    while (context.ModuleData.Count > added) context.ModuleData.RemoveAt(context.ModuleData.Count - 1);
                serializedModule = default;
                if (!scratch) Record.Refuse(_kind, FairingRefusal.Failed);
                NativeModules.ImporterFailed(part, FairingLines.ModuleOf(_kind), e);
                return false;
            }
        }

        private void Refuse(string part, bool scratch, FairingRefusal reason, string detail)
        {
            if (scratch) return;
            Record.Refuse(_kind, reason);
            NativeModules.Note(part, FairingLines.RefusedNote(_kind, reason, detail));
        }

        /// <summary>A <c>Data_Fairing</c> with <paramref name="v"/>; every other field keeps KSP2's default. The deploy type is
        /// parsed by name (<see cref="FairingDeploy"/> mirrors <c>FairingDeployType</c>) and set as the default and the state.</summary>
        internal static Data_Fairing NewData(FairingValues v)
        {
            var deploy = (FairingDeployType)Enum.Parse(typeof(FairingDeployType), v.DefaultDeployType.ToString());
            var d = new Data_Fairing
            {
                BaseRadius = v.BaseRadius,
                MaxRadius = v.MaxRadius,
                CapRadius = v.CapRadius,
                CloseRadius = v.CloseRadius,
                SnapThreshold = v.SnapThreshold,
                CrossSectionHeightMin = v.CrossSectionHeightMin,
                CrossSectionHeightMax = v.CrossSectionHeightMax,
                NoseTip = v.NoseTip,
                EdgeWarp = v.EdgeWarp,
                Pivot = new Vector3(v.Pivot.X, v.Pivot.Y, v.Pivot.Z),
                LocalUpAxis = new Vector3(v.LocalUpAxis.X, v.LocalUpAxis.Y, v.LocalUpAxis.Z),
                BaseModelTransformName = v.BaseModelTransformName,
                FairingSideCount = v.FairingSideCount,
                MassAreaRatio = v.MassAreaRatio,
                DefaultDeployType = deploy,
                StageToggleDefault = v.StageToggleDefault,
                AllowInterstageNode = v.AllowInterstageNode,
            };
            d.DeployType.SetValue(deploy);
            d.EjectionForce.SetValue(v.EjectionForce);
            return d;
        }

        internal static void ResetSession()
        {
            Record.Clear();
            FaultForTests = null;
            PrefabPatchArmedSource = null;
        }
    }
}
