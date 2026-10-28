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
    /// <summary>KSP1 <c>ModuleToggleCrossfeed</c> → KSP2's <c>Module_ToggleCrossfeed</c> (<c>ksp1_crossfeed_toggle</c>, plan
    /// (e)): builds <c>Data_ToggleCrossFeed</c> from <see cref="CrossfeedMapping"/>, sets the part's start state
    /// (<c>PartData.fuelCrossFeed</c>) when the module has a <c>crossfeedStatus</c>, and returns the module Redux's own
    /// <c>CreateSerialized</c> builds. A second one on a part, or a part that already has a <c>Data_ToggleCrossFeed</c>, gets
    /// nothing more (noted). Any failure leaves the part as Redux 0.7.0 imports it (the module dropped).</summary>
    internal sealed class CrossfeedImporter : IKsp1PartModuleImporter
    {
        public const string DataTypeName = "KSP.Modules.Data_ToggleCrossFeed";
        public const string ModuleTypeName = "KSP.Modules.Module_ToggleCrossfeed";
        private const string FlagStartOn = "crossfeed:on";
        private const string FlagTech = "crossfeed:techRequired";

        /// <summary>The members this converter reads or writes, resolved by name; empty when all are found.</summary>
        internal static List<string> MissingMembers()
        {
            var missing = new List<string>();
            var data = AccessTools.TypeByName(DataTypeName);
            if (data == null) missing.Add(DataTypeName);
            else
            {
                foreach (var field in new[] { "ToggleInEditor", "ToggleInFlight" })
                {
                    var f = AccessTools.Field(data, field);
                    if (f == null || f.IsStatic || f.FieldType != typeof(bool)) missing.Add("Data_ToggleCrossFeed." + field);
                }
            }
            if (AccessTools.TypeByName(ModuleTypeName) == null) missing.Add(ModuleTypeName);
            var crossFeed = AccessTools.Field(AccessTools.TypeByName("KSP.Sim.Definitions.PartData"), "fuelCrossFeed");
            if (crossFeed == null || crossFeed.IsStatic || crossFeed.FieldType != typeof(bool)) missing.Add("PartData.fuelCrossFeed");
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
                if (context.ModuleData.Any(d => d is Data_ToggleCrossFeed))
                {
                    NativeModules.Note(part, CrossfeedMapping.ExtraModuleNote);
                    return false;
                }
                var partData = context.PartData;
                var map = CrossfeedMapping.From(new Ksp1NodeAdapter(moduleNode), partData?.fuelCrossFeed);
                var data = new Data_ToggleCrossFeed
                {
                    ToggleInEditor = map.ToggleInEditor,
                    ToggleInFlight = map.ToggleInFlight,
                };
                added = context.ModuleData.Count;
                serializedModule = NativeModules.CreateSerialized(typeof(Module_ToggleCrossfeed), data, context);
                added = -1;
                if (map.OverridesPart && partData != null) partData.fuelCrossFeed = map.StartCrossFeed;

                var record = NativeModules.Record;
                record.AddConversion(part, CrossfeedMapping.NativeModule);
                if (partData?.fuelCrossFeed ?? map.StartCrossFeed) record.Flag(part, FlagStartOn);
                if (map.TechRequired != null) record.Flag(part, FlagTech);
                foreach (var note in map.Notes) NativeModules.Note(part, note);
                return true;
            }
            catch (Exception e)
            {
                // Redux's own result for this module: dropped, nothing of ours left in the part's module data.
                if (added >= 0 && context != null)
                    while (context.ModuleData.Count > added) context.ModuleData.RemoveAt(context.ModuleData.Count - 1);
                serializedModule = default;
                NativeModules.ImporterFailed(part, CrossfeedMapping.Ksp1Module, e);
                return false;
            }
        }

        /// <summary>The <c>native: crossfeed:</c> line of the session.</summary>
        internal static string Summary()
        {
            var r = NativeModules.Record;
            return NativeLines.CrossfeedSummary(r.PartsWith(CrossfeedMapping.NativeModule), r.CountFlag(FlagStartOn), r.CountFlag(FlagTech));
        }
    }
}
