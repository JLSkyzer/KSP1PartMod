using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;
using KSP.Sim.State;
using KSP1PartMod.Native;

namespace KSP1PartMod.Game
{
    /// <summary>Old saves under the Ore remap (<see cref="OreRemap"/>): a KSP1 part saved before the remap holds its Ore as
    /// <c>TestRocks</c>, while its container now holds the remap target. Without help, the game's <c>PartComponent.SetState</c>
    /// writes the saved <c>TestRocks</c> amount into a container that lacks it (nothing stored), and the target keeps the
    /// definition's initial units: the Ore is lost.
    /// <para>A postfix on <c>PartComponent.SetState(PartState, ISimulationModelMap)</c>, for KSP1-imported parts only (tag
    /// <c>KSP1</c>): when the saved state holds a <c>TestRocks</c> amount and no target entry, and the container holds the
    /// target and no <c>TestRocks</c>, the target gets the saved amount converted by mass (the database's unit masses, so the
    /// stored mass is kept), capped at the target's capacity. One line per part type and a session total. A missing member
    /// arms nothing (one line); a failure stops the migration for the session (one line). Never throws into the load.</para></summary>
    internal static class OreMigration
    {
        public const string HarmonyId = "KSP1PartMod.OreMigration";
        public const string Source = "TestRocks";

        /// <summary>What one migration did.</summary>
        internal struct Result
        {
            public bool Migrated;
            public double SourceUnits, TargetUnits, Capacity;
            public bool Capped;
        }

        private static readonly object Lock = new object();
        private static readonly HashSet<string> LoggedParts = new HashSet<string>(StringComparer.Ordinal);
        private static int _parts;
        private static double _sourceUnits, _targetUnits;
        private static bool _broken, _totalPending, _noDatabaseLogged;

        /// <summary>Tests: replaces the deferred total (null: logged on the next frame through the mod's coroutine runner).</summary>
        internal static Action ScheduleTotal;

        private static string Inv(double v) => v.ToString("G6", CultureInfo.InvariantCulture);

        /// <summary>The members the postfix needs, resolved by name; empty when all are found.</summary>
        internal static List<string> MissingMembers()
        {
            var missing = new List<string>();
            var setState = AccessTools.Method(typeof(PartComponent), "SetState", new[] { typeof(PartState), typeof(ISimulationModelMap) });
            if (setState == null || setState.IsStatic) missing.Add("PartComponent.SetState(PartState, ISimulationModelMap)");
            if (AccessTools.Property(typeof(PartComponent), "PartResourceContainer")?.PropertyType != typeof(ResourceContainer))
                missing.Add("PartComponent.PartResourceContainer");
            if (AccessTools.Property(typeof(PartComponent), "PartData") == null) missing.Add("PartComponent.PartData");
            if (AccessTools.Field(typeof(PartState), "resources")?.FieldType != typeof(Dictionary<string, ContainedResourceState>))
                missing.Add("PartState.resources");
            foreach (var name in new[] { "IsResourceContained", "GetResourceCapacityUnits", "SetResourceStoredUnits" })
                if (AccessTools.Method(typeof(ResourceContainer), name) == null) missing.Add("ResourceContainer." + name);
            return missing;
        }

        /// <summary>Arms the postfix (only while the remap is active). Returns true when armed.</summary>
        public static bool Arm(Harmony harmony)
        {
            if (OreRemap.Active == null) return false;
            try
            {
                var missing = MissingMembers();
                if (missing.Count > 0)
                {
                    NativeModules.Warn(Lines.NotArmed(string.Join(", ", missing)));
                    return false;
                }
                var setState = AccessTools.Method(typeof(PartComponent), "SetState", new[] { typeof(PartState), typeof(ISimulationModelMap) });
                harmony.Patch(setState, postfix: new HarmonyMethod(typeof(OreMigration), nameof(AfterSetState)));
                return true;
            }
            catch (Exception e)
            {
                NativeModules.Warn(Lines.NotArmed(e.GetType().Name + ": " + e.Message));
                return false;
            }
        }

        private static void AfterSetState(PartComponent __instance, PartState partState)
        {
            if (_broken || __instance == null || partState.resources == null) return;
            try
            {
                if (!partState.resources.ContainsKey(Source)) return; // the common path: nothing to do
                var target = OreRemap.Active;
                if (target == null) return;
                var data = __instance.PartData;
                if (data?.tags == null || data.tags.IndexOf("KSP1", StringComparison.OrdinalIgnoreCase) < 0) return;
                var db = GameManager.Instance?.Game?.ResourceDefinitionDatabase;
                if (db == null)
                {
                    NoDatabase(data.partName ?? __instance.PartName ?? "?", partState.resources[Source].storedUnits);
                    return;
                }
                var r = Migrate(partState.resources, __instance.PartResourceContainer, db, target);
                if (r.Migrated) Count(data.partName ?? __instance.PartName ?? "?", target, r);
            }
            catch (Exception e)
            {
                _broken = true;
                NativeModules.Error(Lines.Failed(e.ToString()));
            }
        }

        /// <summary>The migration itself, on Redux's real container: true when the target got the saved amount.</summary>
        internal static Result Migrate(IDictionary<string, ContainedResourceState> saved, ResourceContainer container,
            ResourceDefinitionDatabase db, string target)
        {
            var none = new Result();
            if (saved == null || container == null || db == null || string.IsNullOrEmpty(target)) return none;
            if (!saved.TryGetValue(Source, out var state) || !(state.storedUnits > 0)) return none;
            if (saved.ContainsKey(target)) return none; // saved after the remap
            var sourceId = db.GetResourceIDFromName(Source);
            var targetId = db.GetResourceIDFromName(target);
            if (sourceId == ResourceDefinitionID.InvalidID || targetId == ResourceDefinitionID.InvalidID) return none;
            if (!container.IsResourceContained(targetId) || container.IsResourceContained(sourceId)) return none;
            var sourceMass = db.GetResourceUnitMass(sourceId);
            var targetMass = db.GetResourceUnitMass(targetId);
            if (!(sourceMass > 0) || !(targetMass > 0)) return none;

            var units = state.storedUnits * sourceMass / targetMass;
            var capacity = container.GetResourceCapacityUnits(targetId);
            var set = container.SetResourceStoredUnits(targetId, Math.Min(units, capacity));
            return new Result { Migrated = true, SourceUnits = state.storedUnits, TargetUnits = set, Capacity = capacity, Capped = units > capacity };
        }

        /// <summary>The game has no resource database while a KSP1 part with saved <c>TestRocks</c> loads: not migrated, one
        /// line per session (the Task 2 review: that skip was silent).</summary>
        internal static void NoDatabase(string part, double savedUnits)
        {
            if (!(savedUnits > 0)) return;
            bool first;
            lock (Lock)
            {
                first = !_noDatabaseLogged;
                _noDatabaseLogged = true;
            }
            if (first) NativeModules.Warn(Lines.NoDatabase(part, savedUnits));
        }

        internal static void Count(string part, string target, Result r)
        {
            bool first, schedule;
            lock (Lock)
            {
                _parts++;
                _sourceUnits += r.SourceUnits;
                _targetUnits += r.TargetUnits;
                first = LoggedParts.Add(part);
                schedule = !_totalPending;
                _totalPending = true;
            }
            if (first) NativeModules.Info(Lines.Part(part, r.SourceUnits, target, r.TargetUnits, r.Capped ? r.Capacity : (double?)null));
            if (!schedule) return;
            if (ScheduleTotal != null) ScheduleTotal();
            else TechTreeLoadStep.Runner().StartCoroutine(TotalNextFrame());
        }

        private static IEnumerator TotalNextFrame()
        {
            yield return null;
            LogTotal();
        }

        /// <summary>The session total so far (once per batch of loaded parts).</summary>
        internal static void LogTotal()
        {
            string line;
            lock (Lock)
            {
                _totalPending = false;
                line = Lines.Total(_parts, _sourceUnits, OreRemap.Active ?? "?", _targetUnits);
            }
            NativeModules.Info(line);
        }

        internal static void Reset()
        {
            lock (Lock)
            {
                LoggedParts.Clear();
                _parts = 0;
                _sourceUnits = _targetUnits = 0;
                _broken = _totalPending = _noDatabaseLogged = false;
            }
            ScheduleTotal = null;
        }

        /// <summary>The lines (exact text).</summary>
        internal static class Lines
        {
            public static string Part(string part, double source, string target, double units, double? cappedAt) =>
                NativeLines.Prefix + $"ore: old save: {part}: saved {Source} {Inv(source)} moved to {target} {Inv(units)} (mass kept" +
                (cappedAt is double cap ? $", capped at the capacity {Inv(cap)}" : "") + ")";

            public static string Total(int parts, double source, string target, double units) =>
                NativeLines.Prefix + $"ore: old saves: {parts} part(s) migrated this session, {Source} {Inv(source)} -> {target} {Inv(units)}";

            public static string NotArmed(string what) =>
                NativeLines.Prefix + $"ore: old saves: {what} not found, not migrated (Ore saved before the remap is lost on load)";

            public static string NoDatabase(string part, double source) =>
                NativeLines.Prefix + $"ore: old saves: no resource database while {part} loads, its saved {Source} {Inv(source)} not migrated " +
                "(logged once; later parts without a database are not migrated either)";

            public static string Failed(string error) =>
                NativeLines.Prefix + "ore: old saves: migration failed, stopped for this session: " + error;
        }
    }
}
