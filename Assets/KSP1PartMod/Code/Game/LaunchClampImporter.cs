using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.Definitions;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;

namespace KSP1PartMod.Game
{
    /// <summary>KSP1 <c>LaunchClamp</c> → KSP2's <c>Module_Decouple</c> (<c>ksp1_launch_clamps</c>, launch clamps 0.19.0): an omni
    /// decoupler on the part's surface attach node, stageable, with no ejection push (<see cref="LaunchClampMapping"/>). A part that
    /// already has a decoupler gets nothing more (noted). Any failure leaves the part as Redux imports it (the module dropped).</summary>
    internal sealed class LaunchClampImporter : IKsp1PartModuleImporter
    {
        public const string DataTypeName = "KSP.Modules.Data_Decouple";
        public const string ModuleTypeName = "KSP.Modules.Module_Decouple";

        /// <summary>The members this converter writes, resolved by name; empty when all are found.</summary>
        internal static List<string> MissingMembers()
        {
            var missing = new List<string>();
            var data = AccessTools.TypeByName(DataTypeName);
            if (data == null) missing.Add(DataTypeName);
            else
            {
                foreach (var (field, type) in new[] { ("isOmniDecoupler", typeof(bool)), ("IsStageable", typeof(bool)), ("ejectionForce", typeof(float)), ("explosiveNodeID", typeof(string)) })
                {
                    var f = AccessTools.Field(data, field);
                    if (f == null || f.IsStatic || f.FieldType != type) missing.Add("Data_Decouple." + field);
                }
            }
            if (AccessTools.TypeByName(ModuleTypeName) == null) missing.Add(ModuleTypeName);
            return missing;
        }

        public bool TryImport(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule)
        {
            serializedModule = default;
            var part = context?.PartName ?? "?";
            var added = -1;
            try
            {
                if (context == null || moduleNode == null) return false;
                if (context.ModuleData.Any(d => d is Data_Decouple))
                {
                    NativeModules.Note(part, LaunchClampMapping.ExtraModuleNote);
                    return false;
                }
                var ids = context.PartData?.attachNodes?.Select(n => n.nodeID).ToList() ?? new List<string>();
                var map = LaunchClampMapping.From(new Ksp1NodeAdapter(moduleNode), ids);
                var data = new Data_Decouple
                {
                    isOmniDecoupler = true,
                    IsStageable = true,
                    ejectionForce = 0f, // KSP1's clamp releases without a push
                    explosiveNodeID = map.NodeId,
                };
                added = context.ModuleData.Count;
                serializedModule = NativeModules.CreateSerialized(typeof(Module_Decouple), data, context);
                NativeModules.Record.AddConversion(part, LaunchClampMapping.NativeModule);
                foreach (var note in map.Notes) NativeModules.Note(part, note);
                added = -1;
                return true;
            }
            catch (Exception e)
            {
                if (added >= 0 && context != null)
                    while (context.ModuleData.Count > added) context.ModuleData.RemoveAt(context.ModuleData.Count - 1);
                serializedModule = default;
                NativeModules.ImporterFailed(part, LaunchClampMapping.Ksp1Module, e);
                return false;
            }
        }

        /// <summary>The <c>native: launch clamps:</c> line of the session.</summary>
        internal static string Summary() =>
            NativeLines.LaunchClampSummary(NativeModules.Record.PartsWith(LaunchClampMapping.NativeModule));
    }
}
